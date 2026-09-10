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
                var aux = new List<AuxMeshDraw>();
                for (var x = -12; x <= 12; x += 6)
                    for (var y = -6; y <= 6; y += 6)
                        aux.Add(new AuxMeshDraw(Box(new(x - 0.6f, y - 0.6f, 0), new(x + 0.6f, y + 0.6f, 12)), new(0.47f, 0.54f, 0.57f), 1));
                var report = new List<string> { $"GL: {_renderer.GlVersion}", $"GPU: {gl.GetStringS(StringName.Renderer)}", $"Framebuffer {width}x{height}; synthetic closed box model and 15 support-like aux pillars; no user config loaded." };
                foreach (var path in new[] { RenderPathMode.Deferred, RenderPathMode.Classic })
                foreach (var shot in new[] { "above", "below", "grazing", "contact", "effects-off", "ao-off", "cavity-off", "reflections", "reflections-off", "below-reflections-off", "isolation", "transparent", "ortho", "selected", "transition-0", "transition-3", "transition-9", "transition-12" })
                {
                    var camera = new Camera { Target = new(0, 0, 12), Distance = shot == "above" ? 260 : 85 };
                    camera.SetView(-65, shot.StartsWith("below") ? -35 : shot.StartsWith("transition-") ? float.Parse(shot[11..]) : shot == "grazing" ? 6 : 28);
                    camera.Orthographic = shot == "ortho";
                    var frame = new RenderFrame
                    {
                        Framebuffer = fb, Width = width, Height = height, Camera = camera, Scene = doc.Scene,
                        IsSelected = o => shot == "selected" && o == model, Printer = doc.Printer, RenderPath = path,
                        PlateOpacityFromBelow = new ViewportConfig().PlateOpacityFromBelow,
                        AuxMeshes = shot == "transparent" ? aux.Select(a => a with { Opacity = 0.3f }).ToArray() : aux,
                        Deferred = new DeferredEffects { AmbientOcclusionEnabled = shot is not ("effects-off" or "ao-off"), CavityEnabled = shot is not ("effects-off" or "cavity-off") },
                        PlateReflectionsEnabled = shot is not ("effects-off" or "reflections-off" or "below-reflections-off"),
                        ShowPlateShadows = shot is not ("reflections" or "reflections-off"),
                        ClipRange = shot == "isolation" ? new(0, 27, 6, 23, true) : default,
                        CapStyle = ClipCapStyle.Painted, ShowViewCube = false,
                    };
                    for (var warm = 0; warm < 3; warm++) _renderer.Render(frame);
                    gl.Finish();
                    var timer = Stopwatch.StartNew();
                    for (var sample = 0; sample < 8; sample++) { _renderer.Render(frame); gl.Finish(); }
                    timer.Stop();
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
                    using var bitmap = new WriteableBitmap(new(width, height), new(96, 96), Avalonia.Platform.PixelFormat.Rgba8888, AlphaFormat.Opaque);
                    using (var buffer = bitmap.Lock())
                        for (var row = 0; row < height; row++)
                            Marshal.Copy(pixels, (height - 1 - row) * width * 4, buffer.Address + row * buffer.RowBytes, width * 4);
                    bitmap.Save(Path.Combine(directory, $"{path}-{shot}.png"), PngBitmapEncoderOptions.Default);
                    var error = gl.GetError();
                    if (error != GLEnum.NoError) throw new InvalidOperationException($"GL error {error}");
                    report.Add($"{path}-{shot}: {timer.Elapsed.TotalMilliseconds / 8:F2} ms/frame (CPU submission + glFinish, 8 frames after 3 warmups).");
                }
                File.WriteAllLines(Path.Combine(directory, "result.txt"), report);
                Finish(0);
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(directory, "error.txt"), ex.ToString()); Finish(1); }
        }

        private static Mesh Box(Vector3 lo, Vector3 hi) => new(
            [new(lo.X, lo.Y, lo.Z), new(hi.X, lo.Y, lo.Z), new(hi.X, hi.Y, lo.Z), new(lo.X, hi.Y, lo.Z),
             new(lo.X, lo.Y, hi.Z), new(hi.X, lo.Y, hi.Z), new(hi.X, hi.Y, hi.Z), new(lo.X, hi.Y, hi.Z)],
            [0,2,1, 0,3,2, 4,5,6, 4,6,7, 0,1,5, 0,5,4, 1,2,6, 1,6,5, 2,3,7, 2,7,6, 3,0,4, 3,4,7]);
    }
}



