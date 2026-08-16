# ChineseLocalization

PunchLoader 的简体中文模组。它在 `OnLoad()` 中同步加载图集与翻译表，随后才注册渲染回调；文本在 `GUILayout.Label` 真正绘制前转换，原始菜单动作键不会被改写。

## 构建

```powershell
C:\Users\HODPEL\AppData\Local\Programs\Python\Python311\python.exe tools\build_font_atlas.py
C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe @mods\ChineseLocalization\build.rsp
```

生成的 `font_atlas.png` 和 `glyphs.tsv` 与 DLL、`plugin.json` 一并放入游戏的 `MegabytePunch_Data\Mods\ChineseLocalization\`。

字体：Noto Sans SC Regular，采用 SIL Open Font License 1.1；发布包必须保留 `FONT_LICENSE.txt` 与 `FONT_NOTICE.md`。
