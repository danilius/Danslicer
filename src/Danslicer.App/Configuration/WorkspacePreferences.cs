using System.Text.Json;
using System.Text.Json.Serialization;

namespace Danslicer.App.Configuration;

/// <summary>Versioned workspace-only sidecar. Never rewrites the printer/resin/user configuration.</summary>
public sealed class WorkspacePreferences
{
    public int Version { get; set; } = 1;
    public bool ShowToolbarLabels { get; set; }
    public Dictionary<string, double> PopoutWidths { get; set; } = [];
    public Dictionary<string, List<string>> SectionOrder { get; set; } = [];
    public Dictionary<string, bool> Expanded { get; set; } = [];
    public List<string> RecentProjects { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    [JsonIgnore] public bool IsReadOnly => Version != 1;

    public static WorkspacePreferences Load(string path)
    {
        try
        {
            var result = JsonSerializer.Deserialize<WorkspacePreferences>(File.ReadAllText(path)) ?? new();
            result.PopoutWidths ??= []; result.SectionOrder ??= []; result.Expanded ??= [];
            result.RecentProjects = (result.RecentProjects ?? []).Where(p => !string.IsNullOrWhiteSpace(p) && Path.IsPathFullyQualified(p))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
            return result;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return new(); }
    }

    public double Width(string id, double fallback) => PopoutWidths.TryGetValue(id, out var width) && double.IsFinite(width)
        && width >= 240 && width <= 1600 ? width : fallback;

    public IReadOnlyList<string> Order(string group, IEnumerable<string> available)
    {
        var ids = available.Distinct(StringComparer.Ordinal).ToArray();
        var saved = SectionOrder.GetValueOrDefault(group) ?? [];
        return saved.Where(id => id != null && ids.Contains(id, StringComparer.Ordinal))
            .Concat(ids).Distinct(StringComparer.Ordinal).ToArray();
    }

    public void Save(string path)
    {
        if (IsReadOnly) return; // A newer application's schema must not be downgraded.
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
