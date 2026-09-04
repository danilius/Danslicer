using Danslicer.App.ViewModels;

namespace Danslicer.Tests;

public sealed class UvtoolsLauncherTests
{
    [Fact]
    public void NoSessionExportDisablesCheckWithExplanation()
    {
        var availability = UvtoolsLauncher.GetAvailability(null, _ => true);

        Assert.Equal(UvtoolsExportState.NoExport, availability.State);
        Assert.False(availability.IsEnabled);
        Assert.Contains("Export", availability.Tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingSessionExportDisablesCheckWithExplanation()
    {
        var availability = UvtoolsLauncher.GetAvailability("missing.pwmx", _ => false);

        Assert.Equal(UvtoolsExportState.ExportMissing, availability.State);
        Assert.False(availability.IsEnabled);
        Assert.Contains("no longer exists", availability.Tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingSessionExportEnablesCheck()
    {
        var availability = UvtoolsLauncher.GetAvailability("ready.pwmx", _ => true);

        Assert.Equal(UvtoolsExportState.Ready, availability.State);
        Assert.True(availability.IsEnabled);
    }

    [Fact]
    public void LaunchPlanPassesExportPathAsOneUnquotedArgument()
    {
        var startInfo = UvtoolsLauncher.CreateStartInfo(
            @" C:\Program Files\UVtools\UVtools.exe ",
            @"C:\prints\dragon test.pwmx");

        Assert.Equal(@"C:\Program Files\UVtools\UVtools.exe", startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal([@"C:\prints\dragon test.pwmx"], startInfo.ArgumentList);
        Assert.Empty(startInfo.Arguments);
    }
}
