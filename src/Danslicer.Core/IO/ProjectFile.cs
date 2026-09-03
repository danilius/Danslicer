using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Slicing;
using Danslicer.Core.Supports;

namespace Danslicer.Core.IO;

/// <summary>The non-derived UI state stored beside a project document.</summary>
public sealed record ProjectViewState
{
    public Vector3 CameraTarget { get; init; } = new(0, 0, 30);
    public float CameraDistance { get; init; } = 400;
    public float CameraYaw { get; init; } = -MathF.PI / 3;
    public float CameraPitch { get; init; } = MathF.PI / 6;
    public float CameraFovDegrees { get; init; } = 40;
    public bool CameraOrthographic { get; init; }
    public WorkspaceMode WorkspaceMode { get; init; } = WorkspaceMode.Layout;
}

public sealed record ProjectLoadResult(Document Document, ProjectViewState ViewState);

/// <summary>
/// Reads and writes the .danslicer zip container. JSON is deliberately mapped through explicit
/// DTOs: unknown fields are ignored while loading and dropped on re-save, so stale future
/// semantics are never copied into a file that claims to be written by this version.
/// </summary>
public static class ProjectFile
{
    public const string Extension = "danslicer";
    public const int CurrentMajorVersion = 1;
    public const int CurrentMinorVersion = 0;

    private const uint MeshMagic = 0x48534D44; // DMSH, little endian
    private const int MeshVersion = 1;
    private const int MaxMeshItems = 100_000_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static bool IsProjectPath(string path) =>
        string.Equals(Path.GetExtension(path), $".{Extension}", StringComparison.OrdinalIgnoreCase);

    public static void Save(string path, Document document, ProjectViewState viewState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(viewState);

        var meshes = new Dictionary<Mesh, string>(ReferenceEqualityComparer.Instance);
        var meshList = new List<(string Reference, Mesh Mesh)>();
        foreach (var obj in document.Scene.Objects)
        {
            if (meshes.ContainsKey(obj.Mesh)) continue;
            var reference = $"meshes/{meshList.Count:D4}.bin";
            meshes.Add(obj.Mesh, reference);
            meshList.Add((reference, obj.Mesh));
        }

        var manifest = new ManifestDto
        {
            FormatVersion = new VersionDto { Major = CurrentMajorVersion, Minor = CurrentMinorVersion },
            Objects = document.Scene.Objects.Select(obj => ObjectDto.From(obj, meshes[obj.Mesh])).ToList(),
            SupportGraph = SupportGraphDto.From(document.Supports),
            PrintSettings = document.PrintSettings,
            ViewState = ViewStateDto.From(viewState),
        };

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = fullPath + $".tmp-{Guid.NewGuid():N}";
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                var manifestEntry = archive.CreateEntry("project.json", CompressionLevel.Optimal);
                using (var stream = manifestEntry.Open())
                    JsonSerializer.Serialize(stream, manifest, JsonOptions);

                foreach (var (reference, mesh) in meshList)
                {
                    var entry = archive.CreateEntry(reference, CompressionLevel.Optimal);
                    using var stream = entry.Open();
                    WriteMesh(stream, mesh);
                }
            }
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static ProjectLoadResult Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var archive = ZipFile.OpenRead(path);
        var manifestEntry = archive.GetEntry("project.json") ??
            throw new InvalidDataException("Project is missing project.json.");
        ManifestDto manifest;
        try
        {
            using var stream = manifestEntry.Open();
            manifest = JsonSerializer.Deserialize<ManifestDto>(stream, JsonOptions) ??
                throw new InvalidDataException("Project manifest is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Project manifest is invalid: {ex.Message}", ex);
        }

        if (manifest.FormatVersion is null)
            throw new InvalidDataException("Project manifest has no formatVersion.");
        if (manifest.FormatVersion.Major > CurrentMajorVersion)
            throw new InvalidDataException(
                $"Project format {manifest.FormatVersion.Major}.{manifest.FormatVersion.Minor} is newer than supported version {CurrentMajorVersion}.{CurrentMinorVersion}.");
        if (manifest.FormatVersion.Major != CurrentMajorVersion)
            throw new InvalidDataException(
                $"Project format {manifest.FormatVersion.Major}.{manifest.FormatVersion.Minor} is not supported.");

