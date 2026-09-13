using Avalonia.Controls;
using Avalonia.Interactivity;
using Danslicer.App.ViewModels;
using Danslicer.Core.Printers;

namespace Danslicer.App.Views;

/// <summary>Nonmodal, immediately persisted printer collection editor; no viewport or driver session.</summary>
public partial class PrinterPresetEditorWindow : Window
{
    private readonly Action<PrinterDefinition> _usePrinter;

    public PrinterPresetEditorWindow()
        : this(new PrinterEditorViewModel(new Core.Config.UserConfig(), () => { }), _ => { }) { }

    public PrinterPresetEditorWindow(PrinterEditorViewModel editor, Action<PrinterDefinition> usePrinter)
    {
        InitializeComponent();
        DataContext = editor;
        _usePrinter = usePrinter;
        Resources.MergedDictionaries.Add(Controls.Refresh.RefreshPalette.CreateResources());
        Closing += (_, _) => Controls.Refresh.ScrubField.CancelActive();
        Deactivated += (_, _) => Controls.Refresh.ScrubField.CancelActive();
        Configuration.WindowStatePersistence.Track(this, "printer-preset-editor");
    }

    private void OnUsePrinterClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PrinterEditorViewModel { SelectedPrinter: { } printer })
            _usePrinter(printer);
    }
}
