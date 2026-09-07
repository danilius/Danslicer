using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Supports.Generation;

namespace Danslicer.Core.Supports.Guided;

/// <summary>
/// The ring gesture (SUPPORT-GEOMETRY-SPEC "Guided tip placement"): a click sets the centre,
/// the cursor sets the radius, the next click places tips at pitch around the circumference.
/// The circle is drawn in the tangent plane of the centre's face and each point projected to
/// the nearest point of the surface, so on a curved underside the ring hugs the surface rather
/// than floating off it.
/// </summary>
public sealed class SurfaceRingGesture : IGuidedGesture
{
    private const int MinSegments = 24;
    private const int MaxSegments = 256;

    private readonly Mesh _mesh;
    private readonly TriangleBvh _bvh;
    private float _pitch;
    private (Vector3 Point, int Face)? _centre;
    private (Vector3 Point, int Face)? _cursor;
    private SurfacePath? _ring;

    public SurfaceRingGesture(Mesh worldMesh, float pitchMm)
    {
        _mesh = worldMesh;
        _bvh = MeshAnalysis.For(worldMesh).Bvh;
        _pitch = Math.Clamp(pitchMm, SurfaceLineGesture.MinPitchMm, SurfaceLineGesture.MaxPitchMm);
    }

    public string Name => "Support ring";
    public bool ReadyToPlace => _centre is not null && _ring is not null;
    public string Hint => _centre is null ? "LMB set centre"
        : _ring is null ? "move out to set the radius · Backspace re-centre"
        : "LMB place · Backspace re-centre";
    public float PitchMm => _pitch;
    public IReadOnlyList<(Vector3 Point, int Face)> Vertices => _centre is { } c ? [c] : [];
    public bool HasVertices => _centre is not null;
    public bool CursorPathIsChord => false;
    public float RadiusMm => _centre is { } c && _cursor is { } k ? Vector3.Distance(c.Point, k.Point) : 0f;

    public void SetPitch(float pitchMm)
    {
        _pitch = Math.Clamp(pitchMm, SurfaceLineGesture.MinPitchMm, SurfaceLineGesture.MaxPitchMm);
        Rebuild();
    }

    public void StepPitch(int notches)
    {
        var step = MathF.Max(0.1f, MathF.Round(_pitch * 0.1f, 1));
        SetPitch(_pitch + notches * step);
    }

    public bool AddVertex()
    {
        if (_cursor is not { } cursor) return false;
        if (_centre is null)
        {
            _centre = cursor;
            Rebuild();
            return true;
        }
        return _ring is not null;
    }

    public bool RemoveLastVertex()
    {
        if (_centre is null) return false;
        _centre = null;
        _ring = null;
        return true;
    }

    public void SetCursor(Vector3 point, int face) => SetCursor(point, face, null);

    public void SetCursor(Vector3 point, int face, SurfacePath? bridge)
    {
        _cursor = (point, face);
        Rebuild();
    }

    public void ClearCursor()
    {
        _cursor = null;
        _ring = null;
    }

    public IReadOnlyList<SurfacePath> Route() => _ring is { } ring ? [ring] : [];

    public IReadOnlyList<TipCandidate> Preview(TipPlacementParameters parameters, SupportGraph? existing)
    {
        if (_ring is { } ring)
            return GuidedTipPlacement.Candidates(_mesh,
                SurfacePath.SampleAtPitch([ring], _pitch, parameters.MinSpacingMm), parameters, existing);
        var single = _centre ?? _cursor;
        return single is { } s
            ? GuidedTipPlacement.Candidates(_mesh, [s], parameters, existing)
            : [];
    }

    private void Rebuild()
    {
        _ring = null;
        if (_centre is not { } centre || _cursor is not { } cursor) return;
        var radius = Vector3.Distance(centre.Point, cursor.Point);
        // Smaller than a pitch across, a ring would be a single tip at best: no ring yet.
        if (radius < _pitch * 0.5f) return;

        var normal = _mesh.FaceNormals[centre.Face];
        var u = Vector3.Normalize(Vector3.Cross(normal,
            MathF.Abs(normal.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX));
        var v = Vector3.Cross(normal, u);
        // Segments no longer than half a pitch, so the chords sit close to the surface.
        var segments = Math.Clamp((int)MathF.Ceiling(MathF.Tau * radius / (_pitch * 0.5f)), MinSegments, MaxSegments);
        var points = new List<Vector3>(segments + 1);
        var faces = new List<int>(segments + 1);
        for (var k = 0; k < segments; k++)
        {
            var angle = k * MathF.Tau / segments;
            var q = centre.Point + (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * radius;
            _bvh.ClosestPoint(q, out var onMesh, out var face);
            points.Add(onMesh);
            faces.Add(face);
        }
        points.Add(points[0]);
        faces.Add(faces[0]);
        var length = 0f;
        for (var i = 0; i + 1 < points.Count; i++) length += Vector3.Distance(points[i], points[i + 1]);
        _ring = new SurfacePath(points, faces, length);
    }
}
