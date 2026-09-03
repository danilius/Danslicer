using Danslicer.Core.Geometry;

namespace Danslicer.Core.IO;

/// <summary>Dispatches mesh loading by file extension. STL is the fallback for unknown extensions.</summary>
public static class MeshFile
{
    /// <summary>Supported extensions, lower-case with the leading dot.</summary>
    public static readonly string[] Extensions = [".stl", ".obj"];

    public static bool IsSupported(string path) =>
        Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static Mesh Read(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".obj" => ObjReader.Read(path),
        _ => StlReader.Read(path),
    };
}
