using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Clipper2Lib;
using Danslicer.Core.Geometry;
using Danslicer.Core.IO;
using Danslicer.Core.Slicing;
using Danslicer.Core.Supports.Generation;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: dotnet run --project build/IslandDetectionBenchmark -- <mesh.stl> <output-directory>");
    return 1;
}
Directory.CreateDirectory(args[1]);
var source = StlReader.Read(args[0]);
Console.WriteLine($"Triangles: {source.TriangleCount}");
foreach (var tilt in new[] { 0, 30 })
foreach (var height in new[] { 0.1f, 0.05f })
{
    var rotation = Matrix4x4.CreateRotationX(tilt * MathF.PI / 180);
    var positions = source.Positions.Select(p => Vector3.Transform(p, rotation)).ToArray();
    var minimum = positions.Min(p => p.Z);
    var mesh = new Mesh(positions.Select(p => p - new Vector3(0, 0, minimum)).ToArray(), source.Indices);
    var timer = Stopwatch.StartNew();
    var islands = IslandDetection.FindUnsupported(mesh, null, height, 0.1f, 0, 45);
    var elapsed = timer.Elapsed.TotalSeconds;
    var repeat = IslandDetection.FindUnsupported(mesh, null, height, 0.1f, 0, 45);
    if (!islands.SequenceEqual(repeat)) throw new Exception("Island detection is not deterministic.");
    var layers = MeshSlicer.LayerPolygons(new MeshSlicer.PreparedMesh(mesh, Matrix4x4.Identity), height);
    foreach (var island in islands)
    {
        var point = new Point64(island.X * MeshSlicer.UnitsPerMm, island.Y * MeshSlicer.UnitsPerMm);
        var winding = 0;
        foreach (var path in layers[island.LayerIndex])
            if (Clipper.PointInPolygon(point, path) != PointInPolygonResult.IsOutside)
                winding += Math.Sign(Clipper.Area(path));
        if (winding == 0) throw new Exception("Island marker is outside the solid slice.");
    }
    File.WriteAllText(Path.Combine(args[1], $"tilt-{tilt}-layer-{height}.json"),
        JsonSerializer.Serialize(islands, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
    Console.WriteLine($"Tilt={tilt}, layer={height}: {islands.Count} islands, {elapsed:F2}s, maximum area={islands.Select(i=>i.AreaMm2).DefaultIfEmpty().Max():F3} mm2; deterministic and all markers on solid.");
}
return 0;
