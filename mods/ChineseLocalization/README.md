# ChineseLocalization

PunchLoader 的简体中文模组。它在 `OnLoad()` 中同步加载图集与翻译表，随后才注册渲染回调；文本在 `GUILayout.Label` 真正绘制前转换，原始菜单动作键不会被改写。图集保留游戏原始 ACKNOWTT 的 ASCII 字形，并使用 BoutiqueBitmap 中文字形，因此同一行里的英文仍为原版风格。

## 构建

```powershell
py -3.11 tools\build_font_atlas.py `
  --ack-font ..\文件整理\ExportedProject\Assets\Font\ACKNOWTT_white.asset `
  --ack-texture "..\文件整理\ExportedProject\Assets\Texture2D\Font Texture_0.texture2D" `
  --ack-small-font ..\文件整理\ExportedProject\Assets\Font\ACKNOWTT_white_small.asset `
  --ack-small-texture "..\文件整理\ExportedProject\Assets\Texture2D\Font Texture_2.texture2D"
C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe @mods\ChineseLocalization\build.rsp
```

构建产物按用途归入三个目录：

```text
ChineseLocalization/
├─ fonts/           # ack_large、ack_small、visitor 三套图集与映射
├─ data/            # 运行时所需的 UI、对话、零件和技能译文
└─ licenses/        # 字体许可与声明
```

对话自动换行的词块和人工覆盖规则属于构建工具输入，维护在仓库的
`tools/localization/`，不会打包进运行时模组。

三套字体分别是：

- `ack_large`：游戏原始 ACKNOWTT 大字号 ASCII，以及菜单、仓库和零件名称所需中文；
- `ack_small`：游戏原始 ACKNOWTT 小字号 ASCII，以及小字号菜单所需中文；
- `visitor`：Visitor TT2 BRK ASCII，以及对话、零件说明和技能说明所需中文。

同一规格不再按业务页面重复生成图集。所有中文均使用 BoutiqueBitmap 9x9 Bold 1.93（Add Spacing），采用 SIL Open Font License 1.1；发布包必须保留 `licenses/FONT_LICENSE.txt` 与 `licenses/FONT_NOTICE.md`。

