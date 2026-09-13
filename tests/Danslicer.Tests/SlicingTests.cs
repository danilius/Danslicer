using System.Numerics;
using Clipper2Lib;
using Danslicer.Core.Geometry;
using Danslicer.Core.IO;
using Danslicer.Core.Printers;
using Danslicer.Core.Scene;
using Danslicer.Core.Slicing;

namespace Danslicer.Tests;

public class SlicingTests
{
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

    private static readonly PrinterDefinition Printer = PrinterDefinition.PhotonMonoX;

    [Fact]
    public void CubeLayerIsSingleCounterClockwiseSquare()
    {
        var mesh = Box(10, 10, 10);
        var prepared = new MeshSlicer.PreparedMesh(mesh, Matrix4x4.Identity);
        var buckets = MeshSlicer.BucketTriangles(prepared, 0.05, 200);
        var segments = new List<MeshSlicer.Segment>();
        MeshSlicer.CollectSegments(prepared, buckets[100], 5.025, segments);
        Assert.Equal(8, segments.Count); // two triangles per side face, four side faces

        var loops = MeshSlicer.ChainSegments(segments);
        Assert.Single(loops);
        var area = Clipper.Area(loops[0]) / (MeshSlicer.UnitsPerMm * MeshSlicer.UnitsPerMm);
        Assert.Equal(100.0, area, 3); // positive: outer loop is counter-clockwise
    }

    [Fact]
    public void HollowBoxProducesHoleWithNegativeArea()
    {
        var outer = new MeshSlicer.PreparedMesh(Box(20, 20, 10), Matrix4x4.Identity);
        // Inner box with flipped winding acts as a cavity.
        var innerMesh = Box(10, 10, 10);
        var flipped = new int[innerMesh.Indices.Length];
        for (int t = 0; t < innerMesh.TriangleCount; t++)
        {
            flipped[t * 3] = innerMesh.Indices[t * 3];
            flipped[t * 3 + 1] = innerMesh.Indices[t * 3 + 2];
            flipped[t * 3 + 2] = innerMesh.Indices[t * 3 + 1];
        }
        var inner = new MeshSlicer.PreparedMesh(new Mesh(innerMesh.Positions, flipped), Matrix4x4.CreateTranslation(5, 5, 0));

        var segments = new List<MeshSlicer.Segment>();
        MeshSlicer.CollectSegments(outer, Enumerable.Range(0, 12).ToList(), 5, segments);
        MeshSlicer.CollectSegments(inner, Enumerable.Range(0, 12).ToList(), 5, segments);
        var polygons = MeshSlicer.Finish(MeshSlicer.ChainSegments(segments), 0);
        Assert.Equal(2, polygons.Count);
        Assert.Equal(300.0, MeshSlicer.AreaMm2(polygons), 3);
    }

    [Fact]
    public void RasterizerFillsExpectedPixelCount()
    {
        // 10 x 10 mm square centred on the plate: 200 x 200 pixels at 50 µm.
        var square = new Paths64
        {
            new Path64
            {
                new Point64(-5000, -5000), new Point64(5000, -5000),
                new Point64(5000, 5000), new Point64(-5000, 5000),
            },
        };
        var raster = new LayerRasterizer(Printer, antiAliasing: false);
        var pixels = new byte[Printer.ResolutionX * Printer.ResolutionY];
        var lit = raster.Rasterize(square, pixels);
        Assert.Equal(200u * 200u, lit);

        // Centre of the plate must be lit, far corner clear.
        Assert.Equal(255, pixels[1200 * 3840 + 1920]);
        Assert.Equal(0, pixels[0]);
    }

    [Fact]
    public void AntiAliasedEdgeHasIntermediateValues()
    {
        // Square offset by a quarter pixel so its edges cut through pixel columns.
        var square = new Paths64
        {
            new Path64
            {
                new Point64(-5012, -5012), new Point64(4988, -5012),
                new Point64(4988, 4988), new Point64(-5012, 4988),
            },
        };
        var raster = new LayerRasterizer(Printer, antiAliasing: true);
        var pixels = new byte[Printer.ResolutionX * Printer.ResolutionY];
        raster.Rasterize(square, pixels);
        var distinct = pixels.Distinct().Count();
        Assert.True(distinct > 2, $"expected grey levels on edges, got {distinct} distinct values");
    }

