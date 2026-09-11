using System.Text;
using System.Text.Json;
using Clipper2Lib;
using Danslicer.Core.Config;
using Danslicer.Core.IO;
using Danslicer.Core.Printers;
using Danslicer.Core.Slicing;

namespace Danslicer.Tests;

public sealed class NativePrinterWriterTests
{
    // Fixed expectations taken from the binary format specification, not the production reader.
    [Theory]
    [InlineData("pwms", 1u, 48, 80, 4u)]
    [InlineData("pmsq", 515u, 48, 80, 5u)]
    [InlineData("pwma", 516u, 52, 84, 8u)]
    [InlineData("px6s", 517u, 56, 92, 9u)]
    [InlineData("pm5s", 518u, 64, 96, 11u)]
    public void VersionLayoutsHaveIndependentOffsetsAndValidImages(string extension, uint version,
        int markSize, int headerSize, uint tables)
    {
        var result = Fixture(extension, version);
        using var output = new MemoryStream();
        PhotonWorkshopWriter.Write(result, output);
        var bytes = output.ToArray();
        Assert.Equal("ANYCUBIC", Text(bytes, 0, 12));
        Assert.Equal(version, U(bytes, 12));
        Assert.Equal(tables, U(bytes, 16));
        Assert.Equal((uint)markSize, U(bytes, 20));
        Assert.Equal("HEADER", Text(bytes, markSize, 12));
        Assert.Equal((uint)headerSize, U(bytes, markSize + 12));
        Assert.Equal((uint)(markSize + 16 + headerSize), U(bytes, 28));
        Assert.Equal(0u, U(bytes, markSize + 80)); // no per-layer override on experimental profiles
        int preview = (int)U(bytes, 28);
        Assert.Equal("PREVIEW", Text(bytes, preview, 12));
        Assert.Equal(36u, U(bytes, preview + 12)); // 28-byte envelope + four RGB565 pixels
        Assert.Equal(result.Preview, bytes.AsSpan(preview + 28, 8).ToArray());
        int definition = (int)U(bytes, 36);
        Assert.Equal("LAYERDEF", Text(bytes, definition, 12));
        Assert.Equal(68u, U(bytes, definition + 12));
        Assert.Equal(2u, U(bytes, definition + 16));
        for (int i = 0; i < 2; i++)
        {
            int entry = definition + 20 + 32 * i;
            var address = (int)U(bytes, entry);
            var length = (int)U(bytes, entry + 4);
            Assert.Equal(result.Layers[i].Rle, bytes.AsSpan(address, length).ToArray());
            Assert.Equal(i == 0 ? 30f : 2f, F(bytes, entry + 16));
            Assert.Equal(0.05f, F(bytes, entry + 20));
            // Independent decoder checks fixed asymmetric pixels rather than round-tripping the same codec.
            Assert.Equal(Pixels(), DecodePw0(bytes.AsSpan(address, length)));
        }
        if (version < 516)
        {
            Assert.Equal(0u, U(bytes, 24));
            Assert.Equal(version == 1 ? 0u : U(bytes, 44), U(bytes, 40));
        }
        else
        {
            int machine = (int)U(bytes, 44);
            Assert.Equal("MACHINE", Text(bytes, machine, 12));
            Assert.Equal(version == 518 ? 224u : 156u, U(bytes, machine + 12));
            Assert.Equal("pw0Img", Text(bytes, machine + 112, 16));
            Assert.Equal(7u, U(bytes, machine + 132));
        }
        if (version >= 517)
        {
            int software = (int)U(bytes, 24), model = (int)U(bytes, 52);
            Assert.Equal("Danslicer", Text(bytes, software, 32));
            Assert.Equal(164u, U(bytes, software + 32));
            Assert.Equal((uint)(software + 164), U(bytes, 52));
            Assert.Equal("MODEL", Text(bytes, model, 12));
            Assert.Equal(48u, U(bytes, model + 12));
            Assert.Equal(result.PrintHeight, F(bytes, model + 36));
        }
        if (version == 518)
        {
            int sub = (int)U(bytes, 56), preview2 = (int)U(bytes, 60), machine = (int)U(bytes, 44);
            Assert.Equal("SUBIMGS", Text(bytes, sub, 12));
            Assert.Equal(112u, U(bytes, sub + 12));
            Assert.Equal(2u, U(bytes, sub + 16));
            Assert.Equal(U(bytes, definition + 20), U(bytes, sub + 24));
            Assert.Equal(U(bytes, definition + 52), U(bytes, sub + 68));
            Assert.Equal("PREVIEW2", Text(bytes, preview2, 12));
            Assert.Equal(330u, U(bytes, preview2 + 16));
            Assert.Equal(190u, U(bytes, preview2 + 24));
            Assert.Equal(result.Printer.PixelPitchX * 1000, F(bytes, machine + 156));
            Assert.Equal(result.Printer.PixelPitchY * 1000, F(bytes, machine + 160));
            Assert.Equal(0u, U(bytes, markSize + 108)); // intelligent mode disabled
        }
        Assert.Equal(Pixels(), PhotonWorkshopFile.Read(bytes).DecodeLayer(0));
    }

