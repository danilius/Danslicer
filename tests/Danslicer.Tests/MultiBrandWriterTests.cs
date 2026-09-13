using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Danslicer.Core.IO;
using Danslicer.Core.Printers;
using Danslicer.Core.Slicing;

namespace Danslicer.Tests;

public sealed class MultiBrandWriterTests
{
    public static IEnumerable<object[]> Containers => PrinterCatalog.BuiltIn.Where(p => p.NativeFormat != "photon-workshop")
        .GroupBy(p => (p.NativeFormat, p.FileExtension)).Select(g => new object[] { g.First() });

    [Theory, MemberData(nameof(Containers))]
    public void EveryContainerCanWriteSeekableOutputAndReplaceLongerContent(PrinterDefinition printer)
    {
        var r = Fixture(printer); using var output = new MemoryStream(); output.SetLength(2_000_000);
        NativePrintWriter.Write(r, output);
        Assert.Equal(output.Position, output.Length); Assert.InRange(output.Length, 50, 1_999_999);
        if (printer.NativeFormat is "sl1" or "cws" or "cws-rgb" or "chitu-zip")
        {
            using var archive = new ZipArchive(output, ZipArchiveMode.Read, true);
            Assert.Equal(2, archive.Entries.Count(e => e.Name.EndsWith(".png") && char.IsDigit(e.Name[^5]) && !e.FullName.Contains("thumbnail") && !e.Name.StartsWith("preview")));
        }
    }

    [Fact]
    public void ExistingPhotonDispatchKeepsIdenticalBytes()
    {
        var r = Fixture(PrinterDefinition.PhotonMonoX); using var old = new MemoryStream(); using var native = new MemoryStream();
        PhotonWorkshopWriter.Write(r, old); NativePrintWriter.Write(r, native);
        Assert.Equal(old.ToArray(), native.ToArray());
    }

    [Fact]
    public void CtbVariantsHaveDifferentMagicAndEncryptedHeaderContainsTheJob()
    {
        var plain = PrinterCatalog.BuiltIn.First(p => p.NativeFormat == "ctb");
        var encrypted = PrinterCatalog.BuiltIn.First(p => p.NativeFormat == "ctb-encrypted");
        using var a = new MemoryStream(); using var b = new MemoryStream();
        NativePrintWriter.Write(Fixture(plain), a); NativePrintWriter.Write(Fixture(encrypted), b);
        Assert.Equal(0x12fd0086u, BinaryPrimitives.ReadUInt32LittleEndian(a.ToArray()));
        var bytes = b.ToArray(); Assert.Equal(0x12fd0107u, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
        using var aes = Aes.Create(); aes.Key = Convert.FromHexString("D05B8E3371DE3D1AE54F22DDDF5BFD94AB5D643A9D7EBFAF4203F310D8522AEA");
        var header = aes.DecryptCbc(bytes.AsSpan(48, 304), Convert.FromHexString("0F010A05050B060708060A0C0C0D090F"), PaddingMode.None);
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(64)));
        Assert.Equal(20u, BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(56)));
        Assert.Equal(15u, BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(60)));
    }

    [Fact]
    public void FailedEncodingPreservesExistingDestinationAndRemovesTemporaryFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "danslicer-export-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "existing.goo"); File.WriteAllBytes(path, [7, 8, 9]);
        try
        {
            var p = PrinterCatalog.BuiltIn.First(p => p.NativeFormat == "goo");
            var r = Fixture(p, [255]); // Deliberately incomplete internal RLE; catches failure after serialization begins.
            Assert.ThrowsAny<Exception>(() => NativePrintWriter.Write(r, path));
            Assert.Equal(new byte[] { 7, 8, 9 }, File.ReadAllBytes(path)); Assert.Single(Directory.GetFiles(directory));
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }

    [Fact]
    public void FormatIdentitySurvivesCopyAndCannotBeChangedBySuffixAlone()
    {
        var p = PrinterCatalog.BuiltIn.First(p => p.NativeFormat == "ctb-encrypted");
        Assert.Equal("ctb-encrypted", p.CreateUserCopy("Tester").NativeFormat);
        Assert.Throws<NotSupportedException>(() => NativePrintWriter.ValidatePrinter(p with { FileExtension = "goo" }));
        Assert.Throws<NotSupportedException>(() => NativePrintWriter.ValidatePrinter(p with { FormatVersion = 2 }));
    }

    [Fact]
    public void NovaArchiveCarriesRelativeMotionAndBothCureTimes()
    {
        var r = Fixture(PrinterCatalog.BuiltIn.First(p => p.NativeFormat == "cws")); using var output = new MemoryStream();
        NativePrintWriter.Write(r, output); using var archive = new ZipArchive(output, ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("danslicer.gcode")!.Open()); var code = reader.ReadToEnd();
        Assert.Contains("G91", code); Assert.Contains("G1 Z8.05 F60", code); Assert.Contains("G1 Z6.05 F120", code); Assert.Contains("G1 Z-6 F180", code);
        Assert.Contains("M106 S255\n;<Delay> 30000\nM106 S0", code.Replace("\r", ""));
        Assert.Contains("M106 S255\n;<Delay> 3000\nM106 S0", code.Replace("\r", ""));
    }

    private static SliceResult Fixture(PrinterDefinition p, byte[]? malformedRle = null)
    {
        p = p with { ResolutionX = 20, ResolutionY = 15, DisplayWidthMm = 20, DisplayHeightMm = 15, PrintWidthMm = null, PrintHeightMm = null };
        byte[] pixels = new byte[300]; pixels[41] = 255; pixels[42] = 136; pixels[81] = 17;
        return new SliceResult { Printer = p, Settings = PrintSettings.Default,
            ResinSettings = ResinSettings.Default with { BottomLayers = 1, Exposure = 3, BottomExposure = 30, LiftHeight = 6, LiftSpeed = 120, RetractSpeed = 180, BottomLiftHeight = 8, BottomLiftSpeed = 60 },
            Layers = Enumerable.Range(0, 2).Select(i => new SlicedLayer { Rle = malformedRle ?? PhotonRle.Encode(pixels), LitPixels = 3, AreaMm2 = 3, Z = (i + 1) * .05f }).ToArray(),
            Preview = new byte[8], PreviewWidth = 2, PreviewHeight = 2, VolumeMl = .001f, MinX = 0, MinY = 0, MaxX = 3, MaxY = 3 };
    }
}
