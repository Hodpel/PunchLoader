using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
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
    private static Font _partFont;
    private static Font _partDescriptionFont;
    private static Font _menuTextMeshRuntimeFont;
    private static Dictionary<string, string> _dialogueTranslations;
    private static Dictionary<string, string> _partNameTranslations;
    private static Dictionary<string, string> _partDescriptionTranslations;
    private static Dictionary<string, string> _abilityTranslations;
    // Mono 2.x lacks HashSet<T>; use Dictionary keys for membership instead.
    private static Dictionary<string, bool> _localizedPartNames;
    private static Dictionary<string, bool> _localizedPartDescriptions;
    private static Dictionary<string, bool> _localizedAbilityDescriptions;
    private static Dictionary<TextMesh, bool> _pendingBottomPrompts;
    private static Dictionary<TextMesh, Vector3> _inventoryWheelTitleBasePositions;
    private static Font _dialogueRuntimeFont;
    private static Font _partRuntimeFont;
    private static Font _partDescriptionRuntimeFont;
    private static ChineseDialogueTextWatcher _dialogueTextWatcher;
    private static bool _dialogueDataPatched;
    private static Dictionary<GUIStyle, GUIStyle> _layoutStyles;
    private static Dictionary<GUIStyle, GUIStyle> _renderStyles;
    private static Dictionary<GUIStyle, GUIStyle> _modListRenderStyles;
    // InputConfigMenuScript builds a 700px-wide local GUILayout area and gives
    // every binding value (including X/Z) GUILayout.MinWidth(64).
    private const float InputConfigAreaWidth = 700f;
    private const float InputBindingWidth = 64f;
    // The Visitor description panel is substantially wider than its English
    // source lines.  Chinese is reflowed to this measured visual width instead
    // of inheriting the original English line breaks.
    private const float PartDescriptionLineWidth = 24f;
    // The original ASCII space is deliberately wide in both ACKNOWTT and
    // Visitor.  Localized text uses this dedicated glyph only at a CJK/ASCII
    // boundary, where it renders at half the original space advance.
    private const char MixedTextSpace = '\u2009';
    // Dynamic Font has no way to inherit ACKNOWTT's native ascent.  This is the
    // measured GUI-space correction that centers the 19px Boutique glyphs in
    // the original menu label rectangle (between the selector bars).
    // CJK glyph metrics are centred against ACKNOWTT internally.  Move the
    // complete menu overlay down by the same 2px so its established visual
    // position between the selector bars remains unchanged.
    private const float LocalizedTextYOffset = -23f;
    private const float SmallLocalizedTextYOffset = -16f;
    public string GetId() { return "ChineseLocalization"; }
    public string GetName() { return "Simplified Chinese"; }
    public string GetVersion() { return "1.0.0"; }

    public void OnLoad()
    {
        if (_registered) return;

        try
        {
            string modDirectory = ModPaths.GetModDirectory(GetId());
            string dataDirectory = Path.Combine(modDirectory, "data");
            string fontDirectory = Path.Combine(modDirectory, "fonts");
            Dictionary<string, string> translations = LoadTranslations(
                Path.Combine(dataDirectory, "ui.tsv"));
            Dictionary<string, string> dialogueTranslations = LoadDialogueTranslations(
                Path.Combine(dataDirectory, "dialogue.tsv"));
            LoadPartTranslations(Path.Combine(dataDirectory, "parts.tsv"));
            LoadAbilityTranslations(Path.Combine(dataDirectory, "abilities.tsv"));
            Font font = LoadFont(fontDirectory, "ack_large.png", "ack_large.tsv",
                "PunchLoader ACKNOWTT + BoutiqueBitmap");
            Font smallFont = LoadFont(fontDirectory, "ack_small.png", "ack_small.tsv",
                "PunchLoader ACKNOWTT Small + BoutiqueBitmap");
            Font dialogueFont = LoadFont(fontDirectory, "visitor.png", "visitor.tsv",
                "PunchLoader visitor2 + BoutiqueBitmap Bold");
            if (font == null || smallFont == null || dialogueFont == null) return;

            _translations = translations;
            _font = font;
            _smallFont = smallFont;
            _dialogueFont = dialogueFont;
            _partFont = font;
            _partDescriptionFont = dialogueFont;
            _dialogueTranslations = dialogueTranslations;
            _layoutStyles = new Dictionary<GUIStyle, GUIStyle>();
            _renderStyles = new Dictionary<GUIStyle, GUIStyle>();
            _modListRenderStyles = new Dictionary<GUIStyle, GUIStyle>();
            _pendingBottomPrompts = new Dictionary<TextMesh, bool>();
            _inventoryWheelTitleBasePositions = new Dictionary<TextMesh, Vector3>();
            HookManager.Register(new TextTransformHandler(Translate));
            HookManager.Register(new GUILayoutLabelHandler(DrawLocalizedLabel));
            HookManager.Register(new TextMeshTextHandler(DrawLocalizedTextMesh));
            CreateDialogueTextWatcher();
            PatchDialogueData();
            PatchPartDescriptionData();
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
        if (_partRuntimeFont != null)
        {
            UnityEngine.Object.Destroy(_partRuntimeFont);
            _partRuntimeFont = null;
        }
        if (_partDescriptionRuntimeFont != null)
        {
            UnityEngine.Object.Destroy(_partDescriptionRuntimeFont);
            _partDescriptionRuntimeFont = null;
        }
        if (_menuTextMeshRuntimeFont != null)
        {
            UnityEngine.Object.Destroy(_menuTextMeshRuntimeFont);
            _menuTextMeshRuntimeFont = null;
        }
        }
        if (_layoutStyles != null) _layoutStyles.Clear();
        if (_renderStyles != null) _renderStyles.Clear();
        _dialogueDataPatched = false;
        _abilityTranslations = null;
        if (_localizedAbilityDescriptions != null) _localizedAbilityDescriptions.Clear();
        if (_pendingBottomPrompts != null) _pendingBottomPrompts.Clear();
        if (_inventoryWheelTitleBasePositions != null) _inventoryWheelTitleBasePositions.Clear();
        _registered = false;
    }

    private static Dictionary<string, string> LoadTranslations(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("UI translation table missing", path);
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
                result[line.Substring(0, tab).Replace("\\n", "\n")] =
                    line.Substring(tab + 1).Replace("\\n", "\n");
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
            throw new Exception("Could not load bitmap font atlas: " + atlasFile);
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
        string normalizedLineBreaks = NormalizePartText(text);
        if (_translations.TryGetValue(normalizedLineBreaks, out translated)) return translated;
        if (TryTranslateInputBinding(text, out translated)) return translated;

        // Dynamic labels are assembled by the original game; translate their stable prefixes.
        if (text.StartsWith("[ON]")) return "[开]" + TranslateModListSuffix(text.Substring(4));
        if (text.StartsWith("[OFF]")) return "[关]" + TranslateModListSuffix(text.Substring(5));
        if (text.StartsWith("player ")) return "玩家 " + text.Substring(7);
        if (text.StartsWith("Level ")) return "关卡 " + text.Substring(6);
        return text;
    }

    private static string TranslateModListSuffix(string text)
    {
        return text.Replace("Simplified Chinese", "简体中文")
            .Replace("[RESTART]", "[需要重启]");
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
                GUIStyle renderStyle = GetLocalizedRenderStyle(originalText, source);
                float bindingWidth = renderStyle.CalcSize(new GUIContent(renderedText)).x;
                if (bindingWidth < InputBindingWidth) bindingWidth = InputBindingWidth;
                Rect bindingRect = new Rect(rect.xMax - bindingWidth, rect.y,
                    bindingWidth, rect.height);
                GUI.Label(bindingRect, renderedText, renderStyle);
            }
            else
                GUI.Label(rect, renderedText, GetLocalizedRenderStyle(originalText, source));
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
        bool levelCompleteDescription;
        if (TryTranslateBottomPrompt(originalText, out translated))
        {
            QueueBottomPrompt(textMesh, translated, GetBottomPromptKind(originalText));
            return true;
        }
        else if (TryTranslateRepositoryText(textMesh, originalText, out translated))
        {
            ApplyPartFont(textMesh);
            ApplyBuildsRepositoryTextLayout(textMesh, translated);
        }
        else if (TryTranslateInventoryWheelTitle(textMesh, originalText, out translated))
        {
            ApplyPartFont(textMesh);
            ApplyInventoryWheelTitleLayout(textMesh);
        }
        else if (TryTranslateInventoryWheelDescription(textMesh, originalText, out translated))
            ApplyPartDescriptionFont(textMesh);
        else if (TryTranslateInventoryWheelStats(textMesh, originalText, out translated))
        {
            ApplyPartDescriptionFont(textMesh);
            ApplyInventoryWheelStatsLayout(textMesh);
        }
        else if (TryTranslateLevelCompleteText(textMesh, originalText, out translated,
            out levelCompleteDescription))
        {
            if (levelCompleteDescription) ApplyPartDescriptionFont(textMesh);
            else ApplyPartFont(textMesh);
        }
        else if (TryTranslateTransientStatusText(originalText, out translated))
            ApplyPartFont(textMesh);
        else if (TryTranslateShopText(textMesh, originalText, out translated))
            ApplyPartFont(textMesh);
        else if (TryTranslateDialogueProof(originalText, out translated))
            ApplyDialogueFont(textMesh);
        else if (TryTranslatePartText(originalText, out translated))
            ApplyPartDescriptionFont(textMesh);
        else
            return false;
        textMesh.text = translated;
        return true;
    }

    private static bool TryTranslateTransientStatusText(string text, out string translated)
    {
        translated = null;
        if (text == null) return false;
        string normalized = text.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        if (string.Equals(normalized, "Color collected!", StringComparison.OrdinalIgnoreCase))
        {
            translated = "已获得配色!";
            return true;
        }
        if (string.Equals(normalized, "+ 1 life!", StringComparison.OrdinalIgnoreCase))
        {
            translated = "+" + MixedTextSpace + "1" + MixedTextSpace + "条命!";
            return true;
        }
        return false;
    }

    private static bool IsLocalizedTransientStatusText(string text)
    {
        if (text == null) return false;
        string compact = text.Replace(MixedTextSpace.ToString(), string.Empty)
            .Replace(" ", string.Empty).Trim();
        return string.Equals(compact, "已获得配色!", StringComparison.Ordinal) ||
            string.Equals(compact, "+1条命!", StringComparison.Ordinal);
    }

    private static bool TryTranslateLevelCompleteText(TextMesh textMesh, string text,
        out string translated, out bool description)
    {
        translated = null;
        description = false;
        if (text == null) return false;
        string normalized = text.Replace("\r\n", "\n").Replace("\r", "\n").Trim();

        const string levelPrefix = "Level ";
        const string levelSuffix = " completed!";
        if (normalized.StartsWith(levelPrefix, StringComparison.OrdinalIgnoreCase) &&
            normalized.EndsWith(levelSuffix, StringComparison.OrdinalIgnoreCase))
        {
            string number = normalized.Substring(levelPrefix.Length,
                normalized.Length - levelPrefix.Length - levelSuffix.Length).Trim();
            translated = "第" + MixedTextSpace + number + MixedTextSpace + "关完成!";
            return true;
        }

        if (string.Equals(normalized, "GET:", StringComparison.OrdinalIgnoreCase))
        {
            translated = "获得:";
            return true;
        }
        if (string.Equals(normalized, "OK", StringComparison.OrdinalIgnoreCase) &&
            IsLevelCompleteTextMesh(textMesh))
        {
            translated = "确定";
            return true;
        }
        if (string.Equals(normalized, "Tournament won!", StringComparison.OrdinalIgnoreCase))
        {
            translated = "锦标赛获胜!";
            return true;
        }

        description = true;
        if (string.Equals(normalized,
            "You've beaten Warlord Bouldar and recieved his drill part.\n" +
            "Check the collection chest in your house!", StringComparison.Ordinal))
            translated = "你击败了军阀" + MixedTextSpace + "Bouldar,获得了他的钻头零件.\n" +
                "请到家中的收藏仓库查看!";
        else if (string.Equals(normalized, "You've beaten General HB-02!",
            StringComparison.Ordinal))
            translated = "你击败了" + MixedTextSpace + "HB-02" + MixedTextSpace + "将军!";
        else if (string.Equals(normalized, "You have defeated Grand Khotep Scarb!",
            StringComparison.Ordinal))
            translated = "你击败了" + MixedTextSpace + "Khotep" + MixedTextSpace +
                "领主" + MixedTextSpace + "Scarb!";
        else if (string.Equals(normalized, "You have defeated Grand Khotep Muer!",
            StringComparison.Ordinal))
            translated = "你击败了" + MixedTextSpace + "Khotep" + MixedTextSpace +
                "领主" + MixedTextSpace + "Muer!";
        else if (string.Equals(normalized, "You have defeated the Ice-Beak Assassins!",
            StringComparison.Ordinal))
            translated = "你击败了冰喙刺客!";
        else if (string.Equals(normalized, "You have defeated the General HB-03!",
            StringComparison.Ordinal))
            translated = "你击败了" + MixedTextSpace + "HB-03" + MixedTextSpace + "将军!";
        else if (string.Equals(normalized, "You've received a special part!",
            StringComparison.Ordinal))
            translated = "你获得了一个特殊零件!";
        else
        {
            description = false;
            return false;
        }
        return true;
    }

    private static bool IsLevelCompleteTextMesh(TextMesh textMesh)
    {
        if (textMesh == null || textMesh.transform == null) return false;
        Transform current = textMesh.transform;
        while (current != null)
        {
            string name = current.gameObject == null ? string.Empty : current.gameObject.name;
            if (name.IndexOf("levelComplete", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            current = current.parent;
        }
        return false;
    }

    private static bool IsLevelCompleteDescriptionTextMesh(TextMesh textMesh)
    {
        if (!IsLevelCompleteTextMesh(textMesh) || textMesh.transform == null) return false;
        Transform current = textMesh.transform;
        while (current != null)
        {
            string name = current.gameObject == null ? string.Empty : current.gameObject.name;
            if (name.IndexOf("levelCompleteText", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            current = current.parent;
        }
        return false;
    }

    // ShopAbilityScript assembles these labels at runtime by appending the
    // current price/bit count to a stable English prefix.  Translate only the
    // prefix and preserve the game's line break and numeric value verbatim.
    private static bool TryTranslateShopText(TextMesh textMesh, string text,
        out string translated)
    {
        translated = null;
        if (text == null) return false;
        string normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");

        if (normalized.StartsWith("Price:", StringComparison.OrdinalIgnoreCase))
        {
            translated = "价格:" + normalized.Substring("Price:".Length);
            return true;
        }
        if (normalized.StartsWith("Total bits:", StringComparison.OrdinalIgnoreCase))
        {
            translated = "持有" + MixedTextSpace + "Bits:" +
                normalized.Substring("Total bits:".Length);
            return true;
        }
        if (string.Equals(normalized.Trim(), "Sold out.", StringComparison.OrdinalIgnoreCase))
        {
            translated = "已售罄.";
            return true;
        }

        // Yes/No are shared with ordinary menus, but the shop creates them as
        // TextMeshes instead of GUILayout labels.  Restrict this branch to the
        // shop hierarchy so unrelated world text is left untouched.
        if (IsShopTextMesh(textMesh))
        {
            string value = normalized.Trim();
            if (string.Equals(value, "Yes", StringComparison.OrdinalIgnoreCase))
            {
                translated = "是";
                return true;
            }
            if (string.Equals(value, "No", StringComparison.OrdinalIgnoreCase))
            {
                translated = "否";
                return true;
            }
        }
        return false;
    }

    private static bool IsShopTextMesh(TextMesh textMesh)
    {
        if (textMesh == null || textMesh.transform == null) return false;
        Type shopType = FindLoadedType("ShopAbilityScript");
        Transform current = textMesh.transform;
        while (current != null)
        {
            if (shopType != null && current.GetComponent(shopType) != null) return true;
            string name = current.gameObject == null ? string.Empty : current.gameObject.name;
            if (name.IndexOf("shop", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            current = current.parent;
        }
        return false;
    }

    private static bool IsLocalizedShopText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        string normalized = text.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        return normalized.StartsWith("价格:", StringComparison.Ordinal) ||
            normalized.StartsWith("持有" + MixedTextSpace + "Bits:", StringComparison.Ordinal) ||
            string.Equals(normalized, "已售罄.", StringComparison.Ordinal);
    }

    private static void QueueBottomPrompt(TextMesh textMesh, string translated, int collectionPromptKind)
    {
        if (textMesh == null) return;
        if (_dialogueTextWatcher == null)
        {
            ApplyMenuTextMeshFont(textMesh);
            textMesh.text = translated;
            return;
        }
        if (_pendingBottomPrompts != null && _pendingBottomPrompts.ContainsKey(textMesh)) return;
        if (_pendingBottomPrompts != null) _pendingBottomPrompts[textMesh] = true;
        Color sourceColor = textMesh.color;
        Color transparentColor = sourceColor;
        transparentColor.a = 0f;
        // Renderer.enabled prevents Unity 4.2 from building this TextMesh's
        // geometry. Keep it enabled and hide only its vertex colour, so the
        // source bounds exist at the end of the frame without an English flash.
        textMesh.color = transparentColor;
        _dialogueTextWatcher.StartBottomPromptLayout(textMesh, translated, collectionPromptKind, sourceColor);
    }

    // Unity 4.2 reports an empty Renderer.bounds for serialized TextMesh
    // objects even while they are visibly rendered.  The prompt background is
    // an ordinary MeshRenderer, though, so its centre is a reliable layout
    // reference.  The text colour remains transparent throughout, so no
    // English frame is visible.
    public static IEnumerator LayoutBottomPrompt(TextMesh textMesh, string translated,
        int collectionPromptKind, Color sourceColor)
    {
        yield return new WaitForEndOfFrame();
        if (_pendingBottomPrompts != null) _pendingBottomPrompts.Remove(textMesh);
        if (textMesh == null) yield break;

        ApplyMenuTextMeshFont(textMesh);
        textMesh.text = translated;
        CenterRepositoryPrompt(textMesh, collectionPromptKind);
        ApplyInventoryWheelPromptSize(textMesh, collectionPromptKind);
        OffsetInventoryWheelPromptLocal(textMesh, collectionPromptKind);
        textMesh.color = sourceColor;
    }

    // Parts, Color and Builds share the same prompt prefab geometry.  Their
    // buttons have enough room for the menu's large 23px CJK face when the
    // original two lines collapse to one.  The TextMesh scale makes a 23px
    // glyph appear at about 17px on screen, hence this measured 23 / 17
    // correction.  InventoryWheel uses different button artwork.
    private const float CollectionPromptCharacterSize = 1.35f;
    // The uploaded 192x64 capture is displayed at about 40% of the game's
    // native screen size.  The first correction therefore moved only 12px / 4px
    // in that capture.  These native-pixel values include the remaining
    // calibrated 18.5px-left / 4.5px-up visual adjustment.
    private const float CollectionPromptScreenOffsetX = -76.5f;
    private const float CollectionPromptScreenOffsetY = 19f;
    // The inventory wheel's current localized caption is about 17px high.
    // Scale it to the requested 21px without affecting its prefab position.
    private const float InventoryWheelPromptCharacterSize = 1.235f;
    // InventoryWheel is laid out in the direct parent panel's 0..1 local space.
    // These are measured visual-centering deltas, not camera/screen offsets.
    private const float InventoryConfirmLocalOffsetX = -0.017f;
    private const float InventoryConfirmLocalOffsetY = -0.0888f;

    private const float InventoryBackLocalOffsetX = 0.062f;
    private const float InventoryBackLocalOffsetY = -0.1089f;

    // Kind 1: Punch / pick. Kind 2: Special / return.
    private static void CenterRepositoryPrompt(TextMesh textMesh, int kind)
    {
        if (textMesh == null || kind == 0 || !IsRepositoryPrompt(textMesh)) return;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.characterSize = CollectionPromptCharacterSize;

        // The shadow is a child of the white TextMesh.  Centering its parent
        // moves both together; retain the serialized child offset for the
        // original pixel-shadow effect.
        if (textMesh.gameObject.name.EndsWith("Shadow", StringComparison.OrdinalIgnoreCase)) return;

        Transform button = textMesh.transform.parent;
        Renderer buttonRenderer = button == null ? null :
            button.GetComponent(typeof(Renderer)) as Renderer;
        Camera camera = FindContainingGuiCamera(textMesh.transform);
        if (buttonRenderer == null || camera == null)
        {
            Debug.LogWarning("[ChineseLocalization] Repository prompt layout reference unavailable: " +
                textMesh.gameObject.name);
            return;
        }

        Bounds buttonBounds = buttonRenderer.bounds;
        if (buttonBounds.size.sqrMagnitude <= 0.0001f)
        {
            Debug.LogWarning("[ChineseLocalization] Repository prompt panel bounds unavailable: " +
                textMesh.gameObject.name);
            return;
        }

        Vector3 pivot = textMesh.transform.position;
        Vector3 panelScreen = camera.WorldToScreenPoint(buttonBounds.center);
        Vector3 pivotScreen = camera.WorldToScreenPoint(pivot);
        if (panelScreen.z <= 0f || pivotScreen.z <= 0f) return;
        panelScreen.x += CollectionPromptScreenOffsetX;
        panelScreen.y += CollectionPromptScreenOffsetY;
        panelScreen.z = pivotScreen.z;
        textMesh.transform.position = camera.ScreenToWorldPoint(panelScreen);
    }

    private static bool IsRepositoryPrompt(TextMesh textMesh)
    {
        Transform root = textMesh.transform == null ? null : textMesh.transform.root;
        if (root == null || root.gameObject == null) return false;
        string rootName = root.gameObject.name;
        return rootName.IndexOf("CollectionGUI", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rootName.IndexOf("ColorGUI", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rootName.IndexOf("BuildsGUI", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // Color and Builds construct their list text one line at a time through
    // TextMesh.set_text.  Translate each line independently so an already
    // localized earlier line does not prevent a later English line from being
    // recognized.  These labels deliberately use the parts font: their source
    // TextMeshes share the same ACKNOWTT repository presentation.
    private static bool TryTranslateRepositoryText(TextMesh textMesh, string text,
        out string translated)
    {
        translated = null;
        if (text == null || _translations == null) return false;
        if (!IsRepositoryTextMesh(textMesh) && !IsRepositoryStatusSourceText(text))
            return false;

        // ColorGUI and BuildsGUI append one item plus a trailing newline on
        // every setter call. Do not use NormalizePartText here: its TrimEnd()
        // would remove that separator and concatenate the next English item.
        string normalized = NormalizeRepositoryText(text);
        if (_translations.TryGetValue(normalized, out translated)) return true;
        string[] lines = normalized.Split('\n');
        StringBuilder result = new StringBuilder();
        bool replaced = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string localized;
            if (TryTranslateBuildSlot(line, out localized) ||
                _translations.TryGetValue(line, out localized))
            {
                result.Append(localized);
                if (!string.Equals(line, localized, StringComparison.Ordinal)) replaced = true;
            }
            else result.Append(line);
            if (i < lines.Length - 1) result.Append('\n');
        }
        if (!replaced) return false;
        translated = result.ToString();
        return true;
    }

    private static bool IsRepositoryTextMesh(TextMesh textMesh)
    {
        if (textMesh == null || textMesh.transform == null) return false;
        Transform root = textMesh.transform.root;
        if (root == null || root.gameObject == null) return false;
        string rootName = root.gameObject.name;
        return rootName.IndexOf("ColorGUI", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rootName.IndexOf("BuildsGUI", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // Keep the wheel's five top-level action labels separate from generic
    // translations. In particular, the title "Return" must not be confused
    // with the keyboard key label, whose established translation is "回车".
    private static bool TryTranslateInventoryWheelTitle(TextMesh textMesh, string text,
        out string translated)
    {
        translated = null;
        if (!IsInventoryWheelTextMesh(textMesh) || text == null) return false;
        string normalized = NormalizePartText(text);
        bool actionLabel = IsInventoryWheelActionLabel(textMesh);
        if (string.Equals(normalized, "Attach parts", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Attach part", StringComparison.OrdinalIgnoreCase))
            translated = actionLabel ? "装 配 零 件" : "装配零件";
        else if (string.Equals(normalized, "Break parts into bits", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Break into\nbits", StringComparison.OrdinalIgnoreCase))
            translated = actionLabel ? "分 解 零 件" : "分解零件";
        else if (string.Equals(normalized, "Swap Abilities", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Abilities", StringComparison.OrdinalIgnoreCase))
            translated = actionLabel ? "交 换 技 能" : "交换技能";
        else if (string.Equals(normalized, "Parts & Statistics", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Parts", StringComparison.OrdinalIgnoreCase))
            translated = actionLabel ? "部 件 属 性" : "部件属性";
        else if (string.Equals(normalized, "Return", StringComparison.OrdinalIgnoreCase))
            translated = actionLabel ? "返        回" : "返回";
        else if (string.Equals(normalized, "Select part", StringComparison.OrdinalIgnoreCase))
            translated = "选" + MixedTextSpace + "择" + MixedTextSpace +
                "零" + MixedTextSpace + "件";
        else if (string.Equals(normalized, "Pick part slot", StringComparison.OrdinalIgnoreCase))
            translated = "选择零件槽位";
        else if (string.Equals(normalized, "Pick direction slot", StringComparison.OrdinalIgnoreCase))
            translated = "选择方向槽位";
        else if (string.Equals(normalized, "Spare abilities", StringComparison.OrdinalIgnoreCase))
            translated = "备用技能";
        return translated != null;
    }

    // The wheel's source labels appear at about 21px with the composite font.
    // This measured multiplier makes CJK 23px, i.e. 4px taller than the
    // original English.  The stored base position allows manual local offsets
    // without accumulating movement whenever the text watcher runs again.
    private const float InventoryWheelTitleCharacterSize = 1.095f;
    // First-pass visual centring deltas measured from the 363x686 reference
    // capture. Positive local X moves left because these labels face -Y.
    private const float InventoryAttachLocalOffsetX = -0.15f;
    private const float InventoryAttachLocalOffsetY = -0.011f;

    private const float InventoryAbilitiesLocalOffsetX = -0.040f;
    private const float InventoryAbilitiesLocalOffsetY = 0.022f;

    private const float InventoryBreakLocalOffsetX = -0.093f;
    private const float InventoryBreakLocalOffsetY = -0.120f;

    private const float InventoryStatsLocalOffsetX = 0.03f;
    private const float InventoryStatsLocalOffsetY = 0.011f;

    private const float InventoryReturnLocalOffsetX = 0.020f;
    private const float InventoryReturnLocalOffsetY = -0.011f;
    private const float InventorySelectPartLocalOffsetX = -0.16f;

    private static void ApplyInventoryWheelTitleLayout(TextMesh textMesh)
    {
        bool actionLabel = IsInventoryWheelActionLabel(textMesh);
        string compact = NormalizePartText(textMesh == null ? null : textMesh.text)
            .Replace(MixedTextSpace.ToString(), string.Empty)
            .Replace(" ", string.Empty);
        bool selectPart = string.Equals(compact, "Selectpart", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(compact, "选择零件", StringComparison.Ordinal);
        if (!actionLabel && !selectPart) return;
        if (actionLabel) textMesh.characterSize = InventoryWheelTitleCharacterSize;
        if (textMesh.gameObject.name.EndsWith("Shadow", StringComparison.OrdinalIgnoreCase)) return;

        Vector3 basePosition;
        if (_inventoryWheelTitleBasePositions == null)
            _inventoryWheelTitleBasePositions = new Dictionary<TextMesh, Vector3>();
        if (!_inventoryWheelTitleBasePositions.TryGetValue(textMesh, out basePosition))
        {
            basePosition = textMesh.transform.localPosition;
            _inventoryWheelTitleBasePositions[textMesh] = basePosition;
        }
        Vector2 offset = selectPart ? new Vector2(InventorySelectPartLocalOffsetX, 0f) :
            GetInventoryWheelTitleOffset(textMesh.gameObject.name);
        textMesh.transform.localPosition = new Vector3(basePosition.x + offset.x,
            basePosition.y + offset.y, basePosition.z);
    }

    private static Vector2 GetInventoryWheelTitleOffset(string objectName)
    {
        if (string.Equals(objectName, "pick", StringComparison.OrdinalIgnoreCase))
            return new Vector2(InventoryAttachLocalOffsetX, InventoryAttachLocalOffsetY);
        if (string.Equals(objectName, "abilties", StringComparison.OrdinalIgnoreCase))
            return new Vector2(InventoryAbilitiesLocalOffsetX, InventoryAbilitiesLocalOffsetY);
        if (string.Equals(objectName, "break", StringComparison.OrdinalIgnoreCase))
            return new Vector2(InventoryBreakLocalOffsetX, InventoryBreakLocalOffsetY);
        if (string.Equals(objectName, "stats", StringComparison.OrdinalIgnoreCase))
            return new Vector2(InventoryStatsLocalOffsetX, InventoryStatsLocalOffsetY);
        if (string.Equals(objectName, "return", StringComparison.OrdinalIgnoreCase))
            return new Vector2(InventoryReturnLocalOffsetX, InventoryReturnLocalOffsetY);
        return Vector2.zero;
    }

    private static bool IsInventoryWheelActionLabel(TextMesh textMesh)
    {
        if (!IsInventoryWheelTextMesh(textMesh) || textMesh.gameObject == null) return false;
        string name = textMesh.gameObject.name;
        return string.Equals(name, "pick", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "abilties", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "break", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "stats", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "return", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInventoryWheelTextMesh(TextMesh textMesh)
    {
        if (textMesh == null || textMesh.transform == null) return false;
        if (!textMesh.gameObject.activeInHierarchy) return false;
        Transform current = textMesh.transform;
        while (current != null)
        {
            if (current.gameObject.GetComponent("InventoryGraphicScript") != null)
                return true;
            current = current.parent;
        }
        return false;
    }

    private static bool IsInventoryWheelTitleText(TextMesh textMesh, string text)
    {
        if (!IsInventoryWheelTextMesh(textMesh)) return false;
        string compact = NormalizePartText(text)
            .Replace(MixedTextSpace.ToString(), string.Empty)
            .Replace(" ", string.Empty);
        return string.Equals(compact, "装配零件", StringComparison.Ordinal) ||
            string.Equals(compact, "分解零件", StringComparison.Ordinal) ||
            string.Equals(compact, "交换技能", StringComparison.Ordinal) ||
            string.Equals(compact, "部件属性", StringComparison.Ordinal) ||
            string.Equals(compact, "返回", StringComparison.Ordinal) ||
            string.Equals(compact, "选择零件", StringComparison.Ordinal) ||
            string.Equals(compact, "选择零件槽位", StringComparison.Ordinal) ||
            string.Equals(compact, "选择方向槽位", StringComparison.Ordinal) ||
            string.Equals(compact, "备用技能", StringComparison.Ordinal);
    }

    private static bool TryTranslateInventoryWheelDescription(TextMesh textMesh, string text,
        out string translated)
    {
        translated = null;
        if (!IsInventoryWheelTextMesh(textMesh) || text == null || _translations == null)
            return false;
        string normalized = NormalizeAbilityText(text);
        if (_translations.TryGetValue(normalized, out translated)) return true;

        // Several inventory states append a second status block to the same
        // TextMesh. Translate each known block independently instead of
        // requiring the combined runtime string to exist in translations.tsv.
        string combined = normalized;
        bool replaced = false;
        replaced |= ReplaceInventoryTextBlock(ref combined,
            "Destroy this part? No bits will\nbe recieved.",
            "分解此零件? 不会获得 Bits.");
        replaced |= ReplaceInventoryTextBlock(ref combined,
            "Destroy this part to salvage 4\nbits from it.",
            "分解此零件可回收 4 Bits.");
        replaced |= ReplaceInventoryTextBlock(ref combined,
            "Destroy this part to salvage 2\nbits from it.",
            "分解此零件可回收 2 Bits.");
        replaced |= ReplaceInventoryTextBlock(ref combined,
            "This part is in your collection", "此零件已收录.");
        replaced |= ReplaceInventoryTextBlock(ref combined,
            "This part is not yet in your\ncollection", "此零件尚未收录.");
        replaced |= ReplaceInventoryTextBlock(ref combined,
            "While playing, hold the chosen\ndirection and press the ability\nbutton to activate.",
            "游戏中按住所选方向并按技能键即可发动.");
        replaced |= ReplaceInventoryTextBlock(ref combined,
            "No direction + Special.", "无方向 + 技能键.");
        replaced |= ReplaceInventoryTextBlock(ref combined,
            "Up + Special.", "上 + 技能键.");
        replaced |= ReplaceInventoryTextBlock(ref combined,
            "Side + Special.", "左右 + 技能键.");
        replaced |= ReplaceInventoryTextBlock(ref combined,
            "Down + Special.", "下 + 技能键.");
        if (!replaced) return false;
        translated = combined;
        return true;
    }

    private static bool ReplaceInventoryTextBlock(ref string text, string source, string localized)
    {
        if (text.IndexOf(source, StringComparison.OrdinalIgnoreCase) < 0) return false;
        int index = text.IndexOf(source, StringComparison.OrdinalIgnoreCase);
        text = text.Substring(0, index) + localized + text.Substring(index + source.Length);
        return true;
    }

    private static bool IsInventoryWheelDescriptionTextMesh(TextMesh textMesh)
    {
        return IsInventoryWheelTextMesh(textMesh) && textMesh.gameObject != null &&
            textMesh.gameObject.name.StartsWith("Description", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInventoryWheelDescriptionText(TextMesh textMesh, string text)
    {
        if (!IsInventoryWheelTextMesh(textMesh) || text == null) return false;
        string normalized = NormalizeAbilityText(text);
        return string.Equals(normalized, "你目前没有可装配的零件.", StringComparison.Ordinal) ||
            string.Equals(normalized,
                "分解零件以回收 Bits.\n只有关卡中获得的零件\n才能分解为 Bits.",
                StringComparison.Ordinal) ||
            string.Equals(normalized, "分解零件以回收 Bits.", StringComparison.Ordinal) ||
            string.Equals(normalized, "你目前没有可分解的零件.", StringComparison.Ordinal) ||
            string.Equals(normalized, "重新排列技能.", StringComparison.Ordinal) ||
            string.Equals(normalized, "查看当前装配的零件.", StringComparison.Ordinal) ||
            string.Equals(normalized, "返回游戏.", StringComparison.Ordinal) ||
            string.Equals(normalized, "选择要交换的技能槽.", StringComparison.Ordinal) ||
            string.Equals(normalized, "现在选择另一个技能槽进行交换.\n也可以从备用技能中选择.", StringComparison.Ordinal) ||
            string.Equals(normalized, "选择要装配零件的位置.", StringComparison.Ordinal) ||
            string.Equals(normalized, "分解此零件? 不会获得 Bits.", StringComparison.Ordinal) ||
            normalized.StartsWith("分解此零件可回收 ", StringComparison.Ordinal) ||
            string.Equals(normalized, "此零件已收录.", StringComparison.Ordinal) ||
            string.Equals(normalized, "此零件尚未收录.", StringComparison.Ordinal) ||
            normalized.EndsWith(" + 技能键.", StringComparison.Ordinal) ||
            string.Equals(normalized, "将技能配置到上方向.", StringComparison.Ordinal) ||
            string.Equals(normalized, "游戏中按住所选方向并按技能键即可发动.", StringComparison.Ordinal) ||
            normalized.StartsWith("总属性:", StringComparison.Ordinal);
    }

    private static bool TryTranslateInventoryWheelStats(TextMesh textMesh, string text,
        out string translated)
    {
        translated = null;
        if (!IsInventoryWheelTextMesh(textMesh) || text == null) return false;
        string normalized = NormalizeAbilityText(text);
        if (!normalized.StartsWith("Total stats:", StringComparison.OrdinalIgnoreCase)) return false;

        translated = normalized
            .Replace("Total stats:", "总属性:")
            .Replace("Extra damage:", "额外伤害:")
            .Replace("Attack speed:", "攻击速度:")
            .Replace("Armor:", "护甲:")
            .Replace("Shield life:", "护盾值:")
            .Replace("Jumps:", "跳跃次数:")
            .Replace("Movement speed:", "移动速度:")
            .Replace("Red Virus Immunity", "红色病毒免疫");
        return true;
    }

    // The CJK glyphs are intentionally 4px taller than Visitor ASCII. Keep
    // the eight-line statistics block inside its original panel by reducing
    // only the inter-line advance; glyph size remains unchanged.
    private static void ApplyInventoryWheelStatsLayout(TextMesh textMesh)
    {
        if (textMesh == null) return;
        textMesh.lineSpacing = 0.68f;
    }

    private static bool TryTranslateBuildSlot(string text, out string translated)
    {
        translated = null;
        if (text == null || !text.StartsWith("Set ", StringComparison.OrdinalIgnoreCase))
            return false;
        string number = text.Substring(4);
        if (number.Length == 0) return false;
        for (int i = 0; i < number.Length; i++)
            if (number[i] < '0' || number[i] > '9') return false;
        translated = "配装" + MixedTextSpace + number;
        return true;
    }

    private static string NormalizeRepositoryText(string text)
    {
        if (text == null) return string.Empty;
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private static bool IsRepositoryStatusText(string text)
    {
        string normalized = NormalizePartText(text);
        return string.Equals(normalized, "配装已读取!", StringComparison.Ordinal) ||
            string.Equals(normalized, "配装已保存!", StringComparison.Ordinal) ||
            string.Equals(normalized, "零件已获取!", StringComparison.Ordinal) ||
            string.Equals(normalized, "背包已满!", StringComparison.Ordinal);
    }

    private static bool IsRepositoryStatusSourceText(string text)
    {
        string normalized = NormalizeRepositoryText(text);
        return string.Equals(normalized, "Set Loaded!", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Set Saved!", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Part downloaded!", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Maximum amount\nreached!", StringComparison.OrdinalIgnoreCase);
    }

    // These two Builds-only overlays use a smaller original ACKNOWTT mesh than
    // the repository names.  1.235 is the measured 17px -> 21px correction,
    // preserving the project-wide rule that Chinese is 4px taller than its
    // English source.  Positions are in each overlay panel's local space;
    // assigning, rather than adding, keeps repeated TextMesh updates stable.
    private const float BuildsLoadSaveCharacterSize = 1.235f;
    private const float BuildsLoadSaveLineSpacing = 1.1f;
    private const float BuildsLoadSaveLocalOffsetX = -0.002f;
    private const float BuildsLoadSaveLocalOffsetY = 0.217f;

    private const float BuildsStatusCharacterSize = 1.526f;
    private const float BuildsStatusLocalOffsetX = -0.004f;
    private const float BuildsStatusLocalOffsetY = 0.125f;

    private static void ApplyBuildsRepositoryTextLayout(TextMesh textMesh, string text)
    {
        if (textMesh == null) return;
        bool isLoadSave = IsBuildsLoadSaveText(text);
        bool isStatus = IsRepositoryStatusText(text);
        if (!isLoadSave && !isStatus) return;

        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.characterSize = isLoadSave ? BuildsLoadSaveCharacterSize :
            BuildsStatusCharacterSize;

        // The shadow is a child of the white TextMesh and follows its parent.
        if (isLoadSave)
            textMesh.lineSpacing = BuildsLoadSaveLineSpacing;

        if (textMesh.gameObject.name.EndsWith("Shadow", StringComparison.OrdinalIgnoreCase)) return;
        Vector3 local = textMesh.transform.localPosition;
        if (isLoadSave)
        {
            // textMesh.lineSpacing = BuildsLoadSaveLineSpacing;
            local.x = BuildsLoadSaveLocalOffsetX;
            local.y = BuildsLoadSaveLocalOffsetY;
        }
        else
        {
            local.x = BuildsStatusLocalOffsetX;
            local.y = BuildsStatusLocalOffsetY;
        }
        textMesh.transform.localPosition = local;
    }

    private static bool IsBuildsLoadSaveText(string text)
    {
    string normalized = NormalizeRepositoryText(text);
    if (string.Equals(normalized, "Load\nSave\nCancel",
        StringComparison.OrdinalIgnoreCase))
        return true;

    string compact = normalized
        .Replace(MixedTextSpace.ToString(), string.Empty)
        .Replace(" ", string.Empty);

    return string.Equals(compact, "读取\n保存\n取消",
        StringComparison.Ordinal);
    }

    private static void ApplyInventoryWheelPromptSize(TextMesh textMesh, int kind)
    {
        if (textMesh == null || (kind != 3 && kind != 4)) return;
        Transform root = textMesh.transform == null ? null : textMesh.transform.root;
        if (root == null || root.gameObject == null ||
            root.gameObject.name.IndexOf("InventoryWheel", StringComparison.OrdinalIgnoreCase) < 0)
            return;
        textMesh.characterSize = InventoryWheelPromptCharacterSize;
    }

    private static void OffsetInventoryWheelPromptLocal(TextMesh textMesh, int kind)
    {
        if (textMesh == null || (kind != 3 && kind != 4)) return;
        Transform root = textMesh.transform == null ? null : textMesh.transform.root;
        if (root == null || root.gameObject == null ||
            root.gameObject.name.IndexOf("InventoryWheel", StringComparison.OrdinalIgnoreCase) < 0 ||
            textMesh.gameObject.name.EndsWith("Shadow", StringComparison.OrdinalIgnoreCase))
            return;
        Vector3 local = textMesh.transform.localPosition;
        if (kind == 3)
        {
            local.x += InventoryConfirmLocalOffsetX;
            local.y += InventoryConfirmLocalOffsetY;
        }
        else
        {
            // The measured left margin is already within one pixel of centre;
            // only correct the return caption's vertical position for now.
            local.x += InventoryBackLocalOffsetX;
            local.y += InventoryBackLocalOffsetY;
        }
        textMesh.transform.localPosition = local;
    }


    private static Camera FindContainingGuiCamera(Transform transform)
    {
        Transform current = transform;
        while (current != null)
        {
            Camera camera = current.GetComponent(typeof(Camera)) as Camera;
            if (camera != null && camera.enabled) return camera;
            current = current.parent;
        }
        return null;
    }

    // The Build, Collection and Color prefabs all use these two TextMesh
    // prompts (each with a shadow companion). They are serialized with CRLF,
    // whereas the translation table uses the portable \n spelling.
    private static bool TryTranslateBottomPrompt(string text, out string translated)
    {
        translated = null;
        if (text == null || _translations == null) return false;
        string normalized = NormalizePartText(text);
        if (!string.Equals(normalized, "Punch \n- Pick", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(normalized, "Special \n- Return", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(normalized, "Punch \n- confirm", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(normalized, "Special \n- Back", StringComparison.OrdinalIgnoreCase))
            return false;
        return _translations.TryGetValue(normalized, out translated);
    }

    private static int GetBottomPromptKind(string text)
    {
        string normalized = NormalizePartText(text);
        if (string.Equals(normalized, "Punch \n- Pick", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (string.Equals(normalized, "Special \n- Return", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (string.Equals(normalized, "Punch \n- confirm", StringComparison.OrdinalIgnoreCase))
            return 3;
        if (string.Equals(normalized, "Special \n- Back", StringComparison.OrdinalIgnoreCase))
            return 4;
        return 0;
    }

    private static bool IsBottomPromptText(string text)
    {
        string normalized = NormalizePartText(text);
        return string.Equals(normalized, "攻击" + MixedTextSpace + "-" + MixedTextSpace + "选择",
                StringComparison.Ordinal) ||
            string.Equals(normalized, "技能" + MixedTextSpace + "-" + MixedTextSpace + "返回",
                StringComparison.Ordinal) ||
            string.Equals(normalized, "攻击" + MixedTextSpace + "-" + MixedTextSpace + "确认",
                StringComparison.Ordinal);
    }

    // Bottom prompts use the original bold ACKNOWTT face, not the thinner
    // Visitor face used by dialogue/description panels.
    private static void ApplyMenuTextMeshFont(TextMesh textMesh)
    {
        if (textMesh == null || _font == null) return;
        if (_menuTextMeshRuntimeFont == null)
        {
            _menuTextMeshRuntimeFont = UnityEngine.Object.Instantiate(textMesh.font) as Font;
            if (_menuTextMeshRuntimeFont == null) return;
            _menuTextMeshRuntimeFont.name = "PunchLoader ACKNOWTT + BoutiqueBitmap Bottom Prompts Runtime";
            typeof(Font).GetProperty("material").SetValue(_menuTextMeshRuntimeFont, _font.material, null);
            object characters = typeof(Font).GetProperty("characterInfo").GetValue(_font, null);
            typeof(Font).GetProperty("characterInfo").SetValue(_menuTextMeshRuntimeFont, characters, null);
        }
        if (textMesh.font == _menuTextMeshRuntimeFont) return;

        Material sourceMaterial = textMesh.renderer == null ? null : textMesh.renderer.material;
        textMesh.font = _menuTextMeshRuntimeFont;
        if (sourceMaterial != null)
        {
            sourceMaterial.mainTexture = _font.material.mainTexture;
            textMesh.renderer.material = sourceMaterial;
        }
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
        if (textMesh.font != _dialogueRuntimeFont)
        {
            // White text and its dark offset shadow are separate TextMeshes.
            // Keep the existing renderer material (and therefore its colour),
            // but point that material at the localized atlas.  Accessing
            // renderer.material gives this renderer its own material instance;
            // unlike constructing a new Material during startup, this is safe
            // on the game's Unity version.
            Material sourceMaterial = textMesh.renderer == null ? null : textMesh.renderer.material;
            textMesh.font = _dialogueRuntimeFont;
            textMesh.fontSize = 50;
            if (sourceMaterial != null)
            {
                sourceMaterial.mainTexture = _dialogueFont.material.mainTexture;
                textMesh.renderer.material = sourceMaterial;
            }
        }
    }

    // Collection, inventory and reward screens use ACKNOWTT, the same heavier
    // face as the menu.  Their original font size is retained; this atlas uses
    // matching ACKNOWTT metrics for the Chinese baseline.
    private static void ApplyPartFont(TextMesh textMesh)
    {
        if (textMesh == null || _partFont == null) return;
        if (_partRuntimeFont == null)
        {
            _partRuntimeFont = UnityEngine.Object.Instantiate(textMesh.font) as Font;
            if (_partRuntimeFont == null) return;
            _partRuntimeFont.name = "PunchLoader ACKNOWTT + BoutiqueBitmap Bold Parts Runtime";
            typeof(Font).GetProperty("material").SetValue(_partRuntimeFont,
                _partFont.material, null);
            object characters = typeof(Font).GetProperty("characterInfo").GetValue(_partFont, null);
            typeof(Font).GetProperty("characterInfo").SetValue(_partRuntimeFont, characters, null);
        }
        if (textMesh.font == _partRuntimeFont) return;

        Material sourceMaterial = textMesh.renderer == null ? null : textMesh.renderer.material;
        textMesh.font = _partRuntimeFont;
        if (sourceMaterial != null)
        {
            sourceMaterial.mainTexture = _partFont.material.mainTexture;
            textMesh.renderer.material = sourceMaterial;
        }
    }

    private static void ApplyPartDescriptionFont(TextMesh textMesh)
    {
        if (textMesh == null || _partDescriptionFont == null) return;
        if (_partDescriptionRuntimeFont == null)
        {
            _partDescriptionRuntimeFont = UnityEngine.Object.Instantiate(textMesh.font) as Font;
            if (_partDescriptionRuntimeFont == null) return;
            _partDescriptionRuntimeFont.name = "PunchLoader visitor2 + BoutiqueBitmap Bold Part Descriptions Runtime";
            typeof(Font).GetProperty("material").SetValue(_partDescriptionRuntimeFont,
                _partDescriptionFont.material, null);
            object characters = typeof(Font).GetProperty("characterInfo").GetValue(_partDescriptionFont, null);
            typeof(Font).GetProperty("characterInfo").SetValue(_partDescriptionRuntimeFont, characters, null);
        }
        if (textMesh.font == _partDescriptionRuntimeFont) return;

        Material sourceMaterial = textMesh.renderer == null ? null : textMesh.renderer.material;
        textMesh.font = _partDescriptionRuntimeFont;
        textMesh.fontSize = 50;
        if (sourceMaterial != null)
        {
            sourceMaterial.mainTexture = _partDescriptionFont.material.mainTexture;
            textMesh.renderer.material = sourceMaterial;
        }
    }

    public static void TickDialogueTextWatcher()
    {
        PatchDialogueData();
        PatchPartDescriptionData();
        UnityEngine.Object[] objects = Resources.FindObjectsOfTypeAll(typeof(TextMesh));
        for (int i = 0; i < objects.Length; i++)
        {
            TextMesh textMesh = objects[i] as TextMesh;
            if (textMesh == null) continue;
            if (ContainsChinese(textMesh.text))
            {
                if (IsBottomPromptText(textMesh.text)) ApplyMenuTextMeshFont(textMesh);
                else if (IsLevelCompleteTextMesh(textMesh))
                {
                    if (IsLevelCompleteDescriptionTextMesh(textMesh))
                        ApplyPartDescriptionFont(textMesh);
                    else ApplyPartFont(textMesh);
                }
                else if (IsShopTextMesh(textMesh) || IsLocalizedShopText(textMesh.text))
                    ApplyPartFont(textMesh);
                else if (IsLocalizedTransientStatusText(textMesh.text))
                    ApplyPartFont(textMesh);
                else if (IsRepositoryTextMesh(textMesh) || IsRepositoryStatusText(textMesh.text) ||
                    IsInventoryWheelTitleText(textMesh, textMesh.text) || IsPartNameText(textMesh.text))
                {
                    ApplyPartFont(textMesh);
                    ApplyBuildsRepositoryTextLayout(textMesh, textMesh.text);
                    if (IsInventoryWheelTitleText(textMesh, textMesh.text))
                        ApplyInventoryWheelTitleLayout(textMesh);
                }
                else if (IsInventoryWheelDescriptionText(textMesh, textMesh.text) ||
                    IsPartDescriptionText(textMesh.text) || IsAbilityDescriptionText(textMesh.text))
                {
                    ApplyPartDescriptionFont(textMesh);
                    if (IsInventoryWheelTextMesh(textMesh) &&
                        NormalizeAbilityText(textMesh.text).StartsWith("总属性:", StringComparison.Ordinal))
                        ApplyInventoryWheelStatsLayout(textMesh);
                }
                else ApplyDialogueFont(textMesh);
            }
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
        if (text == null || _dialogueTranslations == null) return false;
        string normalized = text.Replace("\r\n", "\n").TrimEnd();
        return _dialogueTranslations.TryGetValue(normalized, out translated);
    }

    private static Dictionary<string, string> LoadDialogueTranslations(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Dialogue translation table missing", path);
        Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
        using (StreamReader reader = new StreamReader(path, System.Text.Encoding.UTF8, true))
        {
            string line;
            int lineNumber = 0;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                if (lineNumber == 1 || line.Length == 0 || line[0] == '#') continue;
                string[] parts = line.Split(new char[] { '\t' }, 4);
                if (parts.Length != 4) throw new Exception("Invalid dialogue translation row " + lineNumber);
                string source = parts[2].Replace("\\n", "\n").TrimEnd();
                string translated = parts[3].Replace("\\n", "\n");
                // Pure ASCII dialogue must keep the game's original Visitor
                // font, material, spacing and authored line breaks. Registering
                // an identity "translation" would route it through the merged
                // Chinese dialogue font for no localization benefit.
                if (!ContainsChinese(translated)) continue;
                result[source] = translated;
            }
        }
        if (result.Count == 0) throw new Exception("No dialogue translations loaded");
        return result;
    }

    private static void LoadAbilityTranslations(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Ability translation table missing", path);
        _abilityTranslations = new Dictionary<string, string>(StringComparer.Ordinal);
        _localizedAbilityDescriptions = new Dictionary<string, bool>(StringComparer.Ordinal);
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
                    throw new Exception("Invalid ability translation row " + lineNumber);

                string source = NormalizeAbilityText(line.Substring(0, tab).Replace("\\n", "\n"));
                string localized = NormalizeAbilityText(line.Substring(tab + 1).Replace("\\n", "\n"));
                _abilityTranslations[source] = localized;
                _localizedAbilityDescriptions[localized] = true;
            }
        }
        if (_abilityTranslations.Count == 0) throw new Exception("No ability translations loaded");
    }

    private static void LoadPartTranslations(string path)
    {
        _partNameTranslations = new Dictionary<string, string>();
        _partDescriptionTranslations = new Dictionary<string, string>();
        _localizedPartNames = new Dictionary<string, bool>();
        _localizedPartDescriptions = new Dictionary<string, bool>();
        foreach (string line in File.ReadAllLines(path))
        {
            string[] p = line.Split(new char[] { '\t' }, 3);
            if (p.Length != 3 || p[0] == "kind") continue;
            Dictionary<string, string> target = p[0] == "name" ? _partNameTranslations : _partDescriptionTranslations;
            string localized = p[2].Replace("\\n", "\n");
            if (p[0] == "description") localized = WrapPartDescription(localized);
            target[NormalizePartText(p[1].Replace("\\n", "\n"))] = localized;
            if (p[0] == "name") _localizedPartNames[localized] = true;
            else _localizedPartDescriptions[localized] = true;
        }
    }

    // Names and collection lists use ACKNOWTT.  Descriptions deliberately do
    // not pass this test: the original game renders them with Visitor.
    private static bool IsPartNameText(string text)
    {
        if (text == null || _localizedPartNames == null)
            return false;
        string normalized = NormalizePartText(text);
        if (_localizedPartNames.ContainsKey(normalized))
            return true;
        string[] lines = normalized.Split('\n');
        for (int i = 0; i < lines.Length; i++)
            if (_localizedPartNames.ContainsKey(lines[i])) return true;
        return false;
    }

    private static bool IsPartDescriptionText(string text)
    {
        if (text == null || _localizedPartDescriptions == null) return false;
        string normalized = NormalizePartText(text);
        if (_localizedPartDescriptions.ContainsKey(normalized)) return true;
        foreach (string description in _localizedPartDescriptions.Keys)
            if (normalized.EndsWith(description, StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool IsAbilityDescriptionText(string text)
    {
        if (text == null || _localizedAbilityDescriptions == null) return false;
        string normalized = NormalizeAbilityText(text);
        if (_localizedAbilityDescriptions.ContainsKey(normalized)) return true;
        foreach (string description in _localizedAbilityDescriptions.Keys)
            if (normalized.StartsWith(description + "\n\n\n", StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool TryTranslatePartText(string text, out string translated)
    {
        translated = null;
        if (text == null || _partNameTranslations == null || _partDescriptionTranslations == null ||
            _abilityTranslations == null)
            return false;
        string normalized = NormalizePartText(text);
        if (_partNameTranslations.TryGetValue(normalized, out translated)) return true;
        if (_partDescriptionTranslations.TryGetValue(normalized, out translated)) return true;

        string current = normalized;
        bool replaced = TryReplaceAbilityDescription(current, out current);
        foreach (KeyValuePair<string, string> item in _partDescriptionTranslations)
        {
            if (!current.EndsWith(item.Key, StringComparison.Ordinal)) continue;
            current = current.Substring(0, current.Length - item.Key.Length) + item.Value;
            replaced = true;
            break;
        }
        if (!replaced) return false;
        translated = current;
        return true;
    }

    private static string NormalizePartText(string text)
    {
        if (text == null) return string.Empty;
        return text.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd();
    }

    // Ability prefab descriptions contain trailing spaces and, in one case, a
    // leading blank line. Normalizing every individual line allows exact,
    // deterministic matching without relaxing the source text lookup.
    private static string NormalizeAbilityText(string text)
    {
        if (text == null) return string.Empty;
        string[] lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        StringBuilder result = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd();
            if (i == 0 && line.Length == 0) continue;
            if (result.Length > 0) result.Append('\n');
            result.Append(line);
        }
        return result.ToString().Trim();
    }

    // The collection screen builds one TextMesh from an ability description
    // followed by three line breaks and the static part description. Translate
    // just the leading ability block here; the caller then replaces the
    // localized/English part-description suffix in the same write.
    private static bool TryReplaceAbilityDescription(string text, out string translated)
    {
        translated = text;
        string normalized = NormalizeAbilityText(text);
        foreach (KeyValuePair<string, string> item in _abilityTranslations)
        {
            if (string.Equals(normalized, item.Key, StringComparison.Ordinal))
            {
                translated = item.Value;
                return true;
            }
            string separator = item.Key + "\n\n\n";
            if (!normalized.StartsWith(separator, StringComparison.Ordinal)) continue;
            translated = item.Value + normalized.Substring(item.Key.Length);
            return true;
        }
        return false;
    }

    private static string WrapPartDescription(string text)
    {
        string source = NormalizePartText(text).Replace("\n", string.Empty);
        StringBuilder result = new StringBuilder();
        float lineWidth = 0f;
        int index = 0;
        while (index < source.Length)
        {
            string token = NextPartToken(source, ref index);
            if (token.Length == 0) continue;
            if (token == " " || token == MixedTextSpace.ToString())
            {
                if (lineWidth > 0f)
                {
                    result.Append(token);
                    lineWidth += token == " " ? 0.35f : 0.18f;
                }
                continue;
            }

            float tokenWidth = MeasurePartToken(token);
            // A punctuation mark after Chinese was previously an individual
            // ASCII token.  When a line was full it was pushed onto a blank
            // next line.  Keep closing punctuation attached to the preceding
            // line; a one-character overflow is preferable to an orphan dot.
            if (lineWidth > 0f && lineWidth + tokenWidth > PartDescriptionLineWidth &&
                !IsTrailingPartPunctuation(token))
            {
                while (result.Length > 0 &&
                    (result[result.Length - 1] == ' ' || result[result.Length - 1] == MixedTextSpace))
                    result.Length--;
                result.Append('\n');
                lineWidth = 0f;
            }
            result.Append(token);
            lineWidth += tokenWidth;
        }
        return result.ToString();
    }

    private static string NextPartToken(string text, ref int index)
    {
        char first = text[index++];
        if (first == ' ' || first == MixedTextSpace) return first.ToString();
        if (first > 126) return first.ToString();

        int start = index - 1;
        while (index < text.Length)
        {
            char next = text[index];
            if (next > 126 || next == ' ' || next == MixedTextSpace) break;
            index++;
        }
        return text.Substring(start, index - start);
    }

    private static float MeasurePartToken(string token)
    {
        float result = 0f;
        for (int i = 0; i < token.Length; i++)
        {
            char character = token[i];
            if (character == MixedTextSpace) result += 0.18f;
            else if (character > 126) result += 1f;
            else if (character == ' ') result += 0.35f;
            else result += 0.65f;
        }
        return result;
    }

    private static bool IsTrailingPartPunctuation(string token)
    {
        if (token.Length == 0) return false;
        for (int i = 0; i < token.Length; i++)
        {
            char character = token[i];
            if (character != '.' && character != ',' && character != '!' &&
                character != '?' && character != ':' && character != ';' &&
                character != ')' && character != ']' && character != '}') return false;
        }
        return true;
    }

    private static void PatchPartDescriptionData()
    {
        Type type = FindLoadedType("GameHandler");
        PropertyInfo instance = type == null ? null : type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        object handler = instance == null ? null : instance.GetValue(null, null);
        FieldInfo field = type == null ? null : type.GetField("partDescriptionData", BindingFlags.Public | BindingFlags.Instance);
        object data = handler == null || field == null ? null : field.GetValue(handler);
        if (data == null) return;
        string[] names = data.GetType().GetField("names").GetValue(data) as string[];
        string[] descriptions = data.GetType().GetField("descriptions").GetValue(data) as string[];
        for (int i = 0; i < names.Length; i++)
        {
            string v;
            if (_partNameTranslations.TryGetValue(NormalizePartText(names[i]), out v)) names[i] = v;
            if (i < descriptions.Length && _partDescriptionTranslations.TryGetValue(NormalizePartText(descriptions[i]), out v)) descriptions[i] = v;
        }
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

    private static GUIStyle GetLocalizedRenderStyle(string originalText, GUIStyle source)
    {
        if (!IsModListEntry(originalText)) return GetRenderStyle(source);

        GUIStyle result;
        if (_modListRenderStyles.TryGetValue(source, out result)) return result;
        result = new GUIStyle(GetRenderStyle(source));

        // The localized baseline is already correct.  The selected
        // fakeButtonStyle merely clips the taller composite glyphs, so only
        // relax clipping here; do not replace the established contentOffset.
        result.clipping = TextClipping.Overflow;

        _modListRenderStyles[source] = result;
        return result;
    }

    private static bool IsModListEntry(string text)
    {
        return text != null &&
            (text.StartsWith("[ON]", StringComparison.Ordinal) ||
             text.StartsWith("[OFF]", StringComparison.Ordinal));
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
    public void StartBottomPromptLayout(TextMesh textMesh, string translated, int collectionPromptKind,
        Color sourceColor)
    {
        StartCoroutine(ChineseLocalizationPlugin.LayoutBottomPrompt(textMesh, translated,
            collectionPromptKind, sourceColor));
    }

    private void Update()
    {
        ChineseLocalizationPlugin.TickDialogueTextWatcher();
    }

}




