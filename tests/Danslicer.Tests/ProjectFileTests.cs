using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Danslicer.Core;
using Danslicer.Core.Geometry;
using Danslicer.Core.IO;
using Danslicer.Core.Printers;
using Danslicer.Core.Scene;
using Danslicer.Core.Slicing;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Tests;

public sealed class ProjectFileTests
{
    [Fact]
    public void SaveLoadRoundTripsDocumentGraphSettingsAndViewState()
    {
        using var file = new TemporaryProject();
        var document = CompleteDocument(sharedMesh: false);
        var view = new ProjectViewState
        {
            CameraTarget = new Vector3(11.25f, -7.5f, 42), CameraDistance = 123.5f,
            CameraYaw = 0.75f, CameraPitch = -0.2f, CameraFovDegrees = 51,
            CameraOrthographic = true, WorkspaceMode = WorkspaceMode.Support,
        };

        ProjectFile.Save(file.Path, document, view);
        var loaded = ProjectFile.Load(file.Path);

        Assert.Equal(view, loaded.ViewState);
        Assert.False(loaded.Document.History.CanUndo);
        Assert.Equal(document.PrintSettings, loaded.Document.PrintSettings);
        Assert.Equal(document.ResinSettings, loaded.Document.ResinSettings);
        Assert.Equal(document.ResinPreset, loaded.Document.ResinPreset);
        Assert.Equal(document.Printer, loaded.Document.Printer);
        Assert.Equal(document.Scene.Objects.Count, loaded.Document.Scene.Objects.Count);
        for (var i = 0; i < document.Scene.Objects.Count; i++)
        {
            var expected = document.Scene.Objects[i];
            var actual = loaded.Document.Scene.Objects[i];
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.Transform, actual.Transform);
            Assert.Equal(expected.RenderState, actual.RenderState);
            Assert.Equal(expected.Mesh.Positions, actual.Mesh.Positions);
            Assert.Equal(expected.Mesh.Indices, actual.Mesh.Indices);
        }

