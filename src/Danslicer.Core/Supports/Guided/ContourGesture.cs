using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Supports.Generation;

namespace Danslicer.Core.Supports.Guided;

/// <summary>
/// The contour gesture (SUPPORT-GEOMETRY-SPEC "Guided tip placement"): the cursor's height on
/// the surface chooses a Z, the target's downward contour at that height is shown with tips at
/// pitch along each run, and one click places them.
/// </summary>
public sealed class ContourGesture : IGuidedGesture
{
    private readonly Mesh _mesh;
    private float _pitch;
    private float? _z;
    private IReadOnlyList<SurfacePath> _runs = [];

    public ContourGesture(Mesh worldMesh, float pitchMm)
    {
        _mesh = worldMesh;
        _pitch = Math.Clamp(pitchMm, SurfaceLineGesture.MinPitchMm, SurfaceLineGesture.MaxPitchMm);
    }

    public string Name => "Support contour";
    public bool ReadyToPlace => _runs.Count > 0;
    public string Hint => _z is null ? "hover the model to choose a height"
        : _runs.Count == 0 ? $"nothing faces down at Z {_z:0.0}"
        : $"LMB place at Z {_z:0.0}";
    public float PitchMm => _pitch;
    /// <summary>The height under the cursor, or null off the model.</summary>
    public float? HeightMm => _z;
    public IReadOnlyList<(Vector3 Point, int Face)> Vertices => [];
    public bool HasVertices => false;
    public bool CursorPathIsChord => false;

    public void SetPitch(float pitchMm) =>
        _pitch = Math.Clamp(pitchMm, SurfaceLineGesture.MinPitchMm, SurfaceLineGesture.MaxPitchMm);

    public void StepPitch(int notches)
    {
        var step = MathF.Max(0.1f, MathF.Round(_pitch * 0.1f, 1));
        SetPitch(_pitch + notches * step);
    }

    public bool AddVertex() => _runs.Count > 0;
    public bool RemoveLastVertex() => false;

    public void SetCursor(Vector3 point, int face) => SetCursor(point, face, null);

    public void SetCursor(Vector3 point, int face, SurfacePath? bridge)
    {
        if (_z is { } z && MathF.Abs(z - point.Z) < 1e-5f) return;
        _z = point.Z;
        _runs = SurfaceContour.AtHeight(_mesh, point.Z);
    }

    public void ClearCursor()
    {
        _z = null;
        _runs = [];
    }

    public IReadOnlyList<SurfacePath> Route() => _runs;

    public IReadOnlyList<TipCandidate> Preview(TipPlacementParameters parameters, SupportGraph? existing)
    {
        if (_runs.Count == 0) return [];
        var samples = new List<(Vector3 Point, int Face)>();
        foreach (var run in _runs)
            samples.AddRange(SurfacePath.SampleAtPitch([run], _pitch, parameters.MinSpacingMm));
        return GuidedTipPlacement.Candidates(_mesh, samples, parameters, existing);
    }
}
