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
//   2. GUILayoutMenuScript.CheckConfirm() → DoAction 前调 HookDispatcher.PreDoAction()
//   3. GUILayoutMenuScript.BeginGUI() 顶部 → HookDispatcher.OnBeginGUI(this)
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

            // === 步骤6: 将所有 GUILayout.Label 调用改为通用文本包装器 ===
            // 文本只在绘制时转换，原始 menuEntries 始终保留动作键。
            InjectGUILayoutLabelHooks(asm);

            // === 步骤7: 将 TextMesh.text 赋值改为通用对话包装器 ===
            // 中文在 setter 前写入，因此对话框不会先显示一帧英文。
            InjectTextMeshSetTextHooks(asm);

            // === 步骤8: 写回修改后的 dll ===
            asm.Write(TARGET_DLL);
            Console.WriteLine("[Injector] Done.");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("[Injector] ERROR: " + ex); return 1; }
    }

    // ====================================================================
    // 钩子2: CheckConfirm() 中 DoAction 前插入通用动作预处理
    // 在 GUILayoutMenuScript.CheckConfirm() 中，每个 callvirt DoAction 之前
    // 插入 call HookDispatcher.PreDoAction(string)
    // mod 可在此把显示文本恢复为动作键，或实现其他确认前处理。
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

        // 从 PunchLoader.dll 中 Import HookDispatcher.PreDoAction(string)
        Cecil.MethodReference preDoAction = null;
        if (File.Exists(PUNCHLOADER_DLL))
        {
            Cecil.AssemblyDefinition punchLoaderAsm = Cecil.AssemblyDefinition.ReadAssembly(
                PUNCHLOADER_DLL, new Cecil.ReaderParameters { ReadSymbols = false });
            foreach (Cecil.TypeDefinition t in punchLoaderAsm.MainModule.Types)
            {
                if (t.FullName == "PunchLoader.HookDispatcher")
                {
                    foreach (Cecil.MethodDefinition m in t.Methods)
                    {
                        if (m.Name == "PreDoAction" && m.Parameters.Count == 1)
                        {
                            preDoAction = asm.MainModule.Import(m);
                            break;
                        }
                    }
                    break;
                }
            }
        }
        if (preDoAction == null)
        {
            Console.WriteLine("WARNING: HookDispatcher.PreDoAction not found in PunchLoader.dll");
            return;
        }

        CecilCil.ILProcessor il = checkConfirm.Body.GetILProcessor();

        // 在 CheckConfirm 的 IL 中，DoAction 调用模式是:
        //   ldarg.0                       // this
        //   ldarg.0 / ldfld menuEntries / ldarg.0 / ldfld selected / ldelem.ref  // menuEntries[selected]
        //   callvirt GUILayoutMenuScript::DoAction(string)
        // 在 DoAction 前插 call HookDispatcher::PreDoAction(string)
        // 栈变化: [this, entry] → [this, transformed_entry]

        int patches = 0;
        CecilCil.Instruction instr = checkConfirm.Body.Instructions[0];
        while (instr != null)
        {
            if (instr.OpCode == CecilCil.OpCodes.Callvirt &&
                ((Cecil.MethodReference)instr.Operand).Name == "DoAction")
            {
                il.InsertBefore(instr, il.Create(CecilCil.OpCodes.Call, preDoAction));
                patches++;
                Console.WriteLine("[Injector] Patched DoAction call at IL_" +
                    instr.Offset.ToString("X4") + " with PreDoAction hook");
            }
            instr = instr.Next;
        }

        if (patches == 0)
            Console.WriteLine("[Injector] WARNING: No DoAction calls found in CheckConfirm");
        else
            Console.WriteLine("[Injector] Patched " + patches + " DoAction call(s)");
    }

    // ====================================================================
    // 钩子3: BeginGUI() 顶部插入通用 UI 绘制前回调
    // 在 GUILayoutMenuScript.BeginGUI() 的第一条指令前插入
    //   ldarg.0                    // this (GUILayoutMenuScript 实例)
    //   call HookDispatcher::OnBeginGUI()
    // 因为是在 BeginGUI 最顶部，mod 可在任何 GUILayout 绘制前更新状态与样式。
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

        // 从 PunchLoader.dll Import HookDispatcher.OnBeginGUI(MonoBehaviour)
        Cecil.MethodReference onBeginGUI = null;
        if (File.Exists(PUNCHLOADER_DLL))
        {
            Cecil.AssemblyDefinition punchLoaderAsm = Cecil.AssemblyDefinition.ReadAssembly(
                PUNCHLOADER_DLL, new Cecil.ReaderParameters { ReadSymbols = false });
            foreach (Cecil.TypeDefinition t in punchLoaderAsm.MainModule.Types)
            {
                if (t.FullName == "PunchLoader.HookDispatcher")
                {
                    foreach (Cecil.MethodDefinition m in t.Methods)
                        if (m.Name == "OnBeginGUI" && m.Parameters.Count == 1) { onBeginGUI = asm.MainModule.Import(m); break; }
                    break;
                }
            }
        }
        if (onBeginGUI == null)
        {
            Console.WriteLine("WARNING: HookDispatcher.OnBeginGUI not found in PunchLoader.dll");
            return;
        }

        CecilCil.ILProcessor il = beginGUI.Body.GetILProcessor();
        CecilCil.Instruction first = beginGUI.Body.Instructions[0];
        il.InsertBefore(first, il.Create(CecilCil.OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(CecilCil.OpCodes.Call, onBeginGUI));
        Console.WriteLine("[Injector] Patched BeginGUI with HookDispatcher.OnBeginGUI hook");
    }

    // ====================================================================
    // 钩子4: 替换 UnityEngine.GUILayout.Label 的两个原版调用签名。
    // 这覆盖原版菜单的按钮、标题和配置页动态标签；每个字符串在真正绘制前才
    // 进入 HookManager 的文本处理链，因此不会污染游戏的动作键或状态字段。
    // ====================================================================
    static void InjectGUILayoutLabelHooks(Cecil.AssemblyDefinition asm)
    {
        Cecil.MethodReference styledLabel = FindPunchLoaderMethod(
            asm, "GUILayoutLabel", 3);
        Cecil.MethodReference plainLabel = FindPunchLoaderMethod(
            asm, "GUILayoutLabel", 2);
        if (styledLabel == null || plainLabel == null)
        {
            Console.WriteLine("WARNING: HookDispatcher.GUILayoutLabel overloads not found in PunchLoader.dll");
            return;
        }

        int patches = 0;
        foreach (Cecil.TypeDefinition type in asm.MainModule.Types)
        {
            foreach (Cecil.MethodDefinition method in type.Methods)
            {
                if (!method.HasBody) continue;
                CecilCil.Instruction instruction = method.Body.Instructions[0];
                while (instruction != null)
                {
                    if ((instruction.OpCode == CecilCil.OpCodes.Call || instruction.OpCode == CecilCil.OpCodes.Callvirt) &&
                        instruction.Operand is Cecil.MethodReference)
                    {
                        Cecil.MethodReference called = (Cecil.MethodReference)instruction.Operand;
                        if (called.DeclaringType.FullName == "UnityEngine.GUILayout" &&
                            called.Name == "Label" &&
                            called.Parameters.Count == 3 &&
                            called.Parameters[0].ParameterType.FullName == "System.String")
                        {
                            instruction.OpCode = CecilCil.OpCodes.Call;
                            instruction.Operand = styledLabel;
                            patches++;
                        }
                        else if (called.DeclaringType.FullName == "UnityEngine.GUILayout" &&
                            called.Name == "Label" &&
                            called.Parameters.Count == 2 &&
                            called.Parameters[0].ParameterType.FullName == "System.String")
                        {
                            instruction.OpCode = CecilCil.OpCodes.Call;
                            instruction.Operand = plainLabel;
                            patches++;
                        }
                    }
                    instruction = instruction.Next;
                }
            }
        }
        Console.WriteLine("[Injector] Patched " + patches + " GUILayout.Label call(s)");
    }

    // ====================================================================
    // 钩子5: 将 UnityEngine.TextMesh.set_text(string) 变为
    // HookDispatcher.SetTextMeshText(TextMesh, string)。实例调用与静态调用
    // 的参数栈完全一致，因此可以直接替换操作数，不改变控制流。
    // ====================================================================
    static void InjectTextMeshSetTextHooks(Cecil.AssemblyDefinition asm)
    {
        Cecil.MethodReference setText = FindPunchLoaderMethod(asm, "SetTextMeshText", 2);
        if (setText == null)
        {
            Console.WriteLine("WARNING: HookDispatcher.SetTextMeshText not found in PunchLoader.dll");
            return;
        }

        int patches = 0;
        foreach (Cecil.TypeDefinition type in asm.MainModule.Types)
        {
            foreach (Cecil.MethodDefinition method in type.Methods)
            {
                if (!method.HasBody) continue;
                CecilCil.Instruction instruction = method.Body.Instructions[0];
                while (instruction != null)
                {
                    if ((instruction.OpCode == CecilCil.OpCodes.Call || instruction.OpCode == CecilCil.OpCodes.Callvirt) &&
                        instruction.Operand is Cecil.MethodReference)
                    {
                        Cecil.MethodReference called = (Cecil.MethodReference)instruction.Operand;
                        if (called.DeclaringType.FullName == "UnityEngine.TextMesh" &&
                            called.Name == "set_text" && called.Parameters.Count == 1 &&
                            called.Parameters[0].ParameterType.FullName == "System.String")
                        {
                            instruction.OpCode = CecilCil.OpCodes.Call;
                            instruction.Operand = setText;
                            patches++;
                        }
                    }
                    instruction = instruction.Next;
                }
            }
        }
        Console.WriteLine("[Injector] Patched " + patches + " TextMesh.set_text call(s)");
    }

    static Cecil.MethodReference FindPunchLoaderMethod(Cecil.AssemblyDefinition targetAsm,
        string name, int parameterCount)
    {
        if (!File.Exists(PUNCHLOADER_DLL)) return null;
        Cecil.AssemblyDefinition loaderAsm = Cecil.AssemblyDefinition.ReadAssembly(
            PUNCHLOADER_DLL, new Cecil.ReaderParameters { ReadSymbols = false });
        foreach (Cecil.TypeDefinition type in loaderAsm.MainModule.Types)
        {
            if (type.FullName != "PunchLoader.HookDispatcher") continue;
            foreach (Cecil.MethodDefinition method in type.Methods)
            {
                if (method.Name == name && method.Parameters.Count == parameterCount)
                    return targetAsm.MainModule.Import(method);
            }
        }
        return null;
    }
}
