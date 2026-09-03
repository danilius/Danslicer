using System.Numerics;

namespace Danslicer.Core.Supports.Generation;

/// <summary>Which stage-1 strategy produced a candidate. Routing never sees this; it is bookkeeping.</summary>
public enum TipStrategy
{
    /// <summary>Layer island: material with nothing below it. Placed first, regardless of density.</summary>
    Island,
    /// <summary>Local Z-minimum of the surface. Placed first, regardless of density.</summary>
    LocalMinimum,
    /// <summary>Poisson-disk sample on an overhang face.</summary>
    Overhang,
    /// <summary>Sample on a sharp edge (CAD preference).</summary>
    Edge,
    /// <summary>Sample on a sharp corner (CAD preference).</summary>
    Corner,
    /// <summary>
    /// Lattice vertical from the base grid (DESIGN.md §8.4 strategy 4) hitting a downward
    /// region face. XY matches the bases the grid router will choose.
    /// </summary>
    GridProjection,
}

/// <summary>
/// A contact the routing stage may turn into a tip node. <see cref="InwardNormal"/> points into
/// the solid (the opposite of the outward face normal); the tip penetrates along this direction.
/// </summary>
public readonly record struct TipCandidate(
    Vector3 Point,
    Vector3 InwardNormal,
    float TipDiameter,
    float Score,
    TipStrategy Strategy,
    int FaceIndex,
    SupportTipShape TipShape = SupportTipShape.Capsule,
    float ConeLength = 2f,
    float BallDiameter = 0f);
