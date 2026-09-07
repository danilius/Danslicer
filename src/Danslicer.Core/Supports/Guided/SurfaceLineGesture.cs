using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Supports.Generation;

namespace Danslicer.Core.Supports.Guided;

/// <summary>
/// The line / polyline gesture (SUPPORT-GEOMETRY-SPEC "Guided tip placement"): vertices the user
/// has clicked, the rubber band from the last vertex to the cursor, and the tips that would be
/// placed along the whole route at the current pitch. Pure state — no viewport, no document —
/// so the viewport only feeds it surface picks and draws what it reports.
///
/// <para>The mesh is the target object's mesh in world space; picks are world points with the
/// face index they hit. When the vertical-plane path between two picks fails, the caller may
/// supply a bridging path of its own (screen-projected) through
/// <see cref="SetCursor(Vector3, int, SurfacePath?)"/>; with none, the rubber band is a straight
/// chord, and the preview shows it as it is so the user sees the line has left the surface.</para>
/// </summary>
public sealed class SurfaceLineGesture : IGuidedGesture
{
    public const float MinPitchMm = 0.5f;
    public const float MaxPitchMm = 50f;

    private readonly Mesh _mesh;
    private readonly List<(Vector3 Point, int Face)> _vertices = [];
    private readonly List<SurfacePath> _segments = [];
    private (Vector3 Point, int Face)? _cursor;
    private SurfacePath? _cursorPath;

    public SurfaceLineGesture(Mesh worldMesh, float pitchMm)
    {
        _mesh = worldMesh;
        PitchMm = Math.Clamp(pitchMm, MinPitchMm, MaxPitchMm);
    }

    public string Name => "Support line";
    public bool ReadyToPlace => false;
    public string Hint => "LMB add point · double-click/Enter place · Backspace remove point";
    public float PitchMm { get; private set; }
    public IReadOnlyList<(Vector3 Point, int Face)> Vertices => _vertices;
    public bool HasVertices => _vertices.Count > 0;
    /// <summary>Where the rubber band currently ends; null while the cursor is off the surface.</summary>
    public (Vector3 Point, int Face)? Cursor => _cursor;
    /// <summary>True when the live rubber band could not follow the surface and is a straight chord.</summary>
    public bool CursorPathIsChord { get; private set; }

    public void SetPitch(float pitchMm) => PitchMm = Math.Clamp(pitchMm, MinPitchMm, MaxPitchMm);

    /// <summary>Scroll-wheel stepping: a tenth of the pitch per notch, at least 0.1 mm.</summary>
    public void StepPitch(int notches)
    {
        var step = MathF.Max(0.1f, MathF.Round(PitchMm * 0.1f, 1));
        SetPitch(PitchMm + notches * step);
    }

    /// <summary>Adds a vertex where the rubber band ends. False when the cursor is off the surface.</summary>
    public bool AddVertex()
    {
        if (_cursor is not { } cursor) return false;
        if (_vertices.Count > 0)
        {
            // A click on the last vertex again is a no-op, not a zero-length segment.
            if (Vector3.DistanceSquared(_vertices[^1].Point, cursor.Point) < 1e-8f) return false;
            _segments.Add(_cursorPath ?? Chord(_vertices[^1], cursor));
        }
        _vertices.Add(cursor);
        _cursorPath = SurfacePath.Single(cursor.Point, cursor.Face);
        CursorPathIsChord = false;
        return true;
    }

    /// <summary>Backspace: drops the last vertex; the rubber band now leaves the one before it.</summary>
    public bool RemoveLastVertex()
    {
        if (_vertices.Count == 0) return false;
        _vertices.RemoveAt(_vertices.Count - 1);
        if (_segments.Count > 0) _segments.RemoveAt(_segments.Count - 1);
        if (_cursor is { } cursor) SetCursor(cursor.Point, cursor.Face);
        return true;
    }

    /// <summary>Moves the rubber band's end.</summary>
    public void SetCursor(Vector3 point, int face) => SetCursor(point, face, null);

    /// <summary>
    /// Moves the rubber band's end, taking <paramref name="bridge"/> as the path from the last
    /// vertex when the vertical-plane path cannot be found.
    /// </summary>
    public void SetCursor(Vector3 point, int face, SurfacePath? bridge)
    {
        _cursor = (point, face);
        CursorPathIsChord = false;
        if (_vertices.Count == 0)
        {
            _cursorPath = SurfacePath.Single(point, face);
            return;
        }
        var last = _vertices[^1];
        var surface = SurfacePath.Between(_mesh, last.Point, last.Face, point, face) ?? bridge;
        if (surface is null)
        {
            surface = Chord(last, (point, face));
            CursorPathIsChord = last.Face != face;
        }
        _cursorPath = surface;
    }

    /// <summary>The cursor has left the surface: the rubber band ends at the last vertex.</summary>
    public void ClearCursor()
    {
        _cursor = null;
        _cursorPath = null;
        CursorPathIsChord = false;
    }

    /// <summary>Every committed segment plus the live rubber band, for drawing and sampling.</summary>
    public IReadOnlyList<SurfacePath> Route()
    {
        var route = new List<SurfacePath>(_segments);
        if (_vertices.Count > 0 && _cursorPath is { Points.Count: > 1 } live) route.Add(live);
        return route;
    }

    /// <summary>
    /// The tips the gesture would place now: along the committed segments and the rubber band,
    /// so Enter with the cursor still on the surface takes the cursor as the last vertex and a
    /// two-click line needs no third action. Before the first vertex, the single tip under the
    /// cursor.
    /// </summary>
    public IReadOnlyList<TipCandidate> Preview(TipPlacementParameters parameters, SupportGraph? existing)
    {
        if (_vertices.Count == 0)
            return _cursor is { } cursor
                ? GuidedTipPlacement.Candidates(_mesh, [cursor], parameters, existing)
                : [];
        var route = Route();
        var samples = route.Count == 0
            ? [_vertices[0]]
            : SurfacePath.SampleAtPitch(route, PitchMm, parameters.MinSpacingMm);
        return GuidedTipPlacement.Candidates(_mesh, samples, parameters, existing);
    }

    private static SurfacePath Chord((Vector3 Point, int Face) from, (Vector3 Point, int Face) to) =>
        SurfacePath.Chord(from.Point, from.Face, to.Point, to.Face);
}
