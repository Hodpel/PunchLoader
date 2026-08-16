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
    private static Font _smallFont;
    private static Dictionary<GUIStyle, GUIStyle> _layoutStyles;
    private static Dictionary<GUIStyle, GUIStyle> _renderStyles;
    // InputConfigMenuScript builds a 700px-wide local GUILayout area and gives
    // every binding value (including X/Z) GUILayout.MinWidth(64).
    private const float InputConfigAreaWidth = 700f;
    private const float InputBindingWidth = 64f;
    // Dynamic Font has no way to inherit ACKNOWTT's native ascent.  This is the
    // measured GUI-space correction that centers the 19px Boutique glyphs in
    // the original menu label rectangle (between the selector bars).
    // Both Boutique variants became 2px taller.  Shift each overlay up half
    // that growth so its visual centre remains on the original ACKNOWTT centre.
    private const float LocalizedTextYOffset = -24f;
    private const float SmallLocalizedTextYOffset = -17f;

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
            Font font = LoadFont(modDirectory, "font_atlas.png", "glyphs.tsv",
                "PunchLoader ACKNOWTT + BoutiqueBitmap");
            Font smallFont = LoadFont(modDirectory, "font_atlas_small.png", "glyphs_small.tsv",
                "PunchLoader ACKNOWTT Small + BoutiqueBitmap");
            if (font == null || smallFont == null) return;

            _translations = translations;
            _font = font;
            _smallFont = smallFont;
            _layoutStyles = new Dictionary<GUIStyle, GUIStyle>();
            _renderStyles = new Dictionary<GUIStyle, GUIStyle>();
            HookManager.Register(new TextTransformHandler(Translate));
            HookManager.Register(new GUILayoutLabelHandler(DrawLocalizedLabel));
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
        HookManager.Unregister(new GUILayoutLabelHandler(DrawLocalizedLabel));
        if (_layoutStyles != null) _layoutStyles.Clear();
        if (_renderStyles != null) _renderStyles.Clear();
        _registered = false;
    }

    private static Dictionary<string, string> LoadTranslations(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("translations.tsv missing", path);
        Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

    private static Font LoadFont(string modDirectory, string atlasFile, string glyphFile, string fontName)
    {
        string pngPath = Path.Combine(modDirectory, atlasFile);
        string glyphPath = Path.Combine(modDirectory, glyphFile);
        if (!File.Exists(pngPath) || !File.Exists(glyphPath))
            throw new FileNotFoundException(atlasFile + " or " + glyphFile + " missing");

        byte[] png = File.ReadAllBytes(pngPath);
        Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
        // The atlas contains native-resolution bitmap pixels.  Bilinear filtering
        // creates translucent columns and destroys BoutiqueBitmap's pixel edges.
        texture.filterMode = FilterMode.Point;
        if (!texture.LoadImage(png))
        {
            UnityEngine.Object.Destroy(texture);
            throw new Exception("Could not load font_atlas.png");
        }

        Font font = (Font)typeof(Font).GetConstructor(new Type[] { typeof(string) }).Invoke(
            new object[] { fontName });
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
        if (TryTranslateInputBinding(text, out translated)) return translated;

        // Dynamic labels are assembled by the original game; translate their stable prefixes.
        if (text.StartsWith("[ON]")) return "[开]" + text.Substring(4);
        if (text.StartsWith("[OFF]")) return "[关]" + text.Substring(5);
        if (text.StartsWith("player ")) return "玩家 " + text.Substring(7);
        if (text.StartsWith("Level ")) return "关卡 " + text.Substring(6);
        return text;
    }

    private static bool TryTranslateInputBinding(string text, out string translated)
    {
        translated = null;

        if (text.StartsWith("mouse ", StringComparison.OrdinalIgnoreCase))
        {
            translated = "鼠标 " + text.Substring(6);
            return true;
        }

        if (text.StartsWith("joystick ", StringComparison.OrdinalIgnoreCase))
        {
            int buttonIndex = text.IndexOf(" button ", StringComparison.OrdinalIgnoreCase);
            if (buttonIndex > 9 && buttonIndex + 8 < text.Length)
            {
                translated = "手柄 " + text.Substring(9, buttonIndex - 9) +
                    " 按键 " + text.Substring(buttonIndex + 8);
                return true;
            }
        }

        // DrawInputButton turns Unity axis IDs such as X+LSHor2 into
        // display strings such as "LS Hor 2" before the text hook runs.
        if (TryTranslateInputBindingPrefix(text, "LS Hor ", "左摇杆横轴 ", out translated) ||
            TryTranslateInputBindingPrefix(text, "LSHor ", "左摇杆横轴 ", out translated) ||
            TryTranslateInputBindingPrefix(text, "LS Vert ", "左摇杆纵轴 ", out translated) ||
            TryTranslateInputBindingPrefix(text, "LSVert ", "左摇杆纵轴 ", out translated) ||
            TryTranslateInputBindingPrefix(text, "RS Hor ", "右摇杆横轴 ", out translated) ||
            TryTranslateInputBindingPrefix(text, "RSHor ", "右摇杆横轴 ", out translated) ||
            TryTranslateInputBindingPrefix(text, "RS Vert ", "右摇杆纵轴 ", out translated) ||
            TryTranslateInputBindingPrefix(text, "RSVert ", "右摇杆纵轴 ", out translated) ||
            TryTranslateInputBindingPrefix(text, "Pad Hor ", "十字键横轴 ", out translated) ||
            TryTranslateInputBindingPrefix(text, "PadHor ", "十字键横轴 ", out translated) ||
            TryTranslateInputBindingPrefix(text, "Pad Vert ", "十字键纵轴 ", out translated) ||
            TryTranslateInputBindingPrefix(text, "PadVert ", "十字键纵轴 ", out translated) ||
            TryTranslateInputBindingPrefix(text, "LT ", "左扳机 ", out translated) ||
            TryTranslateInputBindingPrefix(text, "RT ", "右扳机 ", out translated))
            return true;

        return false;
    }

    private static bool TryTranslateInputBindingPrefix(string text, string prefix,
        string translatedPrefix, out string translated)
    {
        translated = null;
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        translated = translatedPrefix + text.Substring(prefix.Length);
        return true;
    }

    // Keep the original style in GUILayout so all preferred sizes, menu spacing,
    // selection geometry and mouse hit regions remain exactly the game's own.
    // Only the repaint pass receives the localized composite bitmap font.
    private static bool DrawLocalizedLabel(string originalText, string renderedText,
        GUIStyle style, GUILayoutOption[] options)
    {
        if (_font == null || !ContainsChinese(renderedText)) return false;
        GUIStyle source = style;
        if (source == null && GUI.skin != null) source = GUI.skin.label;
        if (source == null) return false;

        GUILayout.Label(originalText, GetLayoutStyle(source), options);
        if (Event.current != null && Event.current.type == EventType.Repaint)
        {
            Rect rect = GUILayoutUtility.GetLastRect();
            if (IsRightInputBindingOption(originalText, rect))
            {
                // Keep the game's original GUILayout call and interaction rect.
                // Only the overlay box changes: it is right-anchored like the
                // direction keys, but uses the translated text's actual width.
                // This handles both English-wider and Chinese-wider bindings.
                GUIStyle renderStyle = GetRenderStyle(source);
                float bindingWidth = renderStyle.CalcSize(new GUIContent(renderedText)).x;
                if (bindingWidth < InputBindingWidth) bindingWidth = InputBindingWidth;
                Rect bindingRect = new Rect(rect.xMax - bindingWidth, rect.y,
                    bindingWidth, rect.height);
                GUI.Label(bindingRect, renderedText, renderStyle);
            }
            else
                GUI.Label(rect, renderedText, GetRenderStyle(source));
        }
        return true;
    }

    private static bool ContainsChinese(string text)
    {
        if (text == null) return false;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] >= '\u2e80') return true;
        }
        return false;
    }

    private static GUIStyle GetLayoutStyle(GUIStyle source)
    {
        GUIStyle result;
        if (_layoutStyles.TryGetValue(source, out result)) return result;
        result = new GUIStyle(source);
        MakeTextTransparent(result.normal);
        MakeTextTransparent(result.hover);
        MakeTextTransparent(result.active);
        MakeTextTransparent(result.focused);
        _layoutStyles[source] = result;
        return result;
    }

    private static GUIStyle GetRenderStyle(GUIStyle source)
    {
        GUIStyle result;
        if (_renderStyles.TryGetValue(source, out result)) return result;
        result = new GUIStyle(source);
        bool useSmallFont = IsSmallFontStyle(source);
        result.font = useSmallFont ? _smallFont : _font;
        result.contentOffset = new Vector2(source.contentOffset.x,
            source.contentOffset.y + (useSmallFont ? SmallLocalizedTextYOffset : LocalizedTextYOffset));
        _renderStyles[source] = result;
        return result;
    }

    private static bool IsRightInputBindingOption(string text, Rect rect)
    {
        if (text == null) return false;
        // GetLastRect is local to BeginArea, while Screen.width is global.
        // Comparing local x with Screen.width/2 made this branch unreachable
        // at wide resolutions.  The left/right halves of this menu's 700px
        // GUILayout area are instead divided at local x=350.
        if (rect.x <= InputConfigAreaWidth * 0.5f) return false;
        if (string.Equals(text, "right", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "left", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "up", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "down", StringComparison.OrdinalIgnoreCase)) return true;

        if (string.Equals(text, "left ctrl", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "right ctrl", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "left shift", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "right shift", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "left alt", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "right alt", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "left cmd", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "right cmd", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "space", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "return", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "enter", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "backspace", StringComparison.OrdinalIgnoreCase)) return true;

        return text.StartsWith("mouse ", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("joystick ", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("LS ", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("RS ", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Pad", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("LT ", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("RT ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSmallFontStyle(GUIStyle source)
    {
        if (source.fontSize > 0) return source.fontSize <= 35;
        if (source.font == null) return false;
        // Unity 4.2's Font API exposes no public fontSize property.  The
        // original assets do retain their font names, including this exact
        // small-face name, so use it before the reflection compatibility path.
        if (source.font.name != null && source.font.name.IndexOf("_small",
            StringComparison.OrdinalIgnoreCase) >= 0) return true;
        PropertyInfo property = typeof(Font).GetProperty("fontSize");
        if (property == null) return false;
        object value = property.GetValue(source.font, null);
        return value is int && (int)value <= 35;
    }

    private static void MakeTextTransparent(GUIStyleState state)
    {
        Color color = state.textColor;
        color.a = 0f;
        state.textColor = color;
    }
}