        var meshReferences = manifest.Objects.Select(obj => obj.Mesh).Distinct(StringComparer.Ordinal).ToList();
        var meshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);
        foreach (var reference in meshReferences)
        {
            if (string.IsNullOrWhiteSpace(reference) || !reference.StartsWith("meshes/", StringComparison.Ordinal))
                throw new InvalidDataException($"Invalid mesh reference '{reference}'.");
            var entry = archive.GetEntry(reference) ??
                throw new InvalidDataException($"Project is missing mesh entry '{reference}'.");
            using var stream = entry.Open();
            meshes.Add(reference, ReadMesh(stream));
        }

        var document = new Document
        {
            PrintSettings = manifest.PrintSettings ?? PrintSettings.Default,
        };
        foreach (var dto in manifest.Objects)
            document.Scene.Add(dto.ToObject(meshes[dto.Mesh]));
        foreach (var node in manifest.SupportGraph.Nodes)
            document.Supports.AddNode(node.ToNode());
        foreach (var segment in manifest.SupportGraph.Segments)
            document.Supports.AddSegment(segment.ToSegment());
        document.History.Clear();

        return new ProjectLoadResult(document, (manifest.ViewState ?? new ViewStateDto()).ToViewState());
    }

    private static void WriteMesh(Stream stream, Mesh mesh)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(MeshMagic);
        writer.Write(MeshVersion);
        writer.Write(mesh.Positions.Length);
        writer.Write(mesh.Indices.Length);
        foreach (var position in mesh.Positions)
        {
            writer.Write(position.X);
            writer.Write(position.Y);
            writer.Write(position.Z);
        }
        foreach (var index in mesh.Indices) writer.Write(index);
    }

    private static Mesh ReadMesh(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (reader.ReadUInt32() != MeshMagic)
            throw new InvalidDataException("Project mesh has an invalid header.");
        var version = reader.ReadInt32();
        if (version != MeshVersion)
            throw new InvalidDataException($"Project mesh version {version} is not supported.");
        var positionCount = reader.ReadInt32();
        var indexCount = reader.ReadInt32();
        if (positionCount < 0 || positionCount > MaxMeshItems ||
            indexCount < 0 || indexCount > MaxMeshItems || indexCount % 3 != 0)
            throw new InvalidDataException("Project mesh has invalid dimensions.");

        var positions = new Vector3[positionCount];
        for (var i = 0; i < positions.Length; i++)
            positions[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        var indices = new int[indexCount];
        for (var i = 0; i < indices.Length; i++)
        {
            indices[i] = reader.ReadInt32();
            if ((uint)indices[i] >= (uint)positionCount)
                throw new InvalidDataException($"Project mesh index {indices[i]} is out of range.");
        }
        return new Mesh(positions, indices);
    }

    private sealed class ManifestDto
    {
        public VersionDto? FormatVersion { get; set; }
        public List<ObjectDto> Objects { get; set; } = [];
        public SupportGraphDto SupportGraph { get; set; } = new();
        public PrintSettings? PrintSettings { get; set; }
        public ViewStateDto? ViewState { get; set; }
    }

    private sealed class VersionDto
    {
        public int Major { get; set; }
        public int Minor { get; set; }
    }

    private sealed class ObjectDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "Object";
        public string Mesh { get; set; } = "";
        public Vector3Dto Translation { get; set; } = new();
        public QuaternionDto Rotation { get; set; } = new() { W = 1 };
        public Vector3Dto Scale { get; set; } = new() { X = 1, Y = 1, Z = 1 };
        public RenderState RenderState { get; set; }

        public static ObjectDto From(SceneObject obj, string mesh) => new()
        {
            Id = obj.Id,
            Name = obj.Name,
            Mesh = mesh,
            Translation = Vector3Dto.From(obj.Transform.Translation),
            Rotation = QuaternionDto.From(obj.Transform.Rotation),
            Scale = Vector3Dto.From(obj.Transform.Scale),
            RenderState = obj.RenderState,
        };

        public SceneObject ToObject(Mesh mesh) => new(Name, mesh, Id)
        {
            Transform = new Transform(Translation.ToVector3(), Rotation.ToQuaternion(), Scale.ToVector3()),
            RenderState = RenderState,
        };
    }

    private sealed class SupportGraphDto
    {
        public List<NodeDto> Nodes { get; set; } = [];
        public List<SegmentDto> Segments { get; set; } = [];

        public static SupportGraphDto From(SupportGraph graph) => new()
        {
            Nodes = graph.Nodes.OrderBy(node => node.Id).Select(NodeDto.From).ToList(),
            Segments = graph.Segments.OrderBy(segment => segment.Id).Select(SegmentDto.From).ToList(),
        };
    }

    private sealed class NodeDto
    {
        public Guid Id { get; set; }
        public SupportNodeType Type { get; set; }
        public Vector3Dto Position { get; set; } = new();
        public OriginDto Origin { get; set; } = new();
        public bool Pinned { get; set; }
        public bool Hidden { get; set; }
        public bool Disabled { get; set; }
        public Vector3Dto SurfaceNormal { get; set; } = new() { Z = 1 };
        public float TipDiameter { get; set; }
        public float PenetrationDepth { get; set; }
        public Guid? ContactObjectId { get; set; }
        public SupportTipShape TipShape { get; set; }
        public float ConeLength { get; set; }
        public float BallDiameter { get; set; }
        public SupportBaseShape BaseShape { get; set; }
        public float BaseDiameter { get; set; }
        public float BaseHeight { get; set; }
        public float BaseConeHeight { get; set; }

        public static NodeDto From(SupportNode node) => new()
        {
            Id = node.Id, Type = node.Type, Position = Vector3Dto.From(node.Position),
            Origin = OriginDto.From(node.Origin), Pinned = node.Pinned, Hidden = node.Hidden,
            Disabled = node.Disabled, SurfaceNormal = Vector3Dto.From(node.SurfaceNormal),
            TipDiameter = node.TipDiameter, PenetrationDepth = node.PenetrationDepth,
            ContactObjectId = node.ContactObjectId, TipShape = node.TipShape,
            ConeLength = node.ConeLength, BallDiameter = node.BallDiameter,
            BaseShape = node.BaseShape, BaseDiameter = node.BaseDiameter,
            BaseHeight = node.BaseHeight, BaseConeHeight = node.BaseConeHeight,
        };

        public SupportNode ToNode() => new()
        {
            Id = Id, Type = Type, Position = Position.ToVector3(), Origin = Origin.ToOrigin(),
            Pinned = Pinned, Hidden = Hidden, Disabled = Disabled,
            SurfaceNormal = SurfaceNormal.ToVector3(), TipDiameter = TipDiameter,
            PenetrationDepth = PenetrationDepth, ContactObjectId = ContactObjectId,
            TipShape = TipShape, ConeLength = ConeLength, BallDiameter = BallDiameter,
            BaseShape = BaseShape, BaseDiameter = BaseDiameter, BaseHeight = BaseHeight,
            BaseConeHeight = BaseConeHeight,
        };
    }

    private sealed class SegmentDto
    {
        public Guid Id { get; set; }
        public SupportSegmentType Type { get; set; }
        public Guid NodeA { get; set; }
        public Guid NodeB { get; set; }
        public float Diameter { get; set; }
        public OriginDto Origin { get; set; } = new();
        public bool Pinned { get; set; }
        public bool Hidden { get; set; }
        public bool Disabled { get; set; }

        public static SegmentDto From(SupportSegment segment) => new()
        {
            Id = segment.Id, Type = segment.Type, NodeA = segment.NodeA, NodeB = segment.NodeB,
            Diameter = segment.Diameter, Origin = OriginDto.From(segment.Origin),
            Pinned = segment.Pinned, Hidden = segment.Hidden, Disabled = segment.Disabled,
        };

        public SupportSegment ToSegment() => new()
        {
            Id = Id, Type = Type, NodeA = NodeA, NodeB = NodeB, Diameter = Diameter,
            Origin = Origin.ToOrigin(), Pinned = Pinned, Hidden = Hidden, Disabled = Disabled,
        };
    }

    private sealed class OriginDto
    {
        public Guid RegionId { get; set; }
        public int Pass { get; set; }
        public Guid? ObjectId { get; set; }

        public static OriginDto From(SupportOrigin origin) => new()
            { RegionId = origin.RegionId, Pass = origin.Pass, ObjectId = origin.ObjectId };
        public SupportOrigin ToOrigin() => new(RegionId, Pass, ObjectId);
    }

    private sealed class ViewStateDto
    {
        public Vector3Dto CameraTarget { get; set; } = new() { Z = 30 };
        public float CameraDistance { get; set; } = 400;
        public float CameraYaw { get; set; } = -MathF.PI / 3;
        public float CameraPitch { get; set; } = MathF.PI / 6;
        public float CameraFovDegrees { get; set; } = 40;
        public bool CameraOrthographic { get; set; }
        public WorkspaceMode WorkspaceMode { get; set; } = WorkspaceMode.Layout;

        public static ViewStateDto From(ProjectViewState state) => new()
        {
            CameraTarget = Vector3Dto.From(state.CameraTarget), CameraDistance = state.CameraDistance,
            CameraYaw = state.CameraYaw, CameraPitch = state.CameraPitch,
            CameraFovDegrees = state.CameraFovDegrees,
            CameraOrthographic = state.CameraOrthographic, WorkspaceMode = state.WorkspaceMode,
        };

        public ProjectViewState ToViewState() => new()
        {
            CameraTarget = CameraTarget.ToVector3(), CameraDistance = CameraDistance,
            CameraYaw = CameraYaw, CameraPitch = CameraPitch,
            CameraFovDegrees = CameraFovDegrees,
            CameraOrthographic = CameraOrthographic, WorkspaceMode = WorkspaceMode,
        };
    }

    private sealed class Vector3Dto
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public static Vector3Dto From(Vector3 value) => new() { X = value.X, Y = value.Y, Z = value.Z };
        public Vector3 ToVector3() => new(X, Y, Z);
    }

    private sealed class QuaternionDto
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float W { get; set; }

        public static QuaternionDto From(Quaternion value) =>
            new() { X = value.X, Y = value.Y, Z = value.Z, W = value.W };
        public Quaternion ToQuaternion() => new(X, Y, Z, W);
    }
}
