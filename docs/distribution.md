# PunchLoader 分发与安装

发布流程生成三个相互独立的包：

- `PunchLoader-v<版本>.zip`：干净的 ModLoader，只包含 Setup 和原版程序集。
- `ChineseLocalization-v<版本>.zip`：独立简体中文模组，解压到游戏的 `Mods` 目录。
- `PunchLoader-v<版本>-with-ChineseLocalization.zip`：可选整合包，同时提供 Loader 和简体中文模组。

用户将两个文件放到 `MegabytePunch.exe` 所在目录，双击 Setup 即可。安装器会：

1. 检查游戏目录与受支持原版程序集的 SHA-256；
2. 将原版程序集永久备份到 `MegabytePunch_Data/Managed/PunchLoader.Backup/`；
3. 识别原版、完整注入或部分注入状态；
4. 只从通过哈希校验的原版程序集生成新文件；
5. 校验注入结果后替换游戏程序集并部署 `PunchLoader.dll`；
6. 对完整注入版本跳过重复注入，仅更新 Loader；
7. 写入 JSON 安装清单。

当前受支持的原版程序集包括历史零售版与 Steam 版。两者文件哈希不同，但均使用相同的
注入入口；安装器会把实际安装前的原版 DLL 备份为 `Assembly-CSharp.original.dll`，并在
安装清单中记录该文件的真实 SHA-256。卸载会恢复这份本机备份，而不会用发布包内的另一版本 DLL 覆盖它。

安装器仅在程序集替换期间创建 `Assembly-CSharp.before-install.dll` 作为事务回滚副本；
完成注入校验并写入安装清单后会立即删除它。`Assembly-CSharp.original.dll` 仍作为卸载所需的永久原版备份保留。

命令行：

```text
PunchLoader.Setup.exe install
PunchLoader.Setup.exe status
PunchLoader.Setup.exe uninstall
```

维护者打包：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\package_release.ps1 -Version 1.0.0
```

未压缩的三个发布目录统一写入 `dist/unpacked/`，三个最终 ZIP 直接写入 `dist/`
根目录。标准 PunchLoader 包始终不携带任何第三方或示例模组。
