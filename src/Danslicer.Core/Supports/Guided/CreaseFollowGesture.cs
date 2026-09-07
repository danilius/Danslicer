using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Supports.Generation;

namespace Danslicer.Core.Supports.Guided;

/// <summary>
/// The edge-follow gesture (SUPPORT-GEOMETRY-SPEC "Guided tip placement"): the cursor near a
/// crease shows the traced feature line with tips at pitch outward from the pick; one click
/// places them. No vertices to collect, so <see cref="AddVertex"/> merely confirms the trace and
/// the host commits on the first click (<see cref="PlacesOnClick"/>).
/// </summary>
public sealed class CreaseFollowGesture : IGuidedGesture
{
    /// <summary>Stop the trace where the crease bends more than this at a vertex.</summary>
    public const float MaxTurnDegrees = 60f;
    /// <summary>A face must point down at least this much for a crease tip to aim into it.</summary>
    private const float MinDownwardComponent = 1e-3f;

    private readonly Mesh _mesh;
    private readonly float _sharpEdgeDegrees;
    private CreaseTrace.Result? _trace;
    private float _pitch;

    public CreaseFollowGesture(Mesh worldMesh, float pitchMm, float sharpEdgeDegrees)
    {
        _mesh = worldMesh;
        _sharpEdgeDegrees = sharpEdgeDegrees;
        _pitch = Math.Clamp(pitchMm, SurfaceLineGesture.MinPitchMm, SurfaceLineGesture.MaxPitchMm);
    }

    public string Name => "Support edge";
    public bool PlacesOnClick => true;
    public float PitchMm => _pitch;
    /// <summary>How far from a crease the cursor may be and still snap to it; the host sets it from the zoom.</summary>
    public float SnapDistanceMm { get; set; } = 1.5f;
    public IReadOnlyList<(Vector3 Point, int Face)> Vertices => [];
    public bool HasVertices => false;
    public bool CursorPathIsChord => false;
    /// <summary>The crease under the cursor, or null when none is within snapping distance.</summary>
    public CreaseTrace.Result? Trace => _trace;

    public void SetPitch(float pitchMm) =>
        _pitch = Math.Clamp(pitchMm, SurfaceLineGesture.MinPitchMm, SurfaceLineGesture.MaxPitchMm);

    public void StepPitch(int notches)
    {
        var step = MathF.Max(0.1f, MathF.Round(_pitch * 0.1f, 1));
        SetPitch(_pitch + notches * step);
    }

    public bool AddVertex() => _trace is not null;
    public bool RemoveLastVertex() => false;

    public void SetCursor(Vector3 point, int face) => SetCursor(point, face, null);

    public void SetCursor(Vector3 point, int face, SurfacePath? bridge) =>
        _trace = CreaseTrace.Trace(_mesh, point, face, _sharpEdgeDegrees, SnapDistanceMm, MaxTurnDegrees);

    public void ClearCursor() => _trace = null;

    public IReadOnlyList<SurfacePath> Route() => _trace?.Paths ?? [];

    public IReadOnlyList<TipCandidate> Preview(TipPlacementParameters parameters, SupportGraph? existing)
    {
        if (_trace is not { } trace) return [];
        var samples = new List<(Vector3 Point, int Face)>();
        foreach (var half in trace.Paths)
            samples.AddRange(SurfacePath.SampleAtPitch([half], _pitch, parameters.MinSpacingMm));
        // A crease between a wall and a top face has no underside to aim a tip into.
        samples.RemoveAll(s => _mesh.FaceNormals[s.Face].Z >= -MinDownwardComponent);
        return GuidedTipPlacement.Candidates(_mesh, samples, parameters, existing);
    }
}
