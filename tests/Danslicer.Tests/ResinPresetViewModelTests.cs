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
}
