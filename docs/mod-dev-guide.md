# Mod 开发指引

## 目录结构

```markdown
{游戏目录}/MegabytePunch_Data/Mods/
└── YourModName/
    ├── plugin.json        ← mod 清单
    ├── YourMod.dll        ← 编译产物
    └── YourMod.cs         ← 源码
```

## plugin.json 格式

```json
{
    "id": "your-mod-id",
    "name": "Your Mod",
    "version": "1.0.0",
    "author": "You",
    "entryType": "YourMod.EntryClass",
    "priority": 0
}
```

| 字段 | 说明 |
|------|------|
| `id` | 唯一标识符 |
| `entryType` | 实现了 `IModPlugin` 的完整类型名（含命名空间） |
| `priority` | 加载顺序，越小越先（默认 0） |

## 实现 IModPlugin

```csharp
using System;
using MBPModLoader;

namespace YourMod
{
    public class EntryClass : IModPlugin
    {
        public string GetId() { return "your-mod-id"; }
        public string GetName() { return "Your Mod"; }
        public string GetVersion() { return "1.0.0"; }

        public void OnLoad()
        {
            // 在这里注册钩子
            HookManager.Register("BeginGUI",
                new Action<MonoBehaviour>(OnBeginGUI));
        }

        public void OnUnload()
        {
            // 清理
        }

        private void OnBeginGUI(MonoBehaviour menu)
        {
            // 每帧 GUI 渲染前执行
        }
    }
}
```

## 可用 Hook 点

| 注册名 | 回调签名 | 触发时机 |
|--------|---------|---------|
| `"BeginGUI"` | `Action<MonoBehaviour>` | 每个菜单渲染前，可修改文本/字体 |
| `"CheckConfirm"` | `Func<string, string>` | 菜单确认时，参数是选中条目的文本，返回英文原文 |

## 多个 mod 共存

多个 mod 注册同一个 hook 点时，按 `priority` 顺序链式执行：
- `"CheckConfirm"`: entry → modA(entry) → modB(result) → ... → DoAction
- `"BeginGUI"`: 按 priority 顺序逐个调用

## 编译示例（.rsp 文件）

```
/target:library
/out:YourMod.dll
/reference:"{游戏目录}/MegabytePunch_Data/Managed/UnityEngine.dll"
/reference:"{游戏目录}/MegabytePunch_Data/Managed/MBPModLoader.dll"
/reference:"{游戏目录}/MegabytePunch_Data/Managed/Assembly-CSharp.dll"
/nostdlib
YourMod.cs
```

编译命令:

```bash
csc @build.rsp
```
