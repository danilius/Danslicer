using System.Numerics;
using Danslicer.Core.Supports.Generation;

namespace Danslicer.Core.Supports.Guided;

/// <summary>
/// What the viewport's modal host needs from any point-by-point guided gesture
/// (SUPPORT-GEOMETRY-SPEC "Guided tip placement"): picks in, a route and ghost tips out. The
/// line and the polygon share one host this way; only the geometry behind them differs.
/// </summary>
public interface IGuidedGesture
{
    /// <summary>Shown in the status line and used as the undo step's name, e.g. "Support line".</summary>
    string Name { get; }

    /// <summary>
    /// True when the next click places: the host adds the click as a vertex and, if that
    /// succeeds, commits at once. Edge follow is ready whenever a crease is under the cursor;
    /// the ring once its centre is set; the line and polygon never (they place on Enter or a
    /// double-click).
    /// </summary>
    bool ReadyToPlace { get; }

    /// <summary>What the left button does right now, for the status line, e.g. "LMB set centre".</summary>
    string Hint { get; }

    float PitchMm { get; }
    void SetPitch(float pitchMm);
    void StepPitch(int notches);

    IReadOnlyList<(Vector3 Point, int Face)> Vertices { get; }
    bool HasVertices { get; }
    /// <summary>True when the live rubber band could not follow the surface and is a straight chord.</summary>
    bool CursorPathIsChord { get; }

    bool AddVertex();
    bool RemoveLastVertex();
    void SetCursor(Vector3 point, int face);
    void SetCursor(Vector3 point, int face, SurfacePath? bridge);
    void ClearCursor();

    /// <summary>Every path to draw: committed segments, the rubber band, and for a polygon the closing edge.</summary>
    IReadOnlyList<SurfacePath> Route();

    /// <summary>The tips the gesture would place now; the same list is committed on Enter.</summary>
    IReadOnlyList<TipCandidate> Preview(TipPlacementParameters parameters, SupportGraph? existing);
}
