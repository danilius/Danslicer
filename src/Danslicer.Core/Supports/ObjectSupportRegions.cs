namespace Danslicer.Core.Supports;

/// <summary>
/// The support region painted onto one object: which of its mesh faces generation may place
/// contacts on, and which faces must be left clean. Stage 1 of DESIGN 8.3 — the model and the
/// overlay. The selection tools that will fill these sets (click-and-grow, facing-down, brush)
/// are stages 2 and 3; nothing here should need reshaping to add them.
///
/// <para><b>An empty region means "all faces", not "no faces".</b> That is the compatibility
/// guard: an object nobody has painted generates exactly as it did before regions existed, and
/// generation is bit-identical. Only <see cref="KeepCleanFaces"/> subtracts.</para>
///
/// <para>Face indices are triangle indices into the object's own mesh, so they are only valid
/// for the mesh they were painted on. Anything that replaces an object's geometry — mirror bakes
/// a reflected mesh, for instance — invalidates them.</para>
/// </summary>
public sealed record ObjectSupportRegions
{
    public static readonly ObjectSupportRegions Empty = new();

    /// <summary>Faces generation may use. Empty means every face, i.e. today's behaviour.</summary>
    public IReadOnlySet<int> Faces { get; init; } = new HashSet<int>();

    /// <summary>Faces that must stay free of contacts, whatever <see cref="Faces"/> says.</summary>
    public IReadOnlySet<int> KeepCleanFaces { get; init; } = new HashSet<int>();

    public bool IsEmpty => Faces.Count == 0 && KeepCleanFaces.Count == 0;

    public static ObjectSupportRegions From(IEnumerable<int>? faces, IEnumerable<int>? keepClean)
    {
        var region = faces is null ? new HashSet<int>() : [.. faces];
        var clean = keepClean is null ? new HashSet<int>() : [.. keepClean];
        return region.Count == 0 && clean.Count == 0
            ? Empty
            : new ObjectSupportRegions { Faces = region, KeepCleanFaces = clean };
    }

    /// <summary>
    /// The face set generation should actually run on for a mesh of
    /// <paramref name="triangleCount"/> triangles: the painted region (or every face when none is
    /// painted), minus anything marked keep-clean. Keep-clean is applied here as well as inside
    /// the tip placer so the two can never disagree about a face that is in both sets.
    /// </summary>
    public IReadOnlySet<int> EffectiveFaces(int triangleCount)
    {
        var region = Faces.Count == 0
            ? [.. Enumerable.Range(0, triangleCount)]
            : Faces.Where(face => face >= 0 && face < triangleCount).ToHashSet();
        if (KeepCleanFaces.Count > 0) region.ExceptWith(KeepCleanFaces);
        return region;
    }

    public bool Equals(ObjectSupportRegions? other) =>
        other is not null && Faces.SetEquals(other.Faces) &&
        KeepCleanFaces.SetEquals(other.KeepCleanFaces);

    public override int GetHashCode() => HashCode.Combine(Faces.Count, KeepCleanFaces.Count);
}
