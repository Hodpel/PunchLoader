using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// ============================================================================
// PunchLoader — Megabyte Punch mod 框架
// 编译: csc @build_loader.rsp → PunchLoader.dll
// 放置: MegabytePunch_Data\Managed\PunchLoader.dll
// 入口: Bootstrap.Init() 由 Injector.exe IL 注入到 MenuScript.Start() 中调用
// 平台: .NET 2.0 (Mono 2.x) + Unity 4.2.2f1
// 注意: 不使用 var / LINQ / lambda / System.Core（.NET 2.0 不支持）
// ============================================================================
namespace PunchLoader
{
    // ====================================================================
    // ModInfo: 单个 mod 的运行时信息
    // 一个 ModInfo 对应一个 plugin.json → 一个 .dll → 一个 IModPlugin 实例
    // ====================================================================
    public class ModInfo
    {
        public string Id;          // plugin.json 中的 "id"
        public string Name;        // plugin.json 中的 "name"
        public string Version;     // plugin.json 中的 "version"
        public string Author;      // plugin.json 中的 "author"
        public string EntryType;   // plugin.json 中的 "entryType" — 入口类的完整类型名
        public int Priority;       // plugin.json 中的 "priority" — 加载优先级（越小越先）
        public IModPlugin Plugin;  // 实例化后的 mod 对象
        public Assembly Assembly;  // mod 的 dll 程序集
        public bool Loaded;        // 是否已调用 OnLoad()
        public bool Enabled = true;// 是否启用（ModsSubMenu 可切换）
    }

    // ====================================================================
    // IModPlugin: 所有 mod 必须实现此接口
    // 放在 Mods/ 子目录的每个 .dll 中有一个实现此接口的类
    // ====================================================================
    public interface IModPlugin
    {
        string GetId();       // 返回 plugin.json 中的 id
        string GetName();     // 返回 plugin.json 中的 name
        string GetVersion();  // 返回 plugin.json 中的 version
        void OnLoad();        // 游戏启动时 mod 加载回调
        void OnUnload();      // 游戏退出时 mod 卸载回调
    }

    // ====================================================================
    // SimpleJson: 手写 JSON 解析器
    // .NET 2.0 没有 Json.NET / System.Json，只能用纯字符串解析
    // 只支持一层 { "key": "value" } — 够解析 plugin.json 了
    // ====================================================================
    public static class SimpleJson
    {
        public static Dictionary<string, string> ParseObject(string json)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            json = json.Trim();
            if (json.StartsWith("{")) json = json.Substring(1);
            if (json.EndsWith("}")) json = json.Substring(0, json.Length - 1);

            int i = 0;
            while (i < json.Length)
            {
                // 跳过空白和逗号
                while (i < json.Length && (json[i] == ' ' || json[i] == '\r' || json[i] == '\n' || json[i] == '\t' || json[i] == ','))
                    i++;
                if (i >= json.Length) break;

                // 解析 key: "key"
                int quoteStart = json.IndexOf('"', i);
                if (quoteStart < 0) break;
                int quoteEnd = json.IndexOf('"', quoteStart + 1);
                if (quoteEnd < 0) break;
                string key = json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);

                // 跳过 ':'
                i = json.IndexOf(':', quoteEnd + 1);
                if (i < 0) break;
                i++;

                // 跳过空白
                while (i < json.Length && (json[i] == ' ' || json[i] == '\r' || json[i] == '\n' || json[i] == '\t'))
                    i++;
                if (i >= json.Length) break;

                // 解析 value: "string" 或 数字/bool
                string val;
                if (json[i] == '"')
                {
                    int vEnd = json.IndexOf('"', i + 1);
                    if (vEnd < 0) break;
                    val = json.Substring(i + 1, vEnd - i - 1);
                    i = vEnd + 1;
                }
                else
                {
                    int vEnd = i;
                    while (vEnd < json.Length && json[vEnd] != ',' && json[vEnd] != '}' && json[vEnd] != '\r' && json[vEnd] != '\n')
                        vEnd++;
                    val = json.Substring(i, vEnd - i).Trim();
                    i = vEnd;
                }

