using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using PunchLoader;
using UnityEngine;

public class LocalizationDebugPlugin : IModPlugin
{
    private static LocalizationDebugBehaviour _behaviour;
    private static TextMeshTextHandler _captureHandler;

    public string GetId() { return "LocalizationDebug"; }
    public string GetName() { return "Localization Debug"; }
    public string GetVersion() { return "1.9.0"; }

    public void OnLoad()
    {
        if (_behaviour != null) return;
        GameObject host = new GameObject("PunchLoader.LocalizationDebug");
        UnityEngine.Object.DontDestroyOnLoad(host);
        _behaviour = (LocalizationDebugBehaviour)host.AddComponent(typeof(LocalizationDebugBehaviour));
        _behaviour.Initialize(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
        _captureHandler = new TextMeshTextHandler(CaptureSourceText);
        HookManager.Register(_captureHandler);
        Debug.Log("[LocalizationDebug] Loaded. F8 panel, F9 export, F10 untranslated filter.");
    }

    public void OnUnload()
    {
        if (_captureHandler != null) HookManager.Unregister(_captureHandler);
        _captureHandler = null;
        if (_behaviour != null) UnityEngine.Object.Destroy(_behaviour.gameObject);
        _behaviour = null;
    }

    private static bool CaptureSourceText(TextMesh textMesh, string originalText)
    {
        if (_behaviour != null) _behaviour.CaptureSource(textMesh, originalText);
        return false;
    }
}

public sealed class LocalizationDebugBehaviour : MonoBehaviour
{
    private sealed class TextEntry
    {
        public int Id;
        public TextMesh Mesh;
        public string Path;
        public string SourceText;
        public string LocalizedText;
        public string LastObservedText;
        public Font OriginalFont;
        public int OriginalFontSize;
        public float OriginalCharacterSize;
        public float OriginalLineSpacing;
        public Vector3 OriginalLocalPosition;
        public TextAnchor OriginalAnchor;
        public TextAlignment OriginalAlignment;
        public string FirstScene;
        public string LastScene;
        public float FirstSeen;
        public float LastSeen;
        public bool Active;
        public int ChangeCount;
        public string LastLayoutIssue;
    }

    private sealed class TextEvent
    {
        public float Time;
        public string Scene;
        public string Path;
        public string Kind;
        public string Before;
        public string After;
    }

    private sealed class LayoutPreset
    {
        public string PathKey;
        public string SourceKey;
        public float CharacterSize;
        public float LineSpacing;
        public Vector3 LocalPosition;
        public TextAnchor Anchor;
        public TextAlignment Alignment;
    }

    private sealed class SceneRecord
    {
        public int Index;
        public string Name;
        public int Visits;
        public string LastVisited;
    }

    private sealed class ReviewRecord
    {
        public string Path;
        public string Source;
        public string Status;
        public string Note;
        public string UpdatedAt;
    }

    private sealed class TextAggregate
    {
        public string Source;
        public string CurrentExample;
        public int Occurrences;
        public int ActiveOccurrences;
        public int UntranslatedOccurrences;
        public int AcceptedOccurrences;
        public int PendingOccurrences;
        public int IgnoredOccurrences;
        public readonly Dictionary<string, bool> Scenes = new Dictionary<string, bool>();
        public readonly Dictionary<string, bool> Paths = new Dictionary<string, bool>();
    }

    private sealed class DialogueAuditRow
    {
        public int GroupId;
        public int LineId;
        public string Source;
        public string Translation;
        public string RuntimeStatus;
        public string Issue;
    }

    private sealed class DialogueRenderResult
    {
        public int GroupId;
        public int LineId;
        public string Translation;
        public string Font;
        public float CharacterSize;
        public float LineSpacing;
        public Rect TextRect;
        public Rect BoxRect;
        public string Issue;
        public string Screenshot;
    }

    private readonly Dictionary<int, TextEntry> _entries = new Dictionary<int, TextEntry>();
    private readonly Dictionary<int, TextEntry> _entriesByInstanceId = new Dictionary<int, TextEntry>();
    private readonly Dictionary<int, string> _pendingSources = new Dictionary<int, string>();
    private readonly List<TextEntry> _visible = new List<TextEntry>();
    private readonly List<TextEvent> _events = new List<TextEvent>();
    private readonly List<LayoutPreset> _presets = new List<LayoutPreset>();
    private readonly Dictionary<int, SceneRecord> _sceneCatalog = new Dictionary<int, SceneRecord>();
    private readonly Dictionary<string, ReviewRecord> _reviews = new Dictionary<string, ReviewRecord>();
    private readonly Dictionary<string, bool> _untranslatedAllowlist =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    private readonly List<DialogueAuditRow> _dialogueAuditRows = new List<DialogueAuditRow>();
    private readonly List<DialogueAuditRow> _dialogueRenderRows = new List<DialogueAuditRow>();
    private readonly List<DialogueRenderResult> _dialogueRenderResults =
        new List<DialogueRenderResult>();
    private readonly List<Renderer> _dialogueHiddenRenderers = new List<Renderer>();
    private readonly List<bool> _dialogueHiddenRendererStates = new List<bool>();
    private string _modDirectory;
    private bool _show;
    private bool _onlyUntranslated = true;
    private bool _onlyUnreviewed;
    private bool _onlyLayoutIssues;
    private bool _onlyActiveScene = true;
    private bool _autoApplyPresets = true;
    private bool _highlightSelected = true;
    private bool _pickFromScreen;
    private bool _captureScreenshotNextFrame;
    private string _pendingScreenshotPath;
    private Texture2D _highlightTexture;
    private string _filter = string.Empty;
    private string _customText = string.Empty;
    private string _reviewNote = string.Empty;
    private string _storageWarnings = string.Empty;
    private string _dialogueAuditStatus = "尚未巡检对话";
    private string _dialogueRenderTargets = "10/3,19/4,45/5,73/5,118/2";
    private int _dialogueAuditApplied;
    private int _dialogueAuditProblems;
    private bool _dialogueRuntimeAvailable;
    private bool _dialogueRenderWaiting;
    private bool _dialogueRenderRunning;
    private bool _dialogueRenderCaptureAll;
    private int _dialogueRenderIndex;
    private int _dialogueRenderProblems;
    private float _dialogueRenderNextAction;
    private GameObject _dialogueRenderClone;
    private TextMesh _dialogueRenderText;
    private TextMesh _dialogueRenderShadow;
    private Renderer _dialogueRenderBox;
    private string _status = "F8: 打开/关闭, F9: 导出, F10: 仅看疑似未汉化文本";
    private int _selectedId = -1;
    private int _nextEntryId = 1;
    private float _nextScan;
    private float _step = 0.01f;
    private MonoBehaviour _inventoryController;
    private int _lastSceneIndex = -1;
    private int _returnSceneIndex = -1;
    private int _fifthAcceptanceReturnSceneIndex = -1;
    private string _sceneTargetText = string.Empty;
    private string _levelCompletePreviewLevel = "1";
    private GameObject _levelCompletePreview;
    private GameObject _runtimeUiPreview;
    private Component _runtimeUiPreviewMenu;
    private string _runtimeUiPreviewKind = string.Empty;
    private int _armedSceneIndex = -1;
    private float _sceneArmExpires;
    private bool _automaticAudit;
    private bool _automaticAuditIncludeInventory = true;
    private bool _automaticAuditArmed;
    private float _automaticAuditArmExpires;
    private float _automaticAuditDelay = 1.5f;
    private float _automaticAuditNextAction;
    private int _automaticAuditStartScene = -1;
    private int _automaticAuditTargetScene = -1;
    private int _automaticAuditPhase;
    private int _automaticAuditCompletedScenes;
    private int _automaticInventoryStep;
    private bool _automaticInventoryAuditDone;
    private string _automaticOriginalInventoryState = string.Empty;
    private static readonly string[] AutomaticInventoryStates = new string[] {
        "INVENTORY", "ATTACHMENT", "INVENTORY", "ABILITIES", "INVENTORY",
        "BREAKINTOBITS", "INVENTORY", "STATS", "INVENTORY"
    };
    private Rect _windowRect = new Rect(18f, 18f, 820f, 690f);
    private Vector2 _listScroll;
    private Vector2 _detailScroll;

    public void Initialize(string modDirectory)
    {
        _modDirectory = modDirectory;
        LoadPersistentState();
        _lastSceneIndex = Application.loadedLevel;
        _sceneTargetText = _lastSceneIndex.ToString(CultureInfo.InvariantCulture);
        _highlightTexture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
        _highlightTexture.SetPixel(0, 0, Color.white);
        _highlightTexture.Apply();
        RecordCurrentScene();
        RunSelfCheck();
    }

