using Danslicer.Core;
using Xunit;

namespace Danslicer.Tests;

public class WorkspaceNavigationPropertyTests
{
    [Fact]
    public void Next_CyclesThroughAllModes_WithHasSliceTrue()
    {
        // Starting from Layout
        Assert.Equal(WorkspaceMode.Support, WorkspaceNavigation.Next(WorkspaceMode.Layout, true));
        Assert.Equal(WorkspaceMode.Slicing, WorkspaceNavigation.Next(WorkspaceMode.Support, true));
        Assert.Equal(WorkspaceMode.Layout, WorkspaceNavigation.Next(WorkspaceMode.Slicing, true));

        // Starting from Support
        Assert.Equal(WorkspaceMode.Slicing, WorkspaceNavigation.Next(WorkspaceMode.Support, true));
        Assert.Equal(WorkspaceMode.Layout, WorkspaceNavigation.Next(WorkspaceMode.Slicing, true));
        Assert.Equal(WorkspaceMode.Support, WorkspaceNavigation.Next(WorkspaceMode.Layout, true));

        // Starting from Slicing
        Assert.Equal(WorkspaceMode.Layout, WorkspaceNavigation.Next(WorkspaceMode.Slicing, true));
        Assert.Equal(WorkspaceMode.Support, WorkspaceNavigation.Next(WorkspaceMode.Layout, true));
        Assert.Equal(WorkspaceMode.Slicing, WorkspaceNavigation.Next(WorkspaceMode.Support, true));
    }

    [Fact]
    public void Next_NeverYieldsSlicing_WithHasSliceFalse()
    {
        // Starting from Layout
        Assert.Equal(WorkspaceMode.Support, WorkspaceNavigation.Next(WorkspaceMode.Layout, false));
        Assert.Equal(WorkspaceMode.Layout, WorkspaceNavigation.Next(WorkspaceMode.Support, false));

        // Starting from Support
        Assert.Equal(WorkspaceMode.Layout, WorkspaceNavigation.Next(WorkspaceMode.Support, false));

        // Starting from Slicing
        Assert.NotEqual(WorkspaceMode.Slicing, WorkspaceNavigation.Next(WorkspaceMode.Slicing, false));
    }

    [Fact]
    public void ThreeConsecutiveNext_ReturnsToStartMode_WithHasSliceTrue()
    {
        // Starting from Layout
        Assert.Equal(WorkspaceMode.Layout, WorkspaceNavigation.Next(WorkspaceNavigation.Next(WorkspaceNavigation.Next(WorkspaceMode.Layout, true), true), true));

        // Starting from Support
        Assert.Equal(WorkspaceMode.Support, WorkspaceNavigation.Next(WorkspaceNavigation.Next(WorkspaceNavigation.Next(WorkspaceMode.Support, true), true), true));

        // Starting from Slicing
        Assert.Equal(WorkspaceMode.Slicing, WorkspaceNavigation.Next(WorkspaceNavigation.Next(WorkspaceNavigation.Next(WorkspaceMode.Slicing, true), true), true));
    }
}
