using System.Runtime.CompilerServices;
using Danslicer.App.Configuration;

namespace Danslicer.Tests;

internal static class IsolatedAppConfiguration
{
    // Every test VM uses a fresh config, including older tests that construct the real MainViewModel.
    [ModuleInitializer]
    internal static void Initialize()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Danslicer-tests-" + Guid.NewGuid().ToString("N"));
        AppConfig.UseIsolatedDirectory(directory);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
    }
}
