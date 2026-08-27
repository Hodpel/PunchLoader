# PunchLoader 分发与安装

PunchLoader 核心发布流程只生成一个包：

- `PunchLoader-v<版本>.zip`：只包含单文件 `PunchLoader.Setup.exe`。

发布包不携带任何版本的游戏 `Assembly-CSharp.dll`。安装器直接识别并备份用户本机的
Retail 或 Steam 原版程序集，避免把错误渠道的 DLL 写入游戏，也避免分发游戏文件。

标准 PunchLoader 包不携带正式模组、示例模组或整合包。官方模组由各自仓库独立发布：

- [ChineseLocalization Releases](https://github.com/PunchLoader/ChineseLocalization/releases)
- [ContentUnlocker Releases](https://github.com/PunchLoader/ContentUnlocker/releases)
- [DebugTools](https://github.com/PunchLoader/DebugTools) 仅提供开发源码和标签。

## 安装流程

用户将 `PunchLoader.Setup.exe` 放到 `MegabytePunch.exe` 所在目录并双击。图形安装器会显示
当前程序集状态、原版备份状态，以及安装/更新和卸载操作。安装器会：

1. 检查游戏目录与受支持原版程序集的 SHA-256；
2. 将原版程序集永久备份到 `MegabytePunch_Data/Managed/PunchLoader.Backup/`；
3. 识别原版、完整注入或部分注入状态；
4. 只从用户本机通过哈希校验的原版程序集生成新文件；
5. 校验注入结果后替换游戏程序集并部署 `PunchLoader.dll`；
6. 对完整注入版本跳过重复注入，仅更新 Loader；
7. 写入 JSON 安装清单。

完整注入状态即使缺少原版备份也可以更新 `PunchLoader.dll`，但安装器会明确提示当前不能
卸载。部分注入状态必须存在通过校验的同版本原版备份，否则安装器会停止并提示通过
Steam 验证游戏文件或恢复正确版本。

当前受支持的原版程序集包括历史零售版与 Steam 版。安装器会保存本机实际原版为
`Assembly-CSharp.original.dll`，卸载时恢复这份备份。

`Assembly-CSharp.before-install.dll` 只在程序集替换事务期间存在；安装成功后立即删除。
`Assembly-CSharp.before-uninstall.dll` 同样只用于卸载失败回滚，卸载成功后立即删除。

## 命令行

无参数启动会打开图形界面。为自动化脚本保留以下命令：

```text
PunchLoader.Setup.exe install
PunchLoader.Setup.exe status
PunchLoader.Setup.exe uninstall
```

PowerShell 调用 GUI 子系统程序时如需等待退出码，请使用：

```powershell
$process = Start-Process .\PunchLoader.Setup.exe -ArgumentList status -Wait -PassThru
$process.ExitCode
```

## 维护者打包

```powershell
powershell -ExecutionPolicy Bypass -File scripts\package_release.ps1 -Version 1.1.0
```

未压缩目录写入 `dist/unpacked/PunchLoader-v<版本>/`，最终 ZIP 写入 `dist/`。
