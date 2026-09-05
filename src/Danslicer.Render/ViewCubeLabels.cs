namespace Danslicer.Render;

/// <summary>
/// Deterministic 5x7 dot-matrix glyph data for the view cube's face labels (Front/Back/Left/
/// Right/Top/Bottom). Pure data and math, no GL dependency, so it is unit-testable without a
/// context. <see cref="ViewCube"/> turns the "on" cells of each label into small quads baked
/// straight into the cube's own vertex buffer — no texture, no second shader, no sampler
/// differences between the GL and GLES (Avalonia/ANGLE) code paths.
/// </summary>
public static class ViewCubeLabels
{
    public const int GlyphWidth = 5;
    public const int GlyphHeight = 7;

    /// <summary>
    /// Axis/sign/text for each of the cube's six faces. The mapping mirrors
    /// <see cref="Camera"/>'s SetView angles as exercised by ViewCube.ViewAngles: +X=Right,
    /// -X=Left, +Y=Back, -Y=Front, +Z=Top, -Z=Bottom.
    /// </summary>
    public static readonly (int Axis, int Sign, string Text)[] Faces =
    [
        (0, 1, "RIGHT"), (0, -1, "LEFT"),
        (1, 1, "BACK"), (1, -1, "FRONT"),
        (2, 1, "TOP"), (2, -1, "BOTTOM"),
    ];

    // 5x7 dot-matrix font, one byte per scanline (top row first), bit (GlyphWidth-1-col) = column.
    // Only the letters used by Faces above are defined.
    private static readonly Dictionary<char, byte[]> Font = new()
    {
        ['A'] = [0b01110, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001],
        ['B'] = [0b11110, 0b10001, 0b10001, 0b11110, 0b10001, 0b10001, 0b11110],
        ['C'] = [0b01111, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b01111],
        ['E'] = [0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b11111],
        ['F'] = [0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b10000],
        ['G'] = [0b01111, 0b10000, 0b10000, 0b10111, 0b10001, 0b10001, 0b01111],
        ['H'] = [0b10001, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001],
        ['I'] = [0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b11111],
        ['K'] = [0b10001, 0b10010, 0b10100, 0b11000, 0b10100, 0b10010, 0b10001],
        ['L'] = [0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b11111],
        ['M'] = [0b10001, 0b11011, 0b10101, 0b10101, 0b10001, 0b10001, 0b10001],
        ['N'] = [0b10001, 0b11001, 0b10101, 0b10011, 0b10001, 0b10001, 0b10001],
        ['O'] = [0b01110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110],
        ['P'] = [0b11110, 0b10001, 0b10001, 0b11110, 0b10000, 0b10000, 0b10000],
        ['R'] = [0b11110, 0b10001, 0b10001, 0b11110, 0b10100, 0b10010, 0b10001],
        ['T'] = [0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100],
    };

    /// <summary>Lit cells of one glyph, row-major, (0,0) at the top-left.</summary>
    public static IEnumerable<(int Row, int Col)> Glyph(char c)
    {
        if (!Font.TryGetValue(c, out var rows))
            throw new ArgumentOutOfRangeException(nameof(c), c, "No glyph defined for this character.");
        for (var row = 0; row < GlyphHeight; row++)
        for (var col = 0; col < GlyphWidth; col++)
            if ((rows[row] & (1 << (GlyphWidth - 1 - col))) != 0)
                yield return (row, col);
    }

    /// <summary>Pixel size of a rendered string: <see cref="GlyphWidth"/> per char plus a 1px gap.</summary>
    public static (int Width, int Height) Measure(string text) =>
        (text.Length * GlyphWidth + Math.Max(0, text.Length - 1), GlyphHeight);

    /// <summary>Lit cells of a full string, row-major, (0,0) at the top-left of the whole string.</summary>
    public static IEnumerable<(int Row, int Col)> Rasterize(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var colOffset = i * (GlyphWidth + 1);
            foreach (var (row, col) in Glyph(text[i]))
                yield return (row, col + colOffset);
        }
    }

    /// <summary>
    /// Lit cells of a string collapsed into maximal horizontal runs: (row, first column, length).
    /// <see cref="ViewCube"/> emits one quad per run rather than one per cell, so a stroke renders
    /// as a single solid bar instead of a line of separate dots with a gutter between them. At the
    /// cube's on-screen scale a font cell is barely over a pixel wide, which is where the original
    /// dot-per-cell rendering lost its legibility — the gutters ate most of the stroke.
    /// </summary>
    public static IEnumerable<(int Row, int Col, int Length)> Runs(string text)
    {
        var lit = new HashSet<(int, int)>(Rasterize(text));
        var (width, _) = Measure(text);
        for (var row = 0; row < GlyphHeight; row++)
        {
            var col = 0;
            while (col < width)
            {
                if (!lit.Contains((row, col))) { col++; continue; }
                var start = col;
                while (col < width && lit.Contains((row, col))) col++;
                yield return (row, start, col - start);
            }
        }
    }
}
