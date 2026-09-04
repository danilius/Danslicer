using System.Diagnostics;

namespace Danslicer.App.ViewModels;

public enum UvtoolsExportState
{
    NoExport,
    ExportMissing,
    Ready,
}

public readonly record struct UvtoolsLaunchAvailability(
    UvtoolsExportState State,
    bool IsEnabled,
    string Tooltip);

/// <summary>
/// Pure launch-state and argument construction for the external UVtools process. Keeping the
/// argument in <see cref="ProcessStartInfo.ArgumentList"/> avoids platform-specific quoting.
/// </summary>
public static class UvtoolsLauncher
{
    public static UvtoolsLaunchAvailability GetAvailability(
        string? lastExportPath,
        Func<string, bool>? fileExists = null)
    {
        if (string.IsNullOrWhiteSpace(lastExportPath))
        {
            return new UvtoolsLaunchAvailability(
                UvtoolsExportState.NoExport,
                false,
                "Export a print this session before checking it in UVtools.");
        }

        fileExists ??= File.Exists;
        if (!fileExists(lastExportPath))
        {
            return new UvtoolsLaunchAvailability(
                UvtoolsExportState.ExportMissing,
                false,
                "The most recently exported file no longer exists.");
        }

        return new UvtoolsLaunchAvailability(
            UvtoolsExportState.Ready,
            true,
            "Open the most recently exported print in UVtools.");
    }

    public static ProcessStartInfo CreateStartInfo(string executablePath, string exportedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(exportedPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath.Trim(),
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(exportedPath);
        return startInfo;
    }
}
