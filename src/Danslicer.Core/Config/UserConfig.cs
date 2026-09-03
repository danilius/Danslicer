using System.Text.Json;
using System.Text.Json.Serialization;

namespace Danslicer.Core.Config;

/// <summary>
/// SpaceMouse navigation tuning. Sensitivities are multipliers on the app's base step sizes, so
/// 1.0 reproduces the untuned behaviour; inversion flags flip individual motion axes.
/// </summary>
public sealed class SpaceMouseConfig
{
    public float OrbitSensitivity { get; set; } = 1f;
    public float PanSensitivity { get; set; } = 1f;
    public float ZoomSensitivity { get; set; } = 1f;
    public bool InvertOrbitYaw { get; set; }
    public bool InvertOrbitPitch { get; set; }
    public bool InvertPanX { get; set; }
    public bool InvertPanY { get; set; }
    public bool InvertZoom { get; set; }
    /// <summary>Fraction of full deflection below which motion is ignored.</summary>
    public float Deadzone { get; set; } = 0.001f;
}

/// <summary>Viewport display tuning.</summary>
public sealed class ViewportConfig
{
    /// <summary>Overhang tint threshold, degrees from the vertical wall.</summary>
    public float OverhangAngleDegrees { get; set; } = 45f;

    /// <summary>Build-plate opacity when the camera is below it: 0 invisible, 1 fully opaque.</summary>
    public float PlateOpacityFromBelow { get; set; } = 0.3f;
}

/// <summary>
/// User configuration persisted as JSON in the user profile. Unknown properties in the file are
/// ignored and missing ones keep their defaults, so the file survives version changes in both
/// directions. A corrupt or unreadable file yields defaults rather than an error: configuration
/// must never stop the app from starting.
/// </summary>
public sealed class UserConfig
{
    public SpaceMouseConfig SpaceMouse { get; set; } = new();
    public ViewportConfig Viewport { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Danslicer", "config.json");

    public static UserConfig Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new UserConfig();
            return JsonSerializer.Deserialize<UserConfig>(File.ReadAllText(path), JsonOptions)
                   ?? new UserConfig();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new UserConfig();
        }
    }

    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
