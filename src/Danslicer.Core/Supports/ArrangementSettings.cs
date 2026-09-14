using Danslicer.Core.Config;

namespace Danslicer.Core.Supports;

/// <summary>Arrangement choices are independent of contact, member, placement and base presets.</summary>
public static class ArrangementSettings
{
    public static void CopyTo(SupportConfig source, SupportConfig target, bool bracing)
    {
        foreach (var property in typeof(SupportConfig).GetProperties())
        {
            var name = property.Name;
            if (name == nameof(SupportConfig.ParentingHierarchical)) continue;
            var belongs = bracing ? name.StartsWith("Bracing", StringComparison.Ordinal) || name == nameof(SupportConfig.AutoBracing)
                : name.StartsWith("Parenting", StringComparison.Ordinal) || name.StartsWith("Candelabra", StringComparison.Ordinal) || name == nameof(SupportConfig.AutoParenting);
            if (belongs && property.CanWrite) property.SetValue(target, property.GetValue(source));
        }
    }
}
