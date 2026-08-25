using System;
using System.Collections;
using System.Reflection;
using PunchLoader;
using UnityEngine;

public sealed class ContentUnlockerPlugin : IModPlugin
{
    private static ContentUnlockerBehaviour _behaviour;

    public string GetId() { return "ContentUnlocker"; }
    public string GetName() { return "Content Unlocker"; }
    public string GetVersion() { return "0.1.0"; }

    public void OnLoad()
    {
        if (_behaviour != null) return;
        GameObject host = new GameObject("PunchLoader.ContentUnlocker");
        UnityEngine.Object.DontDestroyOnLoad(host);
        _behaviour = (ContentUnlockerBehaviour)host.AddComponent(
            typeof(ContentUnlockerBehaviour));
        Debug.Log("[ContentUnlocker] Loaded: 14 hidden colors will be added to the color repository.");
    }

    public void OnUnload()
    {
        if (_behaviour != null) UnityEngine.Object.Destroy(_behaviour.gameObject);
        _behaviour = null;
    }
}

public sealed class ContentUnlockerBehaviour : MonoBehaviour
{
    private sealed class HiddenColor
    {
        public string Name;
        public string MaterialResource;

        public HiddenColor(string name, string materialResource)
        {
            Name = name;
            MaterialResource = materialResource;
        }
    }

    private static readonly HiddenColor[] HiddenColors = new HiddenColor[] {
        new HiddenColor("Army Green Yellow", "ArmyGreenYellowMat"),
        new HiddenColor("Beige Bordeaux", "beigebordeauxMat"),
        new HiddenColor("Beige Dark Orange", "beigedarkorangedarkMat"),
        new HiddenColor("Brown Orange", "brownorangeMat"),
        new HiddenColor("Cyber Bone", "CyberBone"),
        new HiddenColor("Drill Ability", "DrillAbilityColor"),
        new HiddenColor("Final Boss", "FinalBossMat"),
        new HiddenColor("Green Blue", "greenblueMat"),
        new HiddenColor("Light Green Beige", "lightgreenbeigeMat"),
        new HiddenColor("Orange Yellow 2", "orangeyellow2Mat"),
        new HiddenColor("Pink Brown", "pinkbrownMat"),
        new HiddenColor("Purple Light Blue", "purplelightblueMat"),
        new HiddenColor("Salmon Purple", "salmonpurpleMat"),
        new HiddenColor("Turquoise Beige", "turqoisebeigeMat")
    };

    private Type _colorGuiType;
    private Type _colorEntryType;
    private FieldInfo _colorsField;
    private FieldInfo _maxPagesField;
    private FieldInfo _entryNameField;
    private FieldInfo _entryMaterialField;
    private MethodInfo _listMethod;
    private float _nextScan;
    private bool _reportedReady;

    private void Update()
    {
        if (Time.realtimeSinceStartup < _nextScan) return;
        _nextScan = Time.realtimeSinceStartup + 0.25f;

        if (!ResolveGameTypes()) return;
        UnityEngine.Object[] objects = Resources.FindObjectsOfTypeAll(_colorGuiType);
        for (int i = 0; i < objects.Length; i++)
        {
            Component gui = objects[i] as Component;
            if (gui == null || gui.gameObject == null || !gui.gameObject.activeInHierarchy)
                continue;
            ExtendColorRepository(gui);
        }
    }

    private bool ResolveGameTypes()
    {
        if (_colorGuiType != null) return true;
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length && _colorGuiType == null; i++)
            _colorGuiType = assemblies[i].GetType("ColorGUIScript", false);
        if (_colorGuiType == null) return false;

