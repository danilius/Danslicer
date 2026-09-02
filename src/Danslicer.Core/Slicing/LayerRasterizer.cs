using Clipper2Lib;
using Danslicer.Core.Printers;

namespace Danslicer.Core.Slicing;

/// <summary>
/// Scanline polygon rasteriser with coverage anti-aliasing. Fills with the non-zero winding rule.
/// Horizontal coverage is exact per span; vertical coverage uses <see cref="SubSamples"/> sub-scanlines.
/// Maps plate millimetres (origin at plate centre, +Y towards the back) to printer pixels, applying
/// the printer's mirror flags.
/// </summary>
public sealed class LayerRasterizer
{
    private struct Edge
    {
        public double X0, Y0, X1, Y1; // Y0 < Y1
        public int Winding;
        public double DxDy;
    }

    private readonly List<Edge> _edges = new();
    private readonly List<(double x, int w)> _crossings = new();
    private readonly List<int> _active = new();
    private readonly float[] _coverage;

    public int Width { get; }
    public int Height { get; }
    public bool AntiAliasing { get; }
    public int SubSamples => AntiAliasing ? 4 : 1;

    private readonly double _pixelsPerUnitX;
    private readonly double _pixelsPerUnitY;
    private readonly double _halfWidthUnits;
    private readonly double _halfDepthUnits;
    private readonly bool _mirrorX;
    private readonly bool _mirrorY;

    public LayerRasterizer(PrinterDefinition printer, bool antiAliasing)
    {
        Width = printer.ResolutionX;
        Height = printer.ResolutionY;
        AntiAliasing = antiAliasing;
        _coverage = new float[Width + 1];
        _pixelsPerUnitX = Width / (printer.BuildVolume.X * MeshSlicer.UnitsPerMm);
        _pixelsPerUnitY = Height / (printer.BuildVolume.Y * MeshSlicer.UnitsPerMm);
        _halfWidthUnits = printer.BuildVolume.X * MeshSlicer.UnitsPerMm * 0.5;
        _halfDepthUnits = printer.BuildVolume.Y * MeshSlicer.UnitsPerMm * 0.5;
        _mirrorX = printer.MirrorX;
        _mirrorY = printer.MirrorY;
    }

    /// <summary>Fills <paramref name="pixels"/> (Width * Height, row-major, 0 = clear) and returns the lit pixel count.</summary>
    public uint Rasterize(Paths64 paths, byte[] pixels)
    {
        if (pixels.Length < Width * Height) throw new ArgumentException("Pixel buffer too small.", nameof(pixels));
        Array.Clear(pixels, 0, Width * Height);
        BuildEdges(paths);
        if (_edges.Count == 0) return 0;

        _edges.Sort((a, b) => a.Y0.CompareTo(b.Y0));
        var minRow = Math.Max(0, (int)Math.Floor(_edges[0].Y0));
        var maxY = double.NegativeInfinity;
        foreach (var e in _edges) if (e.Y1 > maxY) maxY = e.Y1;
        var maxRow = Math.Min(Height - 1, (int)Math.Ceiling(maxY));

        var subSamples = SubSamples;
        // Quantise to the 16 grey levels the printer file stores, so the lit pixel count and the
        // image the printer sees agree exactly.
        var levelScale = 15f / subSamples;
        uint lit = 0;
        var nextEdge = 0;
        _active.Clear();

        for (int row = minRow; row <= maxRow; row++)
        {
            Array.Clear(_coverage, 0, _coverage.Length);
            var rowTouched = false;

            for (int s = 0; s < subSamples; s++)
            {
                var ys = row + (s + 0.5) / subSamples;

                while (nextEdge < _edges.Count && _edges[nextEdge].Y0 <= ys)
                    _active.Add(nextEdge++);
                for (int i = _active.Count - 1; i >= 0; i--)
                    if (_edges[_active[i]].Y1 <= ys) _active.RemoveAt(i);
                if (_active.Count == 0) continue;

                _crossings.Clear();
                foreach (var idx in _active)
                {
                    ref var e = ref System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_edges)[idx];
                    if (e.Y0 > ys) continue; // not yet started at this sub-scanline
                    var x = e.X0 + (ys - e.Y0) * e.DxDy;
                    _crossings.Add((x, e.Winding));
                }
                if (_crossings.Count < 2) continue;
                _crossings.Sort((a, b) => a.x.CompareTo(b.x));

                var winding = 0;
                for (int i = 0; i < _crossings.Count - 1; i++)
                {
                    winding += _crossings[i].w;
                    if (winding == 0) continue;
                    var xa = Math.Max(0, _crossings[i].x);
                    var xb = Math.Min(Width, _crossings[i + 1].x);
                    if (xb <= xa) continue;
                    AddSpan(xa, xb);
                    rowTouched = true;
                }
            }

            if (!rowTouched) continue;
            var rowOffset = row * Width;
            for (int x = 0; x < Width; x++)
            {
                var c = _coverage[x];
                if (c <= 0) continue;
                int level;
                if (AntiAliasing)
                    level = Math.Min(15, (int)MathF.Round(c * levelScale));
                else
                    level = c >= 0.5f ? 15 : 0;
                if (level == 0) continue;
                pixels[rowOffset + x] = (byte)(level * 17);
                lit++;
            }
        }
        return lit;
    }

    private void AddSpan(double xa, double xb)
    {
        var ia = (int)Math.Floor(xa);
        var ib = (int)Math.Floor(xb);
        if (ia == ib)
        {
            _coverage[ia] += (float)(xb - xa);
            return;
        }
        _coverage[ia] += (float)(ia + 1 - xa);
        for (int i = ia + 1; i < ib; i++) _coverage[i] += 1f;
        if (ib < Width) _coverage[ib] += (float)(xb - ib);
    }

    private void BuildEdges(Paths64 paths)
    {
        _edges.Clear();
        foreach (var path in paths)
        {
            var n = path.Count;
            if (n < 3) continue;
            for (int i = 0; i < n; i++)
            {
                var (x0, y0) = ToPixel(path[i]);
                var (x1, y1) = ToPixel(path[(i + 1) % n]);
                if (y0 == y1) continue;
                var winding = 1;
                if (y0 > y1)
                {
                    (x0, x1) = (x1, x0);
                    (y0, y1) = (y1, y0);
                    winding = -1;
                }
                _edges.Add(new Edge { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Winding = winding, DxDy = (x1 - x0) / (y1 - y0) });
            }
        }
    }

    private (double x, double y) ToPixel(Point64 p)
    {
        var x = (p.X + _halfWidthUnits) * _pixelsPerUnitX;
        var y = (_halfDepthUnits - p.Y) * _pixelsPerUnitY;
        if (_mirrorX) x = Width - x;
        if (_mirrorY) y = Height - y;
        return (x, y);
    }
}
