using System.Text;

namespace Danslicer.Core.IO;

/// <summary>
/// Minimal reader for Photon Workshop files, used to validate our own output and to inspect files
/// from other slicers. Reads the file mark, header and layer table; layer images decode on demand.
/// </summary>
public sealed record PhotonWorkshopFile
{
    public sealed record LayerEntry(uint DataAddress, uint DataLength, float LiftHeight, float LiftSpeed, float Exposure, float LayerHeight, uint LitPixels);

    public uint Version { get; private init; }
    public uint TableCount { get; private init; }
    public float PixelSizeUm { get; private init; }
    public float LayerHeight { get; private init; }
    public float Exposure { get; private init; }
    public float LightOffDelay { get; private init; }
    public float BottomExposure { get; private init; }
    public float BottomLayers { get; private init; }
    public float LiftHeight { get; private init; }
    public float LiftSpeedMmPerSec { get; private init; }
    public float RetractSpeedMmPerSec { get; private init; }
    public float VolumeMl { get; private init; }
    public uint AntiAliasing { get; private init; }
    public int ResolutionX { get; private init; }
    public int ResolutionY { get; private init; }
    public uint PrintTimeSeconds { get; private init; }
    public uint HeaderPayloadLength { get; private init; }
    public bool PerLayerSettings { get; private init; }
    public uint MachinePropertyFields { get; private init; }
    public float DisplayWidthMm { get; private init; }
    public float DisplayHeightMm { get; private init; }
    public float ZTravelMm { get; private init; }
    public float PixelPitchXUm { get; private init; }
    public float PixelPitchYUm { get; private init; }
    public string? MachineName { get; private init; }
    public string? LayerImageFormat { get; private init; }
    public IReadOnlyList<LayerEntry> Layers { get; private init; } = Array.Empty<LayerEntry>();

    private byte[] _data = Array.Empty<byte>();

    public static PhotonWorkshopFile Read(string path) => Read(File.ReadAllBytes(path));