        _colorEntryType = _colorGuiType.GetNestedType("ColorEntry",
            BindingFlags.Public | BindingFlags.NonPublic);
        _colorsField = _colorGuiType.GetField("colors",
            BindingFlags.Instance | BindingFlags.NonPublic);
        _maxPagesField = FindField(_colorGuiType, "maxPages");
        _listMethod = _colorGuiType.GetMethod("List",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (_colorEntryType != null)
        {
            _entryNameField = _colorEntryType.GetField("name",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _entryMaterialField = _colorEntryType.GetField("mat",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }
        return _colorEntryType != null && _colorsField != null &&
            _maxPagesField != null && _listMethod != null &&
            _entryNameField != null && _entryMaterialField != null;
    }

    private void ExtendColorRepository(Component gui)
    {
        ArrayList colors = _colorsField.GetValue(gui) as ArrayList;
        if (colors == null) return; // Init() has not run yet.

        int added = 0;
        for (int i = 0; i < HiddenColors.Length; i++)
        {
            HiddenColor hidden = HiddenColors[i];
            if (ContainsColor(colors, hidden.Name)) continue;

            Material material = Resources.Load(
                "Parts/PartPalets/" + hidden.MaterialResource,
                typeof(Material)) as Material;
            if (material == null)
            {
                Debug.LogWarning("[ContentUnlocker] Missing hidden color material: " +
                    hidden.MaterialResource);
                continue;
            }

            object entry = Activator.CreateInstance(_colorEntryType);
            _entryNameField.SetValue(entry, hidden.Name);
            _entryMaterialField.SetValue(entry, material);
            colors.Add(entry);
            added++;
        }

        int pages = Math.Max(1, (colors.Count + 9) / 10);
        _maxPagesField.SetValue(gui, pages);
        if (added <= 0) return;

        try
        {
            _listMethod.Invoke(gui, null);
            Debug.Log("[ContentUnlocker] Added " + added +
                " hidden colors; repository now has " + colors.Count +
                " entries across " + pages + " pages.");
            _reportedReady = true;
        }
        catch (Exception ex)
        {
            Debug.LogError("[ContentUnlocker] Failed to refresh color repository: " +
                ex.GetBaseException().Message);
        }
    }

    private bool ContainsColor(ArrayList colors, string name)
    {
        for (int i = 0; i < colors.Count; i++)
        {
            object entry = colors[i];
            if (entry == null || !_colorEntryType.IsInstanceOfType(entry)) continue;
            string current = _entryNameField.GetValue(entry) as string;
            if (string.Equals(current, name, StringComparison.Ordinal)) return true;
        }
        return false;
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

    private void OnDestroy()
    {
        RemoveHiddenColorsFromOpenRepositories();
        if (_reportedReady) Debug.Log("[ContentUnlocker] Unloaded and removed hidden colors from open repositories.");
    }

    private void RemoveHiddenColorsFromOpenRepositories()
    {
        if (!ResolveGameTypes()) return;
        UnityEngine.Object[] objects = Resources.FindObjectsOfTypeAll(_colorGuiType);
        for (int i = 0; i < objects.Length; i++)
        {
            Component gui = objects[i] as Component;
            if (gui == null) continue;
            ArrayList colors = _colorsField.GetValue(gui) as ArrayList;
            if (colors == null) continue;

            bool changed = false;
            for (int colorIndex = colors.Count - 1; colorIndex >= 0; colorIndex--)
            {
                object entry = colors[colorIndex];
                if (entry == null || !_colorEntryType.IsInstanceOfType(entry)) continue;
                string name = _entryNameField.GetValue(entry) as string;
                if (!IsHiddenColorName(name)) continue;
                colors.RemoveAt(colorIndex);
                changed = true;
            }
            if (!changed) continue;

            _maxPagesField.SetValue(gui, Math.Max(1, (colors.Count + 9) / 10));
            if (gui.gameObject != null && gui.gameObject.activeInHierarchy)
            {
                try { _listMethod.Invoke(gui, null); }
                catch { }
            }
        }
    }

    private static bool IsHiddenColorName(string name)
    {
        for (int i = 0; i < HiddenColors.Length; i++)
        {
            if (string.Equals(name, HiddenColors[i].Name,
                StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
