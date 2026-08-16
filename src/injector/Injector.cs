using System;
using System.IO;
using System.Reflection;
using Cecil = Mono.Cecil;
using CecilCil = Mono.Cecil.Cil;

// ============================================================================
// Injector — 离线 IL 注入器
// 编译: csc @build_injector.rsp → Injector.exe
// 运行时机: 游戏启动前，手动运行一次（或包含在构建脚本中）
// 功能: 用 Mono.Cecil 修改 Assembly-CSharp.dll 的 IL 字节码，注入 3 个钩子
//   1. MenuScript.Start() 顶部 → Assembly.LoadFrom + Bootstrap.Init() 引导调用
//   2. GUILayoutMenuScript.CheckConfirm() → DoAction 前调 MenuLocalizer.TranslateEntry()
//   3. GUILayoutMenuScript.BeginGUI() 顶部 → FontRouter.Route(this)
// 平台: .NET 2.0 / Mono 2.x — 不依赖 Harmony 或任何运行时库
// ============================================================================
public class Injector
{
    // === 路径配置 ===
    const string GAME_MANAGED = "F:/Codex/MBP_PROJ/ModdedGame/MegabytePunch_Data/Managed";
    const string TARGET_DLL = GAME_MANAGED + "/Assembly-CSharp.dll";
    const string BACKUP_DLL = GAME_MANAGED + "/Assembly-CSharp.dll.orig";
    const string PUNCHLOADER_DLL = GAME_MANAGED + "/PunchLoader.dll";

