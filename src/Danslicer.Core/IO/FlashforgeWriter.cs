using System.Globalization;
using System.Text;
using System.Xml;
using Danslicer.Core.Slicing;

namespace Danslicer.Core.IO;

internal static class FlashforgeWriter
{
    public static void Write(SliceResult r, Stream stream)
    {
        var w = new NativeBinary(stream); var p = r.Printer; var s = r.ResinSettings;
        w.Text("DLP-II 1.1\n", 16); w.Zero(12);
        for (int preview = 0; preview < 2; preview++)
        {
            w.Patch32(16 + preview * 4, w.Position); int width = preview == 0 ? 128 : 512, height = width;
            w.Text("BM", 2); w.U32(54 + width * height * 3); w.U32(0); w.U32(54); w.U32(40); w.U32(width); w.U32(height);
            w.U16(1); w.U16(24); w.U32(0); w.U32(width * height * 3); w.U32(3780); w.U32(3780); w.U32(0); w.U32(0);
            var rgb = NativeBinary.PreviewRgb(r, width, height);
            for (int y = height - 1; y >= 0; y--) for (int x = 0; x < width; x++) { int j = (y * width + x) * 3; w.Byte(rgb[j + 2]); w.Byte(rgb[j + 1]); w.Byte(rgb[j]); }
        }
        w.Patch32(24, w.Position);
        using var xml = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false, Indent = false });
        const string svgNamespace = "http://www.w3.org/2000/svg";
        xml.WriteStartElement("svg", svgNamespace); xml.WriteAttributeString("version", "1.1"); xml.WriteStartElement("printparams", svgNamespace);
        void Attribute(string key, object value) => xml.WriteAttributeString(key, Convert.ToString(value, CultureInfo.InvariantCulture));
        Attribute("machinename", p.MachineName); Attribute("materialname", "User resin"); Attribute("layerheight", r.Settings.LayerHeight); Attribute("volume", r.VolumeMl);
        Attribute("layercount", r.LayerCount); Attribute("lightintensity", 1); Attribute("resolutionx", p.ResolutionX); Attribute("resolutiony", p.ResolutionY);
        Attribute("displaywidth", p.DisplayWidthMm); Attribute("displayheight", p.DisplayHeightMm); Attribute("machinez", p.ZTravelMm);
        xml.WriteStartElement("projectiontime", svgNamespace); Attribute("attachlayer", s.BottomLayers); Attribute("buildinlayer", 0); Attribute("attachtime", s.BottomExposure); Attribute("basetime", s.Exposure); xml.WriteEndElement();
        xml.WriteStartElement("projectionadjust", svgNamespace); Attribute("x", 100); Attribute("y", 100); xml.WriteEndElement();
        xml.WriteStartElement("printrange", svgNamespace); Attribute("minx", r.MinX); Attribute("miny", r.MinY); Attribute("minz", 0); Attribute("maxx", r.MaxX); Attribute("maxy", r.MaxY); Attribute("maxz", r.PrintHeight); xml.WriteEndElement(); xml.WriteEndElement();
        xml.WriteStartElement("g", svgNamespace); Attribute("id", "background"); xml.WriteEndElement();
        // One polygon per horizontal lit run. Degenerate one-row polygons retain pixel-exact
        // raster semantics in the reference decoder; all output is binary as required by SVGX.
        for (int i = 0; i < r.LayerCount; i++)
        {
            xml.WriteStartElement("g", svgNamespace); Attribute("id", $"layer-{i}"); Attribute("area", r.Layers[i].AreaMm2); Attribute("perimeter", 0);
            var pixels = NativeBinary.Pixels(r, i);
            for (int y = 0; y < p.ResolutionY; y++) for (int x = 0; x < p.ResolutionX;)
            {
                if (pixels[y * p.ResolutionX + x] < 128) { x++; continue; }
                int end = x + 1; while (end < p.ResolutionX && pixels[y * p.ResolutionX + end] >= 128) end++;
                float left = (x + .25f) * p.PixelPitchX - p.DisplayWidthMm / 2, right = (end - .75f) * p.PixelPitchX - p.DisplayWidthMm / 2;
                float top = (y + .25f) * p.PixelPitchY - p.DisplayHeightMm / 2, bottom = (y + .75f) * p.PixelPitchY - p.DisplayHeightMm / 2;
                xml.WriteStartElement("path", svgNamespace); Attribute("style", "fill:white"); Attribute("fill-rule", "evenodd");
                Attribute("d", FormattableString.Invariant($"M {left:R} {top:R} L {right:R} {top:R} {right:R} {bottom:R} {left:R} {bottom:R} Z")); xml.WriteEndElement(); x = end;
            }
            xml.WriteEndElement();
        }
        xml.WriteEndElement(); xml.Flush();
    }
}
