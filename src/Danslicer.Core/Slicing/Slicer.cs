using System.Buffers;
using Danslicer.Core.IO;
using Danslicer.Core.Printers;
using Danslicer.Core.Scene;

namespace Danslicer.Core.Slicing;

/// <summary>One sliced layer, stored run-length encoded so a full print fits in memory.</summary>
public sealed class SlicedLayer
{
    public required byte[] Rle { get; init; }
    public required uint LitPixels { get; init; }
    public required float AreaMm2 { get; init; }
    /// <summary>Top of the layer in millimetres.</summary>
    public required float Z { get; init; }

    public void Decode(int width, int height, Span<byte> pixels) => PhotonRle.Decode(Rle, pixels);
}

public sealed class SliceResult
{
    public required PrinterDefinition Printer { get; init; }
    public required PrintSettings Settings { get; init; }
    public required ResinSettings ResinSettings { get; init; }
    public required IReadOnlyList<SlicedLayer> Layers { get; init; }
    public required float VolumeMl { get; init; }
    /// <summary>224 x 168 RGB565 thumbnail for the printer's file browser.</summary>
    public required byte[] Preview { get; init; }
    public required float MinX { get; init; }
    public required float MinY { get; init; }
    public required float MaxX { get; init; }
    public required float MaxY { get; init; }

    public int LayerCount => Layers.Count;
    public float PrintHeight => Layers.Count * Settings.LayerHeight;
    public double EstimatedSeconds => ResinSettings.EstimatePrintTime(Layers.Count);
}

public static class Slicer
{
    public const int PreviewWidth = 224;
    public const int PreviewHeight = 168;

    /// <summary>
    /// Slices all visible objects into layers, plus the support graph's analytic sections when one
    /// is given. Throws if any object sits below the plate or nothing is sliceable. Layers are
    /// processed in parallel and encoded immediately.
    /// </summary>
    public static SliceResult Slice(
        IEnumerable<SceneObject> objects,
        PrinterDefinition printer,
        PrintSettings settings,
        IProgress<double>? progress = null,
        CancellationToken cancellation = default,
        Supports.SupportGraph? supports = null,
        ResinSettings? resinSettings = null)
    {
        resinSettings = (resinSettings ?? ResinSettings.Default).Normalize();
        var prepared = new List<MeshSlicer.PreparedMesh>();
        foreach (var obj in objects)
        {
            if (obj.RenderState == RenderState.Hidden) continue;
            prepared.Add(new MeshSlicer.PreparedMesh(obj.Mesh, obj.Transform.ToMatrix()));
        }
        if (prepared.Count == 0) throw new InvalidOperationException("Nothing to slice.");

        var minZ = prepared.Min(m => m.MinZ);
        var maxZ = prepared.Max(m => m.MaxZ);

        // Supports extend the print height up to their cap tops; sections below the plate are
        // simply never sliced (layers start at zero), matching how bases rest on the plate.
        if (supports is not null)
        {
            foreach (var segment in supports.Segments)
            {
                if (segment.Disabled) continue;
                var top = Math.Max(supports.GetNode(segment.NodeA).Position.Z, supports.GetNode(segment.NodeB).Position.Z)
                          + segment.Diameter * 0.5;
                if (top > maxZ) maxZ = top;
            }
        }
        if (minZ < -1e-3) throw new InvalidOperationException($"Geometry extends {-minZ:0.###} mm below the plate.");
        if (maxZ > printer.BuildVolume.Z + 1e-3) throw new InvalidOperationException($"Geometry exceeds the {printer.BuildVolume.Z} mm build height.");

        // The plate is the LCD, centred on the origin: anything outside would be silently cropped.
        var halfX = printer.BuildVolume.X / 2.0;
        var halfY = printer.BuildVolume.Y / 2.0;
        var overX = Math.Max(prepared.Max(m => m.MaxX) - halfX, -halfX - prepared.Min(m => m.MinX));
        var overY = Math.Max(prepared.Max(m => m.MaxY) - halfY, -halfY - prepared.Min(m => m.MinY));
        if (overX > 1e-3 || overY > 1e-3)
        {
            var axes = string.Join(" and ", new[]
            {
                overX > 1e-3 ? $"{overX:0.#} mm in X" : null,
                overY > 1e-3 ? $"{overY:0.#} mm in Y" : null,
            }.Where(s => s is not null));
            throw new InvalidOperationException(
                $"Geometry extends past the plate by {axes} " +
                $"(build area {printer.BuildVolume.X:0.#} × {printer.BuildVolume.Y:0.#} mm). Scale, rotate or move it to fit.");
        }

        var h = (double)settings.LayerHeight;
        var layerCount = (int)Math.Ceiling(maxZ / h - 1e-6);
        if (layerCount <= 0) throw new InvalidOperationException("Model has no height.");

        var buckets = prepared.Select(m => MeshSlicer.BucketTriangles(m, h, layerCount)).ToList();

        var layers = new SlicedLayer[layerCount];
        var previewHeights = new int[PreviewWidth * PreviewHeight];
        var previewLock = new object();
        var minX = double.PositiveInfinity; var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity; var maxY = double.NegativeInfinity;
        var boundsLock = new object();
        var pixelCount = printer.ResolutionX * printer.ResolutionY;
        var done = 0;

        Parallel.For(0, layerCount,
            new ParallelOptions { CancellationToken = cancellation },
            () => new Worker(printer, settings),
            (i, _, worker) =>
            {
                var z = (i + 0.5) * h;
                worker.Segments.Clear();
                for (int m = 0; m < prepared.Count; m++)
                    MeshSlicer.CollectSegments(prepared[m], buckets[m][i], z, worker.Segments);

                var loops = MeshSlicer.ChainSegments(worker.Segments);
                if (supports is not null)
                    loops.AddRange(Supports.SupportSliceGeometry.SectionsAt(supports, z));
                var polygons = MeshSlicer.Finish(loops, settings.XyCompensation);
                var lit = worker.Rasterizer.Rasterize(polygons, worker.Pixels);

                layers[i] = new SlicedLayer
                {
                    Rle = PhotonRle.Encode(worker.Pixels.AsSpan(0, pixelCount)),
                    LitPixels = lit,
                    AreaMm2 = (float)MeshSlicer.AreaMm2(polygons),
                    Z = (float)((i + 1) * h),
                };

                if (polygons.Count > 0)
                {
                    var b = Clipper2Lib.Clipper.GetBounds(polygons);
                    lock (boundsLock)
                    {
                        minX = Math.Min(minX, b.left / MeshSlicer.UnitsPerMm);
                        maxX = Math.Max(maxX, b.right / MeshSlicer.UnitsPerMm);
                        minY = Math.Min(minY, b.top / MeshSlicer.UnitsPerMm);
                        maxY = Math.Max(maxY, b.bottom / MeshSlicer.UnitsPerMm);
                    }
                    AccumulatePreview(worker.Pixels, printer, i, previewHeights, previewLock);
                }

                var completed = Interlocked.Increment(ref done);
                progress?.Report(completed / (double)layerCount);
                return worker;
            },
            worker => worker.Dispose());

        double volumeMm3 = 0;
        foreach (var l in layers) volumeMm3 += l.AreaMm2 * h;

        return new SliceResult
        {
            Printer = printer,
            Settings = settings,
            ResinSettings = resinSettings,
            Layers = layers,
            VolumeMl = (float)(volumeMm3 / 1000.0),
            Preview = RenderPreview(previewHeights, layerCount),
            MinX = (float)(double.IsInfinity(minX) ? 0 : minX),
            MinY = (float)(double.IsInfinity(minY) ? 0 : minY),
            MaxX = (float)(double.IsInfinity(maxX) ? 0 : maxX),
            MaxY = (float)(double.IsInfinity(maxY) ? 0 : maxY),
        };
    }

