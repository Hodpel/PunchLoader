# PunchLoader 分发与安装

发布包包含安装器、原版程序集和随发行版提供的简体中文模组：

- `PunchLoader.Setup.exe`：单文件安装器，内嵌 Injector、PunchLoader 和 Mono.Cecil。
- `Assembly-CSharp.dll`：受支持游戏版本的原版程序集，供修复、重装和卸载使用。
- `Mods/ChineseLocalization/`：已编译的简体中文模组及其字体、译文和许可证。

用户将两个文件放到 `MegabytePunch.exe` 所在目录，双击 Setup 即可。安装器会：

1. 检查游戏目录与原版程序集 SHA-256；
2. 将原版程序集永久备份到 `MegabytePunch_Data/Managed/PunchLoader.Backup/`；
3. 识别原版、完整注入或部分注入状态；
4. 只从通过哈希校验的原版程序集生成新文件；
5. 校验注入结果后替换游戏程序集并部署 `PunchLoader.dll`；
6. 对完整注入版本跳过重复注入，仅更新 Loader；
7. 写入 JSON 安装清单。

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

未压缩的发布目录写入 `dist/unpacked/PunchLoader-v<版本>/`，最终 ZIP 直接写入
`dist/PunchLoader-v<版本>.zip`。ZIP 解压后可直接覆盖到游戏根目录。
