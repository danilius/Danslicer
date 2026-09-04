using System.Numerics;
using Danslicer.App.ViewModels;
using Danslicer.Core.Geometry;
using Danslicer.Core.Printers;
using Danslicer.Core.Scene;

namespace Danslicer.Tests;

public sealed class SliceWarningViewModelTests
{
    [Fact]
    public async Task SliceWarningSurvivesLaterStatusUpdatesUntilSliceIsInvalidated()
    {
        var viewModel = new MainViewModel();
        viewModel.Document.Printer = PrinterDefinition.PhotonMonoX with
        {
            DisplayWidthMm = 20,
            DisplayHeightMm = 10,
            ZTravelMm = 1,
            ResolutionX = 20,
            ResolutionY = 10,
        };
        var obj = new SceneObject("outside", Box(30, 5, 0.1f));
        obj.Transform = Transform.Identity with { Translation = new Vector3(-15, -2.5f, 0) };
        viewModel.Document.AddObject(obj);

        await viewModel.SliceCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.LastSlice);
        Assert.True(viewModel.HasSliceWarning);
        Assert.Contains("on X was cropped", viewModel.ViewportStatusDisplay);

        viewModel.ViewportStatus = "A later status update.";
        Assert.StartsWith("Warning: content outside the build area on X was cropped.",
            viewModel.ViewportStatusDisplay);
        Assert.EndsWith("A later status update.", viewModel.ViewportStatusDisplay);

        viewModel.Document.NotifyTransientChange();
        Assert.Null(viewModel.LastSlice);
        Assert.False(viewModel.HasSliceWarning);
        Assert.Equal("A later status update.", viewModel.ViewportStatusDisplay);
    }

    private static Mesh Box(float sx, float sy, float sz)
    {
        var positions = new Vector3[8];
        for (var i = 0; i < positions.Length; i++)
            positions[i] = new Vector3((i & 1) * sx, ((i >> 1) & 1) * sy, ((i >> 2) & 1) * sz);
        int[] indices =
        [
            0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4,
            2, 6, 7, 2, 7, 3, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5,
        ];
        return new Mesh(positions, indices);
    }
}
