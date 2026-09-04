using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Slicing;
using Danslicer.Core.Supports;

namespace Danslicer.Tests;

public class PreviewRendererTests
{
    private const int Width = Slicer.PreviewWidth;
    private const int Height = Slicer.PreviewHeight;

    private static Mesh Box(float sx, float sy, float sz)
    {
        var p = new Vector3[8];
        for (int i = 0; i < 8; i++) p[i] = new Vector3((i & 1) * sx, ((i >> 1) & 1) * sy, ((i >> 2) & 1) * sz);
        int[] idx =
        {
            0, 2, 3, 0, 3, 1,  4, 5, 7, 4, 7, 6,  0, 1, 5, 0, 5, 4,
            2, 6, 7, 2, 7, 3,  0, 4, 6, 0, 6, 2,  1, 3, 7, 1, 7, 5,
        };
        return new Mesh(p, idx);
    }

    /// <summary>An L-shaped (asymmetric under X or Y mirror) triangle soup: a flat base plus a
    /// narrow spike planted only at the base's +X, +Y corner, so a mirroring bug shows up as
    /// "the spike lands on the wrong side" rather than an indistinguishable cube.</summary>
    private static Mesh LShape()
    {
        // Base slab spanning X in [0,20], Y in [0,6], Z in [0,2]. Nothing here is symmetric under
        // X<->-X or Y<->-Y.
        var baseBox = Box(20, 6, 2);
        // A thin, tall spike planted only at the far +X, +Y corner of the base, reaching well
        // above it -- narrow enough that its own footprint doesn't blur the position signal.
        var spikeBox = Box(2, 2, 14);
        var spikePositions = new Vector3[8];
        for (int i = 0; i < 8; i++)
            spikePositions[i] = spikeBox.Positions[i] + new Vector3(17, 3, 2);
        var spikeMesh = new Mesh(spikePositions, spikeBox.Indices);

        var vertices = new Vector3[baseBox.Positions.Length + spikeMesh.Positions.Length];
        var indices = new int[baseBox.Indices.Length + spikeMesh.Indices.Length];
        Array.Copy(baseBox.Positions, vertices, baseBox.Positions.Length);
        Array.Copy(spikeMesh.Positions, 0, vertices, baseBox.Positions.Length, spikeMesh.Positions.Length);
        Array.Copy(baseBox.Indices, indices, baseBox.Indices.Length);
        for (int i = 0; i < spikeMesh.Indices.Length; i++)
            indices[baseBox.Indices.Length + i] = spikeMesh.Indices[i] + baseBox.Positions.Length;
        return new Mesh(vertices, indices);
    }

