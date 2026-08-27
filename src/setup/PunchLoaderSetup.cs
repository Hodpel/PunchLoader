using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public static class PunchLoaderSetup
{
    internal const string Version = "1.1.0";
    // 已验证的 Megabyte Punch 原版程序集：历史零售版与 Steam 版。
    // 安装器只接受这两个完整文件哈希；不会把任意未注入 DLL 当作可注入版本。
    const string OriginalAssemblySha256 = "EF53EB7B6438422EA0C40C96C7CE698E03C164CC2B1E2A21F601DAF3DEDB8492";
    const string SteamAssemblySha256 = "98B65733333F24613E2FE1CCF1DD520459F8C3DFBC17DDD618372ED8A8EB3450";
    const int StateOriginal = 0;
    const int StateInjected = 10;
    const int StatePartial = 11;

    static string Root;
    static string Managed;
    static string Target;
    static string BackupDir;
    static string OriginalBackup;
    static string Manifest;
    internal static Action<string> MessageSink;

    [STAThread]
    public static int Main(string[] args)
    {
        InitializePaths();
        if (args.Length == 0)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new PunchLoaderSetupForm());
            return 0;
        }

        AttachParentConsole();
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            string action = args[0].ToLowerInvariant();
            int result;
            if (action == "install") result = Install();
            else if (action == "status") result = Status();
            else if (action == "uninstall") result = Uninstall();
            else
            {
                Console.WriteLine("用法: PunchLoader.Setup.exe [install|status|uninstall]");
                result = 2;
            }
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[失败] " + ex.Message);
            Console.WriteLine(ex.ToString());
            return 1;
        }
    }

    internal static void InitializePaths()
    {
        Root = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        Managed = Path.Combine(Root, "MegabytePunch_Data\\Managed");
        Target = Path.Combine(Managed, "Assembly-CSharp.dll");
        BackupDir = Path.Combine(Managed, "PunchLoader.Backup");
        OriginalBackup = Path.Combine(BackupDir, "Assembly-CSharp.original.dll");
        Manifest = Path.Combine(BackupDir, "install-manifest.json");
    }

    internal static int Install()
    {
        ValidateGameRoot();
        Directory.CreateDirectory(BackupDir);
        string work = Path.Combine(BackupDir, ".setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string injector = Extract("PunchLoader.Setup.Injector.exe", Path.Combine(work, "Injector.exe"));
            Extract("PunchLoader.Setup.Mono.Cecil.dll", Path.Combine(work, "Mono.Cecil.dll"));
            string loader = Extract("PunchLoader.Setup.PunchLoader.dll", Path.Combine(work, "PunchLoader.dll"));
            File.Copy(Path.Combine(Managed, "UnityEngine.dll"), Path.Combine(work, "UnityEngine.dll"), true);

            int currentState = Inspect(injector, Target, work);
            string emergency = Path.Combine(BackupDir, "Assembly-CSharp.before-install.dll");

            // 该文件只服务于一次安装事务。新事务开始时清理上次异常退出可能留下的副本。
            if (File.Exists(emergency)) File.Delete(emergency);

            if (currentState == StateInjected)
            {
                File.Copy(loader, Path.Combine(Managed, "PunchLoader.dll"), true);
                Directory.CreateDirectory(Path.Combine(Root, "Mods"));
                WriteManifest("installed", Sha256(Target), Sha256(loader));
                Emit("[完成] 已识别为完整注入版本，跳过重复注入；PunchLoader.dll 已更新。");
                if (!HasValidOriginalBackup())
                    Emit("[警告] 未找到原版备份；当前可正常更新，但安装器无法执行卸载。");
                return 0;
            }

            if (currentState != StateOriginal && currentState != StatePartial)
                throw new InvalidOperationException("目标 DLL 状态未知，已停止安装。");

            string cleanSource = EnsureOriginalBackup(currentState);

            string patched = Path.Combine(work, "Assembly-CSharp.patched.dll");
            int patchExit = Run(injector,
                "--patch " + Quote(cleanSource) + " " + Quote(patched) + " " +
                Quote(loader) + " " + Quote(Managed), work);
            if (patchExit != 0 || !File.Exists(patched))
                throw new InvalidOperationException("注入器执行失败，退出码: " + patchExit);
            if (Inspect(injector, patched, work) != StateInjected)
                throw new InvalidOperationException("注入结果校验失败，未替换游戏文件。");

            File.Copy(Target, emergency, true);
            File.Copy(loader, Path.Combine(Managed, "PunchLoader.dll"), true);
            Directory.CreateDirectory(Path.Combine(Root, "Mods"));
            try
            {
                File.Replace(patched, Target, emergency, true);
                if (Inspect(injector, Target, work) != StateInjected)
                    throw new InvalidOperationException("替换后的目标 DLL 校验失败。");
                WriteManifest("installed", Sha256(Target), Sha256(loader));
                File.Delete(emergency);
            }
            catch
            {
                if (File.Exists(emergency)) File.Copy(emergency, Target, true);
                throw;
            }

            Emit("[完成] 原版 DLL 已备份，PunchLoader 已注入并部署。");
            Emit("备份: " + OriginalBackup);
            Emit("模组目录: " + Path.Combine(Root, "Mods"));
            return 0;
        }
        finally
        {
            TryDeleteDirectory(work);
        }
    }

    internal static int Status()
    {
        ValidateGameRoot();
        Directory.CreateDirectory(BackupDir);
        string work = Path.Combine(BackupDir, ".status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string injector = Extract("PunchLoader.Setup.Injector.exe", Path.Combine(work, "Injector.exe"));
            Extract("PunchLoader.Setup.Mono.Cecil.dll", Path.Combine(work, "Mono.Cecil.dll"));
            File.Copy(Path.Combine(Managed, "UnityEngine.dll"), Path.Combine(work, "UnityEngine.dll"), true);
            int state = Inspect(injector, Target, work);
            Emit("Assembly-CSharp.dll SHA-256: " + Sha256(Target));
            Emit("状态: " + StateName(state));
            Emit("原版备份: " + (HasValidOriginalBackup() ? OriginalBackup : "不可用"));
            return state == StateInjected || state == StateOriginal ? 0 : 1;
        }
        finally { TryDeleteDirectory(work); }
    }

    internal static int Uninstall()
    {
        ValidateGameRoot();
        if (!File.Exists(OriginalBackup) || !IsKnownOriginal(OriginalBackup))
            throw new InvalidOperationException("未找到通过校验的原版备份，不能执行恢复。");
        string before = Path.Combine(BackupDir, "Assembly-CSharp.before-uninstall.dll");
        if (File.Exists(before)) File.Delete(before);
        File.Copy(Target, before, true);
        try
        {
            File.Copy(OriginalBackup, Target, true);
            string loader = Path.Combine(Managed, "PunchLoader.dll");
            if (File.Exists(loader)) File.Delete(loader);
            WriteManifest("uninstalled", Sha256(Target), null);
            File.Delete(before);
            Emit("[完成] 已恢复原版 Assembly-CSharp.dll。模组目录保持不变。");
            return 0;
        }
        catch
        {
            if (File.Exists(before)) File.Copy(before, Target, true);
            throw;
        }
    }

    static void ValidateGameRoot()
    {
        if (!File.Exists(Path.Combine(Root, "MegabytePunch.exe")))
            throw new InvalidOperationException("请把 PunchLoader.Setup.exe 放在 MegabytePunch.exe 所在目录。");
        if (!File.Exists(Target))
            throw new InvalidOperationException("未找到 MegabytePunch_Data\\Managed\\Assembly-CSharp.dll。");
        if (!File.Exists(Path.Combine(Managed, "UnityEngine.dll")))
            throw new InvalidOperationException("未找到游戏的 UnityEngine.dll。");
    }

    static string EnsureOriginalBackup(int currentState)
    {
        if (File.Exists(OriginalBackup))
        {
            if (!IsKnownOriginal(OriginalBackup))
                throw new InvalidOperationException("永久备份哈希不匹配: " + OriginalBackup);
            if (currentState == StateOriginal &&
                !string.Equals(Sha256(OriginalBackup), Sha256(Target), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "现有原版备份与当前游戏版本不一致。请先移走 PunchLoader.Backup 后重试。");
            return OriginalBackup;
        }

        if (currentState == StateOriginal && !IsKnownOriginal(Target))
            throw new InvalidOperationException(
                "当前 Assembly-CSharp.dll 未注入，但不是受支持版本；已停止安装，避免覆盖其他游戏版本。");

        if (currentState == StateOriginal && IsKnownOriginal(Target))
        {
            File.Copy(Target, OriginalBackup, false);
            return OriginalBackup;
        }

        throw new InvalidOperationException(
            "当前 DLL 已被部分修改，且没有可用的同版本原版备份。" +
            "请通过 Steam 验证游戏文件或恢复正确版本的原版 DLL 后重试。");
    }

    internal static SetupSnapshot ReadSnapshot()
    {
        SetupSnapshot snapshot = new SetupSnapshot();
        snapshot.Root = Root;
        snapshot.BackupPath = OriginalBackup;
        snapshot.GameFound = File.Exists(Path.Combine(Root, "MegabytePunch.exe")) && File.Exists(Target);
        snapshot.BackupValid = HasValidOriginalBackup();
        if (!snapshot.GameFound)
        {
            snapshot.State = "未找到游戏";
            return snapshot;
        }

        Directory.CreateDirectory(BackupDir);
        string work = Path.Combine(BackupDir, ".gui-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string injector = Extract("PunchLoader.Setup.Injector.exe", Path.Combine(work, "Injector.exe"));
            Extract("PunchLoader.Setup.Mono.Cecil.dll", Path.Combine(work, "Mono.Cecil.dll"));
            File.Copy(Path.Combine(Managed, "UnityEngine.dll"), Path.Combine(work, "UnityEngine.dll"), true);
            int state = Inspect(injector, Target, work);
            if (state == StateOriginal && IsKnownOriginal(Target))
                snapshot.State = StateName(state) + "（" + OriginalChannel(Target) + "）";
            else if (state == StateOriginal)
                snapshot.State = "不受支持的未注入版本";
            else
                snapshot.State = StateName(state);
            snapshot.CanInstall = state == StateInjected ||
                (state == StateOriginal && IsKnownOriginal(Target)) ||
                (state == StatePartial && snapshot.BackupValid);
            snapshot.CanUninstall = snapshot.BackupValid;
        }
        catch (Exception ex)
        {
            snapshot.State = "检查失败: " + ex.Message;
        }
        finally { TryDeleteDirectory(work); }
        return snapshot;
    }

    static bool HasValidOriginalBackup()
    {
        return File.Exists(OriginalBackup) && IsKnownOriginal(OriginalBackup);
    }

    static string OriginalChannel(string path)
    {
        string hash = Sha256(path);
        if (string.Equals(hash, SteamAssemblySha256, StringComparison.OrdinalIgnoreCase)) return "Steam";
        if (string.Equals(hash, OriginalAssemblySha256, StringComparison.OrdinalIgnoreCase)) return "Retail";
        return "未知";
    }

    static int Inspect(string injector, string assembly, string work)
    {
        int state = Run(injector, "--inspect " + Quote(assembly), work);
        if (state != StateOriginal && state != StateInjected && state != StatePartial)
            throw new InvalidOperationException("无法识别 Assembly-CSharp.dll，检查退出码: " + state);
        return state;
    }

    static int Run(string fileName, string arguments, string work)
    {
        ProcessStartInfo info = new ProcessStartInfo(fileName, arguments);
        info.WorkingDirectory = work;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        Process process = Process.Start(info);
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (stdout.Length > 0) Emit(stdout.TrimEnd());
        if (stderr.Length > 0) Emit(stderr.TrimEnd());
        return process.ExitCode;
    }

    static string Extract(string resourceName, string destination)
    {
        Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (input == null) throw new InvalidOperationException("安装器资源缺失: " + resourceName);
        using (input)
        using (FileStream output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            byte[] buffer = new byte[32768];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                output.Write(buffer, 0, read);
        }
        return destination;
    }

    static bool IsKnownOriginal(string path)
    {
        string hash = Sha256(path);
        return string.Equals(hash, OriginalAssemblySha256, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(hash, SteamAssemblySha256, StringComparison.OrdinalIgnoreCase);
    }

    static string OriginalBackupSha256()
    {
        return HasValidOriginalBackup() ? Sha256(OriginalBackup) : null;
    }

    static string Sha256(string path)
    {
        using (SHA256 hash = SHA256.Create())
        using (FileStream stream = File.OpenRead(path))
        {
            byte[] value = hash.ComputeHash(stream);
            StringBuilder text = new StringBuilder(value.Length * 2);
            foreach (byte item in value) text.Append(item.ToString("X2"));
            return text.ToString();
        }
    }

    static void WriteManifest(string status, string assemblyHash, string loaderHash)
    {
        Directory.CreateDirectory(BackupDir);
        string json = "{\r\n" +
            "  \"schemaVersion\": 1,\r\n" +
            "  \"setupVersion\": \"" + Version + "\",\r\n" +
            "  \"status\": \"" + status + "\",\r\n" +
            "  \"updatedUtc\": \"" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") + "\",\r\n" +
            "  \"originalSha256\": " + JsonString(OriginalBackupSha256()) + ",\r\n" +
            "  \"assemblySha256\": \"" + assemblyHash + "\",\r\n" +
            "  \"loaderSha256\": " + (loaderHash == null ? "null" : "\"" + loaderHash + "\"") + "\r\n" +
            "}\r\n";
        File.WriteAllText(Manifest, json, new UTF8Encoding(false));
    }

    static string Quote(string value) { return "\"" + value.Replace("\"", "\\\"") + "\""; }
    static string JsonString(string value) { return value == null ? "null" : Quote(value); }
    static string StateName(int state)
    {
        if (state == StateOriginal) return "原版";
        if (state == StateInjected) return "已完整注入";
        if (state == StatePartial) return "部分注入";
        return "未知";
    }
    static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch { }
    }
    static void Emit(string message)
    {
        if (MessageSink != null) MessageSink(message);
        try { Console.WriteLine(message); }
        catch { }
    }

    static void AttachParentConsole()
    {
        try
        {
            if (AttachConsole(AttachParentProcess))
            {
                StreamWriter output = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8);
                output.AutoFlush = true;
                Console.SetOut(output);
                StreamWriter error = new StreamWriter(Console.OpenStandardError(), Encoding.UTF8);
                error.AutoFlush = true;
                Console.SetError(error);
            }
        }
        catch { }
    }

    const uint AttachParentProcess = 0xFFFFFFFF;
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AttachConsole(uint processId);
}

public sealed class SetupSnapshot
{
    public string Root;
    public string State;
    public string BackupPath;
    public bool GameFound;
    public bool BackupValid;
    public bool CanInstall;
    public bool CanUninstall;
}