    [Theory]
    [InlineData("ctb", 516u)]
    [InlineData("pwmx", 517u)]
    [InlineData("pws", 1u)]
    [InlineData("pm5s", 516u)]
    [InlineData("pwmx", 999u)]
    public void UnsupportedFormatDoesNotTruncateExistingOutput(string extension, uint version)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "keep existing print");
            Assert.Throws<NotSupportedException>(() => PhotonWorkshopWriter.Write(Fixture(extension, version), path));
            Assert.Equal("keep existing print", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void HeaderCannotPretendToBeANewerVersion()
    {
        using var output = new MemoryStream();
        PhotonWorkshopWriter.Write(Fixture("pwma", 516), output);
        var bytes = output.ToArray();
        BitConverter.GetBytes(517u).CopyTo(bytes, 12);
        Assert.Throws<InvalidDataException>(() => PhotonWorkshopFile.Read(bytes));
    }

    [Fact]
    public void FileExtensionMustMatchTheSelectedPrinter()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "preserve");
            Assert.Throws<InvalidOperationException>(() => PhotonWorkshopWriter.Write(Fixture("pwma", 516), path));
            Assert.Equal("preserve", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TruncatedLayerDataIsRejectedBeforeDecoding()
    {
        using var output = new MemoryStream();
        PhotonWorkshopWriter.Write(Fixture("pwma", 516), output);
        Assert.Throws<InvalidDataException>(() => PhotonWorkshopFile.Read(output.ToArray()[..^1]));
    }

    [Fact]
    public void SmallerPrintAreaDoesNotRescalePixelsAndKeepsMarginsBlack()
    {
        var p = Fixture("pwma", 516).Printer with
        {
            DisplayWidthMm = 8,
            DisplayHeightMm = 6,
            PrintWidthMm = 4,
            PrintHeightMm = 2,
        };
        var rasterizer = new LayerRasterizer(p, false);
        var pixels = new byte[48];
        var u = MeshSlicer.UnitsPerMm;
        Paths64 full = [new Path64 { new(-4 * u, -3 * u), new(4 * u, -3 * u), new(4 * u, 3 * u), new(-4 * u, 3 * u) }];
        Assert.Equal(8u, rasterizer.Rasterize(full, pixels));
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 8; x++)
                Assert.Equal(x is >= 2 and < 6 && y is >= 2 and < 4 ? 255 : 0, pixels[y * 8 + x]);
        Assert.Equal(1f, p.PixelPitchX);
        Assert.Equal(4f, p.BuildVolume.X);
    }

    [Fact]
    public void CatalogAndCopiesPersistNewFormatAndAreaFields()
    {
        Assert.Equal(18, PrinterCatalog.BuiltIn.Count);
        Assert.Equal(18, PrinterCatalog.BuiltIn.Select(p => p.Id).Distinct().Count());
        foreach (var p in PrinterCatalog.BuiltIn)
        {
            PhotonWorkshopFormat.ValidatePrinter(p);
            Assert.Equal(p, p.Normalize());
            Assert.Equal(p, JsonSerializer.Deserialize<PrinterDefinition>(JsonSerializer.Serialize(p)));
            var copy = p.CreateUserCopy("custom").Normalize();
            Assert.False(copy.IsBuiltIn);
            Assert.Equal(p.BuildVolume, copy.BuildVolume);
            Assert.Equal(p.PerLayerSettings, copy.PerLayerSettings);
        }
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{}");
            Assert.Equal(PrinterCatalog.BuiltIn, UserConfig.Load(path).Printers);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AddingCatalogDoesNotReplaceAnExistingCustomPrinterWithTheSameId()
    {
        var custom = PrinterCatalog.BuiltIn[4] with
        {
            IsBuiltIn = false, Name = "My calibrated printer", PrintWidthMm = 128, PerLayerSettings = true,
        };
        var config = new UserConfig { Printers = [custom] };
        var path = Path.GetTempFileName();
        try
        {
            config.Save(path);
            var loaded = UserConfig.Load(path);
            Assert.Equal(custom, loaded.FindPrinter(custom.Id));
            Assert.Equal(PrinterCatalog.BuiltIn.Count, loaded.Printers.Count);
            Assert.False(loaded.FindPrinter(custom.Id)!.IsBuiltIn);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void InvalidPreviewIsRejectedWithoutWritingAnyBytes()
    {
        var result = Fixture("pwma", 516);
        // Existing bytes exercise validation before the stream is repositioned.
        using var stream = new MemoryStream([1, 2, 3]);
        var badPrinter = result.Printer with { ResolutionX = int.MaxValue };
        Assert.Throws<InvalidOperationException>(() => PhotonWorkshopFormat.ValidatePrinter(badPrinter));
        // The fixture's valid 2x2 preview is represented as a wrong-size result here.
        var bad = new SliceResult
        {
            Printer = result.Printer, Settings = result.Settings, ResinSettings = result.ResinSettings,
            Layers = result.Layers, VolumeMl = result.VolumeMl, Preview = result.Preview,
            PreviewWidth = 3, PreviewHeight = 2, MinX = 0, MinY = 0, MaxX = 0, MaxY = 0,
        };
        Assert.Throws<InvalidOperationException>(() => PhotonWorkshopWriter.Write(bad, stream));
        Assert.Equal(new byte[] { 1, 2, 3 }, stream.ToArray());
    }

    private static byte[] Pixels() => [0, 255, 17, 34, .. Enumerable.Repeat((byte)0, 44)];
    private static SliceResult Fixture(string extension, uint version) => new()
    {
        Printer = PrinterDefinition.PhotonMonoX.CreateUserCopy("Fixture") with
        {
            FileExtension = extension,
            FormatVersion = version,
            ResolutionX = 8,
            ResolutionY = 6,
            DisplayWidthMm = 8,
            DisplayHeightMm = 12,
            PerLayerSettings = false,
            MachinePropertyFields = 7,
        },
        Settings = PrintSettings.Default,
        ResinSettings = ResinSettings.Default with { BottomLayers = 1 },
        Layers = Enumerable.Range(0, 2).Select(i => new SlicedLayer
        {
            Rle = [0x00, 0x01, 0xF0, 0x01, 0x11, 0x21, 0x00, 0x2C],
            LitPixels = 3,
            AreaMm2 = 6,
            Z = (i + 1) * 0.05f,
        }).ToArray(),
        Preview = [0, 0, 255, 255, 0, 248, 224, 7],
        PreviewWidth = 2,
        PreviewHeight = 2,
        VolumeMl = 0.0006f,
        MinX = -3,
        MinY = 2,
        MaxX = 0,
        MaxY = 4,
    };

    private static uint U(byte[] bytes, int offset) => BitConverter.ToUInt32(bytes, offset);
    private static float F(byte[] bytes, int offset) => BitConverter.ToSingle(bytes, offset);
    private static string Text(byte[] bytes, int offset, int count) => Encoding.ASCII.GetString(bytes, offset, count).TrimEnd('\0');
    private static byte[] DecodePw0(ReadOnlySpan<byte> data)
    {
        var pixels = new List<byte>();
        for (int i = 0; i < data.Length; i++)
        {
            int level = data[i] >> 4, count = data[i] & 15;
            if (level is 0 or 15) count = count * 256 + data[++i];
            pixels.AddRange(Enumerable.Repeat((byte)(level * 17), count));
        }
        return pixels.ToArray();
    }
}
