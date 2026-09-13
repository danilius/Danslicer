using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Danslicer.Core.Geometry;
using Danslicer.Core.Printers;
using Danslicer.Core.Scene;
using Danslicer.Core.Slicing;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Rafts;

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
    public const int CurrentMinorVersion = 1; // 1.1: per-object raft (2026-09-09)

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
            PrintSettings = PrintSettingsDto.From(document.PrintSettings),
            ResinSettings = document.ResinSettings,
            ResinPresetId = document.ResinPreset.Id,
            ResinPreset = document.ResinPreset,
            PrinterId = document.Printer.Id,
            Printer = document.Printer,
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

    /// <summary>
    /// Mini supports were removed (user decision 2026-09-07), but old project files may still
    /// carry them. Their segments are never added to the graph; this drops what they leave
    /// behind: a tip node with no member, a branch end that fed only minis (a bare ball on a
    /// stalk) with its branch, and then a trunk top left holding nothing, with its trunk and
    /// an orphaned base, so no support stands with no tip on it.
    /// </summary>
    private static void DropMiniSupportRemnants(SupportGraph graph, IEnumerable<SegmentDto> minis)
    {
        foreach (var mini in minis)
        {
            if (!graph.TryGetNode(mini.NodeA, out var a) || !graph.TryGetNode(mini.NodeB, out var b))
                continue;
            var (tip, carrier) = a.Type == SupportNodeType.Tip ? (a, b) : (b, a);
            if (graph.SegmentsAt(tip.Id).Count == 0) graph.RemoveNode(tip.Id);
            DropBareStalk(graph, carrier.Id, SupportSegmentType.Branch);
        }
    }

    /// <summary>
    /// Removes <paramref name="nodeId"/> when its only remaining member is one
    /// <paramref name="stalkType"/> segment, then repeats down that member: a branch end feeding
    /// nothing takes its branch; a trunk top then left with only its trunk takes the trunk; a
    /// base left with no member goes too.
    /// </summary>
    private static void DropBareStalk(SupportGraph graph, Guid nodeId, SupportSegmentType stalkType)
    {
        if (!graph.TryGetNode(nodeId, out var node)) return;
        var members = graph.SegmentsAt(nodeId);
        if (node.Type == SupportNodeType.Base)
        {
            if (members.Count == 0) graph.RemoveNode(nodeId);
            return;
        }
        if (members.Count != 1 || members[0].Type != stalkType) return;
        var stalk = members[0];
        var farId = stalk.NodeA == nodeId ? stalk.NodeB : stalk.NodeA;
        graph.RemoveNode(nodeId);
        DropBareStalk(graph, farId, SupportSegmentType.Trunk);
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
            PrintSettings = manifest.PrintSettings?.ToSettings() ?? PrintSettings.Default,
            ResinSettings = (manifest.ResinSettings ?? manifest.PrintSettings?.ToLegacyResinSettings() ??
                             ResinSettings.Default).Normalize(),
            ResinPreset = ResolveResinPreset(manifest),
            Printer = ResolvePrinter(manifest),
        };
        foreach (var dto in manifest.Objects)
            document.Scene.Add(dto.ToObject(meshes[dto.Mesh]));
        foreach (var node in manifest.SupportGraph.Nodes)
            document.Supports.AddNode(node.ToNode());
        // Old files may carry mini supports (removed 2026-09-07); those segments are dropped.
        foreach (var segment in manifest.SupportGraph.Segments.Where(segment => !segment.IsMiniSupport))
            document.Supports.AddSegment(segment.ToSegment());
        DropMiniSupportRemnants(document.Supports,
            manifest.SupportGraph.Segments.Where(segment => segment.IsMiniSupport));
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
        public PrintSettingsDto? PrintSettings { get; set; }
        public ResinSettings? ResinSettings { get; set; }
        public string? ResinPresetId { get; set; }
        public ResinPreset? ResinPreset { get; set; }
        public string? PrinterId { get; set; }
        public PrinterDefinition? Printer { get; set; }
        public ViewStateDto? ViewState { get; set; }
    }

    private static PrinterDefinition ResolvePrinter(ManifestDto manifest)
    {
        if (manifest.Printer is null || string.IsNullOrWhiteSpace(manifest.PrinterId))
            return PrinterDefinition.PhotonMonoX;
        var embedded = manifest.Printer.Normalize();
        return string.Equals(embedded.Id, manifest.PrinterId, StringComparison.OrdinalIgnoreCase)
            ? embedded
            : PrinterDefinition.PhotonMonoX;
    }

    private static ResinPreset ResolveResinPreset(ManifestDto manifest)
    {
        if (manifest.ResinPreset is null || string.IsNullOrWhiteSpace(manifest.ResinPresetId))
            return ResinPreset.Default;
        var embedded = manifest.ResinPreset.Normalize();
        return string.Equals(embedded.Id, manifest.ResinPresetId, StringComparison.OrdinalIgnoreCase)
            ? embedded
            : ResinPreset.Default;
    }

    /// <summary>
    /// Explicit project DTO keeps old version-1 files readable after the resin split. Legacy resin
    /// properties are read from printSettings but are never emitted by current saves.
    /// </summary>
    private sealed class PrintSettingsDto
    {
        public float LayerHeight { get; set; } = PrintSettings.Default.LayerHeight;
        public bool AntiAliasing { get; set; } = PrintSettings.Default.AntiAliasing;
        public float XyCompensation { get; set; } = PrintSettings.Default.XyCompensation;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? BottomLayers { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float? BottomExposure { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float? Exposure { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float? LightOffDelay { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float? LiftHeight { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float? LiftSpeed { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float? RetractSpeed { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float? BottomLiftHeight { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float? BottomLiftSpeed { get; set; }

        public static PrintSettingsDto From(PrintSettings settings) => new()
        {
            LayerHeight = settings.LayerHeight,
            AntiAliasing = settings.AntiAliasing,
            XyCompensation = settings.XyCompensation,
        };

        public PrintSettings ToSettings() => new()
        {
            LayerHeight = LayerHeight,
            AntiAliasing = AntiAliasing,
            XyCompensation = XyCompensation,
        };

        public ResinSettings? ToLegacyResinSettings()
        {
            if (BottomLayers is null && BottomExposure is null && Exposure is null &&
                LightOffDelay is null && LiftHeight is null && LiftSpeed is null &&
                RetractSpeed is null && BottomLiftHeight is null && BottomLiftSpeed is null)
                return null;
            var defaults = ResinSettings.Default;
            return defaults with
            {
                BottomLayers = BottomLayers ?? defaults.BottomLayers,
                BottomExposure = BottomExposure ?? defaults.BottomExposure,
                Exposure = Exposure ?? defaults.Exposure,
                LightOffDelay = LightOffDelay ?? defaults.LightOffDelay,
                LiftHeight = LiftHeight ?? defaults.LiftHeight,
                LiftSpeed = LiftSpeed ?? defaults.LiftSpeed,
                RetractSpeed = RetractSpeed ?? defaults.RetractSpeed,
                BottomLiftHeight = BottomLiftHeight ?? defaults.BottomLiftHeight,
                BottomLiftSpeed = BottomLiftSpeed ?? defaults.BottomLiftSpeed,
            };
        }
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

        /// <summary>Painted support region, face indices into this object's mesh (DESIGN 8.3).
        /// Omitted when empty, so a project saved before regions existed loads as "every face"
        /// and generates exactly as it always did.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<int>? RegionFaces { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<int>? KeepCleanFaces { get; set; }

        /// <summary>File the object was imported from, for the object list's update button.
        /// Omitted when there is none, so older projects load unchanged.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SourcePath { get; set; }

        /// <summary>The raft under the object's supports (format 1.1). Omitted when there is
        /// none, so a 1.0 project loads unchanged.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public RaftParameters? Raft { get; set; }

        public static ObjectDto From(SceneObject obj, string mesh) => new()
        {
            Id = obj.Id,
            Name = obj.Name,
            Mesh = mesh,
            Translation = Vector3Dto.From(obj.Transform.Translation),
            Rotation = QuaternionDto.From(obj.Transform.Rotation),
            Scale = Vector3Dto.From(obj.Transform.Scale),
            RenderState = obj.RenderState,
            // Sorted so a saved project is stable byte-for-byte: a HashSet's order is not.
            RegionFaces = obj.Regions.Faces.Count == 0 ? null : [.. obj.Regions.Faces.Order()],
            KeepCleanFaces = obj.Regions.KeepCleanFaces.Count == 0
                ? null
                : [.. obj.Regions.KeepCleanFaces.Order()],
            SourcePath = obj.SourcePath,
            Raft = obj.Raft,
        };

        public SceneObject ToObject(Mesh mesh) => new(Name, mesh, Id)
        {
            Transform = new Transform(Translation.ToVector3(), Rotation.ToQuaternion(), Scale.ToVector3()),
            RenderState = RenderState,
            Regions = ObjectSupportRegions.From(RegionFaces, KeepCleanFaces),
            SourcePath = SourcePath,
            Raft = Raft?.Normalize(),
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
        public float TipNormalLeadIn { get; set; }
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
            TipDiameter = node.TipDiameter, TipNormalLeadIn = node.TipNormalLeadIn,
            PenetrationDepth = node.PenetrationDepth,
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
            TipNormalLeadIn = TipNormalLeadIn,
            PenetrationDepth = PenetrationDepth, ContactObjectId = ContactObjectId,
            TipShape = TipShape, ConeLength = ConeLength, BallDiameter = BallDiameter,
            BaseShape = BaseShape, BaseDiameter = BaseDiameter, BaseHeight = BaseHeight,
            BaseConeHeight = BaseConeHeight,
        };
    }

    private sealed class SegmentDto
    {
        /// <summary>Segment type written by the removed mini-support feature; still found in old files.</summary>
        private const string MiniSupportTypeName = "miniSupport";

        public Guid Id { get; set; }
        /// <summary>
        /// The segment type as its camel-case name, kept as a string so a type this version no
        /// longer has (<see cref="MiniSupportTypeName"/>) can be recognised and skipped instead of
        /// failing the whole load.
        /// </summary>
        public string Type { get; set; } = "";
        public Guid NodeA { get; set; }
        public Guid NodeB { get; set; }
        public float Diameter { get; set; }
        public OriginDto Origin { get; set; } = new();
        public bool Pinned { get; set; }
        public bool Hidden { get; set; }
        public bool Disabled { get; set; }

        [JsonIgnore]
        public bool IsMiniSupport =>
            string.Equals(Type, MiniSupportTypeName, StringComparison.OrdinalIgnoreCase);

        public static SegmentDto From(SupportSegment segment) => new()
        {
            Id = segment.Id, Type = JsonNamingPolicy.CamelCase.ConvertName(segment.Type.ToString()),
            NodeA = segment.NodeA, NodeB = segment.NodeB,
            Diameter = segment.Diameter, Origin = OriginDto.From(segment.Origin),
            Pinned = segment.Pinned, Hidden = segment.Hidden, Disabled = segment.Disabled,
        };

        public SupportSegment ToSegment() => new()
        {
            Id = Id, Type = ParseType(), NodeA = NodeA, NodeB = NodeB, Diameter = Diameter,
            Origin = Origin.ToOrigin(), Pinned = Pinned, Hidden = Hidden, Disabled = Disabled,
        };

        private SupportSegmentType ParseType() =>
            !string.IsNullOrEmpty(Type) && !char.IsDigit(Type[0]) &&
            Enum.TryParse<SupportSegmentType>(Type, ignoreCase: true, out var type) &&
            Enum.IsDefined(type)
                ? type
                : throw new InvalidDataException($"Unknown support segment type '{Type}'.");
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
