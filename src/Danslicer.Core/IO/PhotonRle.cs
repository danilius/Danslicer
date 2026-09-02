namespace Danslicer.Core.IO;

/// <summary>
/// Run-length codec for Photon Workshop "pw0Img" layer images. Each pixel keeps its upper four
/// bits (16 grey levels). Runs of black (0) or white (15) use two bytes: the high nibble is the
/// level, the remaining twelve bits the run length (max 4095). Other levels use one byte: high
/// nibble level, low nibble run length (max 15). Pixels are row-major, no checksum.
/// </summary>
public static class PhotonRle
{
    private const int LongRunLimit = 0xFFF;
    private const int ShortRunLimit = 0xF;

    public static byte[] Encode(ReadOnlySpan<byte> pixels)
    {
        var output = new List<byte>(Math.Max(256, pixels.Length / 64));
        var lastLevel = -1;
        var runLength = 0;

        for (int i = 0; i < pixels.Length; i++)
        {
            var level = pixels[i] >> 4;
            if (level == lastLevel)
            {
                runLength++;
                continue;
            }
            Flush(output, lastLevel, runLength);
            lastLevel = level;
            runLength = 1;
        }
        Flush(output, lastLevel, runLength);
        return output.ToArray();
    }

    private static void Flush(List<byte> output, int level, int runLength)
    {
        while (runLength > 0)
        {
            if (level == 0 || level == 0xF)
            {
                var run = Math.Min(runLength, LongRunLimit);
                var word = (ushort)((level << 12) | run);
                output.Add((byte)(word >> 8));
                output.Add((byte)word);
                runLength -= run;
            }
            else
            {
                var run = Math.Min(runLength, ShortRunLimit);
                output.Add((byte)((level << 4) | run));
                runLength -= run;
            }
        }
    }

    /// <summary>Decodes into <paramref name="pixels"/>, which must hold exactly width * height bytes.</summary>
    public static void Decode(ReadOnlySpan<byte> encoded, Span<byte> pixels)
    {
        var pos = 0;
        for (int i = 0; i < encoded.Length; i++)
        {
            var b = encoded[i];
            var level = b >> 4;
            int run;
            byte value;
            if (level == 0 || level == 0xF)
            {
                if (i + 1 >= encoded.Length) throw new InvalidDataException("Truncated run.");
                run = ((b & 0xF) << 8) | encoded[++i];
                value = level == 0 ? (byte)0 : (byte)255;
            }
            else
            {
                run = b & 0xF;
                value = (byte)((level << 4) | level);
            }
            if (pos + run > pixels.Length)
                throw new InvalidDataException($"Run overflows image: {pos} + {run} > {pixels.Length}.");
            if (value != 0) pixels.Slice(pos, run).Fill(value);
            else pixels.Slice(pos, run).Clear();
            pos += run;
        }
        if (pos != pixels.Length)
            throw new InvalidDataException($"Image ended short: {pos} of {pixels.Length} pixels.");
    }
}
