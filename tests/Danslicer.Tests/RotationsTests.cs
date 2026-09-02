using System.Numerics;
using Danslicer.Core.Scene;

namespace Danslicer.Tests;

public class RotationsTests
{
    private static void AssertClose(Vector3 expected, Vector3 actual, float tolerance = 1e-4f)
    {
        Assert.True(Vector3.Distance(expected, actual) < tolerance, $"expected {expected}, got {actual}");
    }

    [Fact]
    public void ComposeAppliesFirstThenSecond()
    {
        var first = Rotations.AboutAxis(Vector3.UnitZ, MathF.PI / 2);   // X -> Y
        var second = Rotations.AboutAxis(Vector3.UnitX, MathF.PI / 2);  // Y -> Z
        var composed = Rotations.Compose(first, second);

        var expected = Vector3.Transform(Vector3.Transform(Vector3.UnitX, first), second);
        AssertClose(Vector3.UnitZ, expected);
        AssertClose(expected, Vector3.Transform(Vector3.UnitX, composed));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(30, 0, 0)]
    [InlineData(0, 45, 0)]
    [InlineData(0, 0, 60)]
    [InlineData(10, 20, 30)]
    [InlineData(-75, 40, -120)]
    [InlineData(170, -80, 15)]
    public void EulerRoundTrips(float x, float y, float z)
    {
        var input = new Vector3(x, y, z);
        var q = Rotations.FromEulerXyzDegrees(input);
        var output = Rotations.ToEulerXyzDegrees(q);

        // Compare by effect rather than by value so equivalent angle sets pass.
        var q2 = Rotations.FromEulerXyzDegrees(output);
        foreach (var v in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
            AssertClose(Vector3.Transform(v, q), Vector3.Transform(v, q2), 1e-3f);
    }

    [Fact]
    public void EulerOrderIsXThenYThenZ()
    {
        // 90 about X takes Y to Z; then 90 about Z takes X to Y. Combined: X -> Y.
        var q = Rotations.FromEulerXyzDegrees(new Vector3(90, 0, 90));
        AssertClose(Vector3.UnitY, Vector3.Transform(Vector3.UnitX, q));
        // Y -> Z under X rotation, then Z stays Z under the Z rotation.
        AssertClose(Vector3.UnitZ, Vector3.Transform(Vector3.UnitY, q));
    }

    [Fact]
    public void TransformMatrixAppliesScaleRotateTranslate()
    {
        var t = new Transform(new Vector3(10, 0, 0), Rotations.AboutAxis(Vector3.UnitZ, MathF.PI / 2), new Vector3(2, 1, 1));
        var p = Vector3.Transform(Vector3.UnitX, t.ToMatrix());
        // Scale X by 2 -> (2,0,0); rotate 90 about Z -> (0,2,0); translate -> (10,2,0).
        AssertClose(new Vector3(10, 2, 0), p);
    }
}