    [Fact]
    public void RleRoundTrips()
    {
        var rng = new Random(7);
        var image = new byte[3840 * 8];
        // Long black and white runs plus grey islands.
        for (int i = 3840 * 2; i < 3840 * 4; i++) image[i] = 255;
        for (int i = 3840 * 5; i < 3840 * 5 + 500; i++) image[i] = (byte)(rng.Next(1, 15) << 4 | 0x0F);
        for (int i = 0; i < 100; i++) image[3840 * 6 + rng.Next(3840)] = 0x77;

        var encoded = PhotonRle.Encode(image);
        var decoded = new byte[image.Length];
        PhotonRle.Decode(encoded, decoded);

        for (int i = 0; i < image.Length; i++)
        {
            // The codec keeps the top nibble and replicates it into the bottom nibble.
            var expected = (byte)((image[i] >> 4) * 17);
            Assert.Equal(expected, decoded[i]);
        }
        Assert.True(encoded.Length < image.Length / 10);
    }

    [Fact]
    public void SliceAndWriteRoundTrips()
    {
        var obj = new SceneObject("box", Box(10, 20, 2.5f));
        obj.Transform = Transform.Identity with { Translation = new Vector3(-5, -10, 0) };
        var settings = PrintSettings.Default with { LayerHeight = 0.5f };
        var resin = ResinSettings.Default with { BottomLayers = 2 };

        var result = Slicer.Slice(new[] { obj }, Printer, settings, resinSettings: resin);
        Assert.Equal(5, result.LayerCount);
        Assert.Equal(200f * 400f, result.Layers[2].LitPixels, 0);
        Assert.Equal(10 * 20 * 2.5 / 1000.0, result.VolumeMl, 3);
        Assert.Equal(-5f, result.MinX, 2);
        Assert.Equal(10f, result.MaxY, 2);

        using var ms = new MemoryStream();
        PhotonWorkshopWriter.Write(result, ms);
        var file = PhotonWorkshopFile.Read(ms.ToArray());

        Assert.Equal(516u, file.Version);
        Assert.Equal(0.5f, file.LayerHeight);
        Assert.Equal(3840, file.ResolutionX);
        Assert.Equal(2400, file.ResolutionY);
        Assert.Equal(16u, file.AntiAliasing);
        Assert.Equal(5, file.Layers.Count);
        Assert.Equal(resin.BottomExposure, file.Layers[0].Exposure);
        Assert.Equal(resin.Exposure, file.Layers[4].Exposure);
        Assert.Equal(resin.LiftSpeed / 60f, file.LiftSpeedMmPerSec, 4);
        Assert.Equal("Photon Mono X", file.MachineName);
        Assert.Equal("pw0Img", file.LayerImageFormat);

        var pixels = file.DecodeLayer(2);
        var expected = new byte[pixels.Length];
        result.Layers[2].Decode(3840, 2400, expected);
        Assert.Equal(expected, pixels);
        Assert.Equal(result.Layers[2].LitPixels, (uint)pixels.Count(p => p != 0));
    }

    [Fact]
    public void ExportHeaderConsumesSelectedResinExposureBytes()
    {
        var obj = new SceneObject("box", Box(1, 1, 1))
        {
            Transform = Transform.Identity with { Translation = new Vector3(-0.5f, -0.5f, 0) },
        };
        var low = Slicer.Slice([obj], Printer, PrintSettings.Default,
            resinSettings: ResinSettings.Default with { Exposure = 1.25f });
        var high = Slicer.Slice([obj], Printer, PrintSettings.Default,
            resinSettings: ResinSettings.Default with { Exposure = 4.5f });
        using var lowStream = new MemoryStream();
        using var highStream = new MemoryStream();
        PhotonWorkshopWriter.Write(low, lowStream);
        PhotonWorkshopWriter.Write(high, highStream);

        var lowBytes = lowStream.ToArray();
        var highBytes = highStream.ToArray();
        const int exposureHeaderOffset = 76;
        Assert.Equal(1.25f, BitConverter.ToSingle(lowBytes, exposureHeaderOffset));
        Assert.Equal(4.5f, BitConverter.ToSingle(highBytes, exposureHeaderOffset));
        Assert.NotEqual(lowBytes.AsSpan(exposureHeaderOffset, sizeof(float)).ToArray(),
            highBytes.AsSpan(exposureHeaderOffset, sizeof(float)).ToArray());
    }

