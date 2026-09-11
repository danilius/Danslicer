using System.Globalization;
using System.IO.Compression;
using System.Text;
using Danslicer.Core.Slicing;

namespace Danslicer.Core.IO;

internal static class ResinArchiveWriter
{
    public static void Write(SliceResult r, Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, true);
        var p = r.Printer; var s = r.ResinSettings;
        void Text(string name, string text) { using var entry = zip.CreateEntry(name).Open(); entry.Write(Encoding.UTF8.GetBytes(text)); }
        void Image(string name, byte[] pixels, int width, int height, bool rgb = false) { using var entry = zip.CreateEntry(name, CompressionLevel.NoCompression).Open(); entry.Write(NativeBinary.Png(pixels, width, height, rgb)); }
        string Ini(IEnumerable<KeyValuePair<string, object>> values, string prefix = "") => string.Join('\n', values.Select(v => prefix + v.Key + " = " + Convert.ToString(v.Value, CultureInfo.InvariantCulture))) + "\n";
        var config = new Dictionary<string, object>(); var extra = new Dictionary<string, object>();
        bool prusa = p.NativeFormat == "sl1", nova = p.NativeFormat is "cws" or "cws-rgb";
        if (prusa)
        {
            config = new()
            {
                ["action"] = "print",
                ["jobDir"] = "danslicer",
                ["expTime"] = s.Exposure,
                ["expTimeFirst"] = s.BottomExposure,
                ["layerHeight"] = r.Settings.LayerHeight,
                ["numFade"] = s.BottomLayers,
                ["numFast"] = r.LayerCount,
                ["numSlow"] = 0,
                ["printerModel"] = p.FileExtension == "sl1s" ? "SL1S" : "SL1",
                ["printTime"] = r.EstimatedSeconds,
                ["usedMaterial"] = r.VolumeMl,
                ["materialName"] = "User resin",
                ["printProfile"] = "Danslicer",
                ["printerProfile"] = p.Name,
                ["printerVariant"] = "default",
                ["prusaSlicerVersion"] = "Danslicer"
            };
            extra = new()
            {
                ["display_width"] = p.DisplayWidthMm,
                ["display_height"] = p.DisplayHeightMm,
                ["display_pixels_x"] = p.ResolutionX,
                ["display_pixels_y"] = p.ResolutionY,
                ["display_orientation"] = "landscape",
                ["display_mirror_x"] = 0,
                ["display_mirror_y"] = 0,
                ["max_print_height"] = p.ZTravelMm,
                ["layer_height"] = r.Settings.LayerHeight,
                ["initial_layer_height"] = r.Settings.LayerHeight,
                ["exposure_time"] = s.Exposure,
                ["initial_exposure_time"] = s.BottomExposure,
                ["printer_technology"] = "SLA",
                ["printer_model"] = config["printerModel"],
                ["printer_settings_id"] = p.Name,
                ["sla_archive_format"] = "SL1"
            };
            Text("config.ini", Ini(config)); Text("prusaslicer.ini", Ini(extra));
            Image("thumbnail/thumbnail400x400.png", NativeBinary.PreviewRgb(r, 400, 400), 400, 400, true);
            Image("thumbnail/thumbnail800x480.png", NativeBinary.PreviewRgb(r, 800, 480), 800, 480, true);
        }
        else if (nova)
        {
            config = new()
            {
                ["xppm"] = 1 / p.PixelPitchX,
                ["yppm"] = 1 / p.PixelPitchY,
                ["xres"] = p.ResolutionX,
                ["yres"] = p.ResolutionY,
                ["thickness"] = r.Settings.LayerHeight,
                ["layers_num"] = r.LayerCount,
                ["head_layers_num"] = s.BottomLayers,
                ["layers_expo_ms"] = s.Exposure * 1000,
                ["head_layers_expo_ms"] = s.BottomExposure * 1000,
                ["wait_before_expo_ms"] = s.LightOffDelay * 1000,
                ["lift_distance"] = s.LiftHeight,
                ["lift_up_speed"] = s.LiftSpeed,
                ["lift_down_speed"] = s.RetractSpeed,
                ["lift_when_finished"] = 0
            };
            Text("slice.conf", Ini(config));
        }
        else
        {
            config = new()
            {
                ["fileName"] = "danslicer",
                ["machineType"] = p.MachineName,
                ["estimatedPrintTime"] = r.EstimatedSeconds,
                ["volume"] = r.VolumeMl,
                ["layerHeight"] = r.Settings.LayerHeight,
                ["resolutionX"] = p.ResolutionX,
                ["resolutionY"] = p.ResolutionY,
                ["machineX"] = p.DisplayWidthMm,
                ["machineY"] = p.DisplayHeightMm,
                ["machineZ"] = p.ZTravelMm,
                ["normalExposureTime"] = s.Exposure,
                ["bottomLayerExposureTime"] = s.BottomExposure,
                ["bottomLayExposureTime"] = s.BottomExposure,
                ["normalDropSpeed"] = s.RetractSpeed,
                ["normalLayerLiftSpeed"] = s.LiftSpeed,
                ["normalLayerLiftHeight"] = s.LiftHeight,
                ["bottomLayerLiftHeight"] = s.BottomLiftHeight,
                ["bottomLayerLiftSpeed"] = s.BottomLiftSpeed,
                ["bottomLayCount"] = s.BottomLayers,
                ["bottomLayerCount"] = s.BottomLayers,
                ["totalLayer"] = r.LayerCount,
                ["mirror"] = 0,
                ["bottomLightOffTime"] = s.LightOffDelay,
                ["lightOffTime"] = s.LightOffDelay,
                ["bottomPWMLight"] = 255,
                ["PWMLight"] = 255
            };
            Image("preview.png", NativeBinary.PreviewRgb(r, 400, 300), 400, 300, true);
            Image("preview_cropping.png", NativeBinary.PreviewRgb(r, 400, 300), 400, 300, true);
        }
        var gcode = new StringBuilder();
        if (!prusa)
        {
            if (nova)
            {
                var headers = new Dictionary<string, object>
                {
                    ["Pix per mm X"] = 1 / p.PixelPitchX,
                    ["Pix per mm Y"] = 1 / p.PixelPitchY,
                    ["X Resolution"] = p.ResolutionX,
                    ["Y Resolution"] = p.ResolutionY,
                    ["Layer Thickness"] = r.Settings.LayerHeight,
                    ["Number of Slices"] = r.LayerCount,
                    ["Platform X Size"] = p.DisplayWidthMm,
                    ["Platform Y Size"] = p.DisplayHeightMm,
                    ["Platform Z Size"] = p.ZTravelMm,
                    ["Number of Bottom Layers"] = s.BottomLayers,
                    ["Layer Time"] = s.Exposure * 1000,
                    ["Bottom Layers Time"] = s.BottomExposure * 1000
                };
                headers["Lift Distance"] = s.LiftHeight; headers["Z Lift Feed Rate"] = s.LiftSpeed;
                headers["Z Bottom Lift Feed Rate"] = s.BottomLiftSpeed; headers["Z Lift Retract Rate"] = s.RetractSpeed;
                foreach (var h in headers) gcode.AppendLine($";({h.Key} = {Convert.ToString(h.Value, CultureInfo.InvariantCulture)})");
            }
            else foreach (var h in config) gcode.AppendLine($";{h.Key}:{Convert.ToString(h.Value, CultureInfo.InvariantCulture)}");
            gcode.AppendLine("G21\n" + (nova ? "G91" : "G90") + "\nM106 S0\nM17\nG28 Z0\nG4 P0");
        }
        for (int i = 0; i < r.LayerCount; i++)
        {
            string name = prusa ? $"danslicer{i:D5}.png" : nova ? $"danslicer{i:D4}.png" : $"{i + 1}.png";
            var pixels = NativeBinary.Pixels(r, i);
            bool rgb = p.NativeFormat == "cws-rgb";
            if (rgb) { var color = new byte[pixels.Length * 3]; for (int j = 0; j < pixels.Length; j++) { color[j * 3] = pixels[j]; color[j * 3 + 1] = pixels[j]; color[j * 3 + 2] = pixels[j]; } pixels = color; }
            Image(name, pixels, p.ResolutionX, p.ResolutionY, rgb);
            if (prusa) continue;
            float z = r.Layers[i].Z, lift = s.LiftHeightForLayer(i), speed = s.LiftSpeedForLayer(i);
            gcode.AppendLine($";LAYER_START:{i}");
            gcode.AppendLine(nova ? $";<Slice> {i}" : $"M6054 \"{name}\"");
            if (nova)
            {
                float delta = z - (i == 0 ? 0 : r.Layers[i - 1].Z);
                gcode.AppendLine(FormattableString.Invariant($"G1 Z{delta + lift:0.#####} F{speed:0.###}\nG4 P0\nG1 Z{-lift:0.#####} F{s.RetractSpeed:0.###}\nG4 P0"));
            }
            else gcode.AppendLine(FormattableString.Invariant($"G0 Z{z + lift:0.#####} F{speed:0.###}\nG0 Z{z:0.#####} F{s.RetractSpeed:0.###}"));
            gcode.AppendLine((nova ? ";<Delay> " : "G4 P") + (s.LightOffDelay * 1000).ToString("0", CultureInfo.InvariantCulture));
            gcode.AppendLine("M106 S255");
            float exposure = i < s.BottomLayers ? s.BottomExposure : s.Exposure;
            gcode.AppendLine((nova ? ";<Delay> " : "G4 P") + (exposure * 1000).ToString("0", CultureInfo.InvariantCulture));
            gcode.AppendLine("M106 S0\n;<Slice> Blank\n;LAYER_END");
        }
        if (!prusa) { gcode.AppendLine("M106 S0\nM18"); Text(nova ? "danslicer.gcode" : "run.gcode", gcode.ToString()); }
    }
}
