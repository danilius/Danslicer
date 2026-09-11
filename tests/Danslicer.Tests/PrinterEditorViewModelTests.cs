using Danslicer.App.ViewModels;
using Danslicer.Core.Config;
using Danslicer.Core.Printers;

namespace Danslicer.Tests;

public sealed class PrinterEditorViewModelTests
{
    [Fact]
    public void EditingBuiltInCreatesAndContinuesEditingAUserCopy()
    {
        var config = new UserConfig();
        var saves = 0;
        var editor = new PrinterEditorViewModel(config, () => saves++);

        editor.MirrorY = true;

        Assert.Equal(PrinterCatalog.BuiltIn.Count + 1, config.Printers.Count);
        Assert.Equal(PrinterDefinition.PhotonMonoX, config.Printers[0]);
        Assert.False(editor.SelectedPrinter!.IsBuiltIn);
        Assert.True(editor.SelectedPrinter.MirrorY);
        var copyId = editor.SelectedPrinter.Id;

        editor.MachineName = "Edited machine";
        editor.NameDraft = "My printer";
        editor.RenameCommand.Execute(null);

        Assert.Equal(PrinterCatalog.BuiltIn.Count + 1, config.Printers.Count);
        Assert.Equal(copyId, editor.SelectedPrinter.Id);
        Assert.Equal("Edited machine", editor.SelectedPrinter.MachineName);
        Assert.Equal("My printer", editor.SelectedPrinter.Name);
        Assert.Equal(3, saves);
    }

    [Fact]
    public void DuplicateAndDeleteOperateOnlyOnUserDefinitions()
    {
        var config = new UserConfig();
        var editor = new PrinterEditorViewModel(config, () => { });

        Assert.False(editor.DeleteCommand.CanExecute(null));
        editor.DuplicateCommand.Execute(null);
        Assert.Equal(PrinterCatalog.BuiltIn.Count + 1, config.Printers.Count);
        Assert.NotEqual(config.Printers[0].Id, config.Printers[1].Id);
        Assert.True(editor.DeleteCommand.CanExecute(null));

        editor.DeleteCommand.Execute(null);

        Assert.Equal(PrinterCatalog.BuiltIn, config.Printers);
        Assert.True(editor.SelectedPrinter!.IsBuiltIn);
        Assert.False(editor.DeleteCommand.CanExecute(null));
    }
}
