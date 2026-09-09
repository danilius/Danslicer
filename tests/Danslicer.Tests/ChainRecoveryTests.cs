using Clipper2Lib;
using Danslicer.Core.Slicing;
using Xunit;

namespace Danslicer.Tests;

/// <summary>
/// The segment chainer must not drop a whole contour because of a micro-segment fan at a
/// corner or a small gap (roof gripper 2026-09-09: a 187 mm² layer sliced empty).
/// </summary>
public class ChainRecoveryTests
{
    private static MeshSlicer.Segment Seg(long x0, long y0, long x1, long y1) =>
        new(new Point64(x0, y0), new Point64(x1, y1));

    [Fact]
    public void ASquareWithAMicroSpurAtACornerStillCloses()
    {
        // Counter-clockwise 10 mm square with a dead-end spur of two micro-segments leaving
        // the first corner, listed before the real continuation so the greedy chain takes it.
        var segments = new List<MeshSlicer.Segment>
        {
            Seg(0, 0, 10000, 0),
            Seg(10000, 0, 10003, 2),      // spur, 3 units
            Seg(10003, 2, 10006, 4),      // spur, dead end
            Seg(10000, 0, 10000, 10000),
            Seg(10000, 10000, 0, 10000),
            Seg(0, 10000, 0, 0),
        };

        var paths = MeshSlicer.ChainSegments(segments);

        var path = Assert.Single(paths);
        Assert.InRange(Clipper.Area(path) / 1e6, 99.9, 100.1);
    }

    [Fact]
    public void ASquareWithASmallGapStillCloses()
    {
        // 40 units (0.04 mm) missing from one edge: beyond the join tolerance, within recovery.
        var segments = new List<MeshSlicer.Segment>
        {
            Seg(0, 0, 10000, 0),
            Seg(10000, 0, 10000, 10000),
            Seg(10000, 10000, 0, 10000),
            Seg(0, 10000, 0, 40),
        };

        var paths = MeshSlicer.ChainSegments(segments);

        var path = Assert.Single(paths);
        Assert.InRange(Clipper.Area(path) / 1e6, 99.5, 100.1);
    }

    [Fact]
    public void AWideGapIsStillDiscarded()
    {
        // 2 mm missing: not a slicing artefact, the contour is genuinely open.
        var segments = new List<MeshSlicer.Segment>
        {
            Seg(0, 0, 10000, 0),
            Seg(10000, 0, 10000, 10000),
            Seg(10000, 10000, 0, 10000),
            Seg(0, 10000, 0, 2000),
        };

        Assert.Empty(MeshSlicer.ChainSegments(segments));
    }
}
