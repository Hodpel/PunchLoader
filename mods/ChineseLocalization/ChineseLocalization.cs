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
    private static Font _dialogueFont;
    private static Font _dialogueRuntimeFont;
    private static ChineseDialogueTextWatcher _dialogueTextWatcher;
    private static bool _dialogueDataPatched;
    private static bool _dialogueFontTraceLogged;
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
    // First TextMesh proof: keep the scope intentionally to one verified line.
    // Newlines are normalized because the game serializes TextMesh text as CRLF.
    private const string DialogueProofSource = "You have our eternal\ngratitude for returning\nthe heartcore and \nrestoring peace!";
    private const string DialogueProofTranslation = "你带回了心核,\n恢复了和平,\n我们永远感激不尽!";
    private const string DialogueFollowupSource1 = "The doors to the different\nlevels are still open!";
    private const string DialogueFollowupTranslation1 = "各关卡的大门仍然敞开!";
    private const string DialogueFollowupSource2 = "It is up to you to return\nand search for hidden \ncolors, special parts and\nother secrets.";
    private const string DialogueFollowupTranslation2 = "你可以自行返回,\n去寻找隐藏的色彩、\n特殊零件和其他秘密.";

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
            Font dialogueFont = LoadFont(modDirectory, "dialogue_font_atlas.png", "dialogue_glyphs.tsv",
                "PunchLoader visitor2 + BoutiqueBitmap Bold");
            if (font == null || smallFont == null || dialogueFont == null) return;

            _translations = translations;
            _font = font;
            _smallFont = smallFont;
            _dialogueFont = dialogueFont;
            _layoutStyles = new Dictionary<GUIStyle, GUIStyle>();
            _renderStyles = new Dictionary<GUIStyle, GUIStyle>();
            HookManager.Register(new TextTransformHandler(Translate));
            HookManager.Register(new GUILayoutLabelHandler(DrawLocalizedLabel));
            HookManager.Register(new TextMeshTextHandler(DrawLocalizedTextMesh));
            CreateDialogueTextWatcher();
            PatchDialogueData();
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
        HookManager.Unregister(new TextMeshTextHandler(DrawLocalizedTextMesh));
        if (_dialogueTextWatcher != null)
        {
            UnityEngine.Object.Destroy(_dialogueTextWatcher.gameObject);
            _dialogueTextWatcher = null;
        if (_dialogueRuntimeFont != null)
        {
            UnityEngine.Object.Destroy(_dialogueRuntimeFont);
            _dialogueRuntimeFont = null;
        }
        }
        if (_layoutStyles != null) _layoutStyles.Clear();
        if (_renderStyles != null) _renderStyles.Clear();
        _dialogueDataPatched = false;
        _dialogueFontTraceLogged = false;
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
                CharacterInfo info = new CharacterInfo();
                info.index = ParseInt(parts[0]);
                if (parts.Length == 11 && parts[1] == "uv")
                {
                    // visitor2 ASCII is copied without repacking. Preserve its
                    // signed UV rectangles and original metrics exactly.
                    info.uv.x = ParseFloat(parts[2]);
                    info.uv.y = ParseFloat(parts[3]);
                    info.uv.width = ParseFloat(parts[4]);
                    info.uv.height = ParseFloat(parts[5]);
                    info.vert.x = ParseFloat(parts[6]);
                    info.vert.y = ParseFloat(parts[7]);
                    info.vert.width = ParseFloat(parts[8]);
                    info.vert.height = ParseFloat(parts[9]);
                    info.width = ParseFloat(parts[10]);
                }
                else
                {
                    if (parts.Length != 10) throw new Exception("Invalid glyph row");
                    int x = ParseInt(parts[1]);
                    int y = ParseInt(parts[2]);
                    int width = ParseInt(parts[3]);
                    int height = ParseInt(parts[4]);
                    info.uv.x = (float)x / textureWidth;
                    // CJK rows are written top-to-bottom in the PNG.
                    info.uv.y = 1f - (float)(y + height) / textureHeight;
                    info.uv.width = (float)width / textureWidth;
                    info.uv.height = (float)height / textureHeight;
                    info.vert.x = ParseFloat(parts[5]);
                    info.vert.y = ParseFloat(parts[6]);
                    info.vert.width = ParseFloat(parts[7]);
                    info.vert.height = ParseFloat(parts[8]);
                    info.width = ParseFloat(parts[9]);
                }
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

    // This handler runs inside HookDispatcher.SetTextMeshText before Unity's
    // original TextMesh setter. No original English is assigned when the proof
    // line is written through a managed setter.
    private static bool DrawLocalizedTextMesh(TextMesh textMesh, string originalText)
    {
        return ApplyLocalizedTextMesh(textMesh, originalText);
    }

    // Dialogue prefabs can deserialize TextMesh.m_Text directly, bypassing
    // TextMesh.set_text. Scan live TextMeshes so that path is translated too.
    private static void CreateDialogueTextWatcher()
    {
        GameObject host = new GameObject("PunchLoader.ChineseLocalization.DialogueWatcher");
        UnityEngine.Object.DontDestroyOnLoad(host);
        _dialogueTextWatcher = (ChineseDialogueTextWatcher)host.AddComponent(typeof(ChineseDialogueTextWatcher));
    }

    private static bool ApplyLocalizedTextMesh(TextMesh textMesh, string originalText)
    {
        if (textMesh == null || _dialogueFont == null) return false;

        string translated;
        if (!TryTranslateDialogueProof(originalText, out translated)) return false;

        ApplyDialogueFont(textMesh);
        textMesh.text = translated;
        Debug.Log("[ChineseLocalization] Replaced dialogue proof TextMesh: " + textMesh.gameObject.name);
        return true;
    }

    private static void ApplyDialogueFont(TextMesh textMesh)
    {
        if (textMesh == null || _dialogueFont == null) return;
        if (_dialogueRuntimeFont == null)
        {
            // new Font(name) creates a dynamic font. TextMesh does not consume its
            // injected CharacterInfo table reliably in this Unity version. Clone
            // the original static visitor2 instance instead, then replace only its
            // material and character table with the mixed atlas.
            _dialogueRuntimeFont = UnityEngine.Object.Instantiate(textMesh.font) as Font;
            if (_dialogueRuntimeFont == null) return;
            _dialogueRuntimeFont.name = "PunchLoader visitor2 + BoutiqueBitmap Bold Runtime";
            typeof(Font).GetProperty("material").SetValue(_dialogueRuntimeFont,
                _dialogueFont.material, null);
            object characters = typeof(Font).GetProperty("characterInfo").GetValue(_dialogueFont, null);
            typeof(Font).GetProperty("characterInfo").SetValue(_dialogueRuntimeFont, characters, null);
        }
        textMesh.font = _dialogueRuntimeFont;
        textMesh.fontSize = 50;
        if (textMesh.renderer != null) textMesh.renderer.material = _dialogueRuntimeFont.material;        if (!_dialogueFontTraceLogged)
        {
            CharacterInfo[] infos = (CharacterInfo[])typeof(Font).GetProperty("characterInfo").GetValue(_dialogueRuntimeFont, null);
            bool hasChineseGlyph = false;
            for (int i = 0; i < infos.Length; i++)
                if (infos[i].index == 20320) { hasChineseGlyph = true; break; }
            _dialogueFontTraceLogged = true;
            Debug.Log("[ChineseLocalization] Dialogue font trace: object=" + textMesh.gameObject.name +
                ", assigned=" + textMesh.font.name + ", meshFontSize=" + textMesh.fontSize +
                ", glyphs=" + infos.Length + ", hasU4F60=" + hasChineseGlyph +
                ", text=" + textMesh.text);
        }
    }    public static void TickDialogueTextWatcher()
    {
        PatchDialogueData();
        UnityEngine.Object[] objects = Resources.FindObjectsOfTypeAll(typeof(TextMesh));
        for (int i = 0; i < objects.Length; i++)
        {
            TextMesh textMesh = objects[i] as TextMesh;
            if (textMesh == null) continue;
            if (ContainsChinese(textMesh.text)) ApplyDialogueFont(textMesh);
            else ApplyLocalizedTextMesh(textMesh, textMesh.text);
        }
    }
    // TextBoxScript reveals dialogue character by character. Replacing its source
    // data before StartFirstLine makes the typewriter reveal Chinese from its first
    // glyph rather than waiting until the English line has completed.
    private static void PatchDialogueData()
    {
        if (_dialogueDataPatched) return;

        Type gameHandlerType = FindLoadedType("GameHandler");
        if (gameHandlerType == null) return;
        PropertyInfo instanceProperty = gameHandlerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        object gameHandler = instanceProperty == null ? null : instanceProperty.GetValue(null, null);
        if (gameHandler == null) return;

        FieldInfo dialogDataField = gameHandlerType.GetField("dialogData", BindingFlags.Public | BindingFlags.Instance);
        object dialogData = dialogDataField == null ? null : dialogDataField.GetValue(gameHandler);
        if (dialogData == null) return;
        FieldInfo dialogsField = dialogData.GetType().GetField("dialogs", BindingFlags.Public | BindingFlags.Instance);
        Array dialogs = dialogsField == null ? null : dialogsField.GetValue(dialogData) as Array;
        if (dialogs == null) return;

        for (int dialogIndex = 0; dialogIndex < dialogs.Length; dialogIndex++)
        {
            object dialog = dialogs.GetValue(dialogIndex);
            if (dialog == null) continue;
            FieldInfo linesField = dialog.GetType().GetField("lines", BindingFlags.Public | BindingFlags.Instance);
            string[] lines = linesField == null ? null : linesField.GetValue(dialog) as string[];
            if (lines == null) continue;
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string translated;
                if (!TryTranslateDialogueProof(lines[lineIndex], out translated)) continue;
                lines[lineIndex] = translated;
                _dialogueDataPatched = true;
                Debug.Log("[ChineseLocalization] Replaced dialogue data: dialog " + dialogIndex + ", line " + lineIndex);
            }
        }
    }

    private static Type FindLoadedType(string typeName)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type type = assemblies[i].GetType(typeName, false);
            if (type != null) return type;
        }
        return null;
    }
    private static bool TryTranslateDialogueProof(string text, out string translated)
    {
        translated = null;
        if (text == null) return false;
        string normalized = text.Replace("\r\n", "\n").TrimEnd();
        if (string.Equals(normalized, DialogueProofSource, StringComparison.Ordinal))
        {
            translated = DialogueProofTranslation;
            return true;
        }
        if (string.Equals(normalized, DialogueFollowupSource1, StringComparison.Ordinal))
        {
            translated = DialogueFollowupTranslation1;
            return true;
        }
        if (string.Equals(normalized, DialogueFollowupSource2, StringComparison.Ordinal))
        {
            translated = DialogueFollowupTranslation2;
            return true;
        }
        return false;
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
// Must be a top-level MonoBehaviour. Unity 4.2 refuses nested plug-in behaviours
// when adding a component at runtime.
public class ChineseDialogueTextWatcher : MonoBehaviour
{
    private void Update()
    {
        ChineseLocalizationPlugin.TickDialogueTextWatcher();
    }
}
















