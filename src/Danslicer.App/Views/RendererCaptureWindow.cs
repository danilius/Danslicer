using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;
using Danslicer.Core.Slicing;
using Danslicer.Render;
using Silk.NET.OpenGL;

namespace Danslicer.App.Views;

/// <summary>Opt-in real GL evidence, with a deterministic synthetic contact fixture and no config IO.</summary>
internal sealed class RendererCaptureWindow : Window
{
    public RendererCaptureWindow(string directory)
    {
        Width = 1000; Height = 760; Title = "Danslicer renderer verification";
        var output = Path.GetFullPath(directory);
        Directory.CreateDirectory(output);
        File.Delete(Path.Combine(output, "error.txt"));
        File.Delete(Path.Combine(output, "result.txt"));
        Content = new CaptureViewport(output);
        Opened += async (_, _) =>
        {
            await Task.Delay(45000);
            if (!File.Exists(Path.Combine(output, "result.txt")))
            {
                File.WriteAllText(Path.Combine(output, "error.txt"), "No completed GL callback within 45 seconds.");
                Finish(1);
            }
        };
    }

    private static void Finish(int code) => Dispatcher.UIThread.Post(() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(code));

    private sealed class CaptureViewport(string directory) : OpenGlControlBase
    {
        private SceneRenderer? _renderer;
        private bool _done;
        protected override void OnOpenGlInit(GlInterface gl) => _renderer = new SceneRenderer(gl.GetProcAddress);
        protected override void OnOpenGlDeinit(GlInterface gl) => _renderer?.Dispose();
        protected override unsafe void OnOpenGlRender(GlInterface glInterface, int fb)
        {
            if (_done || _renderer is null) return;
            _done = true;
            try
            {
                using var gl = GL.GetApi(glInterface.GetProcAddress);
                var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
                int width = (int)(Bounds.Width * scaling), height = (int)(Bounds.Height * scaling);
                var doc = new Document();
                var model = new SceneObject("Contact fixture", Box(new(-16, -10, 12), new(16, 10, 19)));
                doc.AddObject(model);
                doc.AddObject(new SceneObject("Ridge", Box(new(-9, -5, 19), new(0, 5, 27))));
                var selfDoc = new Document();
                var baseMesh = model.Mesh; var ridgeMesh = doc.Scene.Objects[1].Mesh;
                selfDoc.AddObject(new SceneObject("Single mesh self-shadow fixture", new Mesh(
                    baseMesh.Positions.Concat(ridgeMesh.Positions).ToArray(),
                    baseMesh.Indices.Concat(ridgeMesh.Indices.Select(i => i + baseMesh.VertexCount)).ToArray())));
                var aux = new List<AuxMeshDraw>();
                for (var x = -12; x <= 12; x += 6)
                    for (var y = -6; y <= 6; y += 6)
                        aux.Add(new AuxMeshDraw(Box(new(x - 0.6f, y - 0.6f, 0), new(x + 0.6f, y + 0.6f, 12)), new(0.47f, 0.54f, 0.57f), 1));
                var realSupports = SupportFixture(5, 3);
                var denseSupports = SupportFixture(30, 20);
                var captures = new Dictionary<string, byte[]>();
                var report = new List<string> { $"GL: {_renderer.GlVersion}", $"GPU: {gl.GetStringS(StringName.Renderer)}", $"Framebuffer {width}x{height}; synthetic closed box model and 15 support-like aux pillars; no user config loaded." };
                foreach (var path in new[] { RenderPathMode.Deferred, RenderPathMode.Classic })
                foreach (var shot in new[] { "above", "below", "grazing", "contact", "effects-off", "ao-off", "cavity-off", "reflections", "reflections-off", "below-reflections-off", "isolation", "transparent", "ortho", "selected", "transition-0", "transition-3", "transition-9", "transition-12", "supports-below", "supports-contact", "supports-ao-off", "supports-isolation", "supports-cap-off", "supports-isolation-below", "supports-cap-off-below", "transparent-grazing", "matcap", "dense", "dense-effects-off", "cube", "cube-below", "cube-top", "shadow-off", "shadow-working", "shadow-presentation", "shadow-hard", "shadow-zero", "shadow-below", "shadow-clipped", "shadow-transparent", "shadow-self", "shadow-self-off" })
                {
                    var camera = new Camera { Target = new(0, 0, 12), Distance = shot == "above" || shot.StartsWith("dense") ? 260 : 85 };
                    camera.SetView(-65, (shot.StartsWith("below") || shot == "supports-below" || shot.EndsWith("-below")) ? -35 : shot.StartsWith("transition-") ? float.Parse(shot[11..]) : shot is "grazing" or "transparent-grazing" ? 6 : 28);
                    if (shot == "cube-top") camera.ViewTop();
                    camera.Orthographic = shot == "ortho";
                    var clip = shot == "shadow-clipped" || shot == "isolation" || shot.StartsWith("supports-isolation") || shot.StartsWith("supports-cap-off") ? new ViewportClipRange(0, 27, 6, 23, true) : default;
                    var sourceAux = shot.StartsWith("supports-") ? realSupports : shot.StartsWith("dense") ? denseSupports : aux;
                    var draws = sourceAux.Select(a => (shot.StartsWith("transparent") || shot == "shadow-transparent") ? a with { Opacity = 0.3f } : a).ToList();
                    var cap = !shot.StartsWith("supports-cap-off");
                    // Mirror the app's Classic fallback: exact sliced caps are supplied by the host.
                    if (clip.IsClipping && cap && path == RenderPathMode.Classic)
                    {
                        foreach (var (z, upper) in clip.ActiveCapPlanes())
                        {
                            foreach (var obj in doc.Scene.Objects) AddCap(obj.Mesh, obj.Transform.ToMatrix(), new(0.7f, 0.71f, 0.74f));
                            foreach (var a in sourceAux) AddCap(a.Mesh, Matrix4x4.Identity, a.Color);
                            void AddCap(Mesh mesh, Matrix4x4 world, Vector3 color)
                            {
                                if (ClipCapBuilder.Build(mesh, world, z, upper ? ClipCapFace.Upper : ClipCapFace.Lower) is { } capMesh)
                                    draws.Add(new(capMesh, color, 1));
                            }
                        }
                    }
                    var frame = new RenderFrame
                    {
                        Framebuffer = fb, Width = width, Height = height, Camera = camera, Scene = shot.StartsWith("shadow-self") ? selfDoc.Scene : doc.Scene,
                        IsSelected = o => shot == "selected" && o == model, Printer = doc.Printer, RenderPath = path,
                        PlateOpacityFromBelow = new ViewportConfig().PlateOpacityFromBelow,
                        AuxMeshes = draws,
                        Shadows = new ShadowEffects { Mode = shot is "shadow-off" or "shadow-self-off" or "effects-off" or "dense-effects-off" ? ModelShadowMode.Off : shot is "shadow-presentation" or "shadow-hard" ? ModelShadowMode.Presentation : ModelShadowMode.Working, Strength = shot == "shadow-zero" ? 0 : shot is "shadow-presentation" or "shadow-hard" ? 0.5f : 0.22f, SoftnessMm = shot == "shadow-hard" ? 0 : shot == "shadow-presentation" ? 1.2f : 0.6f },
                        Deferred = new DeferredEffects { AmbientOcclusionEnabled = shot is not ("effects-off" or "ao-off" or "supports-ao-off" or "dense-effects-off"), CavityEnabled = shot is not ("effects-off" or "cavity-off" or "dense-effects-off"), Shading = shot == "matcap" ? ViewportShadingMode.MatCapClay : ViewportShadingMode.Studio },
                        PlateReflectionsEnabled = shot is not ("effects-off" or "reflections-off" or "below-reflections-off" or "dense-effects-off"),
                        ShowPlateShadows = shot is not ("reflections" or "reflections-off"),
                        ClipRange = clip, CapInterior = cap,
                        CapStyle = path == RenderPathMode.Deferred ? ClipCapStyle.Painted : ClipCapStyle.Sliced, ShowViewCube = shot.StartsWith("cube"),
                    };
                    for (var warm = 0; warm < 12; warm++) _renderer.Render(frame);
                    if (_renderer.ModelShadowsActive != (frame.Shadows.Mode != ModelShadowMode.Off && frame.Shadows.Strength > 0))
                        throw new InvalidOperationException("Model shadow activation failed: " + shot);
                    gl.Finish();
                    var times = new List<double>();
                    for (var sample = 0; sample < 30; sample++)
                    {
                        var timer = Stopwatch.StartNew(); _renderer.Render(frame); gl.Finish(); timer.Stop();
                        times.Add(timer.Elapsed.TotalMilliseconds);
                    }
                    times.Sort();
                    if (path == RenderPathMode.Deferred && !_renderer.CanPickDeferred)
                        throw new InvalidOperationException("Deferred fell back; evidence cannot claim deferred success.");
                    if (shot == "below" && path == RenderPathMode.Deferred)
                    {
                        var p = camera.WorldToScreen(new(3, -8, 12), width, height)!.Value;
                        if (!_renderer.TryPickObject((int)p.X, height - 1 - (int)p.Y, out var hit) || hit != model)
                            throw new InvalidOperationException("Below-plate GPU selection did not reach model.");
                        report.Add("Below-plate GPU pick reaches model.");
                    }
                    gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);
                    var pixels = new byte[width * height * 4];
                    fixed (byte* p = pixels) gl.ReadPixels(0, 0, (uint)width, (uint)height, Silk.NET.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, p);
                    captures[$"{path}-{shot}"] = pixels;
                    using var bitmap = new WriteableBitmap(new(width, height), new(96, 96), Avalonia.Platform.PixelFormat.Rgba8888, AlphaFormat.Opaque);
                    using (var buffer = bitmap.Lock())
                        for (var row = 0; row < height; row++)
                            Marshal.Copy(pixels, (height - 1 - row) * width * 4, buffer.Address + row * buffer.RowBytes, width * 4);
                    bitmap.Save(Path.Combine(directory, $"{path}-{shot}.png"), PngBitmapEncoderOptions.Default);
                    var error = gl.GetError();
                    if (error != GLEnum.NoError) throw new InvalidOperationException($"GL error {error}");
                    report.Add($"{path}-{shot}: median {times[15]:F2}, p95 {times[28]:F2}, max {times[29]:F2} ms/frame (CPU submission + glFinish, 30 frames after 12 warmups).");
                }
                foreach (var path in new[] { "Classic", "Deferred" })
                {
                    // A broad illuminated flat face must not acquire shadow acne/PCF patterns.
                    var checkCamera = new Camera { Target = new(0, 0, 12), Distance = 85 };
                    checkCamera.SetView(-65, 28);
                    foreach (var world in new[] { new Vector3(10, 0, 19), new Vector3(10, -10, 15) })
                    {
                        var screen = checkCamera.WorldToScreen(world, width, height)!.Value;
                        for (var oy = -3; oy <= 3; oy++)
                        for (var ox = -3; ox <= 3; ox++)
                        for (var c = 0; c < 3; c++)
                        {
                            var pixel = ((height - 1 - (int)screen.Y + oy) * width + (int)screen.X + ox) * 4 + c;
                            if (Math.Abs(captures[$"{path}-shadow-off"][pixel] - captures[$"{path}-shadow-presentation"][pixel]) > 2)
                                throw new InvalidOperationException($"False shadow on illuminated flat face: {path} {world}");
                        }
                    }
                    report.Add($"{path} illuminated flat-face acne regression passed.");
                    Compare(path, "shadow-self", "shadow-self-off", same: false);
                    Compare(path, "shadow-off", "shadow-zero", same: true);
                    Compare(path, "shadow-off", "shadow-working", same: false);
                    Compare(path, "shadow-working", "shadow-presentation", same: false);
                    Compare(path, "shadow-presentation", "shadow-hard", same: false);
                    Compare(path, "below", "below-reflections-off", same: true);
                    Compare(path, "reflections", "reflections-off", same: false);
                    Compare(path, "contact", "ao-off", same: path == "Classic");
                    Compare(path, "contact", "cavity-off", same: path == "Classic");
                    Compare(path, "supports-isolation", "supports-cap-off", same: false);
                    Compare(path, "supports-isolation-below", "supports-cap-off-below", same: false);
                }
                // Regression for disjoint support parts leaking stencil marks onto the upper cap.
                var capped = captures["Deferred-supports-isolation"];
                var uncapped = captures["Deferred-supports-cap-off"];
                for (var row = 0; row < height / 2; row++)
                    for (var x = 0; x < width * 4; x++)
                        if (capped[row * width * 4 + x] != uncapped[row * width * 4 + x])
                            throw new InvalidOperationException("Painted upper cap leaked into the lower half of the contact fixture.");
                report.Add($"Cap isolation leak regression passed. Real fixture: 15 supports; dense fixture: 600 supports / {denseSupports.Sum(d => d.Mesh.TriangleCount)} aux triangles.");
                File.WriteAllLines(Path.Combine(directory, "result.txt"), report);
                void Compare(string path, string first, string second, bool same)
                {
                    var a = captures[$"{path}-{first}"]; var b = captures[$"{path}-{second}"];
                    long difference = 0; int changed = 0, max = 0;
                    for (var i = 0; i < a.Length; i++)
                    {
                        var delta = Math.Abs(a[i] - b[i]); difference += delta;
                        if (delta != 0) changed++; max = Math.Max(max, delta);
                    }
                    if ((difference == 0) != same) throw new InvalidOperationException($"Unexpected toggle comparison: {path} {first}/{second}");
                    report.Add($"{path} {first}/{second}: {changed} changed channels; mean {difference / (double)a.Length:F4}/255; max {max}/255; expected {(same ? "identical" : "different")}.");
                }
                Finish(0);
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(directory, "error.txt"), ex.ToString()); Finish(1); }
        }

        private static List<AuxMeshDraw> SupportFixture(int columns, int rows)
        {
            var graph = new SupportGraph();
            for (var x = 0; x < columns; x++)
            for (var y = 0; y < rows; y++)
            {
                var px = (x - (columns - 1) / 2f) * 6;
                var py = (y - (rows - 1) / 2f) * 6;
                var foot = new SupportNode { Type = SupportNodeType.Base, Position = new(px, py, 0), BaseShape = SupportBaseShape.DiscCone, BaseDiameter = 3, BaseHeight = 0.6f };
                var joint = new SupportNode { Type = SupportNodeType.Junction, Position = new(px, py, 9) };
                var tip = new SupportNode { Type = SupportNodeType.Tip, Position = new(px, py, 12), SurfaceNormal = -Vector3.UnitZ, TipShape = SupportTipShape.Cone, TipDiameter = 0.4f, ConeLength = 2, PenetrationDepth = 0.15f };
                graph.AddNode(foot); graph.AddNode(joint); graph.AddNode(tip);
                graph.AddSegment(new SupportSegment { Type = SupportSegmentType.Trunk, NodeA = foot.Id, NodeB = joint.Id, Diameter = 1.2f });
                graph.AddSegment(new SupportSegment { Type = SupportSegmentType.Tip, NodeA = joint.Id, NodeB = tip.Id, Diameter = 0.8f });
            }
            return SupportRenderMesh.Build(graph).Select(p => new AuxMeshDraw(p.Mesh, new(0.47f, 0.54f, 0.57f), 1)).ToList();
        }

        private static Mesh Box(Vector3 lo, Vector3 hi) => new(
            [new(lo.X, lo.Y, lo.Z), new(hi.X, lo.Y, lo.Z), new(hi.X, hi.Y, lo.Z), new(lo.X, hi.Y, lo.Z),
             new(lo.X, lo.Y, hi.Z), new(hi.X, lo.Y, hi.Z), new(hi.X, hi.Y, hi.Z), new(lo.X, hi.Y, hi.Z)],
            [0,2,1, 0,3,2, 4,5,6, 4,6,7, 0,1,5, 0,5,4, 1,2,6, 1,6,5, 2,3,7, 2,7,6, 3,0,4, 3,4,7]);
    }
}
