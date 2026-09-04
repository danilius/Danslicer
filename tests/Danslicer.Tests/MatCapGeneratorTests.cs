using Danslicer.Render;

namespace Danslicer.Tests;

public sealed class MatCapGeneratorTests
{
    private static (float R, float G, float B) Sample(byte[] data, int size, int col, int row)
    {
        var k = (row * size + col) * 3;
        return (data[k] / 255f, data[k + 1] / 255f, data[k + 2] / 255f);
    }

    [Theory]
    [InlineData(MatCapStyle.Clay)]
    [InlineData(MatCapStyle.Metal)]
    [InlineData(MatCapStyle.Pearl)]
    public void GeneratesTightlyPackedRgbOfRequestedSize(MatCapStyle style)
    {
        Assert.Equal(MatCapGenerator.Size * MatCapGenerator.Size * 3,
            MatCapGenerator.Generate(style).Length);
        Assert.Equal(16 * 16 * 3, MatCapGenerator.Generate(style, 16).Length);
    }

    [Theory]
    [InlineData(MatCapStyle.Clay)]
    [InlineData(MatCapStyle.Metal)]
    [InlineData(MatCapStyle.Pearl)]
    public void IsDeterministic(MatCapStyle style)
    {
        Assert.Equal(MatCapGenerator.Generate(style, 64), MatCapGenerator.Generate(style, 64));
    }

    [Theory]
    [InlineData(MatCapStyle.Clay)]
    [InlineData(MatCapStyle.Metal)]
    [InlineData(MatCapStyle.Pearl)]
    public void MeanBrightnessSitsNearMidGrey(MatCapStyle style)
    {
        // The composite pass multiplies the lookup by 2, so the disc must average near 0.5 for the
        // MatCap modes to hold roughly the same exposure as studio lighting.
        var size = 128;
        var data = MatCapGenerator.Generate(style, size);
        double sum = 0;
        var count = 0;
        for (var row = 0; row < size; row++)
        for (var col = 0; col < size; col++)
        {
            var nx = (col + 0.5f) / size * 2f - 1f;
            var ny = (row + 0.5f) / size * 2f - 1f;
            if (nx * nx + ny * ny > 1f) continue;
            var (r, g, b) = Sample(data, size, col, row);
            sum += (r + g + b) / 3.0;
            count++;
        }
        var mean = sum / count;
        Assert.InRange(mean, 0.25, 0.7);
    }

    [Theory]
    [InlineData(MatCapStyle.Clay)]
    [InlineData(MatCapStyle.Metal)]
    [InlineData(MatCapStyle.Pearl)]
    public void LightComesFromTheUpperKeySide(MatCapStyle style)
    {
        // Key light sits up and to the right (the studio key direction), so the upper-right of the
        // sphere must read brighter than the lower-left for every style.
        var size = 128;
        var data = MatCapGenerator.Generate(size: size, style: style);
        var upperRight = Sample(data, size, size * 3 / 4, size * 3 / 4);
        var lowerLeft = Sample(data, size, size / 4, size / 4);
        Assert.True(upperRight.R + upperRight.G + upperRight.B >
            lowerLeft.R + lowerLeft.G + lowerLeft.B);
    }

    [Fact]
    public void RimPixelsExtendOutsideTheDiscInsteadOfGoingBlack()
    {
        var size = 64;
        var data = MatCapGenerator.Generate(MatCapStyle.Clay, size);
        var corner = Sample(data, size, 0, 0);
        Assert.True(corner.R + corner.G + corner.B > 0.05f,
            "corner texels should carry rim shading for bilinear safety");
    }
}
