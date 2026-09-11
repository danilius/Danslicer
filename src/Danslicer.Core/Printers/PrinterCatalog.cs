namespace Danslicer.Core.Printers;

/// <summary>Native Photon Workshop profiles. New profiles are experimental, not print certifications.
/// Format facts and source revisions: docs/ui-refresh/native-printer-writers.md.</summary>
public static class PrinterCatalog
{
    public static IReadOnlyList<PrinterDefinition> BuiltIn { get; } = Array.AsReadOnly(new[]
    {
        PrinterDefinition.PhotonMonoX,
        Profile("mono", "Photon Mono", "pwmo", 515, 82.62f, 130.56f, 165, 1620, 2560, 80, 130),
        Profile("mono-se", "Photon Mono SE", "pwms", 1, 82.62f, 130.56f, 160, 1620, 2560, 80, 130),
        Profile("mono-sq", "Photon Mono SQ", "pmsq", 515, 120, 128, 200, 2400, 2560),
        Profile("mono-4k", "Photon Mono 4K", "pwma", 516, 134.4f, 84, 165, 3840, 2400, 132.9f, 80),
        Profile("m3", "Photon M3", "pm3", 516, 163.84f, 102.4f, 180, 4096, 2560, 163.84f, 102)
            with { MachineName = "Anycubic Photon M3" },
        Profile("m3-max", "Photon M3 Max", "pm3m", 516, 298.08f, 165.6f, 300, 6480, 3600, 298, 164),
        Profile("mono-x-6k", "Photon Mono X 6K", "pwmb", 516, 198.15f, 123.84f, 245, 5760, 3600),
        Profile("mono-x2", "Photon Mono X2", "pmx2", 517, 196.61f, 122.88f, 200, 4096, 2560),
        Profile("mono-x-6ks", "Photon Mono X 6Ks", "px6s", 517, 195.84f, 122.4f, 200, 5760, 3600),
        Profile("m3-plus", "Photon M3 Plus", "pwmb", 517, 198.15f, 123.84f, 245, 5760, 3600),
        Profile("m3-premium", "Photon M3 Premium", "pm3r", 517, 218.88f, 123.12f, 250, 7680, 4320),
        Profile("mono-2", "Photon Mono 2", "pm3n", 517, 143.36f, 89.6f, 165, 4096, 2560, 143.36f, 89.1f),
        Profile("mono-m5", "Photon Mono M5", "pm5", 517, 218.88f, 122.88f, 200, 11520, 5120),
        Profile("mono-m5s", "Photon Mono M5s", "pm5s", 518, 218.88f, 122.88f, 200, 11520, 5120),
        Profile("mono-m5s-pro", "Photon Mono M5s Pro", "m5sp", 518, 223.642f, 126.976f, 200, 13312, 5120),
        Profile("ultra", "Photon Ultra", "dlp", 516, 102.4f, 57.6f, 165, 1280, 720),
        Profile("d2", "Photon D2", "dl2p", 517, 130.56f, 73.44f, 165, 2560, 1440),
    }.Concat(MultiBrandPrinterCatalog.Profiles).ToArray());

    public static bool IsBuiltInId(string id) => BuiltIn.Any(p =>
        string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    private static PrinterDefinition Profile(string id, string name, string extension, uint version,
        float width, float height, float z, int pixelsX, int pixelsY,
        float? printWidth = null, float? printHeight = null) =>
        new($"anycubic-photon-{id}", true, $"Anycubic {name}", name, extension,
            width, height, z, pixelsX, pixelsY, true, false, version)
        {
            PrintWidthMm = printWidth,
            PrintHeightMm = printHeight,
            PerLayerSettings = false,
            MachinePropertyFields = version >= 518 ? 15u : 7u,
        };
}
