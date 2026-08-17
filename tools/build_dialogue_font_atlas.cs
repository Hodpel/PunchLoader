using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Drawing.Drawing2D;

// Builds a Visitor-based mixed TextMesh font for dialogue or long part descriptions.
// ASCII is rasterized from Visitor TT2 BRK (the visitor2 face) at its native 50px
// source size; Chinese is BoutiqueBitmap 9x9 Bold, rasterized at the dialogue target height.
class BuildDialogueFontAtlas
{
    class Glyph
    {
        public int Code;
        public Bitmap Image;
        public float VX, VY, VW, VH, Advance;
        public int X, Y;
    }

    static float ParseFloat(string value) { return float.Parse(value, System.Globalization.CultureInfo.InvariantCulture); }
    static string Combine(params string[] parts) { string result = parts[0]; for (int i = 1; i < parts.Length; i++) result = Path.Combine(result, parts[i]); return result; }
    static string Float(float value) { return value.ToString(System.Globalization.CultureInfo.InvariantCulture); }

    static List<Glyph> ReadVisitorMetrics(string fontPath)
    {
        string font = File.ReadAllText(fontPath);
        Regex pattern = new Regex(
            @"index: (?<index>\d+)\s+uv:\s+serializedVersion: 2\s+" +
            @"x: (?<uvx>-?[\d.]+)\s+y: (?<uvy>-?[\d.]+)\s+" +
            @"width: (?<uvw>-?[\d.]+)\s+height: (?<uvh>-?[\d.]+)\s+" +
            @"vert:\s+serializedVersion: 2\s+x: (?<vx>-?[\d.]+)\s+" +
            @"y: (?<vy>-?[\d.]+)\s+width: (?<vw>-?[\d.]+)\s+" +
            @"height: (?<vh>-?[\d.]+)\s+width: (?<advance>-?[\d.]+)", RegexOptions.Singleline);
        List<Glyph> glyphs = new List<Glyph>();
        foreach (Match match in pattern.Matches(font))
        {
            int code = int.Parse(match.Groups["index"].Value);
            if (code < 32 || code > 126) continue;
            Glyph glyph = new Glyph();
            glyph.Code = code;
            glyph.VX = ParseFloat(match.Groups["vx"].Value);
            glyph.VY = ParseFloat(match.Groups["vy"].Value);
            glyph.VW = ParseFloat(match.Groups["vw"].Value);
            glyph.VH = ParseFloat(match.Groups["vh"].Value);
            glyph.Advance = ParseFloat(match.Groups["advance"].Value);
            glyphs.Add(glyph);
        }
        if (glyphs.Count != 95) throw new Exception("visitor2 ASCII glyph count was " + glyphs.Count);
        return glyphs;
    }

    static Bitmap RasterGlyph(Font font, char character)
    {
        Bitmap work = new Bitmap(192, 192, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(work))
        {
            g.Clear(Color.Transparent);
            g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawString(character.ToString(), font, Brushes.White, 48, 48, StringFormat.GenericTypographic);
        }
        int minX = work.Width, minY = work.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < work.Height; y++)
            for (int x = 0; x < work.Width; x++)
                if (work.GetPixel(x, y).A >= 128)
                {
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
        if (maxX < minX) { work.Dispose(); return new Bitmap(1, 1, PixelFormat.Format32bppArgb); }
        Bitmap cropped = new Bitmap(maxX - minX + 1, maxY - minY + 1, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(cropped)) g.DrawImageUnscaled(work, -minX, -minY);
        work.Dispose();
        return cropped;
    }

