using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Danslicer.Core.IO;
using Danslicer.Core.Printers;
using Danslicer.Core.Scene;
using Danslicer.Core.Slicing;

namespace Danslicer.Cli;

internal static class SliceCommand
{
    public static int Run(string[] args, Action? usage = null, TextWriter? standardOutput = null,
        TextWriter? standardError = null, bool reportProgress = true)
    {
        var ci = CultureInfo.InvariantCulture;
        var outputWriter = standardOutput ?? Console.Out;
        var errorWriter = standardError ?? Console.Error;
        var inputs = new List<string>();
        string? output = null;
        float? layerHeight = null;
        float? exposure = null;
        float? bottomExposure = null;
        int? bottomLayers = null;
        float? xyCompensation = null;
        var noAntiAliasing = false;
        var allowOutOfBounds = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o": output = args[++i]; break;
                case "--layer": layerHeight = Parse(args[++i]); break;
                case "--exposure": exposure = Parse(args[++i]); break;
                case "--bottom-exposure": bottomExposure = Parse(args[++i]); break;
                case "--bottom-layers": bottomLayers = int.Parse(args[++i], ci); break;
                case "--xy": xyCompensation = Parse(args[++i]); break;
                case "--no-aa": noAntiAliasing = true; break;
                case "--allow-out-of-bounds": allowOutOfBounds = true; break;
                default: inputs.Add(args[i]); break;
            }
        }
        if (inputs.Count == 0 || output is null)
        {
            usage?.Invoke();
            return 1;
        }

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
                errorWriter.WriteLine("error: a project file cannot be combined with mesh inputs.");
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
                var bounds = mesh.Bounds;
                obj.Transform = Transform.Identity with
                {
                    Translation = new Vector3(-bounds.Center.X, -bounds.Center.Y, -bounds.Min.Z),
                };
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

        var stopwatch = Stopwatch.StartNew();
        var lastPercent = -1;
        IProgress<double>? progress = reportProgress ? new Progress<double>(p =>
        {
            var percent = (int)(p * 100);
            if (percent / 10 != lastPercent / 10) errorWriter.Write($"{percent}% ");
            lastPercent = percent;
        }) : null;
        SliceResult result;
        try
        {
            result = Slicer.Slice(objects, printer, settings, progress, supports: supports,
                resinSettings: resin, allowOutOfBounds: allowOutOfBounds);
        }
        catch (InvalidOperationException ex)
        {
            errorWriter.WriteLine();
            errorWriter.WriteLine($"error: {ex.Message}");
            return 2;
        }
        errorWriter.WriteLine();
        if (result.BuildVolumeWarning is { } warning)
            errorWriter.WriteLine($"warning: {warning["Warning: ".Length..]}");
        PhotonWorkshopWriter.Write(result, output);
        stopwatch.Stop();

        outputWriter.WriteLine($"Wrote:      {output}");
        outputWriter.WriteLine($"Layers:     {result.LayerCount} x {Format(settings.LayerHeight)} mm = {Format(result.PrintHeight)} mm");
        outputWriter.WriteLine($"Volume:     {Format(result.VolumeMl)} ml");
        outputWriter.WriteLine($"Est. time:  {TimeSpan.FromSeconds(result.EstimatedSeconds):h\\:mm\\:ss}");
        outputWriter.WriteLine($"Footprint:  X {Format(result.MinX)}..{Format(result.MaxX)}  Y {Format(result.MinY)}..{Format(result.MaxY)} mm");
        outputWriter.WriteLine($"Sliced in:  {stopwatch.Elapsed.TotalSeconds:0.0} s");
        return 0;
    }

    private static float Parse(string value) => float.Parse(value, CultureInfo.InvariantCulture);
    private static string Format(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
