using System.Numerics;
using Clipper2Lib;
using Danslicer.Core.Slicing;
using Danslicer.Core.Supports.Generation;

namespace Danslicer.Tests;

public class IslandDetectionTests
{
    private static Path64 Rect(double x, double y, double width, double height) => new()
    {
        new Point64(x*1000,y*1000), new Point64((x+width)*1000,y*1000),
        new Point64((x+width)*1000,(y+height)*1000), new Point64(x*1000,(y+height)*1000)
    };
    private static List<Island> Detect(params Paths64[] paths) => IslandFinder.Find(
        paths.Select((p,i) => new SliceLayer(i, (i+0.5f)*0.1f,p)).ToList(),
        0, 0.1f, 0.01f, 0, includeOverhangs:false);

    [Fact]
    public void SubThresholdStartIsReportedWhenItGrowsLargeEnough()
    {
        var islands = Detect(new Paths64(), new Paths64 { Rect(0, 0, 0.05, 0.1) },
            new Paths64 { Rect(0, 0, 1, 1) }, new Paths64 { Rect(0, 0, 2, 2) });
        Assert.Equal(2, Assert.Single(islands).LayerIndex);
    }

    [Fact]
    public void ExpandingConnectedLayerDoesNotGenerateIslandMarkers()
    {
        Assert.Empty(Detect(new Paths64 { Rect(0,0,1,1) }, new Paths64 { Rect(0,0,5,5) }));
    }

    [Fact]
    public void SeparateComponentInsideOverhangAllowanceIsStillAnIsland()
    {
        var islands = Detect(new Paths64 { Rect(0,0,1,1) },
            new Paths64 { Rect(0,0,1,1), Rect(1.01,0,0.1,1) });
        Assert.Single(islands);
        Assert.Equal(0.1f,islands[0].AreaMm2,4);
    }

    [Fact]
    public void IslandIsReportedOnlyAtBirthAndAfterARealGap()
    {
        var solid = new Paths64 { Rect(0,0,1,1) };
        var islands = Detect(new Paths64(),solid,solid,new Paths64(),solid);
        Assert.Equal(new[] {1,4}, islands.Select(i=>i.LayerIndex));
    }

    [Fact]
    public void RingMarkerIsStrictlyInsideMaterialAndSupportInHoleDoesNotClearIt()
    {
        var hole = Rect(2,2,6,6); hole.Reverse();
        var island = Assert.Single(Detect(new Paths64(),new Paths64 { Rect(0,0,10,10),hole }));
        var point = new Point64(island.Centroid.X*1000,island.Centroid.Y*1000);
        Assert.Equal(PointInPolygonResult.IsInside,Clipper.PointInPolygon(point,island.Footprint[0]));
        Assert.Equal(PointInPolygonResult.IsOutside,Clipper.PointInPolygon(point,hole));
        Assert.Equal(64,island.AreaMm2);
        Assert.False(IslandDetection.IsReachedBySupport(island,new Paths64 { Rect(4,4,1,1) }));
        Assert.True(IslandDetection.IsReachedBySupport(island,new Paths64 { Rect(0.5,4,1,1) }));
    }

    [Fact]
    public void LongThinIslandUsesItsWholeFootprintInsteadOfAnEquivalentCircle()
    {
        var island = Assert.Single(Detect(new Paths64(),new Paths64 { Rect(0,0,20,1) }));
        Assert.True(IslandDetection.IsReachedBySupport(island,new Paths64 { Rect(19,0.2,0.5,0.5) }));
        Assert.False(IslandDetection.IsReachedBySupport(island,new Paths64 { Rect(9,1.2,0.5,0.5) }));
    }

    [Fact]
    public void NestedSolidOwnsOnlyItsImmediateHoles()
    {
        var hole = Rect(1,1,8,8); hole.Reverse();
        var innerHole = Rect(4,4,2,2); innerHole.Reverse();
        var islands = Detect(new Paths64(),new Paths64 {Rect(0,0,10,10),hole,Rect(3,3,4,4),innerHole});
        Assert.Equal(new[] {12f,36f},islands.Select(i=>i.AreaMm2).Order());
    }

    [Fact]
    public void ChainingPrefersExactContinuationOverEarlierNearbyEndpoint()
    {
        var a = new Point64(0,0); var b = new Point64(100,0);
        var c = new Point64(100,100); var d = new Point64(0,100);
        var segments = new List<MeshSlicer.Segment>
        {
            new(a,b), new(new Point64(99,0),new Point64(200,0)),
            new(b,c), new(c,d), new(d,a)
        };
        var loop = Assert.Single(MeshSlicer.ChainSegments(segments));
        Assert.Equal(10000,Clipper.Area(loop));
    }
}