    [Fact]
    public void CustomPrinterResolutionAndMirrorsDriveLayerBuffers()
    {
        var obj = new SceneObject("asymmetric", Box(1, 1, 1))
        {
            Transform = Transform.Identity with { Translation = new Vector3(-3, 1, 0) },
        };
        var printer = new PrinterDefinition(
            "custom-mirror", false, "Custom mirror", "Custom mirror", "pmx2",
            8, 6, 10, 8, 6, MirrorX: false, MirrorY: false, FormatVersion: 517);
        var settings = PrintSettings.Default with { LayerHeight = 0.5f, AntiAliasing = false };

        var plain = Slicer.Slice([obj], printer, settings);
        var mirrorX = Slicer.Slice([obj], printer with { MirrorX = true }, settings);
        var mirrorY = Slicer.Slice([obj], printer with { MirrorY = true }, settings);

        Assert.Equal(8, plain.Printer.ResolutionX);
        Assert.Equal(6, plain.Printer.ResolutionY);
        Assert.Equal(8 * 6, Decode(plain).Length);
        Assert.Equal((1, 1), SingleLitPixel(Decode(plain), 8));
        Assert.Equal((6, 1), SingleLitPixel(Decode(mirrorX), 8));
        Assert.Equal((1, 4), SingleLitPixel(Decode(mirrorY), 8));
        using var output = new MemoryStream();
        PhotonWorkshopWriter.Write(plain, output);
        Assert.Equal(517u, PhotonWorkshopFile.Read(output.ToArray()).Version);
    }

    private static byte[] Decode(SliceResult result)
    {
        var pixels = new byte[result.Printer.ResolutionX * result.Printer.ResolutionY];
        result.Layers[0].Decode(result.Printer.ResolutionX, result.Printer.ResolutionY, pixels);
        return pixels;
    }

    private static (int X, int Y) SingleLitPixel(byte[] pixels, int width)
    {
        var index = Assert.Single(Enumerable.Range(0, pixels.Length), i => pixels[i] != 0);
        return (index % width, index / width);
    }

    [Fact]
    public void RefusesGeometryBelowPlate()
    {
        var obj = new SceneObject("box", Box(10, 10, 10));
        obj.Transform = Transform.Identity with { Translation = new Vector3(0, 0, -1) };
        Assert.Throws<InvalidOperationException>(() => Slicer.Slice(new[] { obj }, Printer, PrintSettings.Default));
    }

    [Fact]
    public void RefusesGeometryOutsideBuildArea()
    {
        // Wider than the 192 mm plate even when centred.
        var wide = new SceneObject("wide", Box(300, 10, 10));
        wide.Transform = Transform.Identity with { Translation = new Vector3(-150, -5, 0) };
        var ex = Assert.Throws<InvalidOperationException>(() => Slicer.Slice(new[] { wide }, Printer, PrintSettings.Default));
        Assert.Contains("plate", ex.Message);

        // Fits in size, but shifted off the plate edge.
        var shifted = new SceneObject("shifted", Box(10, 10, 10));
        shifted.Transform = Transform.Identity with { Translation = new Vector3(90, -5, 0) };
        Assert.Throws<InvalidOperationException>(() => Slicer.Slice(new[] { shifted }, Printer, PrintSettings.Default));

        // Centred and inside: fine.
        var ok = new SceneObject("ok", Box(10, 10, 10));
        ok.Transform = Transform.Identity with { Translation = new Vector3(-5, -5, 0) };
        Assert.NotNull(Slicer.Slice(new[] { ok }, Printer, PrintSettings.Default));
    }

    [Fact]
    public void PermissiveSliceCropsEveryViolatedAxisAndReportsWarning()
    {
        var printer = Printer with
        {
            DisplayWidthMm = 20,
            DisplayHeightMm = 10,
            ZTravelMm = 0.1f,
            ResolutionX = 20,
            ResolutionY = 10,
        };
        var obj = new SceneObject("outside", Box(30, 20, 0.2f));
        obj.Transform = Transform.Identity with { Translation = new Vector3(-15, -10, -0.05f) };

        var result = Slicer.Slice([obj], printer, PrintSettings.Default,
            allowOutOfBounds: true);

        Assert.Equal(2, result.LayerCount);
        Assert.All(result.Layers, layer => Assert.True(layer.LitPixels > 0));
        Assert.Equal(BuildVolumeViolationAxes.X | BuildVolumeViolationAxes.Y | BuildVolumeViolationAxes.Z,
            result.CroppedAxes);
        Assert.Equal("Warning: content outside the build area on X, Y and Z was cropped.",
            result.BuildVolumeWarning);
    }
}
