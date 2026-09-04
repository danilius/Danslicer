using Danslicer.App.ViewModels;
using Danslicer.Core;

namespace Danslicer.Tests;

public sealed class ObjectCommandScopeTests
{
    [Fact]
    public void DuplicateAndMirrorCommandsAreLayoutScoped()
    {
        var viewModel = new MainViewModel();

        Assert.True(viewModel.DuplicateScopedCommand.IsAvailable);
        Assert.True(viewModel.MirrorXScopedCommand.IsAvailable);
        Assert.True(viewModel.MirrorYScopedCommand.IsAvailable);
        Assert.True(viewModel.MirrorZScopedCommand.IsAvailable);

        viewModel.ViewMode = WorkspaceMode.Support;

        Assert.False(viewModel.DuplicateScopedCommand.IsAvailable);
        Assert.False(viewModel.MirrorXScopedCommand.IsAvailable);
        Assert.False(viewModel.MirrorYScopedCommand.IsAvailable);
        Assert.False(viewModel.MirrorZScopedCommand.IsAvailable);
    }
}
