using System;
using System.Linq;
using Danslicer.Core.IO;
using Xunit;

namespace Danslicer.Tests;

public class PhotonRlePropertyTests
{
    private static readonly Random Random = new Random(42);

    [Fact]
    public void RoundTrip_AllZeros()
    {
        var pixels = new byte[1024];
        var encoded = PhotonRle.Encode(pixels);
        var decoded = new byte[pixels.Length];
        PhotonRle.Decode(encoded, decoded);
        Assert.Equal(pixels, decoded);
    }

    [Fact]
    public void RoundTrip_All255()
    {
        var pixels = Enumerable.Repeat((byte)255, 1024).ToArray();
        var encoded = PhotonRle.Encode(pixels);
        var decoded = new byte[pixels.Length];
        PhotonRle.Decode(encoded, decoded);
        Assert.Equal(pixels, decoded);
    }

    [Fact]
    public void RoundTrip_Single255AmongZeros()
    {
        var pixels = new byte[1024];
        pixels[512] = 255;
        var encoded = PhotonRle.Encode(pixels);
        var decoded = new byte[pixels.Length];
        PhotonRle.Decode(encoded, decoded);
        Assert.Equal(pixels, decoded);
    }

    [Fact]
    public void RoundTrip_AlternatingRuns()
    {
        var pixels = new byte[1024];
        for (int i = 0; i < pixels.Length; i += 2)
        {
            pixels[i] = 255;
        }
        var encoded = PhotonRle.Encode(pixels);
        var decoded = new byte[pixels.Length];
        PhotonRle.Decode(encoded, decoded);
        Assert.Equal(pixels, decoded);
    }

    [Fact]
    public void RoundTrip_PseudoRandomLevels()
    {
        var pixels = new byte[1024];
        for (int i = 0; i < pixels.Length; i++)
        {
            var level = Random.Next(16);
            pixels[i] = (byte)(level * 17);
        }
        var encoded = PhotonRle.Encode(pixels);
        var decoded = new byte[pixels.Length];
        PhotonRle.Decode(encoded, decoded);
        Assert.Equal(pixels, decoded);
    }

    [Fact]
    public void RoundTrip_LongRunHandling()
    {
        var pixels = Enumerable.Repeat((byte)255, 100000).ToArray();
        var encoded = PhotonRle.Encode(pixels);
        var decoded = new byte[pixels.Length];
        PhotonRle.Decode(encoded, decoded);
        Assert.Equal(pixels, decoded);
    }

    [Fact]
    public void RoundTrip_GreyLevels()
    {
        var pixels = Enumerable.Range(0, 16).Select(level => (byte)(level * 17)).ToArray();
        pixels = pixels.Concat(pixels).Concat(pixels).Concat(pixels).ToArray(); // Repeat to 64 bytes
        pixels = pixels.Concat(pixels).Concat(pixels).Concat(pixels).ToArray(); // Repeat to 256 bytes
        pixels = pixels.Concat(pixels).Concat(pixels).Concat(pixels).ToArray(); // Repeat to 1024 bytes
        var encoded = PhotonRle.Encode(pixels);
        var decoded = new byte[pixels.Length];
        PhotonRle.Decode(encoded, decoded);
        Assert.Equal(pixels, decoded);
    }

    [Fact]
    public void Decoding_OutputLength()
    {
        var pixels = new byte[1024];
        for (int i = 0; i < pixels.Length; i++)
        {
            var level = Random.Next(16);
            pixels[i] = (byte)(level * 17);
        }
        var encoded = PhotonRle.Encode(pixels);
        var decoded = new byte[pixels.Length];
        PhotonRle.Decode(encoded, decoded);
        Assert.Equal(pixels.Length, decoded.Length);
    }
}
