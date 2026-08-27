using System;
using System.Collections.Generic;

namespace PunchLoader
{
    public class ModDependency
    {
        public string Id;
        public string MinVersion;
    }

    public static class ModVersion
    {
        public static bool Satisfies(string actual, string minimum)
        {
            if (string.IsNullOrEmpty(minimum)) return true;
            if (string.IsNullOrEmpty(actual)) return false;
            return Compare(actual, minimum) >= 0;
        }

        public static int Compare(string left, string right)
        {
            string leftPrerelease;
            string rightPrerelease;
            string[] leftParts = SplitCoreVersion(left, out leftPrerelease);
            string[] rightParts = SplitCoreVersion(right, out rightPrerelease);
            int count = Math.Max(leftParts.Length, rightParts.Length);
            for (int i = 0; i < count; i++)
            {
                string a = i < leftParts.Length ? leftParts[i] : "0";
                string b = i < rightParts.Length ? rightParts[i] : "0";
                int ai;
                int bi;
                bool an = int.TryParse(a, out ai);
                bool bn = int.TryParse(b, out bi);
                int comparison;
                if (an && bn) comparison = ai.CompareTo(bi);
                else comparison = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                if (comparison != 0) return comparison;
            }

            // SemVer: a release is newer than its prerelease; numeric prerelease
            // identifiers sort before textual identifiers.
            if (string.IsNullOrEmpty(leftPrerelease) &&
                string.IsNullOrEmpty(rightPrerelease)) return 0;
            if (string.IsNullOrEmpty(leftPrerelease)) return 1;
            if (string.IsNullOrEmpty(rightPrerelease)) return -1;

            string[] leftPre = leftPrerelease.Split('.');
            string[] rightPre = rightPrerelease.Split('.');
            count = Math.Min(leftPre.Length, rightPre.Length);
            for (int i = 0; i < count; i++)
            {
                int ai;
                int bi;
                bool an = int.TryParse(leftPre[i], out ai);
                bool bn = int.TryParse(rightPre[i], out bi);
                int comparison;
                if (an && bn) comparison = ai.CompareTo(bi);
                else if (an) comparison = -1;
                else if (bn) comparison = 1;
                else comparison = string.Compare(leftPre[i], rightPre[i],
                    StringComparison.OrdinalIgnoreCase);
                if (comparison != 0) return comparison;
            }
            return leftPre.Length.CompareTo(rightPre.Length);
        }

        private static string[] SplitCoreVersion(string version, out string prerelease)
        {
            string value = version.Trim();
            if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(1);
            int metadata = value.IndexOf('+');
            if (metadata >= 0) value = value.Substring(0, metadata);
            int separator = value.IndexOf('-');
            if (separator >= 0)
            {
                prerelease = value.Substring(separator + 1);
                value = value.Substring(0, separator);
            }
            else prerelease = null;
            return value.Split('.');
        }
    }

    public static class ModDependencyResolver
    {
        public static List<ModInfo> Resolve(List<ModInfo> mods)
        {
            List<ModInfo> candidates = new List<ModInfo>(mods);
            candidates.Sort(CompareMods);

            Dictionary<string, ModInfo> byId =
                new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (ModInfo mod in candidates)
            {
                ModInfo duplicate;
                if (byId.TryGetValue(mod.Id, out duplicate))
                {
                    string error = "Duplicate mod id: " + mod.Id;
                    mod.DependencyError = error;
                    duplicate.DependencyError = error;
                }
                else byId.Add(mod.Id, mod);
            }

            List<ModInfo> ordered = new List<ModInfo>();
            Dictionary<ModInfo, int> states = new Dictionary<ModInfo, int>();
            foreach (ModInfo mod in candidates)
                Visit(mod, byId, states, ordered);
            return ordered;
        }

        private static int CompareMods(ModInfo a, ModInfo b)
        {
            int priority = a.Priority.CompareTo(b.Priority);
            if (priority != 0) return priority;
            return string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
        }

        private static void Visit(ModInfo mod, Dictionary<string, ModInfo> byId,
            Dictionary<ModInfo, int> states, List<ModInfo> ordered)
        {
            int state;
            if (states.TryGetValue(mod, out state))
            {
                if (state == 1 && string.IsNullOrEmpty(mod.DependencyError))
                    mod.DependencyError = "Circular dependency involving " + mod.Id;
                return;
            }

            states[mod] = 1;
            foreach (ModDependency dependency in mod.Dependencies)
            {
                ModInfo target;
                if (!byId.TryGetValue(dependency.Id, out target))
                {
                    if (string.IsNullOrEmpty(mod.DependencyError))
                        mod.DependencyError = "Missing dependency: " + dependency.Id;
                    continue;
                }
                if (object.ReferenceEquals(mod, target))
                {
                    if (string.IsNullOrEmpty(mod.DependencyError))
                        mod.DependencyError = "Mod depends on itself: " + mod.Id;
                    continue;
                }
                if (!ModVersion.Satisfies(target.Version, dependency.MinVersion))
                {
                    if (string.IsNullOrEmpty(mod.DependencyError))
                        mod.DependencyError = "Dependency " + dependency.Id + " requires v" +
                            dependency.MinVersion + ", found v" + (target.Version ?? "?");
                    continue;
                }

                int targetState;
                if (states.TryGetValue(target, out targetState) && targetState == 1)
                {
                    if (string.IsNullOrEmpty(target.DependencyError))
                        target.DependencyError = "Circular dependency involving " + target.Id;
                    if (string.IsNullOrEmpty(mod.DependencyError))
                        mod.DependencyError = "Circular dependency involving " + mod.Id;
                    continue;
                }

                Visit(target, byId, states, ordered);
                if (!string.IsNullOrEmpty(target.DependencyError) &&
                    string.IsNullOrEmpty(mod.DependencyError))
                    mod.DependencyError = "Dependency " + dependency.Id + " is unavailable";
            }
            states[mod] = 2;
            if (!ordered.Contains(mod)) ordered.Add(mod);
        }
    }
}
