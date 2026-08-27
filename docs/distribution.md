# PunchLoader 分发与安装

PunchLoader 核心发布流程只生成一个包：

- `PunchLoader-v<版本>.zip`：只包含 Setup 和通过哈希校验的原版程序集。

标准 PunchLoader 包不携带正式模组、示例模组或整合包。官方模组由各自仓库独立发布：

- [ChineseLocalization Releases](https://github.com/PunchLoader/ChineseLocalization/releases)
- [ContentUnlocker Releases](https://github.com/PunchLoader/ContentUnlocker/releases)
- [DebugTools](https://github.com/PunchLoader/DebugTools) 仅提供开发源码和标签。

## 安装流程

用户将发布包中的两个文件放到 `MegabytePunch.exe` 所在目录，双击 Setup。安装器会：

1. 检查游戏目录与受支持原版程序集的 SHA-256；
2. 将原版程序集永久备份到 `MegabytePunch_Data/Managed/PunchLoader.Backup/`；
3. 识别原版、完整注入或部分注入状态；
4. 只从通过哈希校验的原版程序集生成新文件；
5. 校验注入结果后替换游戏程序集并部署 `PunchLoader.dll`；
6. 对完整注入版本跳过重复注入，仅更新 Loader；
7. 写入 JSON 安装清单。

当前受支持的原版程序集包括历史零售版与 Steam 版。安装器会保存本机实际原版为
`Assembly-CSharp.original.dll`，卸载时恢复这份备份。

`Assembly-CSharp.before-install.dll` 只在程序集替换事务期间存在；安装成功后立即删除。

## 命令行

```text
PunchLoader.Setup.exe install
PunchLoader.Setup.exe status
PunchLoader.Setup.exe uninstall
```

## 维护者打包

```powershell
powershell -ExecutionPolicy Bypass -File scripts\package_release.ps1 -Version 1.1.0
```

未压缩目录写入 `dist/unpacked/PunchLoader-v<版本>/`，最终 ZIP 写入 `dist/`。
