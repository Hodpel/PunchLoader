# PunchLoader 架构说明

## 三层架构

```
┌──────────────────────────────────────────────┐
│               Injector.exe                   │
│ 离线 IL 打桩，不包含任何业务逻辑               │
│ 职责: 在目标方法的指定位置插入                 │
│       call HookDispatcher::Xxx               │
│ 不依赖: mod 代码、翻译字典、字体               │
├──────────────────────────────────────────────┤
│           PunchLoader.dll (运行时)          │
│ 框架层 — 提供:                                │
│  - HookDispatcher: IL 层固定桩（被 Injector）  │
│  - HookManager: 运行时回调注册表               │
│  - ModLoaderBehaviour: 扫描 Mods/ 目录        │
│  - ModListMenu: 主菜单注入 "mods" 按钮         │
├──────────────────────────────────────────────┤
│           Mod dll (如 ChineseLoc.dll)          │
│ 业务层 — 在 OnLoad() 里:                      │
│  - HookManager.Register("CheckConfirm", cb)   │
│  - HookManager.Register("BeginGUI", cb)       │
│  mod 之间互不干扰，自由注册/注销                │
└──────────────────────────────────────────────┘
```

## 数据流

```
游戏帧循环:
  BeginGUI()  →  [Injector 打的桩]
              →  call HookDispatcher.OnBeginGUI(this)
              →  HookManager 遍历 "BeginGUI" 注册的回调
              →  每个回调拿到的 this 是当前菜单实例
              →  完成文本翻译 + 字体替换
              →  继续原生 BeginGUI 渲染

  CheckConfirm()  →  [Injector 打的桩]
                  →  call HookDispatcher.PreDoAction(entry) : string
                  →  HookManager 遍历 "CheckConfirm" 注册的回调
                  →  回调把中文转回英文
                  →  返回值传给 DoAction(string)
```

## Injector 的注入点

| 目标方法 | HookDispatcher 桩 | 当前用途 | Mod 注册名 |
|----------|------------------|----------|-----------|
| `MenuScript.Start()` | 直接调 `Bootstrap.Init()` | 启动 mod 系统 | 不可注册 |
| `GUILayoutMenuScript.BeginGUI()` | `OnBeginGUI(self)` | 文本翻译 + 字体替换 | `"BeginGUI"` |
| `GUILayoutMenuScript.CheckConfirm()` | `PreDoAction(entry)` | 中文→英文回译 | `"CheckConfirm"` |

## 共存的 HookDispatcher vs HookManager

HookDispatcher 是 IL 层的 "固定电话"，签名固定，位置固定，被 Injector 硬引用。
HookManager 是运行时的 "电话交换机"，mod 通过它注册/注销回调，支持多播链。

Dispatcher 方法内部只做转发：
```csharp
public static string PreDoAction(string entry)
{
    return HookManager.Dispatch("CheckConfirm", entry) as string ?? entry;
}
```
