using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Slicing;

namespace Danslicer.Tests;

/// <summary>
/// The embedded preview is cosmetic; no geometry it is handed may be able to fail a slice.
/// A NaN vertex previously reached the camera and threw out of CreatePerspectiveFieldOfView.
/// </summary>
public sealed class PreviewRendererRobustnessTests
{
    private static Mesh TriangleWith(Vector3 third) => new(
        [new Vector3(0, 0, 0), new Vector3(10, 0, 0), third], [0, 1, 2]);

    [Fact]
    public void NonFiniteGeometryDoesNotThrowAndStillRendersTheRest()
    {
        var good = new PreviewRenderer.RenderObject(
            TriangleWith(new Vector3(0, 10, 5)), Matrix4x4.Identity);
        var bad = new PreviewRenderer.RenderObject(
            TriangleWith(new Vector3(float.NaN, 0, 0)), Matrix4x4.Identity);

        var data = PreviewRenderer.Render([good, bad], null, 32, 24);

        Assert.Equal(32 * 24 * 2, data.Length);
    }

    [Fact]
    public void AllGeometryNonFiniteYieldsABlankPreviewRatherThanThrowing()
    {
        var bad = new PreviewRenderer.RenderObject(
            TriangleWith(new Vector3(float.NaN, 0, 0)), Matrix4x4.Identity);

        var data = PreviewRenderer.Render([bad], null, 32, 24);

        Assert.Equal(PreviewRenderer.Blank(32, 24), data);
    }
}
