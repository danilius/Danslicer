using Danslicer.App.Configuration;
using Danslicer.App.ViewModels;
using Danslicer.Core.Utilities;

namespace Danslicer.Tests;

public sealed class WorkspacePreferencesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Danslicer-preferences-test-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "workspace-ui.json");
    public WorkspacePreferencesTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    [Fact]
    public void RoundTripPreservesAllWorkspaceChoicesAndDoesNotTouchConfig()
    {
        var config = Path.Combine(_directory, "config.json");
        File.WriteAllText(config, "unrelated printer, resin and saved cap=false");
        var preferences = new WorkspacePreferences { ShowToolbarLabels = true };
        preferences.PopoutWidths["Support"] = 425;
        preferences.Expanded["Support/Tip"] = false;
        preferences.SectionOrder["Support"] = ["Base", "Tip"];
        preferences.RecentProjects = [Path.Combine(_directory, "missing.danslicer")];
        preferences.Save(FilePath);
        var loaded = WorkspacePreferences.Load(FilePath);
        Assert.True(loaded.ShowToolbarLabels);
        Assert.Equal(425, loaded.Width("Support", 340));
        Assert.False(loaded.Expanded["Support/Tip"]);
        Assert.Equal(new[] { "Base", "Tip" }, loaded.Order("Support", ["Tip", "Base"]));
        Assert.Equal(preferences.RecentProjects, loaded.RecentProjects); // Offline files remain actionable recents.
        Assert.Equal("unrelated printer, resin and saved cap=false", File.ReadAllText(config));
    }
    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"PopoutWidths\":null,\"Expanded\":null,\"SectionOrder\":null,\"RecentProjects\":null}")]
    [InlineData("{\"PopoutWidths\":{\"Support\":\"bad\"}}")]
    public void CorruptionAndNullCollectionsFallBackSafely(string json)
    {
        File.WriteAllText(FilePath, json);
        var preferences = WorkspacePreferences.Load(FilePath);
        Assert.Equal(340, preferences.Width("Support", 340));
        Assert.Equal(new[] { "Tip", "Base" }, preferences.Order("Support", ["Tip", "Base"]));
        preferences.Save(FilePath);
    }
    [Fact]
    public void StaleDuplicateAndReorderedIdentifiersMergeWithAvailableSections()
    {
        var preferences = new WorkspacePreferences();
        preferences.SectionOrder["Support"] = ["Gone", "Base", "Base", null!, "Tip"];
        preferences.PopoutWidths["Support"] = double.NaN;
        Assert.Equal(new[] { "Base", "Tip", "New" }, preferences.Order("Support", ["New", "Tip", "Base"]));
        Assert.Equal(340, preferences.Width("Support", 340));
        Assert.Empty(WorkspacePreferences.Load(FilePath).RecentProjects);
    }
    [Fact]
    public void FutureSchemaIsNotOverwrittenAndUnknownMembersRoundTrip()
    {
        const string future = "{\"Version\":2,\"FutureChoice\":true}";
        File.WriteAllText(FilePath, future);
        WorkspacePreferences.Load(FilePath).Save(FilePath);
        Assert.Equal(future, File.ReadAllText(FilePath));
        File.WriteAllText(FilePath, "{\"FutureChoice\":true}");
        WorkspacePreferences.Load(FilePath).Save(FilePath);
        Assert.Contains("\"FutureChoice\": true", File.ReadAllText(FilePath));
    }
    [Fact]
    public void NumericModelCommitUsesOneExistingApplyAndResynchronizesClamping()
    {
        var applies = 0;
        NumericField? field = null;
        field = new NumericField("Distance", UnitKind.Length, "0.##", value => { applies++; field!.SetValue(Math.Clamp(value, 0, 10)); });
        field.SetValue(2);
        field.CommitValue(15);
        Assert.Equal(1, applies);
        Assert.Equal(10, field.Value);
        Assert.Equal("10", field.Text);
        field.CommitValue(10); field.CommitValue(double.NaN);
        Assert.Equal(1, applies);
    }
}
