using System.Numerics;
using Danslicer.App.ViewModels;
using Danslicer.Core.Geometry;
using Danslicer.Core.Printers;
using Danslicer.Core.Scene;
using Danslicer.Core.Slicing;

namespace Danslicer.Tests;

public sealed class SliceEstimateTests
{
    private static readonly PrinterDefinition Printer = PrinterDefinition.PhotonMonoX with
    { DisplayWidthMm = 20, DisplayHeightMm = 20, ZTravelMm = 30, ResolutionX = 100, ResolutionY = 100 };

    private static SceneObject Box(float x = 0) => new("cube", new Mesh(
        [new(0,0,0), new(10,0,0), new(0,10,0), new(10,10,0),
         new(0,0,10), new(10,0,10), new(0,10,10), new(10,10,10)],
        [0,2,3,0,3,1,4,5,7,4,7,6,0,1,5,0,5,4,2,6,7,2,7,3,0,4,6,0,6,2,1,3,7,1,7,5]))
        { Transform = Transform.Identity with { Translation = new Vector3(x, -5, 0) } };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnionVolumeCountsOverlapOnceAndCropsAtPlate(bool aa)
    {
        var settings = new PrintSettings { LayerHeight = 1, AntiAliasing = aa };
        var single = Slicer.Slice([Box(-5)], Printer, settings);
        var duplicate = Slicer.Slice([Box(-5), Box(-5)], Printer, settings);
        var overlap = Slicer.Slice([Box(-5), Box(0)], Printer, settings);
        var cropped = Slicer.Slice([Box(5)], Printer, settings, allowOutOfBounds: true);
        Assert.Equal(1, single.VolumeMl, 5);
        Assert.Equal(single.VolumeMl, duplicate.VolumeMl);
        Assert.Equal(1.5, overlap.VolumeMl, 5);
        Assert.Equal(0.5, cropped.VolumeMl, 5);
        Assert.All(cropped.Layers, layer => Assert.Equal(50, layer.AreaMm2));
        Assert.Equal(single.Layers.Select(l => l.Rle), duplicate.Layers.Select(l => l.Rle));
    }

    [Fact]
    public void TimeUsesActualBottomCountSpeedsAndWaitWithoutWrappingAtADay()
    {
        var resin = new ResinSettings { BottomLayers = 2, BottomExposure = 20, Exposure = 3,
            LightOffDelay = 1, BottomLiftHeight = 6, BottomLiftSpeed = 60,
            LiftHeight = 4, LiftSpeed = 120, RetractSpeed = 240 };
        // Bottom: 20+1+6+1.5 = 28.5; normal: 3+1+2+1 = 7 seconds.
        Assert.Equal(0, resin.EstimatePrintTime(0));
        Assert.Equal(28.5, resin.EstimatePrintTime(1));
        Assert.Equal(78, resin.EstimatePrintTime(5));
        Assert.Equal(7, (resin with { BottomLayers = 0 }).EstimatePrintTime(1));
        Assert.Equal(1205, (resin with { BottomLayers = 0, LiftSpeed = 0.2f }).EstimatePrintTime(1), 2);
        Assert.True(double.IsNaN((resin with { LiftSpeed = 0 }).EstimatePrintTime(5)));
        Assert.True(double.IsNaN((resin with { Exposure = float.NaN }).EstimatePrintTime(5)));
        Assert.True(double.IsNaN(resin.EstimatePrintTime(-1)));
        var slice = Slicer.Slice([Box(-5)], Printer, new PrintSettings { LayerHeight = 1 },
            resinSettings: resin with { BottomLayers = 0, Exposure = 9000 });
        Assert.StartsWith("≈ 25 h", SliceEstimate.Duration(slice));
        Assert.Equal("—", SliceEstimate.Volume(null));
        Assert.Equal("—", SliceEstimate.Duration(null));
    }