    public void CaptureSource(TextMesh mesh, string source)
    {
        if (mesh == null) return;
        int id = mesh.GetInstanceID();
        string captured = source == null ? string.Empty : source;
        string previous = null;
        _pendingSources.TryGetValue(id, out previous);
        _pendingSources[id] = captured;
        TextEntry entry;
        if (!_entriesByInstanceId.TryGetValue(id, out entry) || !object.ReferenceEquals(entry.Mesh, mesh))
        {
            entry = CreateEntry(mesh);
            _entries[entry.Id] = entry;
            _entriesByInstanceId[id] = entry;
        }
        entry.SourceText = captured;
        AddEvent(entry, "setter", previous, captured);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F8)) _show = !_show;
        if (Input.GetKeyDown(KeyCode.F6)) ToggleScreenPick();
        if (Input.GetKeyDown(KeyCode.F7)) RequestScreenshot();
        if (Input.GetKeyDown(KeyCode.F9)) ExportBundle();
        if (Input.GetKeyDown(KeyCode.F10)) _onlyUntranslated = !_onlyUntranslated;
        if (_pickFromScreen && Input.GetKeyDown(KeyCode.Escape))
        {
            _pickFromScreen = false;
            _show = true;
            _status = "已取消画面点选";
        }
        if (_pickFromScreen && Input.GetMouseButtonDown(0)) PickTextAtMouse();
        if (Time.realtimeSinceStartup >= _nextScan)
        {
            _nextScan = Time.realtimeSinceStartup + 0.25f;
            ScanTextMeshes();
        }
        if (_lastSceneIndex != Application.loadedLevel)
        {
            int previous = _lastSceneIndex;
            _lastSceneIndex = Application.loadedLevel;
            _inventoryController = null;
            _armedSceneIndex = -1;
            _sceneTargetText = _lastSceneIndex.ToString(CultureInfo.InvariantCulture);
            RecordCurrentScene();
            AddToolEvent("scene-enter", previous + " -> " + _lastSceneIndex);
            _status = "已进入场景: " + Application.loadedLevelName + " (#" + _lastSceneIndex + ")";
        }
        AdvanceAutomaticAudit();
        AdvanceDialogueRenderAudit();
    }

    private void LateUpdate()
    {
        MaintainRuntimeUiPreview();
        if (_autoApplyPresets)
            foreach (KeyValuePair<int, TextEntry> pair in _entries)
                if (pair.Value.Active && pair.Value.Mesh != null) ApplyMatchingPreset(pair.Value);
        if (_captureScreenshotNextFrame)
        {
            _captureScreenshotNextFrame = false;
            Application.CaptureScreenshot(_pendingScreenshotPath, 1);
            AddToolEvent("screenshot", _pendingScreenshotPath);
            _status = "截图已保存: " + Path.GetFileName(_pendingScreenshotPath);
        }
    }

    private void OnGUI()
    {
        if (_pickFromScreen) DrawPickOverlay();
        if (_highlightSelected) DrawSelectedHighlight();
        if (!_show) return;
        _windowRect.width = Mathf.Min(_windowRect.width, Screen.width - 20f);
        float availableHeight = Screen.height - 20f;
        _windowRect.height = Mathf.Min(Mathf.Max(_windowRect.height, 780f), availableHeight);
        _windowRect = GUILayout.Window(927451, _windowRect, DrawWindow,
            "汉化调试工具 1.9.0");
    }

    private void DrawWindow(int id)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("场景: " + Application.loadedLevelName + "  会话文本: " + _entries.Count +
            "  事件: " + _events.Count);
        if (GUILayout.Button("刷新", GUILayout.Width(80f))) ScanTextMeshes();
        if (GUILayout.Button("全部导出", GUILayout.Width(90f))) ExportBundle();
        if (GUILayout.Button("点选文本(F6)", GUILayout.Width(95f))) ToggleScreenPick();
        if (GUILayout.Button("截图(F7)", GUILayout.Width(80f))) RequestScreenshot();
        if (GUILayout.Button("清空", GUILayout.Width(65f))) ClearSession();
        if (GUILayout.Button("关闭", GUILayout.Width(70f))) _show = false;
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("筛选", GUILayout.Width(42f));
        _filter = GUILayout.TextField(_filter, GUILayout.Width(260f));
        _onlyUntranslated = GUILayout.Toggle(_onlyUntranslated, "只看疑似未汉化", GUILayout.Width(145f));
        _onlyUnreviewed = GUILayout.Toggle(_onlyUnreviewed, "只看未复核", GUILayout.Width(105f));
        _onlyLayoutIssues = GUILayout.Toggle(_onlyLayoutIssues, "只看布局异常", GUILayout.Width(115f));
        _onlyActiveScene = GUILayout.Toggle(_onlyActiveScene, "只看活动对象", GUILayout.Width(145f));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        _autoApplyPresets = GUILayout.Toggle(_autoApplyPresets, "自动套用预设", GUILayout.Width(105f));
        _highlightSelected = GUILayout.Toggle(_highlightSelected, "高亮所选", GUILayout.Width(85f));
        GUILayout.Label("步长", GUILayout.Width(35f));
        string stepText = GUILayout.TextField(_step.ToString("0.###", CultureInfo.InvariantCulture),
            GUILayout.Width(58f));
        float parsed;
        if (float.TryParse(stepText, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) && parsed > 0f)
            _step = parsed;
        if (GUILayout.Button("保存设置", GUILayout.Width(80f))) SaveSettings();
        if (GUILayout.Button("恢复默认", GUILayout.Width(80f))) ResetSettings();
        if (GUILayout.Button("重新自检", GUILayout.Width(75f))) RunSelfCheck();
        if (GUILayout.Button("重载名词", GUILayout.Width(75f)))
        {
            LoadUntranslatedAllowlist();
            RunSelfCheck();
        }
        GUILayout.EndHorizontal();

        DrawInventoryStateControls();
        DrawSceneControls();
        DrawFifthAcceptanceControls();
        DrawRuntimeAcceptanceControls();
        DrawAutomaticAuditControls();

        BuildVisibleEntries();
        GUILayout.BeginHorizontal();
        GUILayout.Label("匹配: " + _visible.Count + " | " + _status);
        if (GUILayout.Button("上一条", GUILayout.Width(65f))) SelectVisibleRelative(-1);
        if (GUILayout.Button("下一条", GUILayout.Width(65f))) SelectVisibleRelative(1);
        GUILayout.EndHorizontal();
        _listScroll = GUILayout.BeginScrollView(_listScroll, GUILayout.Height(220f));
        for (int i = 0; i < _visible.Count; i++)
        {
            TextEntry entry = _visible[i];
            string marker = entry.Id == _selectedId ? "> " : "  ";
            marker += GetReviewMarker(entry);
            string layoutIssue = GetLayoutIssue(entry);
            if (layoutIssue.Length > 0) marker += "[异常:" + layoutIssue + "] ";
            string preview = SingleLine(entry.Mesh == null ? entry.LastObservedText : entry.Mesh.text);
            if (preview.Length > 70) preview = preview.Substring(0, 70) + "...";
            if (GUILayout.Button(marker + entry.Path + " | " + preview, GUILayout.Height(24f)))
                SelectEntry(entry);
        }
        GUILayout.EndScrollView();

        TextEntry selected = GetSelected();
        if (selected != null) DrawSelected(selected);
        else GUILayout.Label("请选择一个 TextMesh 进行检查和调整。");
        GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
    }

    private void DrawInventoryStateControls()
    {
        GUILayout.BeginHorizontal("box");
        if (_inventoryController == null)
        {
            GUILayout.Label("背包状态调试: 当前未找到活动控制器");
            if (GUILayout.Button("查找", GUILayout.Width(60f))) FindInventoryController();
            GUILayout.EndHorizontal();
            return;
        }

        string inventoryState = GetInventoryStateName(_inventoryController);
        GUILayout.Label("背包: " + _inventoryController.GetType().Name +
            " 状态=" + TranslateInventoryState(inventoryState), GUILayout.Width(290f));
        if (GUILayout.Button("主页面")) InvokeInventoryState("INVENTORY");
        if (GUILayout.Button("装配")) InvokeInventoryState("ATTACHMENT");
        if (GUILayout.Button("技能")) InvokeInventoryState("ABILITIES");
        if (GUILayout.Button("分解")) InvokeInventoryState("BREAKINTOBITS");
        if (GUILayout.Button("属性")) InvokeInventoryState("STATS");
        if (GUILayout.Button("关闭")) InvokeInventoryState("STANDBY");
        if (GUILayout.Button("重新查找", GUILayout.Width(75f))) FindInventoryController();
        GUILayout.EndHorizontal();
    }

    private void DrawSceneControls()
    {
        GUILayout.BeginVertical("box");
        GUILayout.BeginHorizontal();
        GUILayout.Label("场景调试: " + Application.loadedLevelName + " (#" +
            Application.loadedLevel + "/" + Math.Max(0, Application.levelCount - 1) + ")",
            GUILayout.Width(300f));
        GUILayout.Label("目标", GUILayout.Width(42f));
        _sceneTargetText = GUILayout.TextField(_sceneTargetText, GUILayout.Width(48f));
        if (GUILayout.Button("-1", GUILayout.Width(38f))) SetSceneTarget(Application.loadedLevel - 1);
        if (GUILayout.Button("+1", GUILayout.Width(38f))) SetSceneTarget(Application.loadedLevel + 1);
        if (GUILayout.Button("当前", GUILayout.Width(62f))) SetSceneTarget(Application.loadedLevel);
        GUI.enabled = _returnSceneIndex >= 0;
        if (GUILayout.Button("上一个", GUILayout.Width(68f))) SetSceneTarget(_returnSceneIndex);
        GUI.enabled = true;
        if (GUILayout.Button("前一个未访问", GUILayout.Width(90f))) SetNearestUnvisited(-1);
        if (GUILayout.Button("后一个未访问", GUILayout.Width(90f))) SetNearestUnvisited(1);
        GUILayout.EndHorizontal();

        int target;
        bool valid = TryGetSceneTarget(out target);
        bool armed = valid && target == _armedSceneIndex &&
            Time.realtimeSinceStartup <= _sceneArmExpires;
        GUILayout.BeginHorizontal();
        GUI.enabled = valid;
        if (GUILayout.Button("准备加载场景", GUILayout.Width(120f)))
        {
            _armedSceneIndex = target;
            _sceneArmExpires = Time.realtimeSinceStartup + 5f;
            _status = "场景 #" + target + " 已准备, 请在5秒内加载";
        }
        GUI.enabled = armed;
        if (GUILayout.Button("加载场景 #" + (valid ? target.ToString() : "?"), GUILayout.Width(120f)))
            LoadScene(target);
        GUI.enabled = true;
        GUILayout.Label("已记录场景: " + _sceneCatalog.Count + "/" + Application.levelCount + "  " +
            (valid ? "直接加载会跳过正常游戏流程。" :
            "请输入0至" + Math.Max(0, Application.levelCount - 1) + "之间的场景索引。"));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("通关界面预览", GUILayout.Width(90f));
        GUILayout.Label("关卡", GUILayout.Width(34f));
        _levelCompletePreviewLevel = GUILayout.TextField(_levelCompletePreviewLevel,
            GUILayout.Width(36f));
        if (GUILayout.Button("预览关卡结算", GUILayout.Width(105f)))
            PreviewLevelComplete(false);
        if (GUILayout.Button("预览锦标赛结算", GUILayout.Width(115f)))
            PreviewLevelComplete(true);
        GUI.enabled = _levelCompletePreview != null;
        if (GUILayout.Button("关闭预览", GUILayout.Width(75f))) CloseLevelCompletePreview();
        GUI.enabled = true;
        GUILayout.Label("仅复制结算界面, 不击败 Boss、不发放奖励、不写入存档。");
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("状态提示测试", GUILayout.Width(90f));
        if (GUILayout.Button("已获得配色", GUILayout.Width(105f)))
            TriggerTransientStatus(false);
        if (GUILayout.Button("+ 1 条命", GUILayout.Width(105f)))
            TriggerTransientStatus(true);
        GUILayout.Label("使用游戏原有提示预制体, 不增加生命、不收集配色、不写入存档。");
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
    }

    private void DrawFifthAcceptanceControls()
    {
        GUILayout.BeginVertical("box");
        GUILayout.BeginHorizontal();
        GUILayout.Label("第五项场景验收", GUILayout.Width(105f));
        if (GUILayout.Button("关卡选择", GUILayout.Width(85f)))
            LoadFifthAcceptanceScene(24, "关卡选择");
        if (GUILayout.Button("锦标赛大厅", GUILayout.Width(95f)))
            LoadFifthAcceptanceScene(38, "锦标赛大厅");
        if (GUILayout.Button("对战大厅", GUILayout.Width(85f)))
            LoadFifthAcceptanceScene(23, "对战大厅");
        if (GUILayout.Button("锦标赛结算", GUILayout.Width(95f)))
            LoadFifthAcceptanceScene(37, "锦标赛结算");
        if (GUILayout.Button("对战结算", GUILayout.Width(85f)))
            LoadFifthAcceptanceScene(22, "对战结算");
        GUI.enabled = _fifthAcceptanceReturnSceneIndex >= 0;
        if (GUILayout.Button("返回进入前场景", GUILayout.Width(110f)))
            ReturnFromFifthAcceptanceScene();
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        GUILayout.Label("直接进入已确认的构建场景；关卡选择可立即验收，锦标赛和对战场景可能还需要临时比赛状态。不会主动发放奖励或写入存档。");
        GUILayout.EndVertical();
    }

    private void LoadFifthAcceptanceScene(int sceneIndex, string displayName)
    {
        if (sceneIndex < 0 || sceneIndex >= Application.levelCount)
        {
            _status = displayName + "验收失败: 场景索引 #" + sceneIndex + " 不存在";
            return;
        }
        int current = Application.loadedLevel;
        if (!IsFifthAcceptanceScene(current)) _fifthAcceptanceReturnSceneIndex = current;
        _returnSceneIndex = current;
        _armedSceneIndex = -1;
        AddToolEvent("fifth-acceptance-load", displayName + " " + current + " -> " + sceneIndex);
        _status = "正在进入" + displayName + " (#" + sceneIndex + ")";
        Application.LoadLevel(sceneIndex);
    }

    private void ReturnFromFifthAcceptanceScene()
    {
        int target = _fifthAcceptanceReturnSceneIndex;
        if (target < 0 || target >= Application.levelCount)
        {
            _status = "没有可返回的第五项验收起始场景";
            return;
        }
        int current = Application.loadedLevel;
        _fifthAcceptanceReturnSceneIndex = -1;
        _returnSceneIndex = current;
        AddToolEvent("fifth-acceptance-return", current + " -> " + target);
        _status = "正在返回第五项验收前场景 (#" + target + ")";
        Application.LoadLevel(target);
    }

    private static bool IsFifthAcceptanceScene(int sceneIndex)
    {
        return sceneIndex == 22 || sceneIndex == 23 || sceneIndex == 24 ||
            sceneIndex == 37 || sceneIndex == 38;
    }

    private void DrawRuntimeAcceptanceControls()
    {
        GUILayout.BeginVertical("box");
        GUILayout.BeginHorizontal();
        GUILayout.Label("菜单运行时验收", GUILayout.Width(105f));
        if (GUILayout.Button("暂停菜单", GUILayout.Width(80f)))
            PreviewRuntimeMenu("InGamePauseMenu", "暂停菜单", false);
        if (GUILayout.Button("游戏结束", GUILayout.Width(80f)))
            PreviewRuntimeMenu("GameOverMenu", "游戏结束", false);
        if (GUILayout.Button("对战设置", GUILayout.Width(80f)))
            PreviewRuntimeMenu("versusOptionsMenu", "对战设置", false);
        if (GUILayout.Button("对战结算", GUILayout.Width(80f)))
            PreviewRuntimeMenu("adventureContinueMenu", "对战结算", true);
        if (GUILayout.Button("新游戏确认", GUILayout.Width(90f)))
            PreviewRuntimeMenu("newGameSureMenu", "新游戏确认", false);
        if (GUILayout.Button("手柄确认", GUILayout.Width(80f)))
            PreviewRuntimeMenu("useGamePadMenu", "手柄确认", false);
        GUI.enabled = _runtimeUiPreview != null;
        if (GUILayout.Button("关闭验收界面", GUILayout.Width(100f))) CloseRuntimeUiPreview();
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        GUILayout.Label("预览冻结菜单操作，只显示游戏原有布局；不会删除进度、切换场景或修改对战设置。");
        GUILayout.EndVertical();
    }

    private void PreviewRuntimeMenu(string prefabFieldName, string displayName,
        bool rematchPreview)
    {
        CloseRuntimeUiPreview();
        GameObject prefab = FindGuiDataPrefab(prefabFieldName);
        if (prefab == null)
        {
            _status = "菜单验收失败: 找不到预制体 " + prefabFieldName;
            return;
        }
        GameObject clone = UnityEngine.Object.Instantiate(prefab) as GameObject;
        if (clone == null)
        {
            _status = "菜单验收失败: 无法复制 " + prefabFieldName;
            return;
        }
        clone.name = "LocalizationDebug.RuntimeMenuPreview." + displayName;
        Type menuType = FindLoadedType("GUILayoutMenuScript");
        Component menu = menuType == null ? null : clone.GetComponent(menuType);
        if (menu == null)
        {
            UnityEngine.Object.Destroy(clone);
            _status = "菜单验收失败: 预制体缺少 GUILayoutMenuScript";
            return;
        }
        _runtimeUiPreview = clone;
        _runtimeUiPreviewMenu = menu;
        _runtimeUiPreviewKind = rematchPreview ? "rematch" : displayName;
        MaintainRuntimeUiPreview();
        _status = "已显示" + displayName + "验收界面; 所有菜单操作均已冻结";
    }

    private static GameObject FindGuiDataPrefab(string fieldName)
    {
        Type type = FindLoadedType("GUIDataScript");
        if (type == null) return null;
        UnityEngine.Object[] objects = Resources.FindObjectsOfTypeAll(type);
        for (int i = 0; i < objects.Length; i++)
        {
            Component component = objects[i] as Component;
            if (component == null) continue;
            FieldInfo field = component.GetType().GetField(fieldName,
                BindingFlags.Public | BindingFlags.Instance);
            GameObject prefab = field == null ? null : field.GetValue(component) as GameObject;
            if (prefab != null) return prefab;
        }
        return null;
    }

    private void MaintainRuntimeUiPreview()
    {
        if (_runtimeUiPreview == null || _runtimeUiPreviewMenu == null) return;
        SetInheritedField(_runtimeUiPreviewMenu, "actionDecided", true);
        if (!string.Equals(_runtimeUiPreviewKind, "rematch", StringComparison.Ordinal)) return;
        SetInheritedField(_runtimeUiPreviewMenu, "labelEntries", new string[0]);
        SetInheritedField(_runtimeUiPreviewMenu, "menuEntries",
            new string[] { "rematch", "to lobby", "quit" });
        SetInheritedField(_runtimeUiPreviewMenu, "largestButtonSize", 400);
        SetInheritedField(_runtimeUiPreviewMenu, "showLogo", false);
        SetInheritedField(_runtimeUiPreviewMenu, "startTextOn", 0.45f);
        SetInheritedField(_runtimeUiPreviewMenu, "standardButtonSpace", 15f);
        SetInheritedField(_runtimeUiPreviewMenu, "spaceLastEntry", true);
    }

    private static void SetInheritedField(Component component, string fieldName, object value)
    {
        if (component == null) return;
        Type type = component.GetType();
        while (type != null)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
            {
                field.SetValue(component, value);
                return;
            }
            type = type.BaseType;
        }
    }

    private void CloseRuntimeUiPreview()
    {
        if (_runtimeUiPreview != null) UnityEngine.Object.Destroy(_runtimeUiPreview);
        _runtimeUiPreview = null;
        _runtimeUiPreviewMenu = null;
        _runtimeUiPreviewKind = string.Empty;
    }

    private void TriggerTransientStatus(bool life)
    {
        Component target = FindPlayerDynamicObject();
        if (target == null) target = FindActiveComponent("DynamicObject");
        if (target == null)
        {
            _status = "状态提示测试失败: 当前场景没有活动的 DynamicObject";
            return;
        }

        GameObject prefab = null;
        if (!life) prefab = FindColorCollectedPrefab();
        if (prefab == null)
        {
            FieldInfo bitsPrefabField = target.GetType().GetField("bitsIndicatorPrefab",
                BindingFlags.Public | BindingFlags.Instance);
            prefab = bitsPrefabField == null ? null : bitsPrefabField.GetValue(target) as GameObject;
        }
        if (prefab == null)
        {
            _status = "状态提示测试失败: 找不到游戏原有提示预制体";
            return;
        }

        GameObject clone = UnityEngine.Object.Instantiate(prefab, target.transform.position,
            Quaternion.identity) as GameObject;
        if (clone == null)
        {
            _status = "状态提示测试失败: 无法复制提示预制体";
            return;
        }
        clone.name = life ? "LocalizationDebug.AddLifePreview" :
            "LocalizationDebug.ColorCollectedPreview";
        clone.transform.parent = target.transform;

        if (life)
        {
            Type bitsType = FindLoadedType("BitsIndicatorScript");
            Component bits = bitsType == null ? null : clone.GetComponent(bitsType);
            MethodInfo addLife = bits == null ? null : bits.GetType().GetMethod("AddLife",
                BindingFlags.Public | BindingFlags.Instance);
            if (addLife == null)
            {
                UnityEngine.Object.Destroy(clone);
                _status = "状态提示测试失败: 预制体缺少 BitsIndicatorScript.AddLife";
                return;
            }
            addLife.Invoke(bits, new object[] { 1 });
        }
        else
        {
            Type indicatorType = FindLoadedType("IndicatorScript");
            Component indicator = indicatorType == null ? null : clone.GetComponent(indicatorType);
            if (indicator == null)
            {
                UnityEngine.Object.Destroy(clone);
                _status = "状态提示测试失败: 预制体缺少 IndicatorScript";
                return;
            }
            MonoBehaviour bitsBehaviour = FindComponentByTypeName(clone, "BitsIndicatorScript")
                as MonoBehaviour;
            if (bitsBehaviour != null) bitsBehaviour.enabled = false;
            SetTextMeshField(indicator, "textYellow", "Color collected!");
            SetTextMeshField(indicator, "textYellowShadow", "Color collected!");
        }

        UnityEngine.Object.Destroy(clone, 3.5f);
        _status = life ? "已触发 + 1 条命提示; 未修改生命或 Bits" :
            "已触发配色收集提示; 未收集配色或写入存档";
    }

    private static GameObject FindColorCollectedPrefab()
    {
        Type type = FindLoadedType("ColorCapsuleScript");
        if (type == null) return null;
        UnityEngine.Object[] objects = Resources.FindObjectsOfTypeAll(type);
        for (int i = 0; i < objects.Length; i++)
        {
            Component component = objects[i] as Component;
            if (component == null) continue;
            FieldInfo field = component.GetType().GetField("colorCollectedPrefab",
                BindingFlags.Public | BindingFlags.Instance);
            GameObject prefab = field == null ? null : field.GetValue(component) as GameObject;
            if (prefab != null) return prefab;
        }
        return null;
    }

    private static Component FindPlayerDynamicObject()
    {
        Type handlerType = FindLoadedType("GameHandler");
        Type dynamicType = FindLoadedType("DynamicObject");
        if (handlerType == null || dynamicType == null) return null;
        PropertyInfo instanceProperty = handlerType.GetProperty("Instance",
            BindingFlags.Public | BindingFlags.Static);
        object handler = instanceProperty == null ? null : instanceProperty.GetValue(null, null);
        MethodInfo getPlayer = handler == null ? null : handlerType.GetMethod("GetPlayer",
            BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(int) }, null);
        object player = getPlayer == null ? null : getPlayer.Invoke(handler, new object[] { 0 });
        GameObject playerObject = player as GameObject;
        Component playerComponent = player as Component;
        if (playerObject == null && playerComponent != null) playerObject = playerComponent.gameObject;
        return playerObject == null ? null : playerObject.GetComponent(dynamicType);
    }

    private static Component FindComponentByTypeName(GameObject gameObject, string typeName)
    {
        if (gameObject == null) return null;
        Type type = FindLoadedType(typeName);
        return type == null ? null : gameObject.GetComponent(type);
    }

    private void PreviewLevelComplete(bool tournament)
    {
        CloseLevelCompletePreview();
        Component level = FindActiveComponent("LevelScript");
        if (level == null)
        {
            _status = "通关界面预览失败: 当前场景没有活动的 LevelScript";
            return;
        }

        FieldInfo prefabField = level.GetType().GetField("levelCompletePrefab",
            BindingFlags.Public | BindingFlags.Instance);
        GameObject prefab = prefabField == null ? null : prefabField.GetValue(level) as GameObject;
        if (prefab == null)
        {
            _status = "通关界面预览失败: 当前关卡没有 levelCompletePrefab";
            return;
        }

        int levelNumber;
        if (!int.TryParse(_levelCompletePreviewLevel, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out levelNumber)) levelNumber = 1;
        levelNumber = Mathf.Clamp(levelNumber, 1, 6);
        _levelCompletePreviewLevel = levelNumber.ToString(CultureInfo.InvariantCulture);

        GameObject clone = UnityEngine.Object.Instantiate(prefab,
            Vector3.down * 100000f, Quaternion.identity) as GameObject;
        if (clone == null)
        {
            _status = "通关界面预览失败: 无法复制 levelCompletePrefab";
            return;
        }
        clone.name = "LocalizationDebug.LevelCompletePreview";
        _levelCompletePreview = clone;

        Type guiType = FindLoadedType("LevelCompleteGUIScript");
        Component gui = guiType == null ? null : clone.GetComponent(guiType);
        MonoBehaviour behaviour = gui as MonoBehaviour;
        if (behaviour != null) behaviour.enabled = false;
        if (gui == null)
        {
            CloseLevelCompletePreview();
            _status = "通关界面预览失败: 预制体缺少 LevelCompleteGUIScript";
            return;
        }

        string title = tournament ? "Tournament won!" :
            "Level " + levelNumber.ToString(CultureInfo.InvariantCulture) + " completed!";
        string body = tournament ? "You've received a special part!" :
            GetLevelCompletePreviewText(levelNumber);
        SetTextMeshField(gui, "levelName", title);
        SetTextMeshField(gui, "levelNameShadow", title);
        SetTextMeshField(gui, "levelComplete", body);
        SetTextMeshField(gui, "levelCompleteShadow", body);

        FieldInfo okField = gui.GetType().GetField("OK", BindingFlags.Public | BindingFlags.Instance);
        GameObject ok = okField == null ? null : okField.GetValue(gui) as GameObject;
        if (ok != null) ok.SetActive(true);

        TextMesh[] meshes = clone.GetComponentsInChildren<TextMesh>(true);
        for (int i = 0; i < meshes.Length; i++)
        {
            if (meshes[i] == null) continue;
            string value = meshes[i].text == null ? string.Empty : meshes[i].text.Trim();
            if (string.Equals(value, "GET:", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "OK", StringComparison.OrdinalIgnoreCase))
                meshes[i].text = meshes[i].text;
        }
        _status = "已预览" + (tournament ? "锦标赛" : "第 " + levelNumber + " 关") +
            "结算界面; 未修改进度、奖励或存档";
    }

    private static string GetLevelCompletePreviewText(int levelNumber)
    {
        switch (levelNumber)
        {
            case 1: return "You've beaten Warlord Bouldar and recieved his drill part.\n" +
                "Check the collection chest in your house!";
            case 2: return "You've beaten General HB-02!";
            case 3: return "You have defeated Grand Khotep Scarb!";
            case 4: return "You have defeated Grand Khotep Muer!";
            case 5: return "You have defeated the Ice-Beak Assassins!";
            default: return "You have defeated the General HB-03!";
        }
    }

    private static void SetTextMeshField(Component component, string fieldName, string value)
    {
        FieldInfo field = component.GetType().GetField(fieldName,
            BindingFlags.Public | BindingFlags.Instance);
        TextMesh mesh = field == null ? null : field.GetValue(component) as TextMesh;
        if (mesh != null) mesh.text = value;
    }

    private void CloseLevelCompletePreview()
    {
        if (_levelCompletePreview != null) UnityEngine.Object.Destroy(_levelCompletePreview);
        _levelCompletePreview = null;
    }

    private void DrawAutomaticAuditControls()
    {
        GUILayout.BeginVertical("box");
        GUILayout.BeginHorizontal();
        GUILayout.Label("自动巡检", GUILayout.Width(70f));
        _automaticAuditIncludeInventory = GUILayout.Toggle(_automaticAuditIncludeInventory,
            "额外巡检一次背包", GUILayout.Width(135f));
        GUILayout.Label("每步等待", GUILayout.Width(60f));
        string delayText = GUILayout.TextField(_automaticAuditDelay.ToString("0.0",
            CultureInfo.InvariantCulture), GUILayout.Width(45f));
        float delay;
        if (float.TryParse(delayText, NumberStyles.Float, CultureInfo.InvariantCulture, out delay))
            _automaticAuditDelay = Mathf.Clamp(delay, 0.5f, 10f);
        GUILayout.Label("秒", GUILayout.Width(22f));

        if (!_automaticAudit)
        {
            if (GUILayout.Button("准备全场景巡检", GUILayout.Width(120f)))
            {
                _automaticAuditArmed = true;
                _automaticAuditArmExpires = Time.realtimeSinceStartup + 5f;
                _status = "自动巡检已准备, 请在5秒内确认开始";
            }
            GUI.enabled = _automaticAuditArmed &&
                Time.realtimeSinceStartup <= _automaticAuditArmExpires;
            if (GUILayout.Button("确认开始", GUILayout.Width(80f))) StartAutomaticAudit();
            GUI.enabled = true;
        }
        else if (GUILayout.Button("停止并导出", GUILayout.Width(100f))) StopAutomaticAudit(true);

        GUILayout.Label(_automaticAudit
            ? "进度 " + _automaticAuditCompletedScenes + "/" + Application.levelCount +
                "，当前目标 #" + _automaticAuditTargetScene
            : "将依次加载全部场景，完成后自动导出并返回起始场景。");
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("对话巡检", GUILayout.Width(70f));
        GUILayout.Label(_dialogueAuditStatus);
        if (GUILayout.Button("立即巡检全部对话", GUILayout.Width(130f))) AuditDialogues();
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("真实渲染", GUILayout.Width(70f));
        _dialogueRenderCaptureAll = GUILayout.Toggle(_dialogueRenderCaptureAll,
            "每行截图", GUILayout.Width(75f));
        if (!_dialogueRenderWaiting && !_dialogueRenderRunning)
        {
            if (GUILayout.Button("等待并绑定对话框", GUILayout.Width(130f)))
            {
                _dialogueRenderWaiting = true;
                _show = false;
                _status = "请正常触发任意一段游戏对话，工具会自动复制真实对话框开始巡检";
            }
        }
        else if (GUILayout.Button("停止渲染巡检", GUILayout.Width(120f)))
            StopDialogueRenderAudit("用户停止");
        GUILayout.Label(_dialogueRenderRunning
            ? "进度 " + _dialogueRenderIndex + "/" + _dialogueRenderRows.Count +
                "，溢出/异常 " + _dialogueRenderProblems
            : (_dialogueRenderWaiting ? "正在等待游戏对话框出现..." :
                "使用游戏实际 TextBox、字体、材质和尺寸逐行检查。"));
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("指定行", GUILayout.Width(70f));
        GUI.enabled = !_dialogueRenderWaiting && !_dialogueRenderRunning;
        _dialogueRenderTargets = GUILayout.TextField(_dialogueRenderTargets);
        GUI.enabled = true;
        GUILayout.Label("例: 10/3,19/4；留空=全部", GUILayout.Width(190f));
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
    }

    private void StartAutomaticAudit()
    {
        if (Application.levelCount <= 0)
        {
            _status = "自动巡检启动失败: 构建场景列表为空";
            return;
        }
        _automaticAuditArmed = false;
        _automaticAudit = true;
        _automaticAuditStartScene = Application.loadedLevel;
        _automaticAuditTargetScene = 0;
        _automaticAuditCompletedScenes = 0;
        _automaticAuditPhase = 0;
        _automaticInventoryStep = 0;
        _automaticInventoryAuditDone = false;
        _automaticAuditNextAction = Time.realtimeSinceStartup;
        AddToolEvent("auto-audit-start", "scenes=" + Application.levelCount +
            ", inventory=" + _automaticAuditIncludeInventory);
        AuditDialogues();
        _status = "自动巡检开始";
    }

    private void AdvanceAutomaticAudit()
    {
        if (!_automaticAudit || Time.realtimeSinceStartup < _automaticAuditNextAction) return;
        try
        {
            if (_automaticAuditPhase == 0)
            {
                if (Application.loadedLevel != _automaticAuditTargetScene)
                {
                    AddToolEvent("auto-scene-load", Application.loadedLevel + " -> " +
                        _automaticAuditTargetScene);
                    Application.LoadLevel(_automaticAuditTargetScene);
                }
                _automaticAuditPhase = 1;
                _automaticAuditNextAction = Time.realtimeSinceStartup + _automaticAuditDelay;
                return;
            }

            if (_automaticAuditPhase == 1)
            {
                if (Application.loadedLevel != _automaticAuditTargetScene)
                {
                    _automaticAuditNextAction = Time.realtimeSinceStartup + 0.25f;
                    return;
                }
                ScanTextMeshes();
                if (!_dialogueRuntimeAvailable) AuditDialogues();
                AddToolEvent("auto-scene-scan", Application.loadedLevelName + " (#" +
                    Application.loadedLevel + "), entries=" + _entries.Count);
                _inventoryController = null;
                FindInventoryController();
                if (_automaticAuditIncludeInventory && !_automaticInventoryAuditDone &&
                    _inventoryController != null)
                {
                    _automaticOriginalInventoryState = GetInventoryStateName(_inventoryController);
                    _automaticInventoryStep = 0;
                    _automaticAuditPhase = 2;
                    _automaticAuditNextAction = Time.realtimeSinceStartup + 0.25f;
                    return;
                }
                CompleteAutomaticAuditScene();
                return;
            }

            if (_automaticAuditPhase == 2)
            {
                if (_inventoryController == null)
                {
                    CompleteAutomaticAuditScene();
                    return;
                }
                if (_automaticInventoryStep > 0) ScanTextMeshes();
                if (_automaticInventoryStep < AutomaticInventoryStates.Length)
                {
                    InvokeInventoryState(AutomaticInventoryStates[_automaticInventoryStep++]);
                    _automaticAuditNextAction = Time.realtimeSinceStartup + _automaticAuditDelay;
                    return;
                }
                ScanTextMeshes();
                if (!string.IsNullOrEmpty(_automaticOriginalInventoryState) &&
                    !_automaticOriginalInventoryState.StartsWith("<", StringComparison.Ordinal))
                    InvokeInventoryState(_automaticOriginalInventoryState);
                _automaticInventoryAuditDone = true;
                CompleteAutomaticAuditScene();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[LocalizationDebug] 自动巡检步骤失败: " + ex);
            AddToolEvent("auto-audit-error", ex.GetType().Name + ": " + ex.Message);
            CompleteAutomaticAuditScene();
        }
    }

    private void CompleteAutomaticAuditScene()
    {
        _automaticAuditCompletedScenes++;
        AddToolEvent("auto-scene-complete", _automaticAuditTargetScene.ToString(
            CultureInfo.InvariantCulture));
        _automaticAuditTargetScene++;
        if (_automaticAuditTargetScene >= Application.levelCount)
        {
            StopAutomaticAudit(true);
            return;
        }
        _automaticAuditPhase = 0;
        _automaticAuditNextAction = Time.realtimeSinceStartup + 0.25f;
        _status = "自动巡检: 已完成 " + _automaticAuditCompletedScenes + "/" +
            Application.levelCount + " 个场景";
    }

    private void StopAutomaticAudit(bool export)
    {
        bool completed = _automaticAuditCompletedScenes >= Application.levelCount;
        int returnScene = _automaticAuditStartScene;
        _automaticAudit = false;
        _automaticAuditArmed = false;
        AddToolEvent(completed ? "auto-audit-complete" : "auto-audit-stopped",
            "scenes=" + _automaticAuditCompletedScenes + "/" + Application.levelCount);
        if (export) ExportBundle();
        if (returnScene >= 0 && returnScene < Application.levelCount &&
            returnScene != Application.loadedLevel)
        {
            _returnSceneIndex = Application.loadedLevel;
            Application.LoadLevel(returnScene);
        }
        _status = (completed ? "自动巡检完成" : "自动巡检已停止") +
            (export ? "，结果已导出" : string.Empty);
    }

    private void AuditDialogues()
    {
        _dialogueAuditRows.Clear();
        _dialogueAuditApplied = 0;
        _dialogueAuditProblems = 0;
        _dialogueRuntimeAvailable = false;
        try
        {
            string modsDirectory = Path.GetDirectoryName(_modDirectory);
            string path = Path.Combine(Path.Combine(
                Path.Combine(modsDirectory, "ChineseLocalization"), "data"), "dialogue.tsv");
            if (!File.Exists(path))
            {
                _dialogueAuditStatus = "未找到 data/dialogue.tsv";
                _dialogueAuditProblems = 1;
                AddToolEvent("dialogue-audit", _dialogueAuditStatus);
                return;
            }

            Dictionary<string, bool> runtimeLines = ReadRuntimeDialogueLines();
            _dialogueRuntimeAvailable = runtimeLines != null;
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0 || lines[i][0] == '#') continue;
                string[] values = lines[i].Split(new char[] { '\t' }, 4);
                int groupId, lineId;
                if (values.Length != 4 || !int.TryParse(values[0], out groupId) ||
                    !int.TryParse(values[1], out lineId))
                {
                    _dialogueAuditProblems++;
                    continue;
                }
                DialogueAuditRow row = new DialogueAuditRow();
                row.GroupId = groupId;
                row.LineId = lineId;
                row.Source = values[2].Replace("\\n", "\n").TrimEnd();
                row.Translation = values[3].Replace("\\n", "\n").TrimEnd();
                ClassifyDialogueRow(row, runtimeLines);
                if (row.RuntimeStatus == "已应用") _dialogueAuditApplied++;
                if (!string.IsNullOrEmpty(row.Issue)) _dialogueAuditProblems++;
                _dialogueAuditRows.Add(row);
            }
            _dialogueAuditStatus = "共 " + _dialogueAuditRows.Count + " 行，运行时已应用 " +
                _dialogueAuditApplied + " 行，问题 " + _dialogueAuditProblems + " 项" +
                (_dialogueRuntimeAvailable ? string.Empty : "（运行时对话数据尚未就绪）");
            AddToolEvent("dialogue-audit", _dialogueAuditStatus);
            _status = "对话巡检完成: " + _dialogueAuditStatus;
        }
        catch (Exception ex)
        {
            _dialogueAuditProblems++;
            _dialogueAuditStatus = "对话巡检失败: " + ex.Message;
            Debug.LogError("[LocalizationDebug] " + _dialogueAuditStatus + "\n" + ex);
            AddToolEvent("dialogue-audit-error", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private void ClassifyDialogueRow(DialogueAuditRow row, Dictionary<string, bool> runtimeLines)
    {
        if (string.IsNullOrEmpty(row.Translation))
        {
            row.RuntimeStatus = "无译文";
            row.Issue = "译文为空";
            return;
        }
        string source = Normalize(row.Source);
        string translation = Normalize(row.Translation);
        bool intentionallyUnchanged = string.Equals(source, translation, StringComparison.Ordinal) &&
            !ContainsTranslatableLatin(row.Translation);
        if (runtimeLines == null)
            row.RuntimeStatus = "运行时未就绪";
        else if (runtimeLines.ContainsKey(translation))
            row.RuntimeStatus = intentionallyUnchanged ? "保留原文" : "已应用";
        else if (runtimeLines.ContainsKey(source))
            row.RuntimeStatus = intentionallyUnchanged ? "保留原文" : "仍为原文";
        else
            row.RuntimeStatus = "运行时未找到";

        if (!intentionallyUnchanged && string.Equals(source, translation, StringComparison.Ordinal))
            row.Issue = "译文与原文相同";
        else if (ContainsTranslatableLatin(row.Translation))
            row.Issue = "译文含未列入保留名词表的英文";
        else if (row.RuntimeStatus == "仍为原文")
            row.Issue = "运行时替换未生效";
        else if (row.RuntimeStatus == "运行时未找到")
            row.Issue = "运行时数据中未找到原文或译文";
    }

    private Dictionary<string, bool> ReadRuntimeDialogueLines()
    {
        Type gameHandlerType = FindLoadedType("GameHandler");
        if (gameHandlerType == null) return null;
        PropertyInfo instanceProperty = gameHandlerType.GetProperty("Instance",
            BindingFlags.Public | BindingFlags.Static);
        object gameHandler = instanceProperty == null ? null : instanceProperty.GetValue(null, null);
        if (gameHandler == null) return null;
        FieldInfo dialogDataField = gameHandlerType.GetField("dialogData",
            BindingFlags.Public | BindingFlags.Instance);
        object dialogData = dialogDataField == null ? null : dialogDataField.GetValue(gameHandler);
        if (dialogData == null) return null;
        FieldInfo dialogsField = dialogData.GetType().GetField("dialogs",
            BindingFlags.Public | BindingFlags.Instance);
        Array dialogs = dialogsField == null ? null : dialogsField.GetValue(dialogData) as Array;
        if (dialogs == null) return null;

        Dictionary<string, bool> result = new Dictionary<string, bool>(StringComparer.Ordinal);
        for (int dialogIndex = 0; dialogIndex < dialogs.Length; dialogIndex++)
        {
            object dialog = dialogs.GetValue(dialogIndex);
            if (dialog == null) continue;
            FieldInfo linesField = dialog.GetType().GetField("lines",
                BindingFlags.Public | BindingFlags.Instance);
            string[] lines = linesField == null ? null : linesField.GetValue(dialog) as string[];
            if (lines == null) continue;
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                if (lines[lineIndex] != null) result[Normalize(lines[lineIndex].TrimEnd())] = true;
        }
        return result;
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

    private void AdvanceDialogueRenderAudit()
    {
        if (_dialogueRenderWaiting)
        {
            Component textBox = FindActiveComponent("TextBoxScript");
            if (textBox != null) StartDialogueRenderAudit(textBox);
            return;
        }
        if (!_dialogueRenderRunning || Time.realtimeSinceStartup < _dialogueRenderNextAction) return;
        if (_dialogueRenderClone == null || _dialogueRenderText == null || _dialogueRenderBox == null)
        {
            StopDialogueRenderAudit("测试对话框失效");
            return;
        }
        if (_dialogueRenderIndex >= _dialogueRenderRows.Count)
        {
            FinishDialogueRenderAudit();
            return;
        }

        DialogueAuditRow row = _dialogueRenderRows[_dialogueRenderIndex];
        string translation = row.Translation == null ? string.Empty : row.Translation;
        _dialogueRenderText.text = translation;
        if (_dialogueRenderShadow != null) _dialogueRenderShadow.text = translation;
        StartCoroutine(MeasureDialogueRenderAfterFrame(row, _dialogueRenderIndex));
        _dialogueRenderNextAction = Time.realtimeSinceStartup + 0.25f;
        _dialogueRenderIndex++;
    }

    private System.Collections.IEnumerator MeasureDialogueRenderAfterFrame(DialogueAuditRow row, int index)
    {
        yield return null;
        if (!_dialogueRenderRunning || _dialogueRenderText == null || _dialogueRenderBox == null)
            yield break;
        DialogueRenderResult result = new DialogueRenderResult();
        result.GroupId = row.GroupId;
        result.LineId = row.LineId;
        result.Translation = row.Translation;
        result.Font = _dialogueRenderText.font == null ? string.Empty : _dialogueRenderText.font.name;
        result.CharacterSize = _dialogueRenderText.characterSize;
        result.LineSpacing = _dialogueRenderText.lineSpacing;
        bool hasTextRect = _dialogueRenderText.renderer != null &&
            TryGetScreenRect(_dialogueRenderText.renderer, out result.TextRect);
        bool hasBoxRect = TryGetScreenRect(_dialogueRenderBox, out result.BoxRect);
        if (!hasTextRect) result.Issue = "文字未投影到画面";
        else if (!hasBoxRect) result.Issue = "对话框未投影到画面";
        else
        {
            float left = Mathf.Max(0f, result.BoxRect.xMin - result.TextRect.xMin);
            float right = Mathf.Max(0f, result.TextRect.xMax - result.BoxRect.xMax);
            float top = Mathf.Max(0f, result.BoxRect.yMin - result.TextRect.yMin);
            float bottom = Mathf.Max(0f, result.TextRect.yMax - result.BoxRect.yMax);
            float overflow = Mathf.Max(Mathf.Max(left, right), Mathf.Max(top, bottom));
            if (overflow > 2f)
                result.Issue = "文字超出对话框 " + F(overflow) + "px";
            else if (result.TextRect.width < 2f || result.TextRect.height < 2f)
                result.Issue = "文字渲染尺寸异常";
        }
        if (!string.IsNullOrEmpty(result.Issue)) _dialogueRenderProblems++;
        if (_dialogueRenderCaptureAll || !string.IsNullOrEmpty(result.Issue))
        {
            string directory = Path.Combine(_modDirectory, "captures");
            Directory.CreateDirectory(directory);
            string name = "dialogue-render-g" + row.GroupId.ToString("000") + "-l" +
                row.LineId.ToString("00") + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff",
                CultureInfo.InvariantCulture) + ".png";
            result.Screenshot = Path.Combine(directory, name);
            Application.CaptureScreenshot(result.Screenshot, 1);
        }
        _dialogueRenderResults.Add(result);
    }

    private Component FindActiveComponent(string typeName)
    {
        Type type = FindLoadedType(typeName);
        if (type == null) return null;
        UnityEngine.Object[] objects = Resources.FindObjectsOfTypeAll(type);
        for (int i = 0; i < objects.Length; i++)
        {
            Component component = objects[i] as Component;
            if (component != null && component.gameObject != null &&
                component.gameObject.activeInHierarchy) return component;
        }
        return null;
    }

    private void StartDialogueRenderAudit(Component source)
    {
        AuditDialogues();
        if (_dialogueAuditRows.Count == 0)
        {
            _dialogueRenderWaiting = false;
            _show = true;
            _status = "真实渲染巡检启动失败: 没有对话译文";
            return;
        }
        string targetError;
        if (!BuildDialogueRenderRows(out targetError))
        {
            _dialogueRenderWaiting = false;
            _show = true;
            _status = "真实渲染巡检启动失败: " + targetError;
            return;
        }
        GameObject clone = UnityEngine.Object.Instantiate(source.gameObject) as GameObject;
        if (clone == null)
        {
            _dialogueRenderWaiting = false;
            _show = true;
            _status = "真实渲染巡检启动失败: 无法复制对话框";
            return;
        }
        clone.name = "LocalizationDebug.DialogueRenderAudit";
        clone.transform.parent = source.transform.parent;
        clone.transform.localPosition = source.transform.localPosition;
        clone.transform.localRotation = source.transform.localRotation;
        clone.transform.localScale = source.transform.localScale;
        Component[] behaviours = clone.GetComponentsInChildren(typeof(MonoBehaviour));
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i] as MonoBehaviour;
            if (behaviour != null) behaviour.enabled = false;
        }
        Component[] textMeshes = clone.GetComponentsInChildren(typeof(TextMesh));
        for (int i = 0; i < textMeshes.Length; i++)
        {
            TextMesh mesh = textMeshes[i] as TextMesh;
            if (mesh == null) continue;
            string name = mesh.gameObject.name.ToLowerInvariant();
            if (name == "text") _dialogueRenderText = mesh;
            else if (name == "textshadow") _dialogueRenderShadow = mesh;
        }
        _dialogueRenderBox = FindChildRenderer(clone.transform, "box");
        if (_dialogueRenderText == null || _dialogueRenderBox == null)
        {
            UnityEngine.Object.Destroy(clone);
            _dialogueRenderWaiting = false;
            _show = true;
            _status = "真实渲染巡检启动失败: 对话框结构不符合预期";
            return;
        }

        _dialogueHiddenRenderers.Clear();
        _dialogueHiddenRendererStates.Clear();
        Component[] originalRenderers = source.gameObject.GetComponentsInChildren(typeof(Renderer));
        for (int i = 0; i < originalRenderers.Length; i++)
        {
            Renderer renderer = originalRenderers[i] as Renderer;
            if (renderer == null) continue;
            _dialogueHiddenRenderers.Add(renderer);
            _dialogueHiddenRendererStates.Add(renderer.enabled);
            renderer.enabled = false;
        }
        _dialogueRenderClone = clone;
        _dialogueRenderResults.Clear();
        _dialogueRenderIndex = 0;
        _dialogueRenderProblems = 0;
        _dialogueRenderWaiting = false;
        _dialogueRenderRunning = true;
        _dialogueRenderNextAction = Time.realtimeSinceStartup + 0.25f;
        _show = false;
        AddToolEvent("dialogue-render-start", "rows=" + _dialogueRenderRows.Count);
        _status = "正在使用真实游戏对话框逐行渲染 " + _dialogueRenderRows.Count + " 行译文";
    }

    private bool BuildDialogueRenderRows(out string error)
    {
        _dialogueRenderRows.Clear();
        error = string.Empty;
        string targets = (_dialogueRenderTargets ?? string.Empty).Trim();
        if (targets.Length == 0)
        {
            _dialogueRenderRows.AddRange(_dialogueAuditRows);
            return true;
        }

        string[] tokens = targets.Split(new char[] { ',', ';', ' ', '\t', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i].Trim().ToUpperInvariant().Replace("G", string.Empty)
                .Replace("L", string.Empty);
            string[] ids = token.Split('/');
            int groupId, lineId;
            if (ids.Length != 2 || !int.TryParse(ids[0], out groupId) ||
                !int.TryParse(ids[1], out lineId))
            {
                error = "指定行格式错误: " + tokens[i];
                return false;
            }
            DialogueAuditRow match = null;
            for (int rowIndex = 0; rowIndex < _dialogueAuditRows.Count; rowIndex++)
            {
                DialogueAuditRow row = _dialogueAuditRows[rowIndex];
                if (row.GroupId == groupId && row.LineId == lineId)
                {
                    match = row;
                    break;
                }
            }
            if (match == null)
            {
                error = "未找到指定行: G" + groupId + "/L" + lineId;
                return false;
            }
            if (!_dialogueRenderRows.Contains(match)) _dialogueRenderRows.Add(match);
        }
        if (_dialogueRenderRows.Count == 0)
        {
            error = "没有可巡检的指定行";
            return false;
        }
        return true;
    }

    private static Renderer FindChildRenderer(Transform root, string objectName)
    {
        if (root == null) return null;
        if (string.Equals(root.gameObject.name, objectName, StringComparison.OrdinalIgnoreCase))
        {
            Renderer renderer = root.gameObject.GetComponent(typeof(Renderer)) as Renderer;
            if (renderer != null) return renderer;
        }
        for (int i = 0; i < root.childCount; i++)
        {
            Renderer found = FindChildRenderer(root.GetChild(i), objectName);
            if (found != null) return found;
        }
        return null;
    }

    private void FinishDialogueRenderAudit()
    {
        WriteDialogueRenderAudit();
        AddToolEvent("dialogue-render-complete", "rows=" + _dialogueRenderResults.Count +
            ", problems=" + _dialogueRenderProblems);
        StopDialogueRenderAudit("完成，共 " + _dialogueRenderResults.Count + " 行，异常 " +
            _dialogueRenderProblems + " 项");
    }

    private void StopDialogueRenderAudit(string reason)
    {
        _dialogueRenderWaiting = false;
        _dialogueRenderRunning = false;
        for (int i = 0; i < _dialogueHiddenRenderers.Count; i++)
            if (_dialogueHiddenRenderers[i] != null)
                _dialogueHiddenRenderers[i].enabled = _dialogueHiddenRendererStates[i];
        _dialogueHiddenRenderers.Clear();
        _dialogueHiddenRendererStates.Clear();
        if (_dialogueRenderClone != null) UnityEngine.Object.Destroy(_dialogueRenderClone);
        _dialogueRenderClone = null;
        _dialogueRenderText = null;
        _dialogueRenderShadow = null;
        _dialogueRenderBox = null;
        _show = true;
        _status = "真实对话渲染巡检: " + reason;
    }

    private void WriteDialogueRenderAudit()
    {
        string directory = Path.Combine(_modDirectory, "captures");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "dialogue-render-audit-" +
            DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".tsv");
        using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true)))
        {
            writer.WriteLine("groupId\tlineId\tissue\tfont\tcharacterSize\tlineSpacing\ttextRect\tboxRect\tscreenshot\ttranslation");
            for (int i = 0; i < _dialogueRenderResults.Count; i++)
            {
                DialogueRenderResult result = _dialogueRenderResults[i];
                writer.Write(result.GroupId); writer.Write('\t');
                writer.Write(result.LineId); writer.Write('\t');
                writer.Write(Tsv(result.Issue)); writer.Write('\t');
                writer.Write(Tsv(result.Font)); writer.Write('\t');
                writer.Write(F(result.CharacterSize)); writer.Write('\t');
                writer.Write(F(result.LineSpacing)); writer.Write('\t');
                writer.Write(Tsv(RectText(result.TextRect))); writer.Write('\t');
                writer.Write(Tsv(RectText(result.BoxRect))); writer.Write('\t');
                writer.Write(Tsv(result.Screenshot)); writer.Write('\t');
                writer.WriteLine(Tsv(result.Translation));
            }
        }
    }

    private static string RectText(Rect rect)
    {
        return "x=" + F(rect.x) + ",y=" + F(rect.y) + ",w=" + F(rect.width) +
            ",h=" + F(rect.height);
    }

    private void LoadPersistentState()
    {
        _storageWarnings = string.Empty;
        TryLoadPersistentFile("调试设置", new PersistentLoader(LoadSettings));
        TryLoadPersistentFile("布局预设", new PersistentLoader(LoadPresets));
        TryLoadPersistentFile("场景目录", new PersistentLoader(LoadSceneCatalog));
        TryLoadPersistentFile("复核记录", new PersistentLoader(LoadReviews));
        TryLoadPersistentFile("保留名词表", new PersistentLoader(LoadUntranslatedAllowlist));
    }

    private delegate void PersistentLoader();

    private void TryLoadPersistentFile(string label, PersistentLoader loader)
    {
        try { loader(); }
        catch (Exception ex)
        {
            if (_storageWarnings.Length > 0) _storageWarnings += "; ";
            _storageWarnings += label + "读取失败";
            Debug.LogError("[LocalizationDebug] " + label + "读取失败: " + ex);
        }
    }

    private void RunSelfCheck()
    {
        List<string> problems = new List<string>();
        if (!string.IsNullOrEmpty(_storageWarnings)) problems.Add(_storageWarnings);
        if (string.IsNullOrEmpty(_modDirectory) || !Directory.Exists(_modDirectory))
            problems.Add("模组目录不存在");
        else
        {
            string probe = Path.Combine(_modDirectory, ".localization-debug-write-test.tmp");
            try
            {
                File.WriteAllText(probe, "ok", Encoding.UTF8);
                File.Delete(probe);
            }
            catch (Exception ex)
            {
                problems.Add("模组目录不可写");
                Debug.LogError("[LocalizationDebug] 写入自检失败: " + ex);
            }
        }
        if (Application.levelCount <= 0) problems.Add("构建场景列表为空");
        if (_highlightTexture == null) problems.Add("高亮纹理未初始化");

        ScanTextMeshes();
        string result = problems.Count == 0
            ? "自检通过: 文本=" + _entries.Count + ", 场景=" + _sceneCatalog.Count + "/" +
                Application.levelCount + ", 预设=" + _presets.Count + ", 复核=" + _reviews.Count +
                ", 保留名词=" + _untranslatedAllowlist.Count
            : "自检发现问题: " + string.Join("; ", problems.ToArray());
        _status = result;
        AddToolEvent("self-check", result);
        Debug.Log("[LocalizationDebug] " + result);
    }

    private void SetNearestUnvisited(int direction)
    {
        int count = Application.levelCount;
        if (count <= 0) return;
        int current = Application.loadedLevel;
        for (int offset = 1; offset < count; offset++)
        {
            int candidate = current + (offset * direction);
            while (candidate < 0) candidate += count;
            while (candidate >= count) candidate -= count;
            if (_sceneCatalog.ContainsKey(candidate)) continue;
            SetSceneTarget(candidate);
            _status = "已选择未访问场景 #" + candidate;
            return;
        }
        _status = "所有场景都已经访问过";
    }

    private void SetSceneTarget(int index)
    {
        if (Application.levelCount > 0)
            index = Mathf.Clamp(index, 0, Application.levelCount - 1);
        _sceneTargetText = index.ToString(CultureInfo.InvariantCulture);
        _armedSceneIndex = -1;
    }

    private bool TryGetSceneTarget(out int index)
    {
        if (!int.TryParse(_sceneTargetText, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out index)) return false;
        return index >= 0 && index < Application.levelCount;
    }

    private void LoadScene(int index)
    {
        if (index < 0 || index >= Application.levelCount)
        {
            _status = "场景索引无效: " + index;
            return;
        }
        int current = Application.loadedLevel;
        _returnSceneIndex = current;
        _armedSceneIndex = -1;
        AddToolEvent("scene-load", current + " -> " + index);
        _status = "正在加载场景 #" + index;
        Application.LoadLevel(index);
    }

    private void LoadSceneCatalog()
    {
        _sceneCatalog.Clear();
        string path = Path.Combine(_modDirectory, "scene-catalog.tsv");
        if (!File.Exists(path)) return;
        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        for (int i = 1; i < lines.Length; i++)
        {
            string[] values = lines[i].Split('\t');
            int index, visits;
            if (values.Length < 4 || !int.TryParse(values[0], out index) ||
                !int.TryParse(values[2], out visits)) continue;
            SceneRecord record = new SceneRecord();
            record.Index = index;
            record.Name = values[1];
            record.Visits = visits;
            record.LastVisited = values[3];
            _sceneCatalog[index] = record;
        }
    }

    private void RecordCurrentScene()
    {
        int index = Application.loadedLevel;
        SceneRecord record;
        if (!_sceneCatalog.TryGetValue(index, out record))
        {
            record = new SceneRecord();
            record.Index = index;
            record.Visits = 0;
            _sceneCatalog[index] = record;
        }
        record.Name = Application.loadedLevelName;
        record.Visits++;
        record.LastVisited = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        WriteSceneCatalog(Path.Combine(_modDirectory, "scene-catalog.tsv"));
    }

    private void WriteSceneCatalog(string path)
    {
        List<int> indexes = new List<int>(_sceneCatalog.Keys);
        indexes.Sort();
        using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true)))
        {
            writer.WriteLine("index\tname\tvisits\tlastVisited");
            for (int i = 0; i < indexes.Count; i++)
            {
                SceneRecord record = _sceneCatalog[indexes[i]];
                writer.Write(record.Index); writer.Write('\t');
                writer.Write(Tsv(record.Name)); writer.Write('\t');
                writer.Write(record.Visits); writer.Write('\t');
                writer.WriteLine(Tsv(record.LastVisited));
            }
        }
    }

    private void DrawSelected(TextEntry entry)
    {
        TextMesh mesh = entry.Mesh;
        if (mesh == null)
        {
            GUILayout.Label("选中的 TextMesh 已被销毁。");
            return;
        }

        _detailScroll = GUILayout.BeginScrollView(_detailScroll);
        GUILayout.Label("路径: " + entry.Path);
        string fontName = mesh.font == null ? "<null>" : mesh.font.name;
        GUILayout.Label("字体: " + fontName + " | 字号=" + mesh.fontSize +
            " 字符尺寸=" + F(mesh.characterSize) + " 行距=" + F(mesh.lineSpacing));
        GUILayout.Label("本地坐标: " + V(mesh.transform.localPosition) + " | 锚点=" + mesh.anchor +
            " 对齐=" + mesh.alignment);
        Renderer renderer = mesh.renderer;
        if (renderer != null)
            GUILayout.Label("边界中心=" + V(renderer.bounds.center) + " 尺寸=" + V(renderer.bounds.size));
        string layoutIssue = GetLayoutIssue(entry);
        GUILayout.Label("布局检查: " + (layoutIssue.Length == 0 ? "未发现异常" : layoutIssue));

        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.Width(385f));
        GUILayout.Label("从游戏赋值方法捕获的原文:");
        GUILayout.TextArea(Escape(entry.SourceText), GUILayout.Height(65f));
        GUILayout.EndVertical();
        GUILayout.BeginVertical(GUILayout.Width(385f));
        GUILayout.Label("当前实际显示文本:");
        GUILayout.TextArea(Escape(mesh.text), GUILayout.Height(65f));
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();

        DrawReviewControls(entry);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("显示原文") && entry.SourceText != null)
            mesh.text = entry.SourceText;
        if (GUILayout.Button("应用汉化") && entry.SourceText != null)
            HookDispatcher.SetTextMeshText(mesh, entry.SourceText);
        if (GUILayout.Button("恢复原始参数")) RestoreMetrics(entry);
        if (GUILayout.Button("复制报告")) CopyReport(entry);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("保存布局预设")) SavePreset(entry);
        if (GUILayout.Button("应用布局预设")) ApplyMatchingPreset(entry);
        if (GUILayout.Button("删除布局预设")) DeletePreset(entry);
        GUILayout.Label("预设: " + _presets.Count, GUILayout.Width(90f));
        GUILayout.EndHorizontal();

        GUILayout.Label("自定义预览文本(支持 \\n):");
        _customText = GUILayout.TextArea(_customText, GUILayout.Height(48f));
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("应用自定义文本")) mesh.text = _customText.Replace("\\n", "\n");
        if (GUILayout.Button("使用当前文本")) _customText = Escape(mesh.text);
        if (GUILayout.Button("使用原文") && entry.SourceText != null) _customText = Escape(entry.SourceText);
        GUILayout.EndHorizontal();

        DrawMetricRow("字符尺寸", delegate(float value) { mesh.characterSize += value; });
        DrawMetricRow("行距", delegate(float value) {
            mesh.lineSpacing = Mathf.Max(0.05f, mesh.lineSpacing + value);
        });
        DrawPositionRow(mesh);
        GUILayout.EndScrollView();
    }

    private delegate void FloatAdjustment(float value);

    private void DrawMetricRow(string label, FloatAdjustment adjustment)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(115f));
        if (GUILayout.Button("-", GUILayout.Width(45f))) adjustment(-_step);
        if (GUILayout.Button("+", GUILayout.Width(45f))) adjustment(_step);
        if (GUILayout.Button("-10x", GUILayout.Width(55f))) adjustment(-_step * 10f);
        if (GUILayout.Button("+10x", GUILayout.Width(55f))) adjustment(_step * 10f);
        GUILayout.EndHorizontal();
    }

    private void DrawReviewControls(TextEntry entry)
    {
        ReviewRecord record = FindReview(entry);
        GUILayout.BeginVertical("box");
        GUILayout.Label("复核状态: " + (record == null ? "未复核" : TranslateReviewStatus(record.Status)));
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("标记待处理")) SetReviewStatus(entry, "pending");
        if (GUILayout.Button("验收通过")) SetReviewStatus(entry, "accepted");
        if (GUILayout.Button("忽略此项")) SetReviewStatus(entry, "ignored");
        if (GUILayout.Button("清除标记")) ClearReview(entry);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("备注", GUILayout.Width(40f));
        _reviewNote = GUILayout.TextField(_reviewNote);
        if (GUILayout.Button("保存备注", GUILayout.Width(80f))) SaveReviewNote(entry);
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
    }

    private void DrawPositionRow(TextMesh mesh)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("本地坐标", GUILayout.Width(115f));
        if (GUILayout.Button("X-", GUILayout.Width(45f))) Offset(mesh, -_step, 0f);
        if (GUILayout.Button("X+", GUILayout.Width(45f))) Offset(mesh, _step, 0f);
        if (GUILayout.Button("Y-", GUILayout.Width(45f))) Offset(mesh, 0f, -_step);
        if (GUILayout.Button("Y+", GUILayout.Width(45f))) Offset(mesh, 0f, _step);
        GUILayout.EndHorizontal();
    }

    private void RequestScreenshot()
    {
        string directory = Path.Combine(_modDirectory, "captures");
        Directory.CreateDirectory(directory);
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        _pendingScreenshotPath = Path.Combine(directory, "screen-" + stamp + ".png");
        _captureScreenshotNextFrame = true;
        _show = false;
        _status = "正在截图: " + Path.GetFileName(_pendingScreenshotPath);
    }

    private void ToggleScreenPick()
    {
        _pickFromScreen = !_pickFromScreen;
        if (_pickFromScreen)
        {
            ScanTextMeshes();
            _show = false;
            _status = "请在游戏画面中点击要检查的文字, Esc取消";
        }
        else
        {
            _show = true;
            _status = "已取消画面点选";
        }
    }

    private void PickTextAtMouse()
    {
        Vector3 mouse = Input.mousePosition;
        Vector2 point = new Vector2(mouse.x, Screen.height - mouse.y);
        TextEntry best = null;
        float bestArea = float.MaxValue;
        foreach (KeyValuePair<int, TextEntry> pair in _entries)
        {
            TextEntry entry = pair.Value;
            if (!entry.Active || entry.Mesh == null || entry.Mesh.renderer == null) continue;
            Rect rect;
            if (!TryGetScreenRect(entry.Mesh.renderer, out rect)) continue;
            if (point.x < rect.xMin || point.x > rect.xMax || point.y < rect.yMin || point.y > rect.yMax)
                continue;
            float area = rect.width * rect.height;
            if (area >= bestArea) continue;
            best = entry;
            bestArea = area;
        }
        _pickFromScreen = false;
        _show = true;
        if (best == null)
        {
            _status = "点击位置没有找到活动 TextMesh";
            return;
        }
        SelectEntry(best);
        _status = "已从画面选中: " + best.Path;
        AddEvent(best, "screen-pick", string.Empty, best.Mesh.text);
    }

    private void DrawPickOverlay()
    {
        Color previous = GUI.color;
        GUI.color = Color.cyan;
        foreach (KeyValuePair<int, TextEntry> pair in _entries)
        {
            TextEntry entry = pair.Value;
            if (!entry.Active || entry.Mesh == null || entry.Mesh.renderer == null) continue;
            Rect rect;
            if (!TryGetScreenRect(entry.Mesh.renderer, out rect)) continue;
            DrawSolidRect(new Rect(rect.xMin, rect.yMin, rect.width, 1f));
            DrawSolidRect(new Rect(rect.xMin, rect.yMax - 1f, rect.width, 1f));
            DrawSolidRect(new Rect(rect.xMin, rect.yMin, 1f, rect.height));
            DrawSolidRect(new Rect(rect.xMax - 1f, rect.yMin, 1f, rect.height));
        }
        GUI.color = previous;
        GUI.Box(new Rect((Screen.width - 420f) * 0.5f, 12f, 420f, 30f),
            "点击要检查的文字, 按 Esc 取消");
    }

    private void DrawSelectedHighlight()
    {
        TextEntry entry = GetSelected();
        if (entry == null || entry.Mesh == null || !entry.Active) return;
        Renderer renderer = entry.Mesh.renderer;
        Rect rect;
        if (renderer == null || !TryGetScreenRect(renderer, out rect)) return;
        Color previous = GUI.color;
        GUI.color = Color.yellow;
        DrawSolidRect(new Rect(rect.xMin, rect.yMin, rect.width, 2f));
        DrawSolidRect(new Rect(rect.xMin, rect.yMax - 2f, rect.width, 2f));
        DrawSolidRect(new Rect(rect.xMin, rect.yMin, 2f, rect.height));
        DrawSolidRect(new Rect(rect.xMax - 2f, rect.yMin, 2f, rect.height));
        GUI.color = previous;
    }

    private void DrawSolidRect(Rect rect)
    {
        if (_highlightTexture != null) GUI.DrawTexture(rect, _highlightTexture);
    }

    private void OnDestroy()
    {
        CloseLevelCompletePreview();
        CloseRuntimeUiPreview();
        if (_dialogueRenderWaiting || _dialogueRenderRunning)
            StopDialogueRenderAudit("模组卸载");
        try { SaveSettings(); }
        catch { }
        if (_highlightTexture != null) UnityEngine.Object.Destroy(_highlightTexture);
        _highlightTexture = null;
    }

    private static bool TryGetScreenRect(Renderer renderer, out Rect rect)
    {
        rect = new Rect();
        Bounds bounds = renderer.bounds;
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;
        Camera[] cameras = Camera.allCameras;
        for (int cameraIndex = 0; cameraIndex < cameras.Length; cameraIndex++)
        {
            Camera camera = cameras[cameraIndex];
            if (camera == null || !camera.enabled || camera.gameObject == null ||
                !camera.gameObject.activeInHierarchy) continue;
            int layerMask = 1 << renderer.gameObject.layer;
            if ((camera.cullingMask & layerMask) == 0) continue;
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            int visibleCorners = 0;
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 world = center + new Vector3(extents.x * x, extents.y * y, extents.z * z);
                        Vector3 screen = camera.WorldToScreenPoint(world);
                        if (screen.z <= 0f) continue;
                        visibleCorners++;
                        minX = Mathf.Min(minX, screen.x);
                        minY = Mathf.Min(minY, screen.y);
                        maxX = Mathf.Max(maxX, screen.x);
                        maxY = Mathf.Max(maxY, screen.y);
                    }
            if (visibleCorners == 0 || maxX < 0f || minX > Screen.width || maxY < 0f || minY > Screen.height)
                continue;
            minX = Mathf.Clamp(minX, 0f, Screen.width);
            maxX = Mathf.Clamp(maxX, 0f, Screen.width);
            minY = Mathf.Clamp(minY, 0f, Screen.height);
            maxY = Mathf.Clamp(maxY, 0f, Screen.height);
            rect = new Rect(minX, Screen.height - maxY, Mathf.Max(2f, maxX - minX),
                Mathf.Max(2f, maxY - minY));
            return true;
        }
        return false;
    }

    private string GetLayoutIssue(TextEntry entry)
    {
        if (entry == null || !entry.Active || entry.Mesh == null) return string.Empty;
        TextMesh mesh = entry.Mesh;
        if (string.IsNullOrEmpty(mesh.text)) return string.Empty;
        if (mesh.characterSize <= 0f) return "字符尺寸无效";
        if (mesh.lineSpacing <= 0f) return "行距无效";
        Renderer renderer = mesh.renderer;
        if (renderer == null) return "没有渲染器";
        Rect textRect;
        if (!TryGetScreenRect(renderer, out textRect)) return "未投影到画面";
        if (textRect.width < 3f || textRect.height < 3f) return "显示尺寸过小";

        Transform current = mesh.transform.parent;
        int depth = 0;
        while (current != null && depth < 8)
        {
            Renderer parentRenderer = current.renderer;
            Rect parentRect;
            if (parentRenderer != null && TryGetScreenRect(parentRenderer, out parentRect) &&
                parentRect.width * parentRect.height > textRect.width * textRect.height * 1.1f)
            {
                float overflow = 0f;
                overflow = Mathf.Max(overflow, parentRect.xMin - textRect.xMin);
                overflow = Mathf.Max(overflow, textRect.xMax - parentRect.xMax);
                overflow = Mathf.Max(overflow, parentRect.yMin - textRect.yMin);
                overflow = Mathf.Max(overflow, textRect.yMax - parentRect.yMax);
                if (overflow > 3f) return "可能超出父容器";
                break;
            }
            current = current.parent;
            depth++;
        }
        return string.Empty;
    }

    private static void Offset(TextMesh mesh, float x, float y)
    {
        Vector3 value = mesh.transform.localPosition;
        mesh.transform.localPosition = new Vector3(value.x + x, value.y + y, value.z);
    }

    private void ScanTextMeshes()
    {
        if (_inventoryController == null) FindInventoryController();
        foreach (KeyValuePair<int, TextEntry> pair in _entries) pair.Value.Active = false;
        UnityEngine.Object[] objects = Resources.FindObjectsOfTypeAll(typeof(TextMesh));
        for (int i = 0; i < objects.Length; i++)
        {
            TextMesh mesh = objects[i] as TextMesh;
            if (mesh == null || mesh.gameObject == null || !mesh.gameObject.activeInHierarchy) continue;
            int id = mesh.GetInstanceID();
            TextEntry entry;
            if (!_entriesByInstanceId.TryGetValue(id, out entry) || !object.ReferenceEquals(entry.Mesh, mesh))
            {
                entry = CreateEntry(mesh);
                _entries[entry.Id] = entry;
                _entriesByInstanceId[id] = entry;
                AddEvent(entry, "discovered", null, mesh.text);
            }
            entry.Mesh = mesh;
            entry.Active = true;
            entry.Path = GetPath(mesh.transform);
            entry.LastScene = Application.loadedLevelName;
            entry.LastSeen = Time.realtimeSinceStartup;
            string observed = mesh.text;
            if (!string.Equals(entry.LastObservedText, observed, StringComparison.Ordinal))
            {
                AddEvent(entry, "rendered", entry.LastObservedText, observed);
                entry.ChangeCount++;
                entry.LastObservedText = observed;
            }
            string source;
            if (_pendingSources.TryGetValue(id, out source)) entry.SourceText = source;
            if (entry.SourceText != null && !string.Equals(mesh.text, entry.SourceText, StringComparison.Ordinal))
                entry.LocalizedText = mesh.text;
            entry.LastLayoutIssue = GetLayoutIssue(entry);
        }
    }

    private void FindInventoryController()
    {
        _inventoryController = null;
        UnityEngine.Object[] objects = Resources.FindObjectsOfTypeAll(typeof(MonoBehaviour));
        for (int i = 0; i < objects.Length; i++)
        {
            MonoBehaviour behaviour = objects[i] as MonoBehaviour;
            if (behaviour == null || behaviour.gameObject == null ||
                !behaviour.gameObject.activeInHierarchy) continue;
            if (!HasTypeInHierarchy(behaviour.GetType(), "InventoryNaviScript")) continue;
            if (FindInstanceMethod(behaviour.GetType(), "SetState") == null) continue;
            _inventoryController = behaviour;
            _status = "已找到背包控制器: " + behaviour.GetType().Name;
            return;
        }
    }

    private void InvokeInventoryState(string stateName)
    {
        if (_inventoryController == null) { _status = "背包控制器当前未激活"; return; }
        try
        {
            string currentState = GetInventoryStateName(_inventoryController);
            if (!string.Equals(currentState, "INVENTORY", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(currentState, "STANDBY", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(stateName, "INVENTORY", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(currentState, stateName, StringComparison.OrdinalIgnoreCase))
            {
                _status = "请先返回主页面, 再从" + TranslateInventoryState(currentState) +
                    "切换到" + TranslateInventoryState(stateName);
                return;
            }
            MethodInfo method = FindInstanceMethod(_inventoryController.GetType(), "SetState");
            if (method == null) throw new MissingMethodException("InventoryNaviScript.SetState");
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 1 || !parameters[0].ParameterType.IsEnum)
                throw new Exception("Unexpected SetState signature");
            object state = Enum.Parse(parameters[0].ParameterType, stateName, true);
            method.Invoke(_inventoryController, new object[] { state });
            _status = "背包状态 -> " + TranslateInventoryState(stateName);
            AddToolEvent("inventory-state", stateName);
            ScanTextMeshes();
        }
        catch (Exception ex)
        {
            Exception cause = ex.InnerException == null ? ex : ex.InnerException;
            _status = "状态切换失败: " + cause.Message;
            Debug.LogError("[LocalizationDebug] " + _status + "\n" + cause);
        }
    }

    private static string GetInventoryStateName(MonoBehaviour controller)
    {
        if (controller == null) return "<none>";
        FieldInfo field = FindInstanceField(controller.GetType(), "state");
        if (field == null) return "<unknown>";
        object value = field.GetValue(controller);
        return value == null ? "<null>" : value.ToString();
    }

    private static string TranslateInventoryState(string stateName)
    {
        if (string.Equals(stateName, "STANDBY", StringComparison.OrdinalIgnoreCase)) return "已关闭(STANDBY)";
        if (string.Equals(stateName, "INVENTORY", StringComparison.OrdinalIgnoreCase)) return "主页面(INVENTORY)";
        if (string.Equals(stateName, "ATTACHMENT", StringComparison.OrdinalIgnoreCase)) return "装配零件(ATTACHMENT)";
        if (string.Equals(stateName, "ABILITYPLACEMENT", StringComparison.OrdinalIgnoreCase)) return "分配技能方向(ABILITYPLACEMENT)";
        if (string.Equals(stateName, "ABILITIES", StringComparison.OrdinalIgnoreCase)) return "交换技能(ABILITIES)";
        if (string.Equals(stateName, "BREAKINTOBITS", StringComparison.OrdinalIgnoreCase)) return "分解零件(BREAKINTOBITS)";
        if (string.Equals(stateName, "STATS", StringComparison.OrdinalIgnoreCase)) return "部件属性(STATS)";
        return stateName;
    }

    private static bool HasTypeInHierarchy(Type type, string name)
    {
        Type current = type;
        while (current != null)
        {
            if (string.Equals(current.Name, name, StringComparison.Ordinal)) return true;
            current = current.BaseType;
        }
        return false;
    }

    private static MethodInfo FindInstanceMethod(Type type, string name)
    {
        Type current = type;
        while (current != null)
        {
            MethodInfo method = current.GetMethod(name, BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (method != null) return method;
            current = current.BaseType;
        }
        return null;
    }

    private static FieldInfo FindInstanceField(Type type, string name)
    {
        Type current = type;
        while (current != null)
        {
            FieldInfo field = current.GetField(name, BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) return field;
            current = current.BaseType;
        }
        return null;
    }

    private void AddToolEvent(string kind, string value)
    {
        TextEvent item = new TextEvent();
        item.Time = Time.realtimeSinceStartup;
        item.Scene = Application.loadedLevelName;
        item.Path = _inventoryController == null ? string.Empty : GetPath(_inventoryController.transform);
        item.Kind = kind;
        item.Before = string.Empty;
        item.After = value;
        _events.Add(item);
    }

    private TextEntry CreateEntry(TextMesh mesh)
    {
        TextEntry entry = new TextEntry();
        entry.Id = _nextEntryId++;
        entry.Mesh = mesh;
        entry.Path = GetPath(mesh.transform);
        entry.LastObservedText = mesh.text;
        string source;
        if (_pendingSources.TryGetValue(mesh.GetInstanceID(), out source)) entry.SourceText = source;
        entry.OriginalFont = mesh.font;
        entry.OriginalFontSize = mesh.fontSize;
        entry.OriginalCharacterSize = mesh.characterSize;
        entry.OriginalLineSpacing = mesh.lineSpacing;
        entry.OriginalLocalPosition = mesh.transform.localPosition;
        entry.OriginalAnchor = mesh.anchor;
        entry.OriginalAlignment = mesh.alignment;
        entry.FirstScene = Application.loadedLevelName;
        entry.LastScene = entry.FirstScene;
        entry.FirstSeen = Time.realtimeSinceStartup;
        entry.LastSeen = entry.FirstSeen;
        entry.Active = true;
        return entry;
    }

    private void BuildVisibleEntries()
    {
        _visible.Clear();
        foreach (KeyValuePair<int, TextEntry> pair in _entries)
        {
            TextEntry entry = pair.Value;
            if (_onlyActiveScene && (!entry.Active || entry.Mesh == null)) continue;
            string current = entry.Mesh == null ? entry.LastObservedText : entry.Mesh.text;
            if (_onlyUntranslated && !LooksUntranslated(entry, current)) continue;
            if (_onlyLayoutIssues && GetLayoutIssue(entry).Length == 0) continue;
            ReviewRecord review = FindReview(entry);
            if (_onlyUnreviewed && review != null &&
                (string.Equals(review.Status, "accepted", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(review.Status, "ignored", StringComparison.OrdinalIgnoreCase))) continue;
            if (_filter.Length > 0 && entry.Path.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                current.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                (entry.SourceText == null || entry.SourceText.IndexOf(_filter,
                    StringComparison.OrdinalIgnoreCase) < 0)) continue;
            _visible.Add(entry);
        }
        _visible.Sort(delegate(TextEntry a, TextEntry b) {
            return string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
        });
    }

    private bool LooksUntranslated(TextEntry entry, string current)
    {
        if (entry.SourceText != null && string.Equals(Normalize(current), Normalize(entry.SourceText),
            StringComparison.Ordinal)) return ContainsTranslatableLatin(current);
        return entry.SourceText == null && ContainsTranslatableLatin(current);
    }

    private bool ContainsTranslatableLatin(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        string normalized = Normalize(value).Trim();
        if (_untranslatedAllowlist.ContainsKey(normalized)) return false;

        StringBuilder token = new StringBuilder();
        for (int i = 0; i <= normalized.Length; i++)
        {
            char c = i < normalized.Length ? normalized[i] : ' ';
            bool tokenCharacter = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                (c >= '0' && c <= '9') || c == '-' || c == '.' || c == '/' || c == ':';
            if (tokenCharacter)
            {
                token.Append(c);
                continue;
            }
            if (token.Length == 0) continue;
            string candidate = token.ToString().Trim('-', '.', '/', ':');
            token.Length = 0;
            if (candidate.Length > 0 && ContainsLatin(candidate) &&
                !_untranslatedAllowlist.ContainsKey(candidate)) return true;
        }
        return false;
    }

    private static bool ContainsLatin(string value)
    {
        if (value == null) return false;
        for (int i = 0; i < value.Length; i++)
            if ((value[i] >= 'A' && value[i] <= 'Z') || (value[i] >= 'a' && value[i] <= 'z')) return true;
        return false;
    }

    private void LoadUntranslatedAllowlist()
    {
        _untranslatedAllowlist.Clear();
        string path = Path.Combine(_modDirectory, "untranslated-allowlist.txt");
        if (!File.Exists(path)) return;
        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
            _untranslatedAllowlist[Normalize(line).Trim()] = true;
        }
    }

    private void SelectEntry(TextEntry entry)
    {
        _selectedId = entry.Id;
        _customText = Escape(entry.Mesh == null ? entry.LastObservedText : entry.Mesh.text);
        ReviewRecord review = FindReview(entry);
        _reviewNote = review == null ? string.Empty : review.Note;
    }

    private void SelectVisibleRelative(int direction)
    {
        BuildVisibleEntries();
        if (_visible.Count == 0)
        {
            _status = "当前筛选条件下没有文本";
            return;
        }
        int current = -1;
        for (int i = 0; i < _visible.Count; i++)
            if (_visible[i].Id == _selectedId) { current = i; break; }
        int next = current < 0 ? (direction > 0 ? 0 : _visible.Count - 1) : current + direction;
        if (next < 0) next = _visible.Count - 1;
        if (next >= _visible.Count) next = 0;
        SelectEntry(_visible[next]);
        _status = "已选择第" + (next + 1) + "/" + _visible.Count + "条";
    }

    private static string ReviewKey(TextEntry entry)
    {
        if (entry == null) return string.Empty;
        string source = entry.SourceText;
        if (string.IsNullOrEmpty(source)) source = entry.LastObservedText;
        return PresetPath(entry.Path) + "\n" + Normalize(source);
    }

    private ReviewRecord FindReview(TextEntry entry)
    {
        ReviewRecord record;
        return _reviews.TryGetValue(ReviewKey(entry), out record) ? record : null;
    }

    private string GetReviewMarker(TextEntry entry)
    {
        ReviewRecord record = FindReview(entry);
        if (record == null) return "[未] ";
        if (string.Equals(record.Status, "accepted", StringComparison.OrdinalIgnoreCase)) return "[验] ";
        if (string.Equals(record.Status, "ignored", StringComparison.OrdinalIgnoreCase)) return "[略] ";
        return "[待] ";
    }

    private static string TranslateReviewStatus(string status)
    {
        if (string.Equals(status, "accepted", StringComparison.OrdinalIgnoreCase)) return "验收通过";
        if (string.Equals(status, "ignored", StringComparison.OrdinalIgnoreCase)) return "已忽略";
        if (string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase)) return "待处理";
        return "未复核";
    }

    private void SetReviewStatus(TextEntry entry, string status)
    {
        ReviewRecord record = FindReview(entry);
        if (record == null)
        {
            record = CreateReview(entry);
            _reviews[ReviewKey(entry)] = record;
        }
        record.Status = status;
        record.Note = _reviewNote;
        record.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        WriteReviews(Path.Combine(_modDirectory, "review-status.tsv"));
        _status = "复核状态已更新: " + TranslateReviewStatus(status);
    }

    private void SaveReviewNote(TextEntry entry)
    {
        ReviewRecord record = FindReview(entry);
        if (record == null)
        {
            record = CreateReview(entry);
            record.Status = "pending";
            _reviews[ReviewKey(entry)] = record;
        }
        record.Note = _reviewNote;
        record.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        WriteReviews(Path.Combine(_modDirectory, "review-status.tsv"));
        _status = "复核备注已保存";
    }

    private void ClearReview(TextEntry entry)
    {
        _reviews.Remove(ReviewKey(entry));
        _reviewNote = string.Empty;
        WriteReviews(Path.Combine(_modDirectory, "review-status.tsv"));
        _status = "复核标记已清除";
    }

    private static ReviewRecord CreateReview(TextEntry entry)
    {
        ReviewRecord record = new ReviewRecord();
        record.Path = PresetPath(entry.Path);
        record.Source = string.IsNullOrEmpty(entry.SourceText) ? entry.LastObservedText : entry.SourceText;
        record.Status = "pending";
        record.Note = string.Empty;
        record.UpdatedAt = string.Empty;
        return record;
    }

    private TextEntry GetSelected()
    {
        TextEntry entry;
        return _entries.TryGetValue(_selectedId, out entry) ? entry : null;
    }

    private void AddEvent(TextEntry entry, string kind, string before, string after)
    {
        if (_events.Count >= 50000) _events.RemoveAt(0);
        TextEvent item = new TextEvent();
        item.Time = Time.realtimeSinceStartup;
        item.Scene = Application.loadedLevelName;
        item.Path = entry == null ? string.Empty : entry.Path;
        item.Kind = kind;
        item.Before = before;
        item.After = after;
        _events.Add(item);
    }

    private void ClearSession()
    {
        _entries.Clear();
        _entriesByInstanceId.Clear();
        _pendingSources.Clear();
        _visible.Clear();
        _events.Clear();
        _selectedId = -1;
        _nextEntryId = 1;
        _status = "会话记录已清空";
        ScanTextMeshes();
    }

    private static void RestoreMetrics(TextEntry entry)
    {
        if (entry.Mesh == null) return;
        entry.Mesh.font = entry.OriginalFont;
        entry.Mesh.fontSize = entry.OriginalFontSize;
        entry.Mesh.characterSize = entry.OriginalCharacterSize;
        entry.Mesh.lineSpacing = entry.OriginalLineSpacing;
        entry.Mesh.transform.localPosition = entry.OriginalLocalPosition;
        entry.Mesh.anchor = entry.OriginalAnchor;
        entry.Mesh.alignment = entry.OriginalAlignment;
    }

    private void ExportBundle()
    {
        try
        {
            RunSelfCheck();
            AuditDialogues();
            string directory = Path.Combine(_modDirectory, "captures");
            Directory.CreateDirectory(directory);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            string snapshotPath = Path.Combine(directory, "active-" + stamp + ".tsv");
            string sessionPath = Path.Combine(directory, "session-" + stamp + ".tsv");
            string eventsPath = Path.Combine(directory, "events-" + stamp + ".tsv");
            string scenesPath = Path.Combine(directory, "scenes-" + stamp + ".tsv");
            string reviewsPath = Path.Combine(directory, "reviews-" + stamp + ".tsv");
            string uniqueTextsPath = Path.Combine(directory, "unique-texts-" + stamp + ".tsv");
            string layoutIssuesPath = Path.Combine(directory, "layout-issues-" + stamp + ".tsv");
            string dialogueAuditPath = Path.Combine(directory, "dialogue-audit-" + stamp + ".tsv");
            string summaryPath = Path.Combine(directory, "summary-" + stamp + ".txt");
            using (StreamWriter writer = new StreamWriter(snapshotPath, false, new UTF8Encoding(true)))
            {
                WriteEntryHeader(writer);
                foreach (KeyValuePair<int, TextEntry> pair in _entries)
                    if (pair.Value.Active) WriteEntry(writer, pair.Value);
            }
            using (StreamWriter writer = new StreamWriter(sessionPath, false, new UTF8Encoding(true)))
            {
                WriteEntryHeader(writer);
                foreach (KeyValuePair<int, TextEntry> pair in _entries) WriteEntry(writer, pair.Value);
            }
            using (StreamWriter writer = new StreamWriter(eventsPath, false, new UTF8Encoding(true)))
            {
                writer.WriteLine("time\tscene\tpath\tkind\tbefore\tafter");
                for (int i = 0; i < _events.Count; i++)
                {
                    TextEvent item = _events[i];
                    writer.Write(F(item.Time)); writer.Write('\t');
                    writer.Write(Tsv(item.Scene)); writer.Write('\t');
                    writer.Write(Tsv(item.Path)); writer.Write('\t');
                    writer.Write(Tsv(item.Kind)); writer.Write('\t');
                    writer.Write(Tsv(item.Before)); writer.Write('\t');
                    writer.WriteLine(Tsv(item.After));
                }
            }
            WriteSceneCatalog(scenesPath);
            WriteReviews(reviewsPath);
            WriteUniqueTexts(uniqueTextsPath);
            WriteLayoutIssues(layoutIssuesPath);
            WriteDialogueAudit(dialogueAuditPath);
            WriteAuditSummary(summaryPath, stamp);
            _status = "已导出数据包: " + stamp;
            Debug.Log("[LocalizationDebug] " + _status);
        }
        catch (Exception ex)
        {
            _status = "导出失败: " + ex.Message;
            Debug.LogError("[LocalizationDebug] " + ex);
        }
    }

    private static void WriteEntryHeader(StreamWriter writer)
    {
        writer.WriteLine("active\tfirstScene\tlastScene\tfirstSeen\tlastSeen\tchanges\tlayoutIssue\tpath\tsource\tcurrent\tfont\tfontSize\tcharacterSize\tlineSpacing\tlocalPosition\tanchor\talignment\tboundsCenter\tboundsSize");
    }

    private void WriteDialogueAudit(string path)
    {
        using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true)))
        {
            writer.WriteLine("groupId\tlineId\truntimeStatus\tissue\tsource\ttranslation");
            for (int i = 0; i < _dialogueAuditRows.Count; i++)
            {
                DialogueAuditRow row = _dialogueAuditRows[i];
                writer.Write(row.GroupId); writer.Write('\t');
                writer.Write(row.LineId); writer.Write('\t');
                writer.Write(Tsv(row.RuntimeStatus)); writer.Write('\t');
                writer.Write(Tsv(row.Issue)); writer.Write('\t');
                writer.Write(Tsv(row.Source)); writer.Write('\t');
                writer.WriteLine(Tsv(row.Translation));
            }
        }
    }

    private void WriteUniqueTexts(string path)
    {
        Dictionary<string, TextAggregate> aggregates = new Dictionary<string, TextAggregate>();
        foreach (KeyValuePair<int, TextEntry> pair in _entries)
        {
            TextEntry entry = pair.Value;
            string current = entry.Mesh == null ? entry.LastObservedText : entry.Mesh.text;
            string source = string.IsNullOrEmpty(entry.SourceText) ? current : entry.SourceText;
            string key = Normalize(source);
            TextAggregate aggregate;
            if (!aggregates.TryGetValue(key, out aggregate))
            {
                aggregate = new TextAggregate();
                aggregate.Source = source;
                aggregate.CurrentExample = current;
                aggregates[key] = aggregate;
            }
            aggregate.Occurrences++;
            if (entry.Active) aggregate.ActiveOccurrences++;
            if (LooksUntranslated(entry, current)) aggregate.UntranslatedOccurrences++;
            if (!string.IsNullOrEmpty(entry.FirstScene)) aggregate.Scenes[entry.FirstScene] = true;
            if (!string.IsNullOrEmpty(entry.LastScene)) aggregate.Scenes[entry.LastScene] = true;
            if (!string.IsNullOrEmpty(entry.Path)) aggregate.Paths[PresetPath(entry.Path)] = true;
            ReviewRecord review = FindReview(entry);
            if (review == null || string.Equals(review.Status, "pending", StringComparison.OrdinalIgnoreCase))
                aggregate.PendingOccurrences++;
            else if (string.Equals(review.Status, "accepted", StringComparison.OrdinalIgnoreCase))
                aggregate.AcceptedOccurrences++;
            else if (string.Equals(review.Status, "ignored", StringComparison.OrdinalIgnoreCase))
                aggregate.IgnoredOccurrences++;
        }

        List<string> keys = new List<string>(aggregates.Keys);
        keys.Sort(StringComparer.Ordinal);
        using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true)))
        {
            writer.WriteLine("source\tcurrentExample\toccurrences\tactive\tuntranslated\taccepted\tpending\tignored\tscenes\tpaths");
            for (int i = 0; i < keys.Count; i++)
            {
                TextAggregate aggregate = aggregates[keys[i]];
                writer.Write(Tsv(aggregate.Source)); writer.Write('\t');
                writer.Write(Tsv(aggregate.CurrentExample)); writer.Write('\t');
                writer.Write(aggregate.Occurrences); writer.Write('\t');
                writer.Write(aggregate.ActiveOccurrences); writer.Write('\t');
                writer.Write(aggregate.UntranslatedOccurrences); writer.Write('\t');
                writer.Write(aggregate.AcceptedOccurrences); writer.Write('\t');
                writer.Write(aggregate.PendingOccurrences); writer.Write('\t');
                writer.Write(aggregate.IgnoredOccurrences); writer.Write('\t');
                writer.Write(Tsv(JoinKeys(aggregate.Scenes))); writer.Write('\t');
                writer.WriteLine(Tsv(JoinKeys(aggregate.Paths)));
            }
        }
    }

    private void WriteLayoutIssues(string path)
    {
        using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true)))
        {
            writer.WriteLine("active\tscene\tpath\tissue\tsource\tcurrent\tcharacterSize\tlineSpacing\tlocalPosition\tboundsCenter\tboundsSize");
            foreach (KeyValuePair<int, TextEntry> pair in _entries)
            {
                TextEntry entry = pair.Value;
                string issue = entry.Active ? GetLayoutIssue(entry) : entry.LastLayoutIssue;
                if (issue.Length == 0) continue;
                TextMesh mesh = entry.Mesh;
                Renderer renderer = mesh == null ? null : mesh.renderer;
                writer.Write(entry.Active ? "1" : "0"); writer.Write('\t');
                writer.Write(Tsv(entry.LastScene)); writer.Write('\t');
                writer.Write(Tsv(entry.Path)); writer.Write('\t');
                writer.Write(Tsv(issue)); writer.Write('\t');
                writer.Write(Tsv(entry.SourceText)); writer.Write('\t');
                writer.Write(Tsv(mesh == null ? entry.LastObservedText : mesh.text)); writer.Write('\t');
                writer.Write(F(mesh == null ? entry.OriginalCharacterSize : mesh.characterSize)); writer.Write('\t');
                writer.Write(F(mesh == null ? entry.OriginalLineSpacing : mesh.lineSpacing)); writer.Write('\t');
                writer.Write(mesh == null ? V(entry.OriginalLocalPosition) : V(mesh.transform.localPosition)); writer.Write('\t');
                writer.Write(renderer == null ? string.Empty : V(renderer.bounds.center)); writer.Write('\t');
                writer.WriteLine(renderer == null ? string.Empty : V(renderer.bounds.size));
            }
        }
    }

    private void WriteAuditSummary(string path, string stamp)
    {
        Dictionary<string, bool> uniqueSources = new Dictionary<string, bool>();
        int active = 0;
        int untranslated = 0;
        int layoutIssues = 0;
        foreach (KeyValuePair<int, TextEntry> pair in _entries)
        {
            TextEntry entry = pair.Value;
            string current = entry.Mesh == null ? entry.LastObservedText : entry.Mesh.text;
            string source = string.IsNullOrEmpty(entry.SourceText) ? current : entry.SourceText;
            uniqueSources[Normalize(source)] = true;
            if (entry.Active) active++;
            if (LooksUntranslated(entry, current)) untranslated++;
            if (!string.IsNullOrEmpty(entry.LastLayoutIssue)) layoutIssues++;
        }
        int accepted = 0;
        int pending = 0;
        int ignored = 0;
        foreach (KeyValuePair<string, ReviewRecord> pair in _reviews)
        {
            if (string.Equals(pair.Value.Status, "accepted", StringComparison.OrdinalIgnoreCase)) accepted++;
            else if (string.Equals(pair.Value.Status, "ignored", StringComparison.OrdinalIgnoreCase)) ignored++;
            else pending++;
        }
        using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true)))
        {
            writer.WriteLine("PunchLoader 汉化调试验收摘要");
            writer.WriteLine("版本: LocalizationDebug 1.5.0");
            writer.WriteLine("导出批次: " + stamp);
            writer.WriteLine("当前场景: " + Application.loadedLevelName + " (#" + Application.loadedLevel + ")");
            writer.WriteLine("已访问场景: " + _sceneCatalog.Count + "/" + Application.levelCount);
            writer.WriteLine("会话文本对象: " + _entries.Count);
            writer.WriteLine("当前活动对象: " + active);
            writer.WriteLine("去重原文数量: " + uniqueSources.Count);
            writer.WriteLine("疑似未汉化对象: " + untranslated);
            writer.WriteLine("记录到布局异常: " + layoutIssues);
            writer.WriteLine("复核通过: " + accepted);
            writer.WriteLine("待处理: " + pending);
            writer.WriteLine("已忽略: " + ignored);
            writer.WriteLine("文本变化事件: " + _events.Count);
            writer.WriteLine("保留英文名词: " + _untranslatedAllowlist.Count);
            writer.WriteLine("对话译文行数: " + _dialogueAuditRows.Count);
            writer.WriteLine("运行时已应用对话: " + _dialogueAuditApplied);
            writer.WriteLine("对话巡检问题: " + _dialogueAuditProblems);
        }
    }

    private void LoadSettings()
    {
        string path = Path.Combine(_modDirectory, "debug-settings.tsv");
        if (!File.Exists(path)) return;
        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        for (int i = 1; i < lines.Length; i++)
        {
            string[] values = lines[i].Split('\t');
            if (values.Length < 2) continue;
            string key = values[0];
            string value = values[1];
            float number;
            bool flag;
            if (key == "windowX" && TryF(value, out number)) _windowRect.x = number;
            else if (key == "windowY" && TryF(value, out number)) _windowRect.y = number;
            else if (key == "windowWidth" && TryF(value, out number)) _windowRect.width = number;
            else if (key == "windowHeight" && TryF(value, out number)) _windowRect.height = number;
            else if (key == "step" && TryF(value, out number) && number > 0f) _step = number;
            else if (key == "onlyUntranslated" && bool.TryParse(value, out flag)) _onlyUntranslated = flag;
            else if (key == "onlyUnreviewed" && bool.TryParse(value, out flag)) _onlyUnreviewed = flag;
            else if (key == "onlyLayoutIssues" && bool.TryParse(value, out flag)) _onlyLayoutIssues = flag;
            else if (key == "onlyActiveScene" && bool.TryParse(value, out flag)) _onlyActiveScene = flag;
            else if (key == "autoApplyPresets" && bool.TryParse(value, out flag)) _autoApplyPresets = flag;
            else if (key == "highlightSelected" && bool.TryParse(value, out flag)) _highlightSelected = flag;
        }
    }

    private void SaveSettings()
    {
        if (string.IsNullOrEmpty(_modDirectory)) return;
        string path = Path.Combine(_modDirectory, "debug-settings.tsv");
        using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true)))
        {
            writer.WriteLine("key\tvalue");
            writer.WriteLine("windowX\t" + F(_windowRect.x));
            writer.WriteLine("windowY\t" + F(_windowRect.y));
            writer.WriteLine("windowWidth\t" + F(_windowRect.width));
            writer.WriteLine("windowHeight\t" + F(_windowRect.height));
            writer.WriteLine("step\t" + F(_step));
            writer.WriteLine("onlyUntranslated\t" + _onlyUntranslated);
            writer.WriteLine("onlyUnreviewed\t" + _onlyUnreviewed);
            writer.WriteLine("onlyLayoutIssues\t" + _onlyLayoutIssues);
            writer.WriteLine("onlyActiveScene\t" + _onlyActiveScene);
            writer.WriteLine("autoApplyPresets\t" + _autoApplyPresets);
            writer.WriteLine("highlightSelected\t" + _highlightSelected);
        }
        _status = "调试设置已保存";
    }

    private void ResetSettings()
    {
        _windowRect = new Rect(18f, 18f, 820f, 690f);
        _step = 0.01f;
        _onlyUntranslated = true;
        _onlyUnreviewed = false;
        _onlyLayoutIssues = false;
        _onlyActiveScene = true;
        _autoApplyPresets = true;
        _highlightSelected = true;
        SaveSettings();
        _status = "已恢复并保存默认设置";
    }

    private static string JoinKeys(Dictionary<string, bool> values)
    {
        List<string> keys = new List<string>(values.Keys);
        keys.Sort(StringComparer.OrdinalIgnoreCase);
        StringBuilder result = new StringBuilder();
        for (int i = 0; i < keys.Count; i++)
        {
            if (i > 0) result.Append(" | ");
            result.Append(keys[i]);
        }
        return result.ToString();
    }

    private static void WriteEntry(StreamWriter writer, TextEntry entry)
    {
        TextMesh mesh = entry.Mesh;
        Renderer renderer = mesh == null ? null : mesh.renderer;
        string current = mesh == null ? entry.LastObservedText : mesh.text;
        Font font = mesh == null ? entry.OriginalFont : mesh.font;
        int fontSize = mesh == null ? entry.OriginalFontSize : mesh.fontSize;
        float characterSize = mesh == null ? entry.OriginalCharacterSize : mesh.characterSize;
        float lineSpacing = mesh == null ? entry.OriginalLineSpacing : mesh.lineSpacing;
        Vector3 localPosition = mesh == null ? entry.OriginalLocalPosition : mesh.transform.localPosition;
        TextAnchor anchor = mesh == null ? entry.OriginalAnchor : mesh.anchor;
        TextAlignment alignment = mesh == null ? entry.OriginalAlignment : mesh.alignment;
        writer.Write(entry.Active ? "1" : "0"); writer.Write('\t');
        writer.Write(Tsv(entry.FirstScene)); writer.Write('\t');
        writer.Write(Tsv(entry.LastScene)); writer.Write('\t');
        writer.Write(F(entry.FirstSeen)); writer.Write('\t');
        writer.Write(F(entry.LastSeen)); writer.Write('\t');
        writer.Write(entry.ChangeCount); writer.Write('\t');
        writer.Write(Tsv(entry.LastLayoutIssue)); writer.Write('\t');
        writer.Write(Tsv(entry.Path)); writer.Write('\t');
        writer.Write(Tsv(entry.SourceText)); writer.Write('\t');
        writer.Write(Tsv(current)); writer.Write('\t');
        writer.Write(Tsv(font == null ? string.Empty : font.name)); writer.Write('\t');
        writer.Write(fontSize); writer.Write('\t');
        writer.Write(F(characterSize)); writer.Write('\t');
        writer.Write(F(lineSpacing)); writer.Write('\t');
        writer.Write(V(localPosition)); writer.Write('\t');
        writer.Write(anchor); writer.Write('\t');
        writer.Write(alignment); writer.Write('\t');
        writer.Write(renderer == null ? string.Empty : V(renderer.bounds.center)); writer.Write('\t');
        writer.WriteLine(renderer == null ? string.Empty : V(renderer.bounds.size));
    }

    private void LoadReviews()
    {
        _reviews.Clear();
        string path = Path.Combine(_modDirectory, "review-status.tsv");
        if (!File.Exists(path)) return;
        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        for (int i = 1; i < lines.Length; i++)
        {
            string[] values = lines[i].Split('\t');
            if (values.Length < 5) continue;
            ReviewRecord record = new ReviewRecord();
            record.Path = values[0];
            record.Source = values[1].Replace("\\n", "\n");
            record.Status = values[2];
            record.Note = values[3].Replace("\\n", "\n");
            record.UpdatedAt = values[4];
            _reviews[record.Path + "\n" + Normalize(record.Source)] = record;
        }
    }

    private void WriteReviews(string path)
    {
        List<ReviewRecord> records = new List<ReviewRecord>(_reviews.Values);
        records.Sort(delegate(ReviewRecord a, ReviewRecord b) {
            int pathOrder = string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
            return pathOrder != 0 ? pathOrder : string.Compare(a.Source, b.Source, StringComparison.Ordinal);
        });
        using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true)))
        {
            writer.WriteLine("path\tsource\tstatus\tnote\tupdatedAt");
            for (int i = 0; i < records.Count; i++)
            {
                ReviewRecord record = records[i];
                writer.Write(Tsv(record.Path)); writer.Write('\t');
                writer.Write(Tsv(record.Source)); writer.Write('\t');
                writer.Write(Tsv(record.Status)); writer.Write('\t');
                writer.Write(Tsv(record.Note)); writer.Write('\t');
                writer.WriteLine(Tsv(record.UpdatedAt));
            }
        }
    }

    private void LoadPresets()
    {
        _presets.Clear();
        string path = Path.Combine(_modDirectory, "layout-presets.tsv");
        if (!File.Exists(path)) return;
        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        for (int i = 1; i < lines.Length; i++)
        {
            string[] values = lines[i].Split('\t');
            if (values.Length != 9) continue;
            float characterSize, lineSpacing, x, y, z;
            if (!TryF(values[2], out characterSize) || !TryF(values[3], out lineSpacing) ||
                !TryF(values[4], out x) || !TryF(values[5], out y) || !TryF(values[6], out z)) continue;
            try
            {
                LayoutPreset preset = new LayoutPreset();
                preset.PathKey = values[0];
                preset.SourceKey = values[1].Replace("\\n", "\n");
                preset.CharacterSize = characterSize;
                preset.LineSpacing = lineSpacing;
                preset.LocalPosition = new Vector3(x, y, z);
                preset.Anchor = (TextAnchor)Enum.Parse(typeof(TextAnchor), values[7], true);
                preset.Alignment = (TextAlignment)Enum.Parse(typeof(TextAlignment), values[8], true);
                _presets.Add(preset);
            }
            catch { }
        }
    }

    private void SavePreset(TextEntry entry)
    {
        if (entry.Mesh == null) return;
        LayoutPreset preset = FindPreset(entry);
        if (preset == null)
        {
            preset = new LayoutPreset();
            preset.PathKey = PresetPath(entry.Path);
            preset.SourceKey = Normalize(entry.SourceText);
            _presets.Add(preset);
        }
        preset.CharacterSize = entry.Mesh.characterSize;
        preset.LineSpacing = entry.Mesh.lineSpacing;
        preset.LocalPosition = entry.Mesh.transform.localPosition;
        preset.Anchor = entry.Mesh.anchor;
        preset.Alignment = entry.Mesh.alignment;
        WritePresets();
        _status = "布局预设已保存";
    }

    private void DeletePreset(TextEntry entry)
    {
        LayoutPreset preset = FindPreset(entry);
        if (preset == null) { _status = "没有匹配的布局预设"; return; }
        _presets.Remove(preset);
        WritePresets();
        _status = "布局预设已删除";
    }

    private void ApplyMatchingPreset(TextEntry entry)
    {
        if (entry.Mesh == null) return;
        LayoutPreset preset = FindPreset(entry);
        if (preset == null) return;
        entry.Mesh.characterSize = preset.CharacterSize;
        entry.Mesh.lineSpacing = preset.LineSpacing;
        entry.Mesh.transform.localPosition = preset.LocalPosition;
        entry.Mesh.anchor = preset.Anchor;
        entry.Mesh.alignment = preset.Alignment;
    }

    private LayoutPreset FindPreset(TextEntry entry)
    {
        string path = PresetPath(entry.Path);
        string source = Normalize(entry.SourceText);
        for (int i = 0; i < _presets.Count; i++)
            if (string.Equals(_presets[i].PathKey, path, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_presets[i].SourceKey, source, StringComparison.Ordinal)) return _presets[i];
        return null;
    }

    private void WritePresets()
    {
        string path = Path.Combine(_modDirectory, "layout-presets.tsv");
        using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true)))
        {
            writer.WriteLine("path\tsource\tcharacterSize\tlineSpacing\tx\ty\tz\tanchor\talignment");
            for (int i = 0; i < _presets.Count; i++)
            {
                LayoutPreset preset = _presets[i];
                writer.Write(Tsv(preset.PathKey)); writer.Write('\t');
                writer.Write(Tsv(preset.SourceKey)); writer.Write('\t');
                writer.Write(F(preset.CharacterSize)); writer.Write('\t');
                writer.Write(F(preset.LineSpacing)); writer.Write('\t');
                writer.Write(F(preset.LocalPosition.x)); writer.Write('\t');
                writer.Write(F(preset.LocalPosition.y)); writer.Write('\t');
                writer.Write(F(preset.LocalPosition.z)); writer.Write('\t');
                writer.Write(preset.Anchor); writer.Write('\t');
                writer.WriteLine(preset.Alignment);
            }
        }
    }

    private static string PresetPath(string path)
    {
        return path == null ? string.Empty : path.Replace("(Clone)", string.Empty);
    }

    private static bool TryF(string text, out float value)
    {
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private void CopyReport(TextEntry entry)
    {
        try
        {
            TextEditor editor = new TextEditor();
            editor.content = new GUIContent(BuildReport(entry));
            editor.SelectAll();
            editor.Copy();
            _status = "所选文本报告已复制";
        }
        catch (Exception ex) { _status = "复制失败: " + ex.Message; }
    }

    private static string BuildReport(TextEntry entry)
    {
        if (entry.Mesh == null) return string.Empty;
        TextMesh mesh = entry.Mesh;
        return "Scene=" + Application.loadedLevelName + "\nPath=" + entry.Path +
            "\nSource=" + Escape(entry.SourceText) + "\nCurrent=" + Escape(mesh.text) +
            "\nFont=" + (mesh.font == null ? "<null>" : mesh.font.name) +
            "\nfontSize=" + mesh.fontSize + " characterSize=" + F(mesh.characterSize) +
            " lineSpacing=" + F(mesh.lineSpacing) + "\nlocalPosition=" + V(mesh.transform.localPosition) +
            " anchor=" + mesh.anchor + " alignment=" + mesh.alignment;
    }

    private static string GetPath(Transform transform)
    {
        if (transform == null) return string.Empty;
        StringBuilder result = new StringBuilder(transform.gameObject.name);
        Transform current = transform.parent;
        while (current != null)
        {
            result.Insert(0, current.gameObject.name + "/");
            current = current.parent;
        }
        return result.ToString();
    }

    private static string Normalize(string value)
    {
        return value == null ? string.Empty : value.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
    }

    private static string Escape(string value)
    {
        return value == null ? string.Empty : value.Replace("\r", string.Empty).Replace("\n", "\\n");
    }

    private static string SingleLine(string value)
    {
        return Escape(value).Replace("\t", " ");
    }

    private static string Tsv(string value)
    {
        return Escape(value).Replace("\t", " ");
    }

    private static string F(float value)
    {
        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string V(Vector3 value)
    {
        return F(value.x) + "," + F(value.y) + "," + F(value.z);
    }
}
