using Danslicer.Core.Scene;

namespace Danslicer.Core.Supports;

/// <summary>
/// Support mode works on ONE model at a time — the support target. Every other model on the
/// plate stays an obstacle for routing to avoid, never a surface to grow supports on.
///
/// <para>The rule is written here, once, because three callers need the same answer and they
/// must not drift: generation scopes to the target, manual placement refuses a click on
/// anything else, and the viewport paints the target with the Layout selection colour so the
/// user can see which model they are working on.</para>
///
/// <para>A null target means "no target chosen" and permits everything. That is what keeps
/// single-model documents, the CLI and every pre-existing test behaving exactly as before:
/// the restriction only bites once a target actually exists.</para>
/// </summary>
public static class SupportTargetPolicy
{
    public static bool CanSupport(SceneObject? target, SceneObject candidate) =>
        target is null || ReferenceEquals(target, candidate);

    /// <summary>
    /// The status-bar line for a refused placement, in the same visible-refusal style as the
    /// routing refusals, or null when the placement is allowed.
    /// </summary>
    public static string? RefusalMessage(SceneObject? target, SceneObject candidate) =>
        CanSupport(target, candidate)
            ? null
            : $"Support: {candidate.Name} is not the support target — {target!.Name} is. " +
              "Choose it in Objects to support it instead.";
}
