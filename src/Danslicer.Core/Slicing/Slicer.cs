using System.Buffers;
using System.Numerics;
using Danslicer.Core.IO;
using Danslicer.Core.Geometry;
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
    /// <summary>RGB565 thumbnail for the printer's file browser, at the printer's preview size.</summary>
    public required byte[] Preview { get; init; }

    /// <summary>
    /// The size the preview was rendered at, taken from the printer. Carried on the result rather
    /// than read from a constant by the writer, so the header can only ever describe the pixels
    /// actually embedded beside it.
    /// </summary>
    public int PreviewWidth { get; init; } = PrinterDefinition.DefaultPreviewWidth;
    public int PreviewHeight { get; init; } = PrinterDefinition.DefaultPreviewHeight;
    public required float MinX { get; init; }
    public required float MinY { get; init; }
    public required float MaxX { get; init; }
    public required float MaxY { get; init; }
    /// <summary>Axes whose out-of-volume content was omitted by permissive slicing.</summary>
    public BuildVolumeViolationAxes CroppedAxes { get; init; }

    public int LayerCount => Layers.Count;
    public float PrintHeight => Layers.Count * Settings.LayerHeight;
    public double EstimatedSeconds => ResinSettings.EstimatePrintTime(Layers.Count);
    public string? BuildVolumeWarning => CroppedAxes == BuildVolumeViolationAxes.None
        ? null
        : BuildVolumeBounds.CroppedWarning(CroppedAxes);
}

public static class Slicer
{
    /// <summary>
    /// The embedded preview is cosmetic. Slicing must never fail because a thumbnail could not be
    /// drawn, so any failure here degrades to a blank preview rather than aborting the slice.
    /// </summary>
    private static byte[] RenderPreviewSafely(
        IReadOnlyList<PreviewRenderer.RenderObject> objects, Supports.SupportGraph? supports,
        int width, int height)
    {
        try
        {
            return PreviewRenderer.Render(objects, supports, width, height);
        }
        catch (Exception ex) when (ex is ArithmeticException or ArgumentException
                                   or IndexOutOfRangeException or InvalidOperationException)
        {
            return PreviewRenderer.Blank(width, height);
        }
    }

    /// <summary>
    /// Slices all visible objects into layers, plus the support graph's analytic sections when one
    /// is given. Unless <paramref name="allowOutOfBounds"/> is set, throws when content exceeds
    /// the build volume. Always throws when nothing is sliceable. Layers are processed in parallel
    /// and encoded immediately.
    /// </summary>
    public static SliceResult Slice(
        IEnumerable<SceneObject> objects,
        PrinterDefinition printer,
        PrintSettings settings,
        IProgress<double>? progress = null,
        CancellationToken cancellation = default,
        Supports.SupportGraph? supports = null,
        ResinSettings? resinSettings = null,
        bool allowOutOfBounds = false)
    {
        resinSettings = (resinSettings ?? ResinSettings.Default).Normalize();
        var prepared = new List<MeshSlicer.PreparedMesh>();
        var previewObjects = new List<PreviewRenderer.RenderObject>();
        // A hidden model is not printed, and neither are its supports: resin holding up something
        // that is not there would be worse than nothing. See SupportOwnerVisibility.
        var hiddenObjectIds = Supports.SupportOwnerVisibility.HiddenObjectIds(objects);
        foreach (var obj in objects)
        {
            if (obj.RenderState == RenderState.Hidden) continue;
            var matrix = obj.Transform.ToMatrix();
            prepared.Add(new MeshSlicer.PreparedMesh(obj.Mesh, matrix));
            previewObjects.Add(new PreviewRenderer.RenderObject(obj.Mesh, matrix));
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
                if (Supports.SupportOwnerVisibility.IsOwnedByHidden(supports, segment.Id, hiddenObjectIds))
                    continue;
                var top = Math.Max(supports.GetNode(segment.NodeA).Position.Z, supports.GetNode(segment.NodeB).Position.Z)
                          + segment.Diameter * 0.5;
                if (top > maxZ) maxZ = top;
            }
        }
        var halfX = printer.BuildVolume.X / 2.0;
        var halfY = printer.BuildVolume.Y / 2.0;
        var overX = Math.Max(prepared.Max(m => m.MaxX) - halfX, -halfX - prepared.Min(m => m.MinX));
        var overY = Math.Max(prepared.Max(m => m.MaxY) - halfY, -halfY - prepared.Min(m => m.MinY));
        var sceneBounds = new Aabb(
            new Vector3((float)prepared.Min(m => m.MinX), (float)prepared.Min(m => m.MinY), (float)minZ),
            new Vector3((float)prepared.Max(m => m.MaxX), (float)prepared.Max(m => m.MaxY), (float)maxZ));
        var croppedAxes = BuildVolumeBounds.Check(sceneBounds, printer.BuildVolume);
        if (!allowOutOfBounds && croppedAxes != BuildVolumeViolationAxes.None)
        {
            if (minZ < -BuildVolumeBounds.ToleranceMm)
                throw new InvalidOperationException($"Geometry extends {-minZ:0.###} mm below the plate.");
            if (maxZ > printer.BuildVolume.Z + BuildVolumeBounds.ToleranceMm)
                throw new InvalidOperationException($"Geometry exceeds the {printer.BuildVolume.Z} mm build height.");

            var axes = string.Join(" and ", new[]
            {
                overX > BuildVolumeBounds.ToleranceMm ? $"{overX:0.#} mm in X" : null,
                overY > BuildVolumeBounds.ToleranceMm ? $"{overY:0.#} mm in Y" : null,
            }.Where(s => s is not null));
            throw new InvalidOperationException(
                $"Geometry extends past the plate by {axes} " +
                $"(build area {printer.BuildVolume.X:0.#} × {printer.BuildVolume.Y:0.#} mm). Scale, rotate or move it to fit.");
        }

        var h = (double)settings.LayerHeight;
        // Permissive slicing crops Z at the printer travel just as the rasterizer crops X/Y at
        // the LCD. Layers always start at the plate, so below-plate geometry is already omitted.
        var printableMaxZ = allowOutOfBounds ? Math.Min(maxZ, printer.BuildVolume.Z) : maxZ;
        var layerCount = (int)Math.Ceiling(printableMaxZ / h - 1e-6);
        if (layerCount <= 0) throw new InvalidOperationException("Model has no height.");

        var buckets = prepared.Select(m => MeshSlicer.BucketTriangles(m, h, layerCount)).ToList();

        var layers = new SlicedLayer[layerCount];
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
                    loops.AddRange(Supports.SupportSliceGeometry.SectionsAt(supports, z,
                        includeSegment: segment => !Supports.SupportOwnerVisibility.IsOwnedByHidden(
                            supports, segment.Id, hiddenObjectIds),
                        includeNode: node => !Supports.SupportOwnerVisibility.IsOwnedByHidden(
                            node, hiddenObjectIds)));
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
            Preview = RenderPreviewSafely(previewObjects, supports,
                printer.PreviewWidth, printer.PreviewHeight),
            PreviewWidth = printer.PreviewWidth,
            PreviewHeight = printer.PreviewHeight,
            MinX = (float)(double.IsInfinity(minX) ? 0 : minX),
            MinY = (float)(double.IsInfinity(minY) ? 0 : minY),
            MaxX = (float)(double.IsInfinity(maxX) ? 0 : maxX),
            MaxY = (float)(double.IsInfinity(maxY) ? 0 : maxY),
            CroppedAxes = croppedAxes,
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
}