    private sealed class Worker : IDisposable
    {
        public LayerRasterizer Rasterizer { get; }
        public byte[] Pixels { get; }
        public List<MeshSlicer.Segment> Segments { get; } = new();

        public Worker(PrinterDefinition printer, PrintSettings settings)
        {
            Rasterizer = new LayerRasterizer(printer, settings.AntiAliasing);
            Pixels = ArrayPool<byte>.Shared.Rent(printer.ResolutionX * printer.ResolutionY);
        }

        public void Dispose() => ArrayPool<byte>.Shared.Return(Pixels);
    }

    private static void AccumulatePreview(byte[] pixels, PrinterDefinition printer, int layer, int[] heights, object sync)
    {
        // Sample the layer on the preview grid; keep the highest layer that is lit at each cell.
        var local = new List<int>();
        for (int gy = 0; gy < PreviewHeight; gy++)
        {
            var py = (int)((gy + 0.5) * printer.ResolutionY / PreviewHeight);
            var rowOffset = py * printer.ResolutionX;
            for (int gx = 0; gx < PreviewWidth; gx++)
            {
                var px = (int)((gx + 0.5) * printer.ResolutionX / PreviewWidth);
                if (pixels[rowOffset + px] > 127) local.Add(gy * PreviewWidth + gx);
            }
        }
        if (local.Count == 0) return;
        lock (sync)
        {
            foreach (var idx in local)
                if (layer + 1 > heights[idx]) heights[idx] = layer + 1;
        }
    }

    private static byte[] RenderPreview(int[] heights, int layerCount)
    {
        var data = new byte[PreviewWidth * PreviewHeight * 2];
        for (int i = 0; i < heights.Length; i++)
        {
            byte r, g, b;
            if (heights[i] == 0)
            {
                r = 40; g = 42; b = 46;
            }
            else
            {
                var t = heights[i] / (float)Math.Max(layerCount, 1);
                r = (byte)(90 + 150 * t);
                g = (byte)(140 + 100 * t);
                b = (byte)(200 + 55 * t);
            }
            var rgb565 = (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));
            data[i * 2] = (byte)rgb565;
            data[i * 2 + 1] = (byte)(rgb565 >> 8);
        }
        return data;
    }
}
