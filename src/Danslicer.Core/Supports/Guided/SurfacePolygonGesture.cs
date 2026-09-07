using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Supports.Generation;

namespace Danslicer.Core.Supports.Guided;

/// <summary>
/// The polygon-fill gesture (SUPPORT-GEOMETRY-SPEC "Guided tip placement"): corners clicked in
/// turn, the loop closed back to the first corner, and the region it encloses filled with the
/// painted-region grid at the gesture's pitch. Tips also run along the boundary at pitch, so
/// the region's edge — where an overhang matters most — is always held.
///
/// <para>Built on <see cref="SurfaceLineGesture"/> for the open route; this class adds the
/// closing path and the fill. With fewer than three corners it previews and places exactly what
/// the line would, so a two-click polygon is not a surprise.</para>
/// </summary>
public sealed class SurfacePolygonGesture : IGuidedGesture
{
    private readonly Mesh _mesh;
    private readonly SurfaceLineGesture _line;
    private SurfacePath? _closing;

    public SurfacePolygonGesture(Mesh worldMesh, float pitchMm)
    {
        _mesh = worldMesh;
        _line = new SurfaceLineGesture(worldMesh, pitchMm);
    }

    public string Name => "Support polygon";
    public bool PlacesOnClick => false;
    public float PitchMm => _line.PitchMm;
    public IReadOnlyList<(Vector3 Point, int Face)> Vertices => _line.Vertices;
    public bool HasVertices => _line.HasVertices;
    public bool CursorPathIsChord => _line.CursorPathIsChord;
    /// <summary>True when the closing edge back to the first corner could not follow the surface.</summary>
    public bool ClosingPathIsChord { get; private set; }

    /// <summary>How many corners the loop has now: the clicked ones plus the cursor when it adds one.</summary>
    public int CornerCount => _line.Vertices.Count + (LiveCursorAddsCorner() ? 1 : 0);

    public void SetPitch(float pitchMm) => _line.SetPitch(pitchMm);
    public void StepPitch(int notches) => _line.StepPitch(notches);

    public bool AddVertex()
    {
        var added = _line.AddVertex();
        UpdateClosing();
        return added;
    }

    public bool RemoveLastVertex()
    {
        var removed = _line.RemoveLastVertex();
        UpdateClosing();
        return removed;
    }

    public void SetCursor(Vector3 point, int face) => SetCursor(point, face, null);

    public void SetCursor(Vector3 point, int face, SurfacePath? bridge)
    {
        _line.SetCursor(point, face, bridge);
        UpdateClosing();
    }

    public void ClearCursor()
    {
        _line.ClearCursor();
        UpdateClosing();
    }

    /// <summary>The open route plus, once the loop has three corners, the closing edge.</summary>
    public IReadOnlyList<SurfacePath> Route()
    {
        var route = new List<SurfacePath>(_line.Route());
        if (_closing is not null) route.Add(_closing);
        return route;
    }

    public IReadOnlyList<TipCandidate> Preview(TipPlacementParameters parameters, SupportGraph? existing)
    {
        if (_closing is null) return _line.Preview(parameters, existing);

        var loop = Route();
        var boundary = SurfacePath.SampleAtPitch(loop, PitchMm, parameters.MinSpacingMm);
        var ring = SurfacePolygon.RingFaces(loop);
        var faces = SurfacePolygon.EnclosedFaces(_mesh, ring);
        var inside = SurfacePolygon.InsideLoop(loop);
        var grid = RegionGridSampler.Sample(_mesh, faces, parameters with
        {
            RegionGrid = new RegionGridOptions { VerticalPitchMm = PitchMm, HorizontalPitchMm = PitchMm },
        });
        // The boundary first, at its own spacing along the loop; then the grid, which keeps a
        // full spacing from the boundary tips so the edge row is not doubled.
        var edge = GuidedTipPlacement.Candidates(_mesh, boundary, parameters, existing);
        var fill = GuidedTipPlacement.Candidates(_mesh,
            grid.Where(c => inside(c.Point)).Select(c => (c.Point, c.FaceIndex)).ToList(),
            parameters, existing, duplicateRadiusMm: parameters.MinSpacingMm,
            keepClearOf: edge.Select(c => c.Point).ToList());
        return edge.Concat(fill).ToList();
    }

    /// <summary>The cursor is a corner of its own when it sits away from the last clicked one.</summary>
    private bool LiveCursorAddsCorner() =>
        _line.Cursor is { } cursor && _line.Vertices.Count > 0 &&
        Vector3.DistanceSquared(cursor.Point, _line.Vertices[^1].Point) > 1e-8f;

    private void UpdateClosing()
    {
        _closing = null;
        ClosingPathIsChord = false;
        if (CornerCount < 3) return;
        var first = _line.Vertices[0];
        var last = LiveCursorAddsCorner() ? _line.Cursor!.Value : _line.Vertices[^1];
        var closing = SurfacePath.Between(_mesh, last.Point, last.Face, first.Point, first.Face);
        if (closing is null)
        {
            closing = SurfacePath.Chord(last.Point, last.Face, first.Point, first.Face);
            ClosingPathIsChord = last.Face != first.Face;
        }
        _closing = closing;
    }
}