        AssertGraphEqual(document.Supports, loaded.Document.Supports);
    }

    [Fact]
    public void SharedMeshIsWrittenOnceAndSharedAfterLoad()
    {
        using var file = new TemporaryProject();
        var document = CompleteDocument(sharedMesh: true);

        ProjectFile.Save(file.Path, document, new ProjectViewState());

        using (var archive = ZipFile.OpenRead(file.Path))
            Assert.Single(archive.Entries, entry => entry.FullName.StartsWith("meshes/"));
        var loaded = ProjectFile.Load(file.Path).Document;
        Assert.Same(loaded.Scene.Objects[0].Mesh, loaded.Scene.Objects[1].Mesh);
    }

    [Fact]
    public void UnknownJsonFieldsAreIgnored()
    {
        using var file = new TemporaryProject();
        ProjectFile.Save(file.Path, CompleteDocument(sharedMesh: false), new ProjectViewState());
        RewriteManifest(file.Path, root =>
        {
            root["futureRoot"] = new JsonObject { ["meaning"] = 42 };
            root["objects"]!.AsArray()[0]!["futureObjectFlag"] = true;
            root["supportGraph"]!["nodes"]!.AsArray()[0]!["futureNodeShape"] = "torus";
        });

        var loaded = ProjectFile.Load(file.Path);

        Assert.Equal(2, loaded.Document.Scene.Objects.Count);
        Assert.Equal(2, loaded.Document.Supports.NodeCount);
    }

    [Fact]
    public void OldProjectWithRemovedMiniRodsLoadsWithoutThem()
    {
        // Mini supports were removed on 2026-09-07; a file written before that may still hold
        // a "miniSupport" segment fanning from a branch end. It loads with the rod, its tip,
        // and the branch end that carried nothing else dropped (a trunk top or base left with
        // nothing on it would go too; here the base still carries a real tip).
        using var file = new TemporaryProject();
        ProjectFile.Save(file.Path, CompleteDocument(sharedMesh: false), new ProjectViewState());
        var branchEndId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var rodTipId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        RewriteManifest(file.Path, root =>
        {
            var nodes = root["supportGraph"]!["nodes"]!.AsArray();
            var segments = root["supportGraph"]!["segments"]!.AsArray();
            var branchEnd = nodes[0]!.DeepClone().AsObject();
            branchEnd["id"] = branchEndId;
            branchEnd["type"] = "junction";
            var rodTip = nodes[0]!.DeepClone().AsObject();
            rodTip["id"] = rodTipId;
            rodTip["type"] = "tip";
            nodes.Add(branchEnd);
            nodes.Add(rodTip);
            var branch = segments[0]!.DeepClone().AsObject();
            branch["id"] = Guid.Parse("66666666-6666-6666-6666-666666666666");
            branch["type"] = "branch";
            branch["nodeA"] = Guid.Parse("22222222-2222-2222-2222-222222222222");
            branch["nodeB"] = branchEndId;
            var mini = segments[0]!.DeepClone().AsObject();
            mini["id"] = Guid.Parse("77777777-7777-7777-7777-777777777777");
            mini["type"] = "miniSupport";
            mini["nodeA"] = branchEndId;
            mini["nodeB"] = rodTipId;
            segments.Add(branch);
            segments.Add(mini);
        });

        var loaded = ProjectFile.Load(file.Path).Document;

        // The base the carrier branch hung from still holds a trunk with a real tip, so that
        // support stays; only the mini's rod, tip and carrier are gone.
        Assert.Equal(2, loaded.Supports.NodeCount);
        Assert.False(loaded.Supports.TryGetNode(branchEndId, out _));
        Assert.False(loaded.Supports.TryGetNode(rodTipId, out _));
        var segment = Assert.Single(loaded.Supports.Segments);
        Assert.Equal(SupportSegmentType.Trunk, segment.Type);
    }

    [Fact]
    public void TooNewMajorVersionHasClearRejectionMessage()
    {
        using var file = new TemporaryProject();
        ProjectFile.Save(file.Path, CompleteDocument(sharedMesh: false), new ProjectViewState());
        RewriteManifest(file.Path, root => root["formatVersion"]!["major"] = 99);

        var error = Assert.Throws<InvalidDataException>(() => ProjectFile.Load(file.Path));

        Assert.Contains("99.0", error.Message);
        Assert.Contains("newer than supported version 1.0", error.Message);
    }

    [Fact]
    public void ProjectWithoutPrinterSelectionFallsBackToBuiltIn()
    {
        using var file = new TemporaryProject();
        ProjectFile.Save(file.Path, CompleteDocument(sharedMesh: false), new ProjectViewState());
        RewriteManifest(file.Path, root =>
        {
            root.Remove("printerId");
            root.Remove("printer");
        });

        var loaded = ProjectFile.Load(file.Path);

        Assert.Equal(PrinterDefinition.PhotonMonoX, loaded.Document.Printer);
    }

    [Fact]
    public void ProjectWithoutResinSelectionFallsBackToDefault()
    {
        using var file = new TemporaryProject();
        ProjectFile.Save(file.Path, CompleteDocument(sharedMesh: false), new ProjectViewState());
        RewriteManifest(file.Path, root =>
        {
            root.Remove("resinSettings");
            root.Remove("resinPresetId");
            root.Remove("resinPreset");
        });

        var loaded = ProjectFile.Load(file.Path);

        Assert.Equal(ResinSettings.Default, loaded.Document.ResinSettings);
        Assert.Equal(ResinPreset.Default, loaded.Document.ResinPreset);
    }

    [Fact]
    public void VersionOneCombinedPrintSettingsMigrateTheirResinFields()
    {
        using var file = new TemporaryProject();
        ProjectFile.Save(file.Path, CompleteDocument(sharedMesh: false), new ProjectViewState());
        RewriteManifest(file.Path, root =>
        {
            root.Remove("resinSettings");
            var print = root["printSettings"]!;
            print["bottomLayers"] = 9;
            print["bottomExposure"] = 41f;
            print["exposure"] = 3.3f;
            print["lightOffDelay"] = 0.8f;
            print["liftHeight"] = 12f;
            print["liftSpeed"] = 77f;
            print["retractSpeed"] = 155f;
            print["bottomLiftHeight"] = 13f;
            print["bottomLiftSpeed"] = 66f;
        });

        var loaded = ProjectFile.Load(file.Path).Document;

        Assert.Equal(9, loaded.ResinSettings.BottomLayers);
        Assert.Equal(41f, loaded.ResinSettings.BottomExposure);
        Assert.Equal(3.3f, loaded.ResinSettings.Exposure);
        Assert.Equal(0.8f, loaded.ResinSettings.LightOffDelay);
        Assert.Equal(12f, loaded.ResinSettings.LiftHeight);
        Assert.Equal(77f, loaded.ResinSettings.LiftSpeed);
        Assert.Equal(155f, loaded.ResinSettings.RetractSpeed);
        Assert.Equal(13f, loaded.ResinSettings.BottomLiftHeight);
        Assert.Equal(66f, loaded.ResinSettings.BottomLiftSpeed);
    }

    [Fact]
    public void ReplacingAnOpenDocumentStartsWithFreshSelectionAndUndoHistory()
    {
        var current = CompleteDocument(sharedMesh: false);
        Assert.True(current.History.CanUndo);
        Assert.NotEmpty(current.Selection);
        var replacement = CompleteDocument(sharedMesh: true);

        current.ReplaceWith(replacement);

        Assert.False(current.History.CanUndo);
        Assert.False(current.History.CanRedo);
        Assert.Empty(current.Selection);
        Assert.Empty(current.SupportSelection);
        Assert.Equal(replacement.Scene.Objects.Select(obj => obj.Id),
            current.Scene.Objects.Select(obj => obj.Id));
        AssertGraphEqual(replacement.Supports, current.Supports);
    }

    [Fact]
    public void GeneratedSupportsSliceToIdenticalRleAfterSaveLoad()
    {
        using var file = new TemporaryProject();
        var document = new Document
        {
            PrintSettings = PrintSettings.Default with
            {
                LayerHeight = 0.25f, AntiAliasing = false, XyCompensation = 0,
            },
        };
        var model = new SceneObject("witness", Box(1, 1, 1))
        {
            Transform = Transform.Identity with { Translation = new Vector3(-6, -6, 0) },
        };
        document.AddObject(model);
        var routed = new TreeSupportRouter(new LinearCollisionScene(), GrowthRuleSet.Default)
            .Route(
            [
                new RoutingTip(new Vector3(0, 0, 5), Vector3.UnitZ, 0.3f,
                    model.Id, TipShape: SupportTipShape.Cone, ConeLength: 0.5f),
            ],
            new TreeRoutingOptions
            {
                Seed = 31, UseBaseGrid = false, BaseShape = SupportBaseShape.DiscCone,
                BaseDiameter = 2, BaseHeight = 0.5f, BaseConeHeight = 0.75f,
                TrunkDiameter = 0.8f, BranchDiameter = 0.6f,
            });
        Assert.Empty(routed.Failures);
        foreach (var node in routed.Graph.Nodes) document.Supports.AddNode(node.Clone());
        foreach (var segment in routed.Graph.Segments) document.Supports.AddSegment(segment.Clone());

        var printer = TestPrinter("generated-support-test");
        var before = Slicer.Slice(document.Scene.Objects, printer, document.PrintSettings,
            supports: document.Supports);
        ProjectFile.Save(file.Path, document, new ProjectViewState());
        var loaded = ProjectFile.Load(file.Path).Document;
        var after = Slicer.Slice(loaded.Scene.Objects, printer, loaded.PrintSettings,
            supports: loaded.Supports);

        Assert.True(before.LayerCount >= 3);
        Assert.Equal(before.LayerCount, after.LayerCount);
        foreach (var index in new[] { 0, before.LayerCount / 2, before.LayerCount - 1 }.Distinct())
            Assert.Equal(before.Layers[index].Rle, after.Layers[index].Rle);
    }

    private static Document CompleteDocument(bool sharedMesh)
    {
        var document = new Document
        {
            Printer = TestPrinter("complete-document"),
            PrintSettings = PrintSettings.Default with
            {
                LayerHeight = 0.075f, AntiAliasing = false, XyCompensation = -0.03f,
            },
            ResinSettings = ResinSettings.Default with
            {
                BottomLayers = 7, BottomExposure = 32, Exposure = 2.4f,
                LightOffDelay = 1.2f, LiftHeight = 9, LiftSpeed = 95,
                RetractSpeed = 165, BottomLiftHeight = 10, BottomLiftSpeed = 80,
            },
            ResinPreset = new ResinPreset
            {
                Id = "project-resin", Name = "Project resin",
                Settings = ResinSettings.Default with { Exposure = 2.4f },
            },
        };
        var mesh = Box(2, 3, 4);
        var first = new SceneObject("first", mesh)
        {
            Transform = new Transform(new Vector3(1, 2, 3),
                Quaternion.CreateFromYawPitchRoll(0.1f, 0.2f, 0.3f), new Vector3(2, 3, 4)),
            RenderState = RenderState.Ghosted,
        };
        document.AddObject(first);
        document.AddObject(new SceneObject("second", sharedMesh ? mesh : Box(1, 2, 3))
        {
            Transform = new Transform(new Vector3(-2, 5, 0),
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f), new Vector3(0.5f)),
            RenderState = RenderState.Hidden,
        });

        var origin = new SupportOrigin(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 7, first.Id);
        var tip = new SupportNode
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Type = SupportNodeType.Tip,
            Position = new Vector3(1.5f, 2.5f, 8), Origin = origin, Pinned = true,
            Hidden = true, Disabled = false, SurfaceNormal = Vector3.Normalize(new Vector3(1, 2, 3)),
            TipDiameter = 0.27f, TipNormalLeadIn = 0.3f,
            PenetrationDepth = 0.12f, ContactObjectId = first.Id,
            TipShape = SupportTipShape.Cone, ConeLength = 1.3f, BallDiameter = 0.42f,
            BaseShape = SupportBaseShape.None, BaseDiameter = 2.2f, BaseHeight = 0.4f,
            BaseConeHeight = 1.1f,
        };
        var supportBase = new SupportNode
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Type = SupportNodeType.Base,
            Position = new Vector3(1, 2, 0), Origin = origin, Disabled = true,
            SurfaceNormal = -Vector3.UnitZ, TipDiameter = 0.31f, PenetrationDepth = 0.2f,
            TipShape = SupportTipShape.Capsule, ConeLength = 0.8f, BallDiameter = 0.1f,
            BaseShape = SupportBaseShape.DiscCone, BaseDiameter = 4.2f, BaseHeight = 0.9f,
            BaseConeHeight = 2.1f,
        };
        document.Supports.AddNode(tip);
        document.Supports.AddNode(supportBase);
        document.Supports.AddSegment(new SupportSegment
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Type = SupportSegmentType.Trunk, NodeA = supportBase.Id, NodeB = tip.Id,
            Diameter = 1.7f, Origin = origin, Pinned = true, Hidden = true, Disabled = true,
        });
        return document;
    }

    private static PrinterDefinition TestPrinter(string id) => new(
        id, false, "Project test", "Project test", "pwmx",
        20, 20, 20, 160, 160, MirrorX: false, MirrorY: true, FormatVersion: 516);

    private static void AssertGraphEqual(SupportGraph expected, SupportGraph actual)
    {
        Assert.Equal(expected.NodeCount, actual.NodeCount);
        Assert.Equal(expected.SegmentCount, actual.SegmentCount);
        foreach (var node in expected.Nodes)
        {
            var copy = actual.GetNode(node.Id);
            Assert.Equal(node.Type, copy.Type);
            Assert.Equal(node.Position, copy.Position);
            Assert.Equal(node.Origin, copy.Origin);
            Assert.Equal(node.Pinned, copy.Pinned);
            Assert.Equal(node.Hidden, copy.Hidden);
            Assert.Equal(node.Disabled, copy.Disabled);
            Assert.Equal(node.SurfaceNormal, copy.SurfaceNormal);
            Assert.Equal(node.TipDiameter, copy.TipDiameter);
            Assert.Equal(node.TipNormalLeadIn, copy.TipNormalLeadIn);
            Assert.Equal(node.PenetrationDepth, copy.PenetrationDepth);
            Assert.Equal(node.ContactObjectId, copy.ContactObjectId);
            Assert.Equal(node.TipShape, copy.TipShape);
            Assert.Equal(node.ConeLength, copy.ConeLength);
            Assert.Equal(node.BallDiameter, copy.BallDiameter);
            Assert.Equal(node.BaseShape, copy.BaseShape);
            Assert.Equal(node.BaseDiameter, copy.BaseDiameter);
            Assert.Equal(node.BaseHeight, copy.BaseHeight);
            Assert.Equal(node.BaseConeHeight, copy.BaseConeHeight);
        }
        foreach (var segment in expected.Segments)
        {
            var copy = actual.GetSegment(segment.Id);
            Assert.Equal(segment.Type, copy.Type);
            Assert.Equal(segment.NodeA, copy.NodeA);
            Assert.Equal(segment.NodeB, copy.NodeB);
            Assert.Equal(segment.Diameter, copy.Diameter);
            Assert.Equal(segment.Origin, copy.Origin);
            Assert.Equal(segment.Pinned, copy.Pinned);
            Assert.Equal(segment.Hidden, copy.Hidden);
            Assert.Equal(segment.Disabled, copy.Disabled);
        }
    }

    private static Mesh Box(float x, float y, float z)
    {
        Vector3[] positions =
        [
            new(0, 0, 0), new(x, 0, 0), new(0, y, 0), new(x, y, 0),
            new(0, 0, z), new(x, 0, z), new(0, y, z), new(x, y, z),
        ];
        int[] indices =
        [
            0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4,
            2, 6, 7, 2, 7, 3, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5,
        ];
        return new Mesh(positions, indices);
    }

    private static void RewriteManifest(string path, Action<JsonObject> edit)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        var oldEntry = archive.GetEntry("project.json")!;
        JsonObject root;
        using (var stream = oldEntry.Open())
            root = JsonNode.Parse(stream)!.AsObject();
        oldEntry.Delete();
        edit(root);
        var newEntry = archive.CreateEntry("project.json");
        using var output = newEntry.Open();
        JsonSerializer.Serialize(output, root);
    }

    private sealed class TemporaryProject : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"danslicer-{Guid.NewGuid():N}.{ProjectFile.Extension}");

        public void Dispose()
        {
            if (File.Exists(Path)) File.Delete(Path);
        }
    }
}
