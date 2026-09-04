using Danslicer.App.ViewModels;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Slicing;

namespace Danslicer.Tests;

public sealed class ResinPresetViewModelTests
{
    [Fact]
    public void EditingShowsModifiedAndSaveAsCreatesIndependentPreset()
    {
        var config = new UserConfig();
        var document = new Document
        {
            PrintSettings = PrintSettings.Default with
                { LayerHeight = 0.025f, AntiAliasing = false, XyCompensation = -0.03f },
        };
        var printBefore = document.PrintSettings;
        var saves = 0;
        var editor = new ResinPresetViewModel(config, document, () => saves++);

        editor.Exposure.Text = "3.4";

        Assert.Equal(3.4f, document.ResinSettings.Exposure);
        Assert.Equal(printBefore, document.PrintSettings);
        Assert.EndsWith(" *", editor.DisplayNames[editor.SelectedIndex]);

        editor.BeginSaveAsCommand.Execute(null);
        editor.NameDraft = "ABS-like grey";
        editor.ConfirmNameCommand.Execute(null);

        Assert.Equal(2, config.ResinPresets.Count);
        Assert.Equal("ABS-like grey", document.ResinPreset.Name);
        Assert.Equal(document.ResinSettings, document.ResinPreset.Settings);
        Assert.False(editor.DisplayNames[editor.SelectedIndex].EndsWith(" *", StringComparison.Ordinal));
        Assert.Equal(1, saves);
    }

    [Fact]
    public void RenameDeleteAndProjectOnlySelectionFollowSharedPresetUx()
    {
        var config = new UserConfig();
        var local = config.SaveResinPresetAs("Local", ResinSettings.Default with { Exposure = 2.8f })!;
        var document = new Document();
        document.ApplyResinPreset(new ResinPreset
        {
            Id = "embedded-only", Name = "Embedded", Settings = ResinSettings.Default with { Exposure = 4.2f },
        });
        var editor = new ResinPresetViewModel(config, document, () => { });

        Assert.Contains("Embedded (project)", editor.DisplayNames);
        Assert.False(editor.BeginRenameCommand.CanExecute(null));
        Assert.False(editor.DeleteCommand.CanExecute(null));
        editor.SelectedIndex = config.ResinPresets.IndexOf(local);
        Assert.Equal(2.8f, document.ResinSettings.Exposure);

        editor.BeginRenameCommand.Execute(null);
        editor.NameDraft = "Renamed";
        editor.ConfirmNameCommand.Execute(null);
        Assert.Equal("Renamed", document.ResinPreset.Name);

        editor.DeleteCommand.Execute(null);
        Assert.Null(config.FindResinPreset(local.Id));
        Assert.Equal(ResinPreset.DefaultId, document.ResinPreset.Id);
    }

    [Fact]
    public void SelectionWriteBacksDuringRefreshDoNotReapplyPresets()
    {
        // Regression: the same view model is bound TwoWay from Preferences and the Slicing
        // panel. Replacing DisplayNames makes each bound control reset its selection and write
        // SelectedIndex back mid-refresh; before the guard those writes re-applied the preset
        // and refreshed again, and the two views recursed until the stack overflowed.
        var config = new UserConfig();
        var local = config.SaveResinPresetAs("Local", ResinSettings.Default with { Exposure = 2.8f })!;
        var document = new Document();
        var editor = new ResinPresetViewModel(config, document, () => { });
        editor.Exposure.Text = "9.9"; // dirty marker, so switching presets changes the names

        var writeBacks = 0;
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(editor.DisplayNames) || writeBacks >= 50) return;
            writeBacks++;
            var keep = editor.SelectedIndex;
            editor.SelectedIndex = -1;   // one control clears selection on the items reset
            editor.SelectedIndex = keep; // the other re-asserts the previous index
        };
        var changes = 0;
        editor.Changed += () => changes++;

        var target = config.ResinPresets.IndexOf(local);
        editor.SelectedIndex = target;

        Assert.Equal(local.Id, document.ResinPreset.Id);
        Assert.Equal(target, editor.SelectedIndex);
        Assert.Equal(1, changes);
        Assert.True(writeBacks <= 2, $"items were reset {writeBacks} times - feedback loop");
    }

    [Fact]
    public void RefreshWithUnchangedContentKeepsTheDisplayNamesInstance()
    {
        // External refreshes (Saved -> Resins.Refresh in MainViewModel) fire on every config
        // change; replacing the list when nothing changed makes bound controls reset their
        // selection for no reason, which is the other half of the feedback loop.
        var config = new UserConfig();
        var document = new Document();
        var editor = new ResinPresetViewModel(config, document, () => { });

        var raised = 0;
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(editor.DisplayNames)) raised++;
        };
        editor.Refresh();

        Assert.Equal(0, raised);
    }
}