    [Fact]
    public async Task CompletedEstimatesInvalidateOnEveryRelevantInputWithoutStartingSlice()
    {
        var vm = new MainViewModel();
        vm.Document.Printer = Printer;
        vm.Document.PrintSettings = new PrintSettings { LayerHeight = 1 };
        vm.Document.AddObject(Box(-5));
        Action[] changes = [
            () => vm.Document.ResinSettings = vm.Document.ResinSettings with { Exposure = 4 },
            () => vm.Document.ResinSettings = vm.Document.ResinSettings with { BottomLayers = 1 },
            () => vm.Document.ResinSettings = vm.Document.ResinSettings with { LightOffDelay = 5 },
            () => vm.Document.ResinSettings = vm.Document.ResinSettings with { LiftSpeed = 60 },
            () => vm.Document.PrintSettings = vm.Document.PrintSettings with { AntiAliasing = false },
            () => vm.Document.PrintSettings = vm.Document.PrintSettings with { LayerHeight = 0.5f },
            () => vm.Document.Printer = Printer with { MirrorX = !Printer.MirrorX },
            () => vm.Document.Scene.Objects[0].Transform = Transform.Identity,
        ];
        foreach (var change in changes)
        {
            await vm.SliceCommand.ExecuteAsync(null);
            Assert.NotNull(vm.LastSlice);
            Assert.EndsWith(" mL", vm.ResinVolumeText);
            Assert.StartsWith("≈", vm.PrintDurationText);
            change(); vm.Document.NotifyTransientChange();
            Assert.Null(vm.LastSlice);
            Assert.Equal("—", vm.ResinVolumeText);
            Assert.Equal("—", vm.PrintDurationText);
            Assert.Contains("Inputs changed", vm.EstimateState);
            Assert.False(vm.IsSlicing);
        }
        vm.NewProject();
        Assert.Equal("Slice to calculate material and time.", vm.EstimateState);
        Assert.False(vm.SliceCommand.CanExecute(null));
    }

    [Fact]
    public void EmptyAndInvalidMetadataNeverFormatsAMisleadingEstimate()
    {
        SliceResult Result(float volume, ResinSettings resin, int count) => new()
        {
            Printer = Printer, Settings = PrintSettings.Default, ResinSettings = resin,
            Layers = Enumerable.Range(0, count).Select(_ => new SlicedLayer
                { Rle = [], LitPixels = 0, AreaMm2 = 0, Z = 1 }).ToArray(),
            VolumeMl = volume, Preview = [], MinX = 0, MinY = 0, MaxX = 0, MaxY = 0,
        };
        Assert.Equal("—", SliceEstimate.Volume(Result(0, ResinSettings.Default, 0)));
        Assert.Equal("—", SliceEstimate.Duration(Result(0, ResinSettings.Default, 0)));
        foreach (var value in new[] { float.NaN, float.PositiveInfinity, -1f })
            Assert.Equal("—", SliceEstimate.Volume(Result(value, ResinSettings.Default, 1)));
        Assert.Equal("Unavailable", SliceEstimate.Duration(Result(1,
            ResinSettings.Default with { RetractSpeed = 0 }, 1)));
        Assert.Equal("Unavailable", SliceEstimate.Duration(Result(1,
            ResinSettings.Default with { BottomExposure = float.MaxValue }, 1)));
    }

    [Fact]
    public async Task FailedAndCancelledSlicesLeaveNoPreviousEstimate()
    {
        var vm = new MainViewModel();
        vm.Document.Printer = Printer;
        vm.Document.AddObject(Box(-5));
        await vm.SliceCommand.ExecuteAsync(null);
        Assert.NotNull(vm.LastSlice);
        var pending = vm.SliceCommand.ExecuteAsync(null);
        vm.CancelSlice();
        await pending;
        Assert.Null(vm.LastSlice);
        Assert.Contains("cancelled", vm.EstimateState);
        vm.Document.Scene.Objects[0].Transform = Transform.Identity with { Translation = new Vector3(0, 0, -20) };
        vm.Document.NotifyTransientChange();
        await vm.SliceCommand.ExecuteAsync(null);
        Assert.Null(vm.LastSlice);
        Assert.Contains("failed", vm.EstimateState);
        Assert.Equal("—", vm.ResinVolumeText);
    }

    [Fact]
    public async Task ChangesDuringSliceCannotPublishStaleResult()
    {
        var vm = new MainViewModel();
        vm.Document.Printer = Printer;
        vm.Document.AddObject(Box(-5));
        var pending = vm.SliceCommand.ExecuteAsync(null);
        Assert.True(vm.IsSlicing);
        vm.Document.NotifyTransientChange();
        await pending;
        Assert.Null(vm.LastSlice);
        Assert.Contains("Inputs changed", vm.EstimateState);
        Assert.Equal("—", vm.PrintDurationText);
    }
}
