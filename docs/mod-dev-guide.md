# Mod 开发指引

## 目录结构

```text
{游戏目录}/Mods/
└── YourModName/
    ├── plugin.json
    └── YourMod.dll
```

源码和构建脚本不需要随运行包分发。

## plugin.json 格式

```json
{
    "id": "your-mod-id",
    "name": "Your Mod",
    "version": "1.0.0",
    "author": "You",
    "entryType": "YourMod.EntryClass",
    "priority": 0,
    "requiresRestart": false
}
```

| 字段 | 说明 |
|------|------|
| `id` | 唯一标识符 |
| `entryType` | 实现 `IModPlugin` 的完整类型名（含命名空间） |
| `priority` | 加载顺序，数值越小越先加载（默认 0） |
| `requiresRestart` | 为 `true` 时，启用状态在重启游戏后生效（默认 `false`） |

## 实现 IModPlugin

```csharp
using PunchLoader;
using UnityEngine;

namespace YourMod
{
    public class EntryClass : IModPlugin
    {
        private BeginGUIHandler _beginGUIHandler;

        public string GetId() { return "your-mod-id"; }
        public string GetName() { return "Your Mod"; }
        public string GetVersion() { return "1.0.0"; }

        public void OnLoad()
        {
            _beginGUIHandler = OnBeginGUI;
            HookManager.Register(_beginGUIHandler);
        }

        public void OnUnload()
        {
            HookManager.Unregister(_beginGUIHandler);
            _beginGUIHandler = null;
        }

        private void OnBeginGUI(MonoBehaviour menu)
        {
            // 每帧菜单 GUI 渲染前执行。
        }
    }
}
```

每个已注册的回调都应在 `OnUnload()` 中注销，避免模组热关闭后继续执行。
仓库根目录的 `ExampleMod/` 提供了可直接编译的最小实现。

## 可用 Hook 类型

| 委托类型 | 回调签名 | 触发时机 |
|----------|----------|----------|
| `BeginGUIHandler` | `void (MonoBehaviour menu)` | 菜单 GUI 渲染前 |
| `PreDoActionHandler` | `string (string action)` | 菜单动作执行前 |
| `TextTransformHandler` | `string (string text)` | GUILayout 文本渲染前 |
| `GUILayoutLabelHandler` | `bool (string original, string rendered, GUIStyle style, GUILayoutOption[] options)` | 接管 GUILayout 标签绘制 |
| `TextMeshTextHandler` | `bool (TextMesh mesh, string original)` | TextMesh 文本写入前 |

通过对应的强类型委托调用 `HookManager.Register(...)` 和
`HookManager.Unregister(...)`。同类回调按照模组加载和注册顺序执行；单个回调抛出异常不会阻断后续回调。

## 编译示例（.rsp 文件）

```text
/target:library
/out:YourMod.dll
/reference:"{游戏目录}/MegabytePunch_Data/Managed/UnityEngine.dll"
/reference:"{游戏目录}/MegabytePunch_Data/Managed/PunchLoader.dll"
/reference:"{游戏目录}/MegabytePunch_Data/Managed/Assembly-CSharp.dll"
/nostdlib
YourMod.cs
```

编译命令：

```text
csc @build.rsp
```