                result[key] = val;
            }
            return result;
        }

        // 从解析后的对象中取 int 值，找不到返回 def
        public static int GetInt(Dictionary<string, string> obj, string key, int def)
        {
            string val;
            if (obj.TryGetValue(key, out val))
            {
                int r;
                if (int.TryParse(val, out r))
                    return r;
            }
            return def;
        }
    }

    // ====================================================================
    // Bootstrap: mod 系统的点火器
    // 由 Injector.exe 注入的 IL 代码调用：Bootstrap.Init()
    // 调用时机：第一个场景 splashScreen.unity 加载时（MenuScript.Start()）
    // 做了：创建 DontDestroyOnLoad 的 GameObject → 挂 ModLoaderBehaviour + ModListMenu
    // ====================================================================
    public class Bootstrap
    {
        public static void Init()
        {
            try
            {
                Debug.Log("[PunchLoader] Bootstrap.Init() called");

                // 创建永不销毁的 GameObject，作为 mod 系统的根
                GameObject go = new GameObject("ModLoader");
                UnityEngine.Object.DontDestroyOnLoad(go);
                // ModLoaderBehaviour: 扫描 Mods/ 目录并加载所有 mod
                go.AddComponent<ModLoaderBehaviour>();
                // ModListMenu: 监控主菜单，注入 "mods" 按钮
                go.AddComponent<ModListMenu>();
                Debug.Log("[PunchLoader] ModLoader GameObject created");
            }
            catch (Exception ex)
            {
                Debug.LogError("[PunchLoader] Bootstrap error: " + ex);
            }
        }
    }

    // ====================================================================
    // ModLoaderBehaviour: mod 加载器
    // Awake() 扫描 Application.dataPath/Mods/ 下每个子目录
    // 找到 plugin.json → 解析 → 加载 .dll → 实例化 IModPlugin → 调用 OnLoad()
    // 按 Priority 升序加载（越小越先）
    // ====================================================================
    public class ModLoaderBehaviour : MonoBehaviour
    {
        private List<ModInfo> _mods = new List<ModInfo>();
        private bool _initialized;

        public List<ModInfo> Mods { get { return _mods; } }

        void Awake()
        {
            if (_initialized) return;  // 防止重复初始化
            _initialized = true;
            LoadAllMods();
        }

        void OnDestroy()
        {
            // 游戏退出时逐个卸载
            foreach (ModInfo mod in _mods)
            {
                if (mod.Loaded && mod.Plugin != null)
                {
                    try { mod.Plugin.OnUnload(); }
                    catch { }
                }
            }
        }

        public void LoadAllMods()
        {
            // 路径: {游戏目录}/MegabytePunch_Data/Mods/
            string modsPath = Path.Combine(Application.dataPath, "Mods");
            modsPath = Path.GetFullPath(modsPath);
            Debug.Log("[PunchLoader] Scanning: " + modsPath);

            if (!Directory.Exists(modsPath))
            {
                Directory.CreateDirectory(modsPath);
                Debug.Log("[PunchLoader] Created Mods/ folder");
                return;
            }

            string[] subDirs = Directory.GetDirectories(modsPath);
            Debug.Log("[PunchLoader] Found " + subDirs.Length + " subdirectories");

            // 阶段1: 扫描所有子目录，收集 ModInfo
            foreach (string dir in subDirs)
            {
                string manifest = Path.Combine(dir, "plugin.json");
                if (!File.Exists(manifest)) continue;
                try { LoadMod(dir, manifest); }
                catch (Exception ex) { Debug.LogError("[PunchLoader] Failed: " + dir + " - " + ex); }
            }

            // 按 Priority 升序排列
            _mods.Sort(delegate(ModInfo a, ModInfo b) { return a.Priority.CompareTo(b.Priority); });

            // 阶段2: 按序调用 OnLoad()
            foreach (ModInfo mod in _mods)
            {
                try
                {
                    mod.Plugin.OnLoad();
                    mod.Loaded = true;
                    Debug.Log("[PunchLoader] Loaded: " + mod.Name + " v" + mod.Version);
                }
                catch (Exception ex)
                {
                    Debug.LogError("[PunchLoader] OnLoad failed: " + mod.Name + " - " + ex);
                }
            }
        }

        // 加载单个 mod: 读 plugin.json → 找入口类所在的 dll → 实例化 IModPlugin
        private void LoadMod(string dir, string manifestPath)
        {
            string json = File.ReadAllText(manifestPath, Encoding.UTF8);
            Dictionary<string, string> data = SimpleJson.ParseObject(json);

            // entryType 必须存在 — 标明哪个类是 mod 入口
            string entryType;
            if (!data.TryGetValue("entryType", out entryType) || string.IsNullOrEmpty(entryType))
            {
                Debug.LogWarning("[PunchLoader] Missing entryType in " + manifestPath);
                return;
            }

            ModInfo info = new ModInfo();
            info.EntryType = entryType;
            string strVal;
            if (data.TryGetValue("id", out strVal)) info.Id = strVal;
            if (data.TryGetValue("name", out strVal)) info.Name = strVal;
            if (data.TryGetValue("version", out strVal)) info.Version = strVal;
            if (data.TryGetValue("author", out strVal)) info.Author = strVal;
            info.Priority = SimpleJson.GetInt(data, "priority", 0);

            // 遍历该目录下所有 .dll，找到包含 entryType 的那个
            string[] dlls = Directory.GetFiles(dir, "*.dll");
            foreach (string dll in dlls)
            {
                try
                {
                    Assembly asm = Assembly.LoadFrom(dll);
                    Type type = asm.GetType(entryType);
                    if (type != null)
                    {
                        object obj = Activator.CreateInstance(type);
                        info.Plugin = obj as IModPlugin;
                        if (info.Plugin == null)
                        {
                            Debug.LogWarning("[PunchLoader] Entry does not implement IModPlugin: " + entryType);
                            continue;
                        }
                        info.Assembly = asm;
                        _mods.Add(info);
                        Debug.Log("[PunchLoader] Discovered: " + (info.Name ?? info.Id));
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[PunchLoader] DLL err: " + dll + " - " + ex.Message);
                }
            }

            Debug.LogWarning("[PunchLoader] Type " + entryType + " not found in " + dir);
        }
    }

    // ====================================================================
    // ModListMenu: 主菜单 "mods" 按钮注入器
    // Update() 每帧检查 MainMenuScript 实例是否存在
    // 找到后 → 反射获取 menuEntries 数组 → 在 "quit" 前插入 "mods"
    // 用户点击 "mods" → Destroy 主菜单 → 创建 ModsSubMenuScript 替代
    // ====================================================================
    public class ModListMenu : MonoBehaviour
    {
        private bool _showMods;
        private bool _menuPatched;
        private object _mainMenuInstance;     // MainMenuScript 实例（反射）
        private Type _mainMenuType;
        private FieldInfo _actionDecidedField; // GUILayoutMenuScript.actionDecided
        private FieldInfo _selectedField;      // GUILayoutMenuScript.selected
        private FieldInfo _menuEntriesField;   // GUILayoutMenuScript.menuEntries
        private object _guiDataObj;            // GUIDataScript 实例（供 ModsSubMenu 复用）
        private object _modsSubMenuObj;        // 当前 ModsSubMenuScript 实例
        private int _subMenuUnlockFrame;       // submenu 消失后锁定帧数（防止误关闭）

        void Update()
        {
            // 如果主菜单还没被 patch，尝试找到并 patch
            if (!_menuPatched || _mainMenuInstance == null || ((UnityEngine.Object)_mainMenuInstance) == null)
            {
                _menuPatched = false;
                if (!_showMods)
                    FindAndPatchMainMenu();
            }

            // 检测用户是否点击了 "mods"
            if (!_showMods && _menuPatched && _mainMenuInstance != null)
            {
                bool actionDecided = (bool)_actionDecidedField.GetValue(_mainMenuInstance);
                if (actionDecided)
                {
                    int selected = (int)_selectedField.GetValue(_mainMenuInstance);
                    string[] entries = (string[])_menuEntriesField.GetValue(_mainMenuInstance);
                    if (selected >= 0 && selected < entries.Length && entries[selected] == "mods")
                    {
                        _actionDecidedField.SetValue(_mainMenuInstance, false);
                        OpenModsMenu();
                    }
                }
            }

            // ModsSubMenu 被销毁后（用户点了 back），延迟解锁返回到主菜单
            if (_showMods && _modsSubMenuObj != null && ((UnityEngine.Object)_modsSubMenuObj) == null)
            {
                if (Time.frameCount > _subMenuUnlockFrame + 3)
                {
                    _showMods = false;
                    _modsSubMenuObj = null;
                }
            }
        }

        // 反射扫描所有程序集，找到 MainMenuScript → 反射获取其字段 → 注入 "mods" 到 menuEntries
        void FindAndPatchMainMenu()
        {
            Type mainMenuType = null;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                mainMenuType = a.GetType("MainMenuScript");
                if (mainMenuType != null) break;
            }
            if (mainMenuType == null) return;
            _mainMenuType = mainMenuType;

            UnityEngine.Object instance = FindObjectOfType(mainMenuType);
            if (instance == null) return;

            _mainMenuInstance = instance;

            // 从基类 GUILayoutMenuScript 反射获取私有字段
            Type baseType = mainMenuType.BaseType;
            _actionDecidedField = baseType.GetField("actionDecided",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _selectedField = baseType.GetField("selected",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _menuEntriesField = baseType.GetField("menuEntries",
                BindingFlags.NonPublic | BindingFlags.Instance);

            // 缓存 GUIDataScript 引用（供后续 ModsSubMenu 继承使用）
            if (_guiDataObj == null)
            {
                FieldInfo guiDataField = baseType.GetField("GUIData");
                if (guiDataField != null)
                    _guiDataObj = guiDataField.GetValue(instance);
            }

            _menuPatched = true;
            InjectModsEntry(instance);
        }

        // 在 menuEntries 末尾（"quit"）前面插入 "mods"
        void InjectModsEntry(object instance)
        {
            string[] old = (string[])_menuEntriesField.GetValue(instance);
            for (int i = 0; i < old.Length; i++)
                if (old[i] == "mods") return;  // 已经注入过了，不重复

            // 插入到 "quit" 之前
            string[] entries = new string[old.Length + 1];
            for (int i = 0; i < old.Length - 1; i++)
                entries[i] = old[i];
            entries[old.Length - 1] = "mods";
            entries[old.Length] = old[old.Length - 1];  // "quit"
            _menuEntriesField.SetValue(instance, entries);
        }

        // 销毁主菜单，创建 ModsSubMenu → 用户看到 mod 列表
        void OpenModsMenu()
        {
            if (_mainMenuInstance == null) return;
            UnityEngine.Object mmObj = (UnityEngine.Object)_mainMenuInstance;
            if (mmObj == null || _guiDataObj == null) return;

            // 获取 ModLoaderBehaviour 中已加载的 mod 列表
            ModLoaderBehaviour loader = (ModLoaderBehaviour)FindObjectOfType(typeof(ModLoaderBehaviour));
            List<ModInfo> mods = (loader != null) ? loader.Mods : new List<ModInfo>();

            // 动态创建子菜单 GameObject → 挂 ModsSubMenuScript
            GameObject go = new GameObject("ModsSubMenu");
            ModsSubMenuScript subMenu = go.AddComponent<ModsSubMenuScript>();

            // 从主菜单实例继承 GUIDataScript 引用 + inGamePause 状态
            Type baseType = typeof(GUILayoutMenuScript);

            FieldInfo guiDataField = baseType.GetField("GUIData");
            if (guiDataField != null)
                guiDataField.SetValue(subMenu, _guiDataObj);

            FieldInfo inGamePauseField = baseType.GetField("inGamePause",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (inGamePauseField != null)
                inGamePauseField.SetValue(subMenu, inGamePauseField.GetValue(_mainMenuInstance));

            subMenu.Init(mods);

            _modsSubMenuObj = subMenu;

            // 销毁主菜单 GameObject → 屏幕只剩 mod 列表
            UnityEngine.Object.Destroy(mmObj);
            _mainMenuInstance = null;
            _showMods = true;
            _subMenuUnlockFrame = Time.frameCount;
        }
    }

    // ====================================================================
    // MenuLocalizer: 中文→英文 反向翻译器
    // 由 Injector.exe IL 注入到 GUILayoutMenuScript.CheckConfirm() 中
    // 在 DoAction(entry) 之前调用 TranslateEntry(entry)
    // 把 menuEntries 中已被 FontRouter 翻译成中文的文本反转回英文
    // 这样 DoAction("quit") / DoAction("play") 的 switch 才能正常工作
    // ====================================================================
    public static class MenuLocalizer
    {
        // 中文 → 英文 反向映射字典（由 Register() 从英→中字典翻转生成）
        private static Dictionary<string, string> _reverse = new Dictionary<string, string>();

        /// <summary>
        /// 将中文菜单项翻译回英文。如果不在字典中则原样返回。
        /// 由 Cecil IL patch 在 CheckConfirm → DoAction 前调用。
        /// </summary>
        public static string TranslateEntry(string entry)
        {
            if (entry == null) return null;
            string english;
            if (_reverse.TryGetValue(entry, out english))
                return english;
            return entry; // 无需翻译（如 "MODS"、"back" 等保持英文）
        }

        /// <summary>
        /// 注册翻译字典。接受英→中映射，内部翻转生成中→英反向映射。
        /// 由 ChineseLoc.cs 的 Awake() 调用。
        /// 传入示例: {"play"→"开始游戏", "quit"→"退出"} → 反向: {"开始游戏"→"play", "退出"→"quit"}
        /// </summary>
        public static void Register(Dictionary<string, string> englishToChinese)
        {
            if (englishToChinese == null) return;
            Dictionary<string, string>.Enumerator en = englishToChinese.GetEnumerator();
            while (en.MoveNext())
            {
                KeyValuePair<string, string> kv = en.Current;
                // 如果中文值相同（多个英文映射到同一中文），保留第一个映射的英文
                if (!_reverse.ContainsKey(kv.Value))
                    _reverse[kv.Value] = kv.Key;
            }
        }
    }

    // ====================================================================
    // FontRouter: 零闪烁字体路由引擎
    // 由 Injector.exe IL 注入到 GUILayoutMenuScript.BeginGUI() 顶部
    // 每帧 OnGUI 渲染前执行：
    //   1. 遍历 menuEntries[] + labelEntries[] → 用 Translations 字典英→中替换
    //   2. 遍历 GUIDataScript 上所有 GUIStyle → 替换字体为 AtlasFont
    // 因为在任何 GUILayout.Label 之前完成，所以用户绝看不到英文闪现
    // ====================================================================
    public static class FontRouter
    {
        // 组合字体图集（ACK + CJK 在同一张纹理上）
        public static Font AtlasFont;
        // 英→中 翻译字典
        public static Dictionary<string, string> Translations;
        // 是否已就绪（ChineseLoc 加载完成后设为 true）
        public static bool Ready;

        /// <summary>
        /// 每帧 BeginGUI 前调用。
        /// 翻译当前菜单的所有文本 + 替换所有 GUIStyle.font 为组合图集字体。
        /// menu 参数是当前 GUILayoutMenuScript 实例（或其子类）。
        /// </summary>
        public static void Route(MonoBehaviour menu)
        {
            if (!Ready || Translations == null || menu == null) return;

            // --- 反射获取字段（沿继承链向上查找） ---
            Type scan = menu.GetType();
            FieldInfo meField = null;   // menuEntries
            FieldInfo leField = null;   // labelEntries
            FieldInfo gdField = null;   // GUIData

            while (scan != null && scan != typeof(MonoBehaviour))
            {
                if (meField == null)
                    meField = scan.GetField("menuEntries", BindingFlags.NonPublic | BindingFlags.Instance);
                if (leField == null)
                    leField = scan.GetField("labelEntries", BindingFlags.NonPublic | BindingFlags.Instance);
                if (gdField == null)
                    gdField = scan.GetField("GUIData", BindingFlags.Public | BindingFlags.Instance);
                if (meField != null && leField != null && gdField != null) break;
                scan = scan.BaseType;
            }

            // --- 翻译 menuEntries（按钮文本） ---
            if (meField != null)
            {
                string[] arr = (string[])meField.GetValue(menu);
                if (arr != null)
                {
                    bool dirty = false;
                    for (int i = 0; i < arr.Length; i++)
                    {
                        if (arr[i] == null) continue;
                        string v;
                        if (Translations.TryGetValue(arr[i], out v) && v != arr[i])
                        {
                            arr[i] = v;
                            dirty = true;
                        }
                    }
                    if (dirty) meField.SetValue(menu, arr);
                }
            }

            // --- 翻译 labelEntries（标题文本，如 "LOADED MODS"） ---
            if (leField != null)
            {
                string[] arr = (string[])leField.GetValue(menu);
                if (arr != null)
                {
                    bool dirty = false;
                    for (int i = 0; i < arr.Length; i++)
                    {
                        if (arr[i] == null) continue;
                        string v;
                        if (Translations.TryGetValue(arr[i], out v) && v != arr[i])
                        {
                            arr[i] = v;
                            dirty = true;
                        }
                    }
                    if (dirty) leField.SetValue(menu, arr);
                }
            }

            // --- 替换 GUIStyle 字体 ---
            // GUIDataScript 上有 8 个 style: buttonStyle, fakeButtonStyle, smallButtonStyle,
            // fakeSmallButtonStyle, smallLabelStyle, sliderStyle, thumbStyle, announcementStyle
            // 前 5 个是菜单渲染用的，后 3 个不常出现
            if (gdField != null && AtlasFont != null)
            {
                object gd = gdField.GetValue(menu);
                if (gd != null)
                {
                    Type gdType = gd.GetType();
                    // 需要替换字体的 style 名称 + 对应的 fixedHeight
                    // button/fakeButton = 44px, smallButton/fakeSmallButton/smallLabel = 31px
                    string[] names = new string[] {
                        "buttonStyle", "fakeButtonStyle", "smallButtonStyle",
                        "fakeSmallButtonStyle", "smallLabelStyle"
                    };
                    int[] heights = new int[] { 44, 44, 31, 31, 31 };

                    for (int i = 0; i < names.Length; i++)
                    {
                        FieldInfo sf = gdType.GetField(names[i]);
                        if (sf == null) continue;
                        GUIStyle s = sf.GetValue(gd) as GUIStyle;
                        if (s == null) continue;

                        // 只替换一次，避免重复
                        if (s.font != AtlasFont)
                        {
                            s.font = AtlasFont;
                            s.fixedHeight = (float)heights[i];
                        }
                    }
                }
            }
        }
    }

    // ====================================================================
    // ModsSubMenuScript: mod 列表 UI
    // 继承 GUILayoutMenuScript 复用原生菜单布局（渐变纹理、按钮样式、选中效果）
    // 分页显示已加载的 mod，每个 mod 一行: [ON]/[OFF]  Mod名称  v版本
    // 点击 mod 项 = 切换 enabled 状态 + 调用 OnLoad/OnUnload
    // Prev / Next 按钮翻页，back 返回主菜单
    // ====================================================================
    public class ModsSubMenuScript : GUILayoutMenuScript
    {
        private List<ModInfo> _mods;
        private int _page;      // 当前页码（0-based）
        private int _perPage;   // 每页显示数量（根据屏幕高度动态计算）

        public void Init(List<ModInfo> mods)
        {
            _mods = mods ?? new List<ModInfo>();

            // 复用原生菜单布局参数
            largestButtonSize = 700;     // 按钮区域宽度
            startTextOn = 0.05f;        // 文本起始位置（屏幕高度 5% 处）

            showLogo = false;
            labelEntries = new string[] { "LOADED MODS" };
            spaceLastEntry = true;
            previousMenuType = 0;
            menuType = 0;

            // 动态计算每页条目数: 可用高度 / 每条高度
            // 开销: 标题(22) + 间距(10) + 最后一个间距(20) + 底部留白(30)
            // 每条: 按钮高(30, 因为 smallButtonStyle 行高 31) + 间隔(15)
            float areaHeight = Screen.height * (1f - startTextOn);
            float overhead = 22f + 10f + 20f + 30f;
            float entryH = 30f + standardButtonSpace;
            _perPage = Mathf.Max(1, Mathf.FloorToInt((areaHeight - overhead) / entryH));

            _page = 0;
            selected = 0;
            actionDecided = false;

            BuildPage();
        }

        // Unity 生命周期: 如果 menuEntries 为空则填入 fallback
        void Start()
        {
            if (menuEntries == null || menuEntries.Length == 0)
            {
                showLogo = true;
                labelEntries = new string[] { "LOADED MODS" };
                menuEntries = new string[] { "back" };
                spaceLastEntry = true;
            }
        }

        // 重建当前页的 menuEntries
        void BuildPage()
        {
            int totalPages = Mathf.Max(1, Mathf.CeilToInt((float)_mods.Count / _perPage));
            if (_page >= totalPages) _page = totalPages - 1;
            if (_page < 0) _page = 0;

            int start = _page * _perPage;
            int end = Mathf.Min(start + _perPage, _mods.Count);

            // 当前页的 mod 条目
            List<string> entries = new List<string>();
            for (int i = start; i < end; i++)
            {
                ModInfo m = _mods[i];
                string state = m.Enabled ? "[ON]" : "[OFF]";
                string name = m.Name ?? m.Id ?? "?";
                string ver = m.Version ?? "?";
                entries.Add(state + "  " + name + "  v" + ver);
            }

            // 翻页按钮（放在 mod 列表和 back 之间）
            if (totalPages > 1)
            {
                if (_page > 0)
                    entries.Add("<  Prev");
                if (_page < totalPages - 1)
                    entries.Add("Next  >");
            }

            // 最后一项永远是 back
            entries.Add("back");
            menuEntries = entries.ToArray();
            if (selected >= menuEntries.Length)
                selected = menuEntries.Length - 1;
        }

        protected override void DoAction(string action)
        {
            // 先调用基类 DoAction 处理基础逻辑（actionDecided = true, 播放音效）
            base.DoAction(action);

            if (action == "back")
            {
                // 根据是否在游戏中暂停返回对应的菜单
                if (inGamePause)
                    StartMenu(Menus.INGAMEPAUSE);
                else
                    StartMenu(Menus.MAIN);
            }
            else if (action == "<  Prev")
            {
                _page--;
                BuildPage();
                selected = 0;
                actionDecided = false;  // 翻页不禁用操作
            }
            else if (action == "Next  >")
            {
                _page++;
                BuildPage();
                selected = 0;
                actionDecided = false;
            }
            else
            {
                // 点击 mod 条目: 切换 enabled 状态
                int modIdx = (int)(selected);
                if (modIdx >= 0 && modIdx < _mods.Count)
                {
                    ModInfo m = _mods[modIdx];
                    m.Enabled = !m.Enabled;
                    if (m.Enabled)
                    {
                        try { m.Plugin.OnLoad(); }
                        catch { }
                    }
                    else
                    {
                        try { m.Plugin.OnUnload(); }
                        catch { }
                    }
                    BuildPage();
                }
                actionDecided = false;
            }
        }
    }
}
