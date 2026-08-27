# PunchLoader

Megabyte Punch (Unity 4.2.2f1, .NET 2.0 / Mono 2.x) 的离线 IL 注入式 mod 框架。

## 目录说明

```
PunchLoader/
├── src/          核心源码
│   ├── injector/  Injector.exe — 用 Mono.Cecil 离线修改 Assembly-CSharp.dll
│   ├── modloader/ PunchLoader.dll — 游戏内 mod 框架
│   └── setup/     PunchLoader.Setup.exe — 安装、备份、注入与卸载
├── ExampleMod/   最小模组开发与 Hook 生命周期示例
├── deps/         版本化编译依赖（含 Retail / Steam 游戏参考 DLL）
├── docs/         技术文档
├── scripts/      编译 / 注入 / 部署脚本
├── build/        本机构建产物（不提交）
└── dist/         发布产物（不提交）
```

## 快速开始

1. 运行 `scripts/build_all.ps1` 编译 PunchLoader 与示例模组
2. 运行 `scripts/deploy.ps1` 部署到游戏目录
3. 运行 `scripts/inject.ps1` 注入 Assembly-CSharp.dll
4. 启动游戏

## 依赖

编译需要 csc (Mono/.NET Framework C# 编译器) 和 Mono.Cecil.dll、UnityEngine.dll、游戏原版 dll。全部在 `deps/` 中已备好。

## 平台限制

- .NET 2.0 / Mono 2.x
- Unity 4.2.2f1
- 不支持 LINQ、System.Core、lambda 表达式

## 构建产物

源码目录不放置 DLL。框架产物写入 `build/`，示例产物写入：

```text
build/ExampleMod/ExampleMod.dll
```

## 官方模组

正式模组使用独立仓库、版本和发布流程：

- [ChineseLocalization](https://github.com/PunchLoader/ChineseLocalization)
- [ContentUnlocker](https://github.com/PunchLoader/ContentUnlocker)
- [DebugTools](https://github.com/PunchLoader/DebugTools)（仅供开发）

PunchLoader 核心仓库不编译、部署或捆绑这些模组。
