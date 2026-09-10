using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Danslicer.App.Configuration;
using Danslicer.App.Themes;
using Danslicer.App.ViewModels;
using Danslicer.App.Views;

namespace Danslicer.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Isolated native gallery: do not construct the main VM, renderer or load user settings.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime previewDesktop
            && (previewDesktop.Args ?? []).Contains("--ui-preview"))
        {
            var args = previewDesktop.Args ?? [];
            var captureIndex = Array.IndexOf(args, "--capture-directory");
            var capture = captureIndex >= 0 && captureIndex + 1 < args.Length ? args[captureIndex + 1] : null;
            previewDesktop.MainWindow = new UiPreviewWindow(capture);
            base.OnFrameworkInitializationCompleted();
            return;
        }

        // Applied before any window is constructed so the very first frame is already themed.
        ThemeCatalog.Apply(AppConfig.Current.Theme);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainViewModel();
            desktop.MainWindow = new MainWindow { DataContext = vm };

            // Allow `Danslicer.App model.stl` for quick testing.
            foreach (var arg in desktop.Args ?? Array.Empty<string>())
            {
                if (File.Exists(arg) && Danslicer.Core.IO.MeshFile.IsSupported(arg))
                    vm.ImportMesh(arg);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
