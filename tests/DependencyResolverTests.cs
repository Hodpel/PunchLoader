using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using PunchLoader;

public static class DependencyResolverTests
{
    private static int _failures;

    public static int Main()
    {
        TestDependencyJson();
        TestVersions();
        TestDependencyOrderAndValidation();
        TestDuplicateIds();
        TestRuntimeDependencyState();

        if (_failures == 0)
        {
            Console.WriteLine("Dependency resolver tests passed.");
            return 0;
        }
        Console.Error.WriteLine(_failures + " dependency resolver test(s) failed.");
        return 1;
    }

    private static void TestDependencyJson()
    {
        string json = "{\"id\":\"Consumer\",\"dependencies\":[" +
            "{\"id\":\"ContentAPI\",\"minVersion\":\"1.2.0\"}]," +
            "\"priority\":7}";
        Dictionary<string, string> data = SimpleJson.ParseObject(json);
        List<Dictionary<string, string>> dependencies =
            SimpleJson.GetObjectArray(data, "dependencies");
        Check(dependencies.Count == 1, "dependency array count");
        Check(dependencies[0]["id"] == "ContentAPI", "dependency id");
        Check(dependencies[0]["minVersion"] == "1.2.0", "minimum version");
        Check(SimpleJson.GetInt(data, "priority", 0) == 7,
            "field after dependency array");
    }

    private static void TestVersions()
    {
        Check(ModVersion.Satisfies("1.2.0", "1.1.9"), "newer version satisfies");
        Check(!ModVersion.Satisfies("1.1.9", "1.2.0"), "older version rejected");
        Check(ModVersion.Compare("v1.0.0", "1.0") == 0, "leading v and zero parts");
        Check(ModVersion.Compare("1.0.0", "1.0.0-beta") > 0,
            "release newer than prerelease");
        Check(ModVersion.Compare("1.0.0-beta.2", "1.0.0-beta.10") < 0,
            "numeric prerelease comparison");
    }

    private static void TestDependencyOrderAndValidation()
    {
        ModInfo api = Mod("ContentAPI", "1.2.0", 100);
        ModInfo consumer = Mod("Consumer", "1.0.0", -100);
        consumer.Dependencies.Add(Dependency("ContentAPI", "1.0.0"));
        ModInfo missing = Mod("MissingConsumer", "1.0.0", 0);
        missing.Dependencies.Add(Dependency("Absent", "1.0.0"));
        ModInfo old = Mod("OldConsumer", "1.0.0", 0);
        old.Dependencies.Add(Dependency("ContentAPI", "2.0.0"));
        ModInfo cycleA = Mod("CycleA", "1.0.0", 0);
        ModInfo cycleB = Mod("CycleB", "1.0.0", 0);
        cycleA.Dependencies.Add(Dependency("CycleB", null));
        cycleB.Dependencies.Add(Dependency("CycleA", null));

        List<ModInfo> mods = new List<ModInfo>();
        mods.Add(consumer);
        mods.Add(api);
        mods.Add(missing);
        mods.Add(old);
        mods.Add(cycleA);
        mods.Add(cycleB);
        Resolve(mods);

        Check(mods.IndexOf(api) < mods.IndexOf(consumer),
            "dependency precedes lower-priority consumer");
        Check(string.IsNullOrEmpty(consumer.DependencyError),
            "valid dependency accepted");
        Check(Contains(missing.DependencyError, "Missing dependency"),
            "missing dependency rejected");
        Check(Contains(old.DependencyError, "requires v2.0.0"),
            "minimum version rejected");
        Check(Contains(cycleA.DependencyError, "Circular") &&
            Contains(cycleB.DependencyError, "Circular"), "cycle rejected");
    }

    private static void TestDuplicateIds()
    {
        ModInfo first = Mod("Duplicate", "1.0.0", 0);
        ModInfo second = Mod("duplicate", "1.0.0", 1);
        List<ModInfo> mods = new List<ModInfo>();
        mods.Add(first);
        mods.Add(second);
        Resolve(mods);
        Check(Contains(first.DependencyError, "Duplicate") &&
            Contains(second.DependencyError, "Duplicate"), "duplicate ids rejected");
    }

    private static void TestRuntimeDependencyState()
    {
        ModInfo api = Mod("ContentAPI", "1.0.0", 0);
        ModInfo consumer = Mod("Consumer", "1.0.0", 0);
        consumer.Dependencies.Add(Dependency("ContentAPI", "1.0.0"));
        List<ModInfo> mods = new List<ModInfo>();
        mods.Add(api);
        mods.Add(consumer);
        ModLoaderBehaviour loader = CreateLoader(mods);

        Check(Contains(GetDependencyIssue(loader, consumer), "not loaded"),
            "unloaded dependency blocks runtime activation");
        api.Loaded = true;
        Check(string.IsNullOrEmpty(GetDependencyIssue(loader, consumer)),
            "loaded dependency permits runtime activation");
        api.Enabled = false;
        Check(Contains(GetDependencyIssue(loader, consumer), "disabled"),
            "disabled dependency blocks runtime activation");

        api.Enabled = true;
        consumer.Enabled = false;
        consumer.Loaded = true;
        Check(GetBlockingDependent(loader, api) == consumer,
            "loaded consumer blocks immediate dependency unload");
        api.RequiresRestart = true;
        Check(GetBlockingDependent(loader, api) == null,
            "restart-only dependency can be scheduled off with consumer");
        consumer.Enabled = true;
        Check(GetBlockingDependent(loader, api) == consumer,
            "desired-enabled consumer blocks dependency disable");
    }

    private static void Resolve(List<ModInfo> mods)
    {
        List<ModInfo> ordered = ModDependencyResolver.Resolve(mods);
        mods.Clear();
        mods.AddRange(ordered);
    }

    private static ModLoaderBehaviour CreateLoader(List<ModInfo> mods)
    {
        ModLoaderBehaviour loader = (ModLoaderBehaviour)
            FormatterServices.GetUninitializedObject(typeof(ModLoaderBehaviour));
        GetModsField().SetValue(loader, mods);
        return loader;
    }

    private static FieldInfo GetModsField()
    {
        return typeof(ModLoaderBehaviour).GetField("_mods",
            BindingFlags.Instance | BindingFlags.NonPublic);
    }

    private static string GetDependencyIssue(ModLoaderBehaviour loader, ModInfo mod)
    {
        MethodInfo method = typeof(ModLoaderBehaviour).GetMethod(
            "GetDependencyIssue", BindingFlags.Instance | BindingFlags.NonPublic);
        return (string)method.Invoke(loader, new object[] { mod, true });
    }

    private static ModInfo GetBlockingDependent(ModLoaderBehaviour loader, ModInfo mod)
    {
        MethodInfo method = typeof(ModLoaderBehaviour).GetMethod(
            "FindBlockingDependent", BindingFlags.Instance | BindingFlags.NonPublic);
        return (ModInfo)method.Invoke(loader, new object[] { mod });
    }

    private static ModInfo Mod(string id, string version, int priority)
    {
        ModInfo mod = new ModInfo();
        mod.Id = id;
        mod.Name = id;
        mod.Version = version;
        mod.Priority = priority;
        return mod;
    }

    private static ModDependency Dependency(string id, string minVersion)
    {
        ModDependency dependency = new ModDependency();
        dependency.Id = id;
        dependency.MinVersion = minVersion;
        return dependency;
    }

    private static bool Contains(string value, string expected)
    {
        return value != null && value.IndexOf(expected,
            StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void Check(bool condition, string name)
    {
        if (condition) return;
        _failures++;
        Console.Error.WriteLine("FAIL: " + name);
    }
}
