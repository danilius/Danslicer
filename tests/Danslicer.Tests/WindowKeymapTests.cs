using Danslicer.App.Configuration;
using Danslicer.Core.Config;

namespace Danslicer.Tests;

public sealed class WindowKeymapTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "danslicer-keymap-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Theory]
    [InlineData("Ctrl+Z")]
    [InlineData("Ctrl+Shift+S")]
    [InlineData("Delete")]
    [InlineData("Shift+H")]
    [InlineData("Ctrl+OemComma")]
    public void GestureParseFormatRoundTrips(string text)
    {
        var parsed = WindowKeymap.Parse(text);
        var formatted = WindowKeymap.Format(parsed);
        var reparsed = WindowKeymap.Parse(formatted);

        Assert.Equal(parsed.Key, reparsed.Key);
        Assert.Equal(parsed.KeyModifiers, reparsed.KeyModifiers);
    }

    [Fact]
    public void DefaultsExactlyReproduceTheExistingWindowBindings()
    {
        (string Id, string Gesture)[] expected =
        [
            ("edit.undo", "Ctrl+Z"),
            ("edit.redo", "Ctrl+Shift+Z"),
            ("edit.redo-alternate", "Ctrl+Y"),
            ("edit.delete", "Delete"),
            ("object.duplicate", "Shift+D"),
            ("object.mirror-x", "Ctrl+Shift+1"),
            ("object.mirror-y", "Ctrl+Shift+2"),
            ("object.mirror-z", "Ctrl+Shift+3"),
            ("object.drop-to-plate", "Ctrl+D"),
            ("support.generate", "Ctrl+G"),
            ("edit.select-all", "Ctrl+A"),
            ("support.hide-unselected", "Shift+H"),
            ("print.slice", "Ctrl+R"),
            ("file.save-project", "Ctrl+S"),
            ("file.save-project-as", "Ctrl+Shift+S"),
            ("file.open-project", "Ctrl+O"),
            ("file.import-mesh", "Ctrl+I"),
            ("file.export-print", "Ctrl+E"),
            ("edit.preferences", "Ctrl+OemComma"),
        ];

        Assert.Equal(expected,
            WindowKeymap.Actions.Select(action => (action.Id, action.DefaultGesture)));
    }

    [Fact]
    public void PersistsOnlyOverridesAndDropsAnOverrideWhenResetToDefault()
    {
        var config = new UserConfig();
        Assert.Empty(config.KeymapOverrides);
        Assert.True(WindowKeymap.TrySetGesture(config, WindowKeymap.Undo,
            WindowKeymap.Parse("Alt+Z"), out _));
        var path = Path.Combine(_dir, "config.json");

        config.Save(path);
        var loaded = UserConfig.Load(path);

        var persisted = Assert.Single(loaded.KeymapOverrides);
        Assert.Equal(WindowKeymap.Undo, persisted.Key);
        Assert.Equal("Alt+Z", persisted.Value);
        Assert.True(WindowKeymap.TrySetGesture(loaded, WindowKeymap.Undo,
            WindowKeymap.Parse("Ctrl+Z"), out _));
        Assert.Empty(loaded.KeymapOverrides);
    }

    [Fact]
    public void RejectsAConflictAndNamesTheExistingAction()
    {
        var config = new UserConfig();

        var changed = WindowKeymap.TrySetGesture(config, WindowKeymap.Undo,
            WindowKeymap.GetGesture(config, WindowKeymap.SaveProject), out var conflict);

        Assert.False(changed);
        Assert.NotNull(conflict);
        Assert.Equal(WindowKeymap.SaveProject, conflict.Id);
        Assert.Equal("Save Project", conflict.DisplayName);
        Assert.Empty(config.KeymapOverrides);
    }
}