    static void Main(string[] args)
    {
        string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
        string parent = Directory.GetParent(root).FullName;
        string visitorAsset = Combine(parent, "文件整理", "ExportedProject", "Assets", "Font", "visitor2.asset");
        string visitorTtf = Combine(root, "tools", "fonts", "VisitorTT2BRK.ttf");
        string boutiqueTtf = Combine(root, "tools", "fonts", "BoutiqueBitmap9x9_Bold_1.93.ttf");
        string outputDir = Combine(root, "mods", "ChineseLocalization");
        bool partDescriptions = args.Length > 1 && args[1] == "part";
        string translations = Combine(outputDir,
            partDescriptions ? "part_translations.tsv" : "dialogue_translations.tsv");
        if (!File.Exists(visitorAsset) || !File.Exists(visitorTtf) || !File.Exists(boutiqueTtf) ||
            !File.Exists(translations))
            throw new FileNotFoundException("visitor2 metrics or localization font source is missing");

        List<Glyph> glyphs = ReadVisitorMetrics(visitorAsset);
        PrivateFontCollection collection = new PrivateFontCollection();
        collection.AddFontFile(visitorTtf);
        collection.AddFontFile(boutiqueTtf);
        FontFamily visitorFamily = collection.Families[0];
        FontFamily boutiqueFamily = collection.Families[1];
        // Dialogue and part descriptions use a 27px Boutique source raster,
        // yielding 24px visible Chinese glyphs next to Visitor's unchanged
        // 20px ASCII caps.
        float boutiquePixels = 27f;
        using (Font visitor = new Font(visitorFamily, 50, FontStyle.Regular, GraphicsUnit.Pixel))
        using (Font boutique = new Font(boutiqueFamily, boutiquePixels, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            foreach (Glyph glyph in glyphs) glyph.Image = RasterGlyph(visitor, (char)glyph.Code);
            string chinese = File.ReadAllText(translations, Encoding.UTF8);
            HashSet<char> emitted = new HashSet<char>();
            foreach (char character in chinese)
            {
                if (character <= 126 || char.IsControl(character) || !emitted.Add(character)) continue;
                Glyph glyph = new Glyph();
                glyph.Code = (int)character;
                if (character == '\u2009')
                {
                    // Match a half-width version of Visitor's 23px space.
                    // This transparent glyph is reserved for CJK/ASCII joins;
                    // ordinary English spaces keep their original metric.
                    glyph.Image = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
                    glyph.VX = 0;
                    glyph.VY = -27.2f;
                    glyph.VW = 0;
                    glyph.VH = 0;
                    glyph.Advance = 12;
                    glyphs.Add(glyph);
                    continue;
                }
                glyph.Image = RasterGlyph(boutique, character);
                glyph.VX = 0;
                glyph.VY = -7.2f + (glyph.Image.Height - 20) * 0.5f;
                glyph.VW = glyph.Image.Width;
                glyph.VH = -glyph.Image.Height;
                // visitor2 leaves roughly 3px between its 20px caps.
                glyph.Advance = glyph.Image.Width + 3;
                glyphs.Add(glyph);
            }
        }
        collection.Dispose();

        int maxWidth = 1, maxHeight = 1;
        foreach (Glyph glyph in glyphs) { if (glyph.Image.Width > maxWidth) maxWidth = glyph.Image.Width; if (glyph.Image.Height > maxHeight) maxHeight = glyph.Image.Height; }
        const int padding = 2;
        int cellWidth = maxWidth + padding * 2;
        int cellHeight = maxHeight + padding * 2;
        // The complete part catalogue adds nearly a thousand CJK glyphs.  A
        // 1024-wide atlas keeps the texture below Unity's conservative 2048px
        // height limit while retaining a small 2px cell gutter.
        const int columns = 32;
        int rows = (glyphs.Count + columns - 1) / columns;
        int atlasWidth = 1024, atlasHeight = 2048;
        if (columns * cellWidth > atlasWidth || rows * cellHeight > atlasHeight) throw new Exception("Dialogue atlas exceeds 512x2048px");

        Bitmap atlas = new Bitmap(atlasWidth, atlasHeight, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(atlas))
        {
            g.Clear(Color.Transparent);
            for (int i = 0; i < glyphs.Count; i++)
            {
                Glyph glyph = glyphs[i];
                glyph.X = (i % columns) * cellWidth + padding;
                glyph.Y = (i / columns) * cellHeight + padding;
                g.DrawImageUnscaled(glyph.Image, glyph.X, glyph.Y);
            }
        }

        string png = Path.Combine(outputDir,
            partDescriptions ? "part_description_font_atlas.png" : "dialogue_font_atlas.png");
        string map = Path.Combine(outputDir,
            partDescriptions ? "part_description_glyphs.tsv" : "dialogue_glyphs.tsv");
        atlas.Save(png, ImageFormat.Png);
        using (StreamWriter writer = new StreamWriter(map, false, new UTF8Encoding(true)))
        {
            writer.WriteLine("# atlas\t" + atlasWidth + "\t" + atlasHeight + "\t50");
            foreach (Glyph glyph in glyphs)
                writer.WriteLine(glyph.Code + "\t" + glyph.X + "\t" + glyph.Y + "\t" + glyph.Image.Width + "\t" + glyph.Image.Height + "\t" + Float(glyph.VX) + "\t" + Float(glyph.VY) + "\t" + Float(glyph.VW) + "\t" + Float(glyph.VH) + "\t" + Float(glyph.Advance));
        }
        foreach (Glyph glyph in glyphs) glyph.Image.Dispose();
        atlas.Dispose();
        Console.WriteLine("[VisitorAtlas] Wrote " + png + " and " + map + " (" + glyphs.Count +
            " glyphs; " + (partDescriptions ? "part descriptions" : "dialogue") + ")");
    }
}



