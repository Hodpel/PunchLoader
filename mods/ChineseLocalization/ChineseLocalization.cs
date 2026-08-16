using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using PunchLoader;
using UnityEngine;

// All initialization is synchronous.  The text and font hooks are registered only
// after every asset has loaded, so a menu can never render one English frame first.
public class ChineseLocalizationPlugin : IModPlugin
{
    private static bool _registered;
    private static Dictionary<string, string> _translations;
    private static Font _font;

    public string GetId() { return "ChineseLocalization"; }
    public string GetName() { return "简体中文"; }
    public string GetVersion() { return "1.0.0"; }

    public void OnLoad()
    {
        if (_registered) return;

        try
        {
            string modDirectory = Path.Combine(Path.Combine(Application.dataPath, "Mods"), GetId());
            Dictionary<string, string> translations = LoadTranslations(
                Path.Combine(modDirectory, "translations.tsv"));
            Font font = LoadFont(modDirectory);
            if (font == null) return;

            _translations = translations;
            _font = font;
            HookManager.Register(new TextTransformHandler(Translate));
            HookManager.Register(new BeginGUIHandler(PrepareMenuStyles));
            _registered = true;
            Debug.Log("[ChineseLocalization] Ready: " + _translations.Count + " translations");
        }
        catch (Exception ex)
        {
            Debug.LogError("[ChineseLocalization] Initialization failed: " + ex);
        }
    }

    public void OnUnload()
    {
        if (!_registered) return;
        HookManager.Unregister(new TextTransformHandler(Translate));
        HookManager.Unregister(new BeginGUIHandler(PrepareMenuStyles));
        _registered = false;
    }

    private static Dictionary<string, string> LoadTranslations(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("translations.tsv missing", path);
        Dictionary<string, string> result = new Dictionary<string, string>();
        using (StreamReader reader = new StreamReader(path, System.Text.Encoding.UTF8, true))
        {
            string line;
            int lineNumber = 0;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                if (line.Length == 0 || line[0] == '#') continue;
                int tab = line.IndexOf('\t');
                if (tab <= 0 || tab == line.Length - 1)
                    throw new Exception("Invalid translation row " + lineNumber);
                result[line.Substring(0, tab)] = line.Substring(tab + 1);
            }
        }
        return result;
    }

    private static Font LoadFont(string modDirectory)
    {
        string pngPath = Path.Combine(modDirectory, "font_atlas.png");
        string glyphPath = Path.Combine(modDirectory, "glyphs.tsv");
        if (!File.Exists(pngPath) || !File.Exists(glyphPath))
            throw new FileNotFoundException("font_atlas.png or glyphs.tsv missing");

        byte[] png = File.ReadAllBytes(pngPath);
        Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
        texture.filterMode = FilterMode.Bilinear;
        if (!texture.LoadImage(png))
        {
            UnityEngine.Object.Destroy(texture);
            throw new Exception("Could not load font_atlas.png");
        }

        Font font = (Font)typeof(Font).GetConstructor(new Type[] { typeof(string) }).Invoke(
            new object[] { "PunchLoader Noto Sans SC" });
        Shader shader = Shader.Find("GUI/Text Shader");
        if (shader == null) shader = Shader.Find("Diffuse");
        Material material = new Material(shader);
        material.mainTexture = texture;
        typeof(Font).GetProperty("material").SetValue(font, material, null);

        int fontSize;
        CharacterInfo[] characters = LoadCharacterInfo(glyphPath, texture.width, texture.height, out fontSize);
        typeof(Font).GetProperty("characterInfo").SetValue(font, characters, null);
        PropertyInfo sizeProperty = typeof(Font).GetProperty("fontSize");
        if (sizeProperty != null) sizeProperty.SetValue(font, fontSize, null);
        return font;
    }

    private static CharacterInfo[] LoadCharacterInfo(string path, int textureWidth, int textureHeight,
        out int fontSize)
    {
        List<CharacterInfo> characters = new List<CharacterInfo>();
        fontSize = 64;
        using (StreamReader reader = new StreamReader(path, System.Text.Encoding.UTF8, true))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                if (line.StartsWith("# atlas\t"))
                {
                    string[] header = line.Split('\t');
                    if (header.Length != 4) throw new Exception("Invalid atlas header");
                    if (ParseInt(header[1]) != textureWidth || ParseInt(header[2]) != textureHeight)
                        throw new Exception("Atlas dimensions do not match glyph map");
                    fontSize = ParseInt(header[3]);
                    continue;
                }
                if (line[0] == '#') continue;

                string[] parts = line.Split('\t');
                if (parts.Length != 10) throw new Exception("Invalid glyph row");
                int x = ParseInt(parts[1]);
                int y = ParseInt(parts[2]);
                int width = ParseInt(parts[3]);
                int height = ParseInt(parts[4]);
                CharacterInfo info = new CharacterInfo();
                info.index = ParseInt(parts[0]);
                info.uv.x = (float)x / textureWidth;
                // Unity's CharacterInfo uses the rectangle direction to choose glyph
                // orientation.  CJK rows were emitted top-to-bottom by Pillow, so a
                // positive UV height is required; the old negative height mirrored all
                // Chinese glyphs vertically at runtime.
                info.uv.y = 1f - (float)(y + height) / textureHeight;
                info.uv.width = (float)width / textureWidth;
                info.uv.height = (float)height / textureHeight;
                info.vert.x = ParseFloat(parts[5]);
                info.vert.y = ParseFloat(parts[6]);
                info.vert.width = ParseFloat(parts[7]);
                info.vert.height = ParseFloat(parts[8]);
                info.width = ParseFloat(parts[9]);
                characters.Add(info);
            }
        }
        if (characters.Count == 0) throw new Exception("Glyph map is empty");
        return characters.ToArray();
    }

    private static int ParseInt(string value)
    {
        return int.Parse(value, CultureInfo.InvariantCulture);
    }

    private static float ParseFloat(string value)
    {
        return float.Parse(value, CultureInfo.InvariantCulture);
    }

    private static string Translate(string text)
    {
        if (text == null || _translations == null) return text;
        string translated;
        if (_translations.TryGetValue(text, out translated)) return translated;

        // Dynamic labels are assembled by the original game; translate their stable prefixes.
        if (text.StartsWith("[ON]")) return "[开]" + text.Substring(4);
        if (text.StartsWith("[OFF]")) return "[关]" + text.Substring(5);
        if (text.StartsWith("player ")) return "玩家 " + text.Substring(7);
        if (text.StartsWith("Level ")) return "关卡 " + text.Substring(6);
        return text;
    }

    private static void PrepareMenuStyles(MonoBehaviour menu)
    {
        if (_font == null || menu == null) return;
        FieldInfo guiDataField = FindField(menu.GetType(), "GUIData");
        if (guiDataField == null) return;
        object guiData = guiDataField.GetValue(menu);
        if (guiData == null) return;

        Type type = guiData.GetType();
        while (type != null)
        {
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; i++)
            {
                if (!typeof(GUIStyle).IsAssignableFrom(fields[i].FieldType)) continue;
                GUIStyle style = fields[i].GetValue(guiData) as GUIStyle;
                if (style != null && style.font != _font) style.font = _font;
            }
            type = type.BaseType;
        }
    }

    private static FieldInfo FindField(Type type, string name)
    {
        while (type != null)
        {
            FieldInfo field = type.GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null) return field;
            type = type.BaseType;
        }
        return null;
    }
}
