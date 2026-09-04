using System.Numerics;

namespace Danslicer.Render;

/// <summary>
/// Packs draw IDs into the RGB channels of the RGBA8 ID buffer. 24 bits of ID; the alpha channel
/// carries the selection flag. ID 0 is reserved for the background.
/// </summary>
public static class DeferredIds
{
    public const int MaxId = 0xFFFFFF;

    /// <summary>Encodes an ID as the RGB uniform written by the geometry pass.</summary>
    public static Vector3 Pack(int id)
    {
        var v = (uint)Math.Clamp(id, 0, MaxId);
        return new Vector3((v & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, ((v >> 16) & 0xFF) / 255f);
    }

    /// <summary>Decodes an ID from RGBA8 bytes read back from the ID buffer.</summary>
    public static int Unpack(byte r, byte g, byte b) => r | (g << 8) | (b << 16);
}
