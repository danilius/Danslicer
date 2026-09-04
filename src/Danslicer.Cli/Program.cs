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
    case "route":
        return RouteCommand.Run(args.Skip(1).ToArray());
    case "tips":
        return Danslicer.Cli.TipsCommand.Run(args.Skip(1).ToArray());
    case "checks":
        return Danslicer.Cli.ChecksCommand.Run(args.Skip(1).ToArray());
    case "areas":
        return Danslicer.Cli.AreasCommand.Run(args.Skip(1).ToArray());
    case "bench":
        return Danslicer.Cli.BenchCommand.Run(args.Skip(1).ToArray());
    default:
        Usage();
        return 1;
}

void Usage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  danslicer info <file.stl|file.obj|file.danslicer>");
    Console.Error.WriteLine("  danslicer slice <file.stl|file.obj>... | <file.danslicer> -o <out.pwmx> [--layer 0.05] [--exposure 2] [--bottom-exposure 30] [--bottom-layers 5] [--no-aa] [--xy 0]");
    Console.Error.WriteLine("  danslicer inspect <file.pwmx>");
    Console.Error.WriteLine("  danslicer route <mesh.stl|mesh.obj> --tips <tips.json> [--seat] [--strategy grid|topdown|tree] [--base-grid on|off] [--island-first on|off] [--reinforce on|off] [--step-height 2] [--spacing 5] [--lattice square|hex] [--offset-x 0] [--offset-y 0] [--rotation 0] [--snap 0.25] [--seed 1] [--json]");
    Console.Error.WriteLine("  danslicer tips <file.stl|file.obj> [--json] [--seat] [--spacing 2.5] [--min-spacing 2.5]");
    Console.Error.WriteLine("                 [--overhang 45] [--min-island 0.5] [--layer 0.05] [--tip 0.4]");
    Console.Error.WriteLine("                 [--tip-shape capsule|cone] [--cone-length 2] [--ball-diameter 0] [--penetration-depth 0]");
    Console.Error.WriteLine("                 [--edge 0] [--force-edges] [--sharp-edge 30] [--seed 0]");
    Console.Error.WriteLine("                 [--grid square|hex] [--grid-spacing 5] [--grid-offset-x 0] [--grid-offset-y 0]");
    Console.Error.WriteLine("                 [--grid-rotation 0] [--keep-clean-distance 0]");
    Console.Error.WriteLine("  danslicer checks <file.stl|file.obj>... [--json] [--seat] [--layer 0.05] [--min-island 0.5]");
    Console.Error.WriteLine("                   [--overhang 45] [--min-suction 5] [--drain 0.8]");
    Console.Error.WriteLine("                   [--support-spacing 1] [--model-clearance 0.5] [--object-spacing 1]");
    Console.Error.WriteLine("  danslicer checks <file.danslicer> --islands-after-supports [--json] [--layer 0.05] [--min-island 0.5]");
    Console.Error.WriteLine("  danslicer areas <file.stl|file.obj> [--json] [--seat] [--overhang 45] [--min-area 0.5]");
    Console.Error.WriteLine("                  [--layer 0.05] [--min-island 0.5] [--sharp-edge 30]");
    Console.Error.WriteLine("  danslicer bench [--drogon <path>] [--gripper <path>] [--reinforce on|off] [--output <summary.json>]");
}

int Info(string[] a)
{
    if (a.Length < 1) { Usage(); return 1; }
    if (ProjectFile.IsProjectPath(a[0]))
    {
        var project = ProjectFile.Load(a[0]).Document;
        var meshes = new HashSet<Danslicer.Core.Geometry.Mesh>(ReferenceEqualityComparer.Instance);
        foreach (var obj in project.Scene.Objects) meshes.Add(obj.Mesh);
        Console.WriteLine($"File:             {a[0]}");
        Console.WriteLine($"Project objects:  {project.Scene.Objects.Count.ToString("N0", ci)}");
        Console.WriteLine($"Unique meshes:    {meshes.Count.ToString("N0", ci)}");
        Console.WriteLine($"Triangles:        {meshes.Sum(mesh => mesh.TriangleCount).ToString("N0", ci)}");
        Console.WriteLine($"Support nodes:    {project.Supports.NodeCount.ToString("N0", ci)}");
        Console.WriteLine($"Support segments: {project.Supports.SegmentCount.ToString("N0", ci)}");
        Console.WriteLine($"Layer height:     {F(project.PrintSettings.LayerHeight)} mm");
        return 0;
    }
    var mesh = MeshFile.Read(a[0]);
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
    float? layerHeight = null;
    float? exposure = null;
    float? bottomExposure = null;
    int? bottomLayers = null;
    float? xyCompensation = null;
    var noAntiAliasing = false;
    for (int i = 0; i < a.Length; i++)
    {
        switch (a[i])
        {
            case "-o": output = a[++i]; break;
            case "--layer": layerHeight = P(a[++i]); break;
            case "--exposure": exposure = P(a[++i]); break;
            case "--bottom-exposure": bottomExposure = P(a[++i]); break;
            case "--bottom-layers": bottomLayers = int.Parse(a[++i], ci); break;
            case "--xy": xyCompensation = P(a[++i]); break;
            case "--no-aa": noAntiAliasing = true; break;
            default: inputs.Add(a[i]); break;
        }
    }
    if (inputs.Count == 0 || output is null) { Usage(); return 1; }

    var printer = PrinterDefinition.PhotonMonoX;
    List<SceneObject> objects;
    Danslicer.Core.Supports.SupportGraph? supports = null;
    PrintSettings settings;
    ResinSettings resin;
    var projectInputs = inputs.Where(ProjectFile.IsProjectPath).ToList();
    if (projectInputs.Count > 0)
    {
        if (inputs.Count != 1)
        {
            Console.Error.WriteLine("error: a project file cannot be combined with mesh inputs.");
            return 1;
        }
        var project = ProjectFile.Load(projectInputs[0]).Document;
        printer = project.Printer;
        objects = project.Scene.Objects.ToList();
        supports = project.Supports;
        settings = project.PrintSettings;
        resin = project.ResinSettings;
    }
    else
    {
        objects = [];
        settings = PrintSettings.Default;
        resin = ResinSettings.Default;
        foreach (var path in inputs)
        {
            var mesh = MeshFile.Read(path);
            var obj = new SceneObject(Path.GetFileNameWithoutExtension(path), mesh);
            var b = mesh.Bounds;
            obj.Transform = Transform.Identity with { Translation = new Vector3(-b.Center.X, -b.Center.Y, -b.Min.Z) };
            objects.Add(obj);
        }
    }
    settings = settings with
    {
        LayerHeight = layerHeight ?? settings.LayerHeight,
        XyCompensation = xyCompensation ?? settings.XyCompensation,
        AntiAliasing = noAntiAliasing ? false : settings.AntiAliasing,
    };
    resin = resin with
    {
        Exposure = exposure ?? resin.Exposure,
        BottomExposure = bottomExposure ?? resin.BottomExposure,
        BottomLayers = bottomLayers ?? resin.BottomLayers,
    };

    var sw = Stopwatch.StartNew();
    var lastPercent = -1;
    var progress = new Progress<double>(p =>
    {
        var percent = (int)(p * 100);
        if (percent / 10 != lastPercent / 10) Console.Error.Write($"{percent}% ");
        lastPercent = percent;
    });
    SliceResult result;
    try
    {
        result = Slicer.Slice(objects, printer, settings, progress, supports: supports,
            resinSettings: resin);
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine($"error: {ex.Message}");
        return 2;
    }
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