    static int Main(string[] args)
    {
        try
        {
            // === 步骤0: 备份原版 dll ===
            // ba只备份一次; 后续运行从 .orig 还原再注入（可重复运行 Injector）
            if (!File.Exists(BACKUP_DLL))
                File.Copy(TARGET_DLL, BACKUP_DLL);
            else
                File.Copy(BACKUP_DLL, TARGET_DLL, true);

            // === 步骤1: 读取目标程序集 ===
            Cecil.AssemblyDefinition asm = Cecil.AssemblyDefinition.ReadAssembly(TARGET_DLL,
                new Cecil.ReaderParameters { ReadSymbols = false });

            // === 步骤2: 找到 MenuScript.Start() 作为注入入口点 ===
            Cecil.MethodDefinition target = null;
            foreach (Cecil.TypeDefinition t in asm.MainModule.Types)
            {
                if (t.Name == "MenuScript")
                {
                    foreach (Cecil.MethodDefinition m in t.Methods)
                        if (m.Name == "Start" && m.HasBody) { target = m; break; }
                    break;
                }
            }
            if (target == null) { Console.WriteLine("ERROR: MenuScript.Start() not found"); return 1; }

            // === 准备: Import 所需的 .NET 反射 API ===
            CecilCil.ILProcessor il = target.Body.GetILProcessor();
            CecilCil.Instruction first = target.Body.Instructions[0];
            string md = GAME_MANAGED.Replace('\\', '/');

            // Assembly.LoadFrom(string)
            Cecil.MethodReference asmLoadFrom = asm.MainModule.Import(
                typeof(Assembly).GetMethod("LoadFrom", new Type[] { typeof(string) }));
            // Assembly.GetType(string)
            Cecil.MethodReference asmGetType = asm.MainModule.Import(
                typeof(Assembly).GetMethod("GetType", new Type[] { typeof(string) }));
            // Type.GetMethod(string)
            Cecil.MethodReference typeGetMethod = asm.MainModule.Import(
                typeof(Type).GetMethod("GetMethod", new Type[] { typeof(string) }));
            // MethodBase.Invoke(object, object[])
            Cecil.MethodReference methodInvoke = asm.MainModule.Import(
                typeof(MethodBase).GetMethod("Invoke", new Type[] { typeof(object), typeof(object[]) }));
            // Debug.Log(object)
            Cecil.MethodReference logObjRef = asm.MainModule.Import(
                typeof(UnityEngine.Debug).GetMethod("Log", new Type[] { typeof(object) }));

            // === 添加局部变量 ===
            CecilCil.VariableDefinition asmVar = new CecilCil.VariableDefinition(asm.MainModule.Import(typeof(Assembly)));
            target.Body.Variables.Add(asmVar);
            CecilCil.VariableDefinition typeVar = new CecilCil.VariableDefinition(asm.MainModule.Import(typeof(Type)));
            target.Body.Variables.Add(typeVar);
            CecilCil.VariableDefinition methodVar = new CecilCil.VariableDefinition(asm.MainModule.Import(typeof(MethodInfo)));
            target.Body.Variables.Add(methodVar);

            // === 步骤3: 注入引导代码到 MenuScript.Start() 顶部 ===
            // 等价于 C#:
            //   Debug.Log("[PunchLoader] booting...");
            //   Assembly punchLoaderAsm = Assembly.LoadFrom("Managed/PunchLoader.dll");
            //   Type t = punchLoaderAsm.GetType("PunchLoader.Bootstrap");
            //   MethodInfo m = t.GetMethod("Init");
            //   m.Invoke(null, null);

            il.InsertBefore(first, il.Create(CecilCil.OpCodes.Ldstr, "[PunchLoader] booting..."));
            il.InsertBefore(first, il.Create(CecilCil.OpCodes.Call, logObjRef));

            // Assembly punchLoaderAsm = Assembly.LoadFrom("PunchLoader.dll")
            il.InsertBefore(first, il.Create(CecilCil.OpCodes.Ldstr, md + "/PunchLoader.dll"));
            il.InsertBefore(first, il.Create(CecilCil.OpCodes.Call, asmLoadFrom));
            il.InsertBefore(first, il.Create(CecilCil.OpCodes.Stloc, asmVar));

            // Type t = punchLoaderAsm.GetType("PunchLoader.Bootstrap")
            il.InsertBefore(first, il.Create(CecilCil.OpCodes.Ldloc, asmVar));
            il.InsertBefore(first, il.Create(CecilCil.OpCodes.Ldstr, "PunchLoader.Bootstrap"));
            il.InsertBefore(first, il.Create(CecilCil.OpCodes.Callvirt, asmGetType));
            il.InsertBefore(first, il.Create(CecilCil.OpCodes.Stloc, typeVar));

            // MethodInfo m = t.GetMethod("Init")
            il.InsertBefore(first, il.Create(CecilCil.OpCodes.Ldloc, typeVar));
            il.InsertBefore(first, il.Create(CecilCil.OpCodes.Ldstr, "Init"));
            il.InsertBefore(first, il.Create(CecilCil.OpCodes.Callvirt, typeGetMethod));
            il.InsertBefore(first, il.Create(CecilCil.OpCodes.Stloc, methodVar));

            // m.Invoke(null, null)
            il.InsertBefore(first, il.Create(CecilCil.OpCodes.Ldloc, methodVar));
            il.InsertBefore(first, il.Create(CecilCil.OpCodes.Ldnull));
            il.InsertBefore(first, il.Create(CecilCil.OpCodes.Ldnull));
            il.InsertBefore(first, il.Create(CecilCil.OpCodes.Callvirt, methodInvoke));
            il.InsertBefore(first, il.Create(CecilCil.OpCodes.Pop));

            // === 步骤4: 注入 CheckConfirm 钩子（反向翻译） ===
            InjectCheckConfirmHook(asm);

            // === 步骤5: 注入 BeginGUI 钩子（零闪烁字体替换） ===
            InjectBeginGUIHook(asm);

            // === 步骤6: 写回修改后的 dll ===
            asm.Write(TARGET_DLL);
            Console.WriteLine("[Injector] Done.");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("[Injector] ERROR: " + ex); return 1; }
    }

