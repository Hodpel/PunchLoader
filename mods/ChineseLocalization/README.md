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

生成的常规与小字号图集（`font_atlas*.png`、`glyphs*.tsv`）与 DLL、`plugin.json` 一并放入游戏的 `MegabytePunch_Data\Mods\ChineseLocalization\`。

`scripts\build_all.ps1` 会生成两类 TextMesh 图集：`dialogue_font_atlas.png` / `dialogue_glyphs.tsv` 使用 Visitor TT2 BRK ASCII，供对话与零件说明使用；`part_font_atlas.png` / `part_glyphs.tsv` 使用游戏原始 ACKNOWTT ASCII，供零件名称与列表使用。两者都会为所需译文补入 BoutiqueBitmap 9x9 Bold 中文字符。

字体：BoutiqueBitmap 9x9 Bold 1.93（Add Spacing），采用 SIL Open Font License 1.1；发布包必须保留 `FONT_LICENSE.txt` 与 `FONT_NOTICE.md`。英文 ASCII 字形来自游戏原版 ACKNOWTT。

对话验证的中文字体同样为 BoutiqueBitmap 9x9 Bold 1.93（Add Spacing）；许可说明由 `FONT_LICENSE.txt` 与 `FONT_NOTICE.md` 覆盖。 

