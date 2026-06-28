# PunchLoader

Megabyte Punch (Unity 4.2.2f1, .NET 2.0 / Mono 2.x) 的离线 IL 注入式 mod 框架。

## 目录说明

```
PunchLoader/
├── src/          核心源码
│   ├── injector/  Injector.exe — 用 Mono.Cecil 离线修改 Assembly-CSharp.dll
│   └── modloader/ PunchModLoader.dll — 游戏内 mod 框架
├── deps/         编译依赖（Cecil、UnityEngine、游戏 dll）
├── docs/         技术文档
└── scripts/      编译 / 注入 / 部署脚本
```

## 快速开始

1. 运行 `scripts/build_all.ps1` 编译所有
2. 运行 `scripts/deploy.ps1` 部署到游戏目录
3. 运行 `scripts/inject.ps1` 注入 Assembly-CSharp.dll
4. 启动游戏

## 依赖

编译需要 csc (Mono/.NET Framework C# 编译器) 和 Mono.Cecil.dll、UnityEngine.dll、游戏原版 dll。全部在 `deps/` 中已备好。

## 平台限制

- .NET 2.0 / Mono 2.x
- Unity 4.2.2f1
- 不支持 LINQ、System.Core、lambda 表达式
