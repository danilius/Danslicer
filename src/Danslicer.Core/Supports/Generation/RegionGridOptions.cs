namespace Danslicer.Core.Supports.Generation;

/// <summary>
/// Pitches for the painted-region grid (<see cref="RegionGridSampler"/>). Vertical is the
/// primary axis by the user's rule: rows must be evenly spaced in Z first, and the horizontal
/// pitch fills each row afterwards.
/// </summary>
public sealed record RegionGridOptions
{
    /// <summary>Z distance between contact rows. Rows are anchored to <see cref="AnchorZ"/>.</summary>
    public float VerticalPitchMm { get; init; } = 2.5f;

    /// <summary>
    /// Distance between contacts along a row, measured along the surface rather than in a
    /// straight line — that is what keeps a curved or tapered wall evenly covered instead of
    /// bunching where a lattice happens to graze it.
    /// </summary>
    public float HorizontalPitchMm { get; init; } = 2.5f;

    /// <summary>
    /// Z the row lattice is anchored to, normally the plate. Anchoring globally rather than to
    /// each patch's own extent keeps rows on neighbouring patches at the same heights, so a
    /// region painted across a corner still reads as one grid.
    /// </summary>
    public float AnchorZ { get; init; }
}
