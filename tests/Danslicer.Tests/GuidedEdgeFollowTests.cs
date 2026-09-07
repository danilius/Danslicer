using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Generation;
using Danslicer.Core.Supports.Guided;

namespace Danslicer.Tests;

/// <summary>
/// Edge follow (SUPPORT-GEOMETRY-SPEC "Guided tip placement"): snapping to the nearest crease,
/// tracing it through straight vertices and stopping at sharp corners, and tips only where the
/// crease bounds an underside.
/// </summary>
public sealed class GuidedEdgeFollowTests
{
    private static Mesh Box(Vector3 min, Vector3 max)
    {
        var p = new[]
        {
            new Vector3(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z),
            new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z),
            new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z),
        };
        int[] indices =
        [
            0, 3, 2, 0, 2, 1, 4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6,
            0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5,
        ];
        return new Mesh(p, indices);
    }

    /// <summary>
    /// An inverted V: a ridge along X at z = 0 with two slopes falling away to z = -5, both facing
    /// down. The ridge is split at x = 5 so the trace has a straight vertex to pass through.
    /// </summary>
    private static Mesh Roof()
    {
        Vector3[] r = [new(0, 0, 0), new(5, 0, 0), new(10, 0, 0)];
        Vector3[] a = [new(0, -5, -5), new(5, -5, -5), new(10, -5, -5)];
        Vector3[] b = [new(0, 5, -5), new(5, 5, -5), new(10, 5, -5)];
        var positions = r.Concat(a).Concat(b).ToArray(); // r: 0-2, a: 3-5, b: 6-8
        var indices = new List<int>();
        for (var i = 0; i < 2; i++)
        {
            int r0 = i, r1 = i + 1, a0 = 3 + i, a1 = 4 + i, b0 = 6 + i, b1 = 7 + i;
            indices.AddRange([r0, a1, a0, r0, r1, a1, r0, b0, b1, r0, b1, r1]);
        }
        return new Mesh(positions, indices.ToArray());
    }

    private static TipPlacementParameters Parameters(float spacing) => TipPlacementParameters.Default with
    {
        SpacingMm = spacing, MinSpacingMm = spacing, TipShape = SupportTipShape.Cone,
    };

    [Fact]
    public void RoofFacesPointDown()
    {
        Assert.All(Roof().FaceNormals, n => Assert.True(n.Z < 0));
    }

    [Fact]
    public void TraceSnapsToTheNearestCreaseAndStopsAtSharpCorners()
    {
        var mesh = Box(new Vector3(-5, -5, 0), new Vector3(5, 5, 10));
        var near = new Vector3(1, -4.8f, 0);

        var trace = CreaseTrace.Trace(mesh, near, 1, 30f, snapDistanceMm: 0.5f, maxTurnDegrees: 60f);

        Assert.NotNull(trace);
        Assert.Equal(new Vector3(1, -5, 0), trace.Origin);
        // The box corners turn 90°, past the limit, so the trace is just this one edge.
        Assert.Equal(new Vector3(5, -5, 0), trace.Forward.Points[^1]);
        Assert.Equal(new Vector3(-5, -5, 0), trace.Backward.Points[^1]);
        Assert.Equal(10f, trace.Length, 4);
        // Both faces along the edge are the underside, whose normal points down.
        Assert.All(trace.Forward.Faces.Concat(trace.Backward.Faces), f => Assert.True(mesh.FaceNormals[f].Z < 0));
    }

    [Fact]
    public void NothingWithinSnapDistanceMeansNoTrace()
    {
        var mesh = Box(new Vector3(-5, -5, 0), new Vector3(5, 5, 10));
        Assert.Null(CreaseTrace.Trace(mesh, new Vector3(0, 0, 0), 1, 30f, 0.5f, 60f));
    }

    [Fact]
    public void TraceContinuesThroughAStraightVertex()
    {
        var mesh = Roof();
        var near = new Vector3(2.5f, 0.2f, -0.2f);
        var face = SurfacePath.NearestFace(mesh, near);

        var trace = CreaseTrace.Trace(mesh, near, face, 30f, 0.5f, 60f);

        Assert.NotNull(trace);
        Assert.Equal(new Vector3(2.5f, 0, 0), trace.Origin);
        Assert.Equal(new Vector3(10, 0, 0), trace.Forward.Points[^1]);
        Assert.Equal(new Vector3(0, 0, 0), trace.Backward.Points[^1]);
        Assert.Equal(7.5f, trace.Forward.Length, 4);
        Assert.Equal(2.5f, trace.Backward.Length, 4);
    }

    [Fact]
    public void GestureLaysTipsOutwardFromThePickAlongTheWholeCrease()
    {
        var mesh = Roof();
        var gesture = new CreaseFollowGesture(mesh, 2.5f, 30f) { SnapDistanceMm = 0.5f };
        var near = new Vector3(2.5f, 0.2f, -0.2f);
        gesture.SetCursor(near, SurfacePath.NearestFace(mesh, near));

        var tips = gesture.Preview(Parameters(2.5f), null);

        Assert.Equal(new[] { 0f, 2.5f, 5f, 7.5f, 10f },
            tips.Select(t => MathF.Round(t.Point.X, 3)).OrderBy(x => x).ToArray());
        Assert.All(tips, t => Assert.True(t.InwardNormal.Z > 0));
        Assert.True(gesture.PlacesOnClick);
        Assert.True(gesture.AddVertex());
        Assert.Equal(2, gesture.Route().Count);
    }

    [Fact]
    public void CreaseAlongATopEdgeGivesNoTips()
    {
        var mesh = Box(new Vector3(-5, -5, 0), new Vector3(5, 5, 10));
        var gesture = new CreaseFollowGesture(mesh, 2.5f, 30f) { SnapDistanceMm = 0.5f };
        var near = new Vector3(1, -5, 9.9f);
        gesture.SetCursor(near, SurfacePath.NearestFace(mesh, near));

        Assert.NotNull(gesture.Trace);
        Assert.Empty(gesture.Preview(Parameters(2.5f), null));
    }

    [Fact]
    public void OffTheSurfaceThereIsNothingToPlace()
    {
        var gesture = new CreaseFollowGesture(Roof(), 2.5f, 30f);
        gesture.ClearCursor();
        Assert.Empty(gesture.Route());
        Assert.False(gesture.AddVertex());
    }
}
