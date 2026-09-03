using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
