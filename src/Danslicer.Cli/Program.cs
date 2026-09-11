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
    case "printers":
        foreach (var printer in PrinterCatalog.BuiltIn)
            Console.WriteLine($"{printer.Id}\t{printer.Name}\t.{printer.FileExtension}\t{printer.NativeFormat}\t{printer.CompatibilityNote}");
        return 0;
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
    Console.Error.WriteLine("  danslicer printers");
    Console.Error.WriteLine("  danslicer slice <mesh|project> -o <output> --printer <profile-id> [slice options]");
    Console.Error.WriteLine("  danslicer info <file.stl|file.obj|file.danslicer>");
    Console.Error.WriteLine("  danslicer slice <file.stl|file.obj>... | <file.danslicer> -o <out.pwmx> [--allow-out-of-bounds] [--layer 0.05] [--exposure 2] [--bottom-exposure 30] [--bottom-layers 5] [--no-aa] [--xy 0]");
    Console.Error.WriteLine("  danslicer inspect <file.pwmx>");
    Console.Error.WriteLine("  danslicer route <mesh.stl|mesh.obj> --tips <tips.json> [--seat] [--strategy grid|topdown|tree] [--base-grid on|off] [--island-first on|off] [--fine-feature-fallback on|off] [--min-member-separation <mm>] [--reinforce on|off] [--step-height 2] [--spacing 5] [--lattice square|hex] [--offset-x 0] [--offset-y 0] [--rotation 0] [--snap 0.25] [--seed 1] [--json]");
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
    Console.Error.WriteLine("  danslicer bench [--drogon <path>] [--gripper <path>] [--reinforce on|off] [--min-member-separation <mm>] [--output <summary.json>]");
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

int Slice(string[] a) => Danslicer.Cli.SliceCommand.Run(a, Usage);

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

string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