    // ====================================================================
    // 钩子2: CheckConfirm() 中 DoAction 前插入反向翻译
    // 在 GUILayoutMenuScript.CheckConfirm() 中，每个 callvirt DoAction 之前
    // 插入 call MenuLocalizer.TranslateEntry(string)
    // 把菜单上显示的中文文本翻译回英文，保证 DoAction("quit") 的 switch 能匹配
    // ====================================================================
    static void InjectCheckConfirmHook(Cecil.AssemblyDefinition asm)
    {
        Cecil.TypeDefinition guiLayoutMenu = null;
        Cecil.MethodDefinition checkConfirm = null;
        foreach (Cecil.TypeDefinition t in asm.MainModule.Types)
        {
            if (t.Name == "GUILayoutMenuScript")
            {
                guiLayoutMenu = t;
                foreach (Cecil.MethodDefinition m in t.Methods)
                    if (m.Name == "CheckConfirm" && m.HasBody) { checkConfirm = m; break; }
                break;
            }
        }
        if (checkConfirm == null) { Console.WriteLine("WARNING: GUILayoutMenuScript.CheckConfirm not found"); return; }

        Console.WriteLine("[Injector] Found GUILayoutMenuScript.CheckConfirm, patching...");

        // 从 PunchLoader.dll 中 Import MenuLocalizer.TranslateEntry(string)
        Cecil.MethodReference translateEntry = null;
        if (File.Exists(PUNCHLOADER_DLL))
        {
            Cecil.AssemblyDefinition punchLoaderAsm = Cecil.AssemblyDefinition.ReadAssembly(
                PUNCHLOADER_DLL, new Cecil.ReaderParameters { ReadSymbols = false });
            foreach (Cecil.TypeDefinition t in punchLoaderAsm.MainModule.Types)
            {
                if (t.Name == "MenuLocalizer")
                {
                    foreach (Cecil.MethodDefinition m in t.Methods)
                    {
                        if (m.Name == "TranslateEntry")
                        {
                            translateEntry = asm.MainModule.Import(m);
                            break;
                        }
                    }
                    break;
                }
            }
        }
        if (translateEntry == null)
        {
            Console.WriteLine("WARNING: MenuLocalizer.TranslateEntry not found in PunchLoader.dll");
            return;
        }

        CecilCil.ILProcessor il = checkConfirm.Body.GetILProcessor();

        // 在 CheckConfirm 的 IL 中，DoAction 调用模式是:
        //   ldarg.0                       // this
        //   ldarg.0 / ldfld menuEntries / ldarg.0 / ldfld selected / ldelem.ref  // menuEntries[selected]
        //   callvirt GUILayoutMenuScript::DoAction(string)
        // 在 DoAction 前插 call MenuLocalizer::TranslateEntry(string)
        // 栈变化: [this, entry] → [this, translated_entry]

        int patches = 0;
        CecilCil.Instruction instr = checkConfirm.Body.Instructions[0];
        while (instr != null)
        {
            if (instr.OpCode == CecilCil.OpCodes.Callvirt &&
                ((Cecil.MethodReference)instr.Operand).Name == "DoAction")
            {
                il.InsertBefore(instr, il.Create(CecilCil.OpCodes.Call, translateEntry));
                patches++;
                Console.WriteLine("[Injector] Patched DoAction call at IL_" +
                    instr.Offset.ToString("X4") + " with translateEntry hook");
            }
            instr = instr.Next;
        }

        if (patches == 0)
            Console.WriteLine("[Injector] WARNING: No DoAction calls found in CheckConfirm");
        else
            Console.WriteLine("[Injector] Patched " + patches + " DoAction call(s)");
    }

    // ====================================================================
    // 钩子3: BeginGUI() 顶部插入字体路由
    // 在 GUILayoutMenuScript.BeginGUI() 的第一条指令前插入
    //   ldarg.0                    // this (GUILayoutMenuScript 实例)
    //   call FontRouter::Route()   // 翻译文本 + 替换字体
    // 因为是在 BeginGUI 最顶部，所有后续 GUILayout.Label 看到的都是已翻译的文本
    // ====================================================================
    static void InjectBeginGUIHook(Cecil.AssemblyDefinition asm)
    {
        Cecil.TypeDefinition guiLayoutMenu = null;
        Cecil.MethodDefinition beginGUI = null;
        foreach (Cecil.TypeDefinition t in asm.MainModule.Types)
        {
            if (t.Name == "GUILayoutMenuScript")
            {
                guiLayoutMenu = t;
                foreach (Cecil.MethodDefinition m in t.Methods)
                    if (m.Name == "BeginGUI" && m.HasBody) { beginGUI = m; break; }
                break;
            }
        }
        if (beginGUI == null) { Console.WriteLine("WARNING: BeginGUI not found"); return; }

        // 从 PunchLoader.dll Import FontRouter.Route(MonoBehaviour)
        Cecil.MethodReference routeMethod = null;
        if (File.Exists(PUNCHLOADER_DLL))
        {
            Cecil.AssemblyDefinition punchLoaderAsm = Cecil.AssemblyDefinition.ReadAssembly(
                PUNCHLOADER_DLL, new Cecil.ReaderParameters { ReadSymbols = false });
            foreach (Cecil.TypeDefinition t in punchLoaderAsm.MainModule.Types)
            {
                if (t.Name == "FontRouter")
                {
                    foreach (Cecil.MethodDefinition m in t.Methods)
                        if (m.Name == "Route") { routeMethod = asm.MainModule.Import(m); break; }
                    break;
                }
            }
        }
        if (routeMethod == null)
        {
            Console.WriteLine("WARNING: FontRouter.Route not found in PunchLoader.dll");
            return;
        }

        CecilCil.ILProcessor il = beginGUI.Body.GetILProcessor();
        CecilCil.Instruction first = beginGUI.Body.Instructions[0];
        il.InsertBefore(first, il.Create(CecilCil.OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(CecilCil.OpCodes.Call, routeMethod));
        Console.WriteLine("[Injector] Patched BeginGUI with FontRouter.Route hook");
    }
}