    private static (int MinX, int MaxX) LitColumnRange(byte[] rgb565, int width, int height)
    {
        var minX = int.MaxValue;
        var maxX = int.MinValue;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var idx = y * width + x;
                var lo = rgb565[idx * 2];
                var hi = rgb565[idx * 2 + 1];
                if (lo == 0 && hi == 0) continue; // background happens to be non-zero, but keep this defensive
                var value = (ushort)(lo | (hi << 8));
                // Background is a fixed dark colour; anything reasonably brighter counts as model.
                var r = (value >> 11) & 0x1F;
                var g = (value >> 5) & 0x3F;
                var b = value & 0x1F;
                if (r < 8 && g < 12 && b < 8) continue; // near-background
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
            }
        }
        return (minX, maxX);
    }

    private static int LitPixelCount(byte[] rgb565, int width, int height)
    {
        var count = 0;
        for (int i = 0; i < width * height; i++)
        {
            var value = (ushort)(rgb565[i * 2] | (rgb565[i * 2 + 1] << 8));
            var r = (value >> 11) & 0x1F;
            var g = (value >> 5) & 0x3F;
            var b = value & 0x1F;
            if (r >= 8 || g >= 12 || b >= 8) count++;
        }
        return count;
    }

    /// <summary>
    /// True for pixels whose hue leans red-over-blue, which only the (red-dominant) support
    /// colour produces -- the (blue-dominant) model colour and background never do, regardless of
    /// how the fixed light brightens or dims them, since brightness scales all three channels by
    /// the same factor.
    /// </summary>
    private static bool HasSupportColoredPixel(byte[] rgb565, int width, int height)
    {
        for (int i = 0; i < width * height; i++)
        {
            var value = (ushort)(rgb565[i * 2] | (rgb565[i * 2 + 1] << 8));
            var r = (value >> 11) & 0x1F;
            var b = value & 0x1F;
            if (r > b + 2) return true;
        }
        return false;
    }

    [Fact]
    public void CubeRendersDeterministically()
    {
        var objects = new[] { new PreviewRenderer.RenderObject(Box(10, 10, 10), Matrix4x4.Identity) };
        var first = PreviewRenderer.Render(objects, null, Width, Height);
        var second = PreviewRenderer.Render(objects, null, Width, Height);
        Assert.Equal(first, second);
    }

    [Fact]
    public void CubeOccupiesASensibleFractionOfTheFrame()
    {
        var objects = new[] { new PreviewRenderer.RenderObject(Box(30, 30, 30), Matrix4x4.Identity) };
        var preview = PreviewRenderer.Render(objects, null, Width, Height);
        Assert.Equal(Width * Height * 2, preview.Length);

        var lit = LitPixelCount(preview, Width, Height);
        var frame = Width * Height;
        Assert.True(lit > frame / 20, $"expected the cube to cover a meaningful part of the frame, got {lit}/{frame} pixels");
        Assert.True(lit < frame, "expected some background to remain visible around the model");
    }

    [Fact]
    public void EmptySceneDegradesGracefullyToABackgroundImage()
    {
        var preview = PreviewRenderer.Render(Array.Empty<PreviewRenderer.RenderObject>(), null, Width, Height);
        Assert.Equal(Width * Height * 2, preview.Length);
        Assert.Equal(0, LitPixelCount(preview, Width, Height));
    }

    private static int TopmostLitRow(byte[] rgb565, int width, int height)
    {
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var idx = y * width + x;
                var value = (ushort)(rgb565[idx * 2] | (rgb565[idx * 2 + 1] << 8));
                var r = (value >> 11) & 0x1F;
                var g = (value >> 5) & 0x3F;
                var b = value & 0x1F;
                if (r >= 8 || g >= 12 || b >= 8) return y;
            }
        return -1;
    }

    /// <summary>Mean screen-X of lit pixels whose screen-Y is at or above (numerically less than
    /// or equal to) <paramref name="yThreshold"/> -- i.e. the higher, world-tall part of the model.</summary>
    private static double MeanXAboveRow(byte[] rgb565, int width, int height, int yThreshold)
    {
        double sumX = 0;
        var count = 0;
        for (int y = 0; y <= yThreshold; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var idx = y * width + x;
                var value = (ushort)(rgb565[idx * 2] | (rgb565[idx * 2 + 1] << 8));
                var r = (value >> 11) & 0x1F;
                var g = (value >> 5) & 0x3F;
                var b = value & 0x1F;
                if (r < 8 && g < 12 && b < 8) continue;
                sumX += x;
                count++;
            }
        }
        return count == 0 ? double.NaN : sumX / count;
    }

    [Fact]
    public void AsymmetricMeshKeepsItsArmOnTheSameSideAsTheAppViewport()
    {
        // The tall arm sits at the +X end of the base and reaches much higher in Z than the rest
        // of the model. Because the renderer's camera math is copied verbatim from
        // Danslicer.Render.Camera (View = CreateLookAt(eye, target, +Z), same NDC-to-screen
        // mapping), the arm's world-space +X/+Y offset must land on the same (right-hand) side of
        // the frame that the app's own viewport would draw it on for this Yaw/Pitch -- not
        // mirrored to the left.
        var mesh = LShape();
        var objects = new[] { new PreviewRenderer.RenderObject(mesh, Matrix4x4.Identity) };
        var preview = PreviewRenderer.Render(objects, null, Width, Height);

        var (minX, maxX) = LitColumnRange(preview, Width, Height);
        Assert.True(minX < maxX, "expected a non-trivial silhouette");
        var overallCentreX = (minX + maxX) / 2.0;

        // The topmost few lit rows can only belong to the tall arm (nothing else in the mesh
        // reaches anywhere near that height once projected). Its mean X must sit on the +X/+Y
        // side of the whole model's centre, matching the app viewport's handedness for this
        // camera, and must not have been flipped to the opposite side.
        var topRow = TopmostLitRow(preview, Width, Height);
        Assert.True(topRow >= 0, "expected a non-trivial silhouette");
        var armMeanX = MeanXAboveRow(preview, Width, Height, topRow + 8);
        Assert.False(double.IsNaN(armMeanX), "expected the tall arm to be visible near the top of the frame");
        Assert.True(armMeanX > overallCentreX,
            $"expected the tall arm (world +X/+Y corner) to render right of the model's centre (armMeanX={armMeanX}, centre={overallCentreX}); a left-right mirror bug would put it on the left");
    }

    [Fact]
    public void SupportsAreIncludedInADistinguishableShade()
    {
        var mesh = Box(10, 10, 2);
        var objects = new[] { new PreviewRenderer.RenderObject(mesh, Matrix4x4.Identity) };
        var withoutSupports = PreviewRenderer.Render(objects, null, Width, Height);

        var graph = new SupportGraph();
        var tip = new SupportNode { Type = SupportNodeType.Tip, Position = new Vector3(5, 5, 30) };
        var basePoint = new SupportNode { Type = SupportNodeType.Base, Position = new Vector3(5, 5, 2) };
        graph.AddNode(tip);
        graph.AddNode(basePoint);
        graph.AddSegment(new SupportSegment
        {
            Type = SupportSegmentType.Trunk,
            NodeA = tip.Id,
            NodeB = basePoint.Id,
            Diameter = 2f,
        });

        var withSupports = PreviewRenderer.Render(objects, graph, Width, Height);
        Assert.NotEqual(withoutSupports, withSupports);

        Assert.False(HasSupportColoredPixel(withoutSupports, Width, Height));
        Assert.True(HasSupportColoredPixel(withSupports, Width, Height));
    }

    [Fact]
    public void DisabledSupportsAreExcluded()
    {
        var mesh = Box(10, 10, 2);
        var objects = new[] { new PreviewRenderer.RenderObject(mesh, Matrix4x4.Identity) };

        var graph = new SupportGraph();
        var tip = new SupportNode { Type = SupportNodeType.Tip, Position = new Vector3(5, 5, 30) };
        var basePoint = new SupportNode { Type = SupportNodeType.Base, Position = new Vector3(5, 5, 2) };
        graph.AddNode(tip);
        graph.AddNode(basePoint);
        graph.AddSegment(new SupportSegment
        {
            Type = SupportSegmentType.Trunk,
            NodeA = tip.Id,
            NodeB = basePoint.Id,
            Diameter = 2f,
            Disabled = true,
        });

        var withoutSupports = PreviewRenderer.Render(objects, null, Width, Height);
        var withDisabledSupport = PreviewRenderer.Render(objects, graph, Width, Height);
        Assert.Equal(withoutSupports, withDisabledSupport);
    }

    [Fact]
    public void PreviewSizeMatchesWhatPhotonWorkshopWriterExpects()
    {
        var objects = new[] { new PreviewRenderer.RenderObject(Box(5, 5, 5), Matrix4x4.Identity) };
        var preview = PreviewRenderer.Render(objects, null, Slicer.PreviewWidth, Slicer.PreviewHeight);
        Assert.Equal(Slicer.PreviewWidth * Slicer.PreviewHeight * 2, preview.Length);
    }
}