    public static PhotonWorkshopFile Read(byte[] data)
    {
        RequireRange(data, 0, 48);
        using var ms = new MemoryStream(data);
        var r = new BinaryReader(ms, Encoding.ASCII);

        var mark = ReadFixedString(r, 12);
        if (mark != "ANYCUBIC") throw new InvalidDataException($"Not a Photon Workshop file (mark '{mark}').");
        var version = r.ReadUInt32();
        if (version is not (1 or 515 or 516 or 517 or 518))
            throw new InvalidDataException($"Unsupported Photon Workshop version {version}.");
        var markSize = version switch { <= 515 => 48u, 516 => 52u, 517 => 56u, _ => 64u };
        RequireRange(data, 0, markSize);
        var tableCount = r.ReadUInt32();
        var headerAddress = r.ReadUInt32();
        _ = r.ReadUInt32(); // software
        _ = r.ReadUInt32(); // preview
        _ = r.ReadUInt32(); // colour table
        var layerDefAddress = r.ReadUInt32();
        _ = r.ReadUInt32(); // extra
        var machineAddress = version >= 516 ? r.ReadUInt32() : 0u;
        _ = r.ReadUInt32(); // layer images

        if (headerAddress < markSize) throw new InvalidDataException("HEADER overlaps the file mark.");
        RequireRange(data, headerAddress, 16);
        ms.Position = headerAddress;
        var headerName = ReadFixedString(r, 12);
        if (headerName != "HEADER") throw new InvalidDataException($"Expected HEADER table, found '{headerName}'.");
        var headerLength = r.ReadUInt32();
        var minimumHeader = version switch { <= 515 => 80u, 516 => 84u, 517 => 92u, _ => 96u };
        if (headerLength < minimumHeader) throw new InvalidDataException("HEADER is too short for its version.");
        RequireRange(data, headerAddress, 16L + headerLength);
        var file = new PhotonWorkshopFile
        {
            Version = version,
            TableCount = tableCount,
            HeaderPayloadLength = headerLength,
            PixelSizeUm = r.ReadSingle(),
            LayerHeight = r.ReadSingle(),
            Exposure = r.ReadSingle(),
            LightOffDelay = r.ReadSingle(),
            BottomExposure = r.ReadSingle(),
            BottomLayers = r.ReadSingle(),
            LiftHeight = r.ReadSingle(),
            LiftSpeedMmPerSec = r.ReadSingle(),
            RetractSpeedMmPerSec = r.ReadSingle(),
            VolumeMl = r.ReadSingle(),
            AntiAliasing = r.ReadUInt32(),
            ResolutionX = (int)r.ReadUInt32(),
            ResolutionY = (int)r.ReadUInt32(),
        };
        _ = r.ReadSingle(); // weight
        _ = r.ReadSingle(); // price
        _ = r.ReadUInt32(); // currency
        var perLayerSettings = r.ReadUInt32() != 0;
        var printTime = r.ReadUInt32();
        _ = headerLength;

        RequireRange(data, layerDefAddress, 20);
        ms.Position = layerDefAddress;
        var layerDefName = ReadFixedString(r, 12);
        if (layerDefName != "LAYERDEF") throw new InvalidDataException($"Expected LAYERDEF table, found '{layerDefName}'.");
        _ = r.ReadUInt32();
        var layerCount = r.ReadUInt32();
        RequireRange(data, layerDefAddress, 20L + layerCount * 32L);
        var layers = new List<LayerEntry>((int)layerCount);
        for (uint i = 0; i < layerCount; i++)
        {
            var address = r.ReadUInt32();
            var length = r.ReadUInt32();
            RequireRange(data, address, length);
            var lift = r.ReadSingle();
            var liftSpeed = r.ReadSingle();
            var exposure = r.ReadSingle();
            var height = r.ReadSingle();
            var lit = r.ReadUInt32();
            _ = r.ReadUInt32();
            layers.Add(new LayerEntry(address, length, lift, liftSpeed, exposure, height, lit));
        }

        string? machineName = null;
        string? imageFormat = null;
        uint machineFields = 0;
        float width = 0, heightMm = 0, zTravel = 0, pitchX = 0, pitchY = 0;
        if (machineAddress > 0)
        {
            RequireRange(data, machineAddress, version >= 518 ? 224 : 156);
            ms.Position = machineAddress;
            var machineTable = ReadFixedString(r, 12);
            if (machineTable == "MACHINE")
            {
                _ = r.ReadUInt32();
                machineName = ReadFixedString(r, 96);
                imageFormat = ReadFixedString(r, 16);
                if (imageFormat != "pw0Img") throw new InvalidDataException($"Unsupported layer codec '{imageFormat}'.");
                _ = r.ReadUInt32(); // max AA
                machineFields = r.ReadUInt32();
                width = r.ReadSingle(); heightMm = r.ReadSingle(); zTravel = r.ReadSingle();
                _ = r.ReadUInt32(); _ = r.ReadUInt32(); // max version, background
                if (version >= 518) { pitchX = r.ReadSingle(); pitchY = r.ReadSingle(); }
            }
            else throw new InvalidDataException("Expected MACHINE table.");
        }

        if (file.ResolutionX <= 0 || file.ResolutionY <= 0 || (long)file.ResolutionX * file.ResolutionY > int.MaxValue)
            throw new InvalidDataException("Invalid layer resolution.");

        return file with
        {
            PrintTimeSeconds = printTime,
            PerLayerSettings = perLayerSettings,
            MachinePropertyFields = machineFields,
            DisplayWidthMm = width,
            DisplayHeightMm = heightMm,
            ZTravelMm = zTravel,
            PixelPitchXUm = pitchX,
            PixelPitchYUm = pitchY,
            Layers = layers,
            MachineName = machineName,
            LayerImageFormat = imageFormat,
            _data = data,
        };
    }

    public byte[] DecodeLayer(int index)
    {
        var entry = Layers[index];
        var pixels = new byte[ResolutionX * ResolutionY];
        PhotonRle.Decode(_data.AsSpan((int)entry.DataAddress, (int)entry.DataLength), pixels);
        return pixels;
    }

    private static string ReadFixedString(BinaryReader r, int length)
    {
        var bytes = r.ReadBytes(length);
        if (bytes.Length != length) throw new InvalidDataException("Truncated string field.");
        var end = Array.IndexOf(bytes, (byte)0);
        return Encoding.ASCII.GetString(bytes, 0, end < 0 ? bytes.Length : end);
    }

    private static void RequireRange(byte[] data, long address, long length)
    {
        if (address < 0 || length < 0 || address > data.LongLength - length)
            throw new InvalidDataException("Table or image lies outside the file.");
    }
}
