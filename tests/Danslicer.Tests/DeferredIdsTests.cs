using Danslicer.Render;

namespace Danslicer.Tests;

public sealed class DeferredIdsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(DeferredIds.MaxId)]
    public void PackedIdsSurviveTheRgba8RoundTrip(int id)
    {
        var packed = DeferredIds.Pack(id);
        // The GPU quantizes each channel to 8 bits exactly as this rounding does.
        var r = (byte)MathF.Round(packed.X * 255f);
        var g = (byte)MathF.Round(packed.Y * 255f);
        var b = (byte)MathF.Round(packed.Z * 255f);
        Assert.Equal(id, DeferredIds.Unpack(r, g, b));
    }

    [Fact]
    public void OutOfRangeIdsClampInsteadOfWrapping()
    {
        Assert.Equal(DeferredIds.Pack(DeferredIds.MaxId), DeferredIds.Pack(DeferredIds.MaxId + 1));
        Assert.Equal(DeferredIds.Pack(0), DeferredIds.Pack(-5));
    }

    [Fact]
    public void AdjacentIdsDifferInAtLeastOneChannel()
    {
        for (var id = 0; id < 600; id++)
            Assert.NotEqual(DeferredIds.Pack(id), DeferredIds.Pack(id + 1));
    }
}
