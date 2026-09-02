using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Danslicer.Core.IO;
using Danslicer.Core.Printers;
using Danslicer.Core.Scene;
using Danslicer.Core.Slicing;

var ci = CultureInfo.InvariantCulture;

if (args.Length == 0)
{
    Usage();
    return 1;
}

switch (args[0])
{
    case "info":
        return Info(args.Skip(1).ToArray());
    case "slice":
        return Slice(args.Skip(1).ToArray());
    case "inspect":
        return Inspect(args.Skip(1).ToArray());
    default:
        Usage();
        return 1;
}

void Usage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  danslicer info <file.stl>");
    Console.Error.WriteLine("  danslicer slice <file.stl>... -o <out.pwmx> [--layer 0.05] [--exposure 2] [--bottom-exposure 30] [--bottom-layers 5] [--no-aa] [--xy 0]");
    Console.Error.WriteLine("  danslicer inspect <file.pwmx>");
}

int Info(string[] a)
{
    if (a.Length < 1) { Usage(); return 1; }
    var mesh = StlReader.Read(a[0]);
    var b = mesh.Bounds;
    var size = b.Size;
    Console.WriteLine($"File:      {a[0]}");
    Console.WriteLine($"Triangles: {mesh.TriangleCount.ToString("N0", ci)}");
    Console.WriteLine($"Vertices:  {mesh.VertexCount.ToString("N0", ci)}");
    Console.WriteLine($"Min:       {F(b.Min.X)}, {F(b.Min.Y)}, {F(b.Min.Z)}");
    Console.WriteLine($"Max:       {F(b.Max.X)}, {F(b.Max.Y)}, {F(b.Max.Z)}");
    Console.WriteLine($"Size:      {F(size.X)} x {F(size.Y)} x {F(size.Z)} mm");
    return 0;
}

int Slice(string[] a)
{
    var inputs = new List<string>();
    string? output = null;
    var settings = PrintSettings.Default;
    for (int i = 0; i < a.Length; i++)
    {
        switch (a[i])
        {
            case "-o": output = a[++i]; break;
            case "--layer": settings = settings with { LayerHeight = P(a[++i]) }; break;
            case "--exposure": settings = settings with { Exposure = P(a[++i]) }; break;
            case "--bottom-exposure": settings = settings with { BottomExposure = P(a[++i]) }; break;
            case "--bottom-layers": settings = settings with { BottomLayers = int.Parse(a[++i], ci) }; break;
            case "--xy": settings = settings with { XyCompensation = P(a[++i]) }; break;
            case "--no-aa": settings = settings with { AntiAliasing = false }; break;
            default: inputs.Add(a[i]); break;
        }
    }
    if (inputs.Count == 0 || output is null) { Usage(); return 1; }

    var printer = PrinterDefinition.PhotonMonoX;
    var objects = new List<SceneObject>();
    foreach (var path in inputs)
    {
        var mesh = StlReader.Read(path);
        var obj = new SceneObject(Path.GetFileNameWithoutExtension(path), mesh);
        var b = mesh.Bounds;
        obj.Transform = Transform.Identity with { Translation = new Vector3(-b.Center.X, -b.Center.Y, -b.Min.Z) };
        objects.Add(obj);
    }

    var sw = Stopwatch.StartNew();
    var lastPercent = -1;
    var progress = new Progress<double>(p =>
    {
        var percent = (int)(p * 100);
        if (percent / 10 != lastPercent / 10) Console.Error.Write($"{percent}% ");
        lastPercent = percent;
    });
    var result = Slicer.Slice(objects, printer, settings, progress);
    Console.Error.WriteLine();
    PhotonWorkshopWriter.Write(result, output);
    sw.Stop();

    Console.WriteLine($"Wrote:      {output}");
    Console.WriteLine($"Layers:     {result.LayerCount} x {F(settings.LayerHeight)} mm = {F(result.PrintHeight)} mm");
    Console.WriteLine($"Volume:     {F(result.VolumeMl)} ml");
    Console.WriteLine($"Est. time:  {TimeSpan.FromSeconds(result.EstimatedSeconds):h\\:mm\\:ss}");
    Console.WriteLine($"Footprint:  X {F(result.MinX)}..{F(result.MaxX)}  Y {F(result.MinY)}..{F(result.MaxY)} mm");
    Console.WriteLine($"Sliced in:  {sw.Elapsed.TotalSeconds:0.0} s");
    return 0;
}

int Inspect(string[] a)
{
    if (a.Length < 1) { Usage(); return 1; }
    var file = PhotonWorkshopFile.Read(a[0]);
    Console.WriteLine($"Version:        {file.Version} ({file.TableCount} tables)");
    Console.WriteLine($"Machine:        {file.MachineName ?? "?"}  image {file.LayerImageFormat ?? "?"}");
    Console.WriteLine($"Resolution:     {file.ResolutionX} x {file.ResolutionY}, pixel {F(file.PixelSizeUm)} µm");
    Console.WriteLine($"Layers:         {file.Layers.Count} x {F(file.LayerHeight)} mm");
    Console.WriteLine($"Exposure:       {F(file.Exposure)} s, bottom {F(file.BottomExposure)} s x {file.BottomLayers}");
    Console.WriteLine($"Light-off:      {F(file.LightOffDelay)} s");
    Console.WriteLine($"Lift:           {F(file.LiftHeight)} mm at {F(file.LiftSpeedMmPerSec * 60)} mm/min, retract {F(file.RetractSpeedMmPerSec * 60)} mm/min");
    Console.WriteLine($"Anti-aliasing:  {file.AntiAliasing}");
    Console.WriteLine($"Volume:         {F(file.VolumeMl)} ml");
    Console.WriteLine($"Print time:     {TimeSpan.FromSeconds(file.PrintTimeSeconds):h\\:mm\\:ss}");
    if (file.Layers.Count > 0)
    {
        var mid = file.Layers.Count / 2;
        var pixels = file.DecodeLayer(mid);
        Console.WriteLine($"Layer {mid}:      {file.Layers[mid].DataLength} bytes RLE, {pixels.Count(p => p != 0)} lit pixels (table says {file.Layers[mid].LitPixels})");
    }
    return 0;
}

float P(string s) => float.Parse(s, CultureInfo.InvariantCulture);
string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
