# 汉化收尾与后续工作清单

本文记录 v1.0 汉化的完成范围、明确排除项和后续低优先级工作，避免继续扩大当前版本范围。

## v1.0 已完成并验收

- 主菜单、选项菜单和操作设置
- Mods 列表
- 游戏内对话文本
- 背包与轮盘界面
- 零件名称、说明和技能属性
- Parts、Color 与 Builds 仓库
- 仓库和轮盘底部操作提示
- 零件商店的价格、余额和售罄状态
- 关卡完成、奖励与 Boss 击败提示
- 临时状态提示（配色、生命、背包已满、零件获取）
- 死亡、暂停、对战设置和对战结算

## 一、已完成的补充界面

### 1. 零件商店（已实施并验收）

`ShopAbilityScript` 会动态创建价格和余额文字，当前通用译表没有覆盖这些组合。

| 原文 | 暂定译文 |
|---|---|
| `PRICE:` | `价格:` |
| `TOTAL BITS:` | `持有 Bits:` |
| `SOLD OUT.` | `已售罄.` |
| `YES` | `是` |
| `NO` | `否` |

实施要点：

- 零件名称和说明应复用现有零件汉化。
- 动态数值应保留，例如 `价格: 512`、`持有 Bits: 140095457`。
- 单独校准价格框、余额框、售罄状态和购买确认框的字体及对齐。

### 2. 关卡完成与奖励界面（已实施并验收）

运行时记录已确认 `LevelCompleteGUIScript` 仍会显示英文。

| 原文 | 暂定译文 |
|---|---|
| `Level N completed!` | `第 N 关完成!` |
| `GET:` | `获得:` |
| `You've beaten Warlord Bouldar and recieved his drill part.` | `你击败了军阀 Bouldar,获得了他的钻头零件.` |
| `Check the collection chest in your house!` | `请到家中的收藏仓库查看!` |
| `You've beaten General HB-02!` | `你击败了 HB-02 将军!` |
| `You have defeated Grand Khotep Scarb!` | `你击败了 Khotep 领主 Scarb!` |
| `You have defeated Grand Khotep Muer!` | `你击败了 Khotep 领主 Muer!` |
| `You have defeated the Ice-Beak Assassins!` | `你击败了冰喙刺客!` |
| `You have defeated the General HB-03!` | `你击败了 HB-03 将军!` |
| `Tournament won!` | `锦标赛获胜!` |
| `You've received a special part!` | `你获得了一个特殊零件!` |

实施状态：标题和奖励标签使用零件/菜单字体，结果正文使用 Visitor 对话细字组合字体；`LocalizationDebug` 已加入不修改进度、奖励和存档的结算界面预览入口。

### 3. 临时状态提示（已实施并验收）

| 原文 | 当前情况 | 暂定译文/处理方式 |
|---|---|---|
| `Color collected!` | 已验收 | `已获得配色!` |
| `+ 1 life!` | 已验收 | `+ 1 条命!` |
| `+ N bit` | 按当前约定保留原样 | 不调整 |
| `Inventory full!` | 已验收 | `背包已满!` |
| `Part downloaded!` | 已汉化 | 已验收 |
| `Maximum amount reached!` | 已汉化 | 已验收 |

## 二、已完成运行时验收

### 4. 死亡、暂停和对战结算（已验收）

- 游戏结束：`you were deleted!`、`play again!`、`quit`
- 暂停菜单：`pause`、`continue`、`options`、`quit`
- 对战结束：`rematch`、`to lobby`、`quit`
- 对战设置：`lives`、`timer`、`versus options`
- 新游戏确认：删除进度警告、确认与取消
- 手柄确认：是否采用默认手柄控制

以上内容已经进入译表，并已通过运行时触发器完成替换与显示验收。

## 三、v1.0 明确排除项

### 5. 关卡选择、锦标赛和对战大厅的美术文字

运行时和导出场景复查确认，下列文字主要是 `GUITexture`、模型材质或静态图片，不属于普通 `TextMesh`：

- `CUSTOM LEVEL 1/2/3` 与 `BACK`
- `ROUND`、`BATTLE` 等锦标赛大厅标牌
- 其他不影响操作理解的装饰性场景文字

这些内容需要维护多套普通、选中状态图片或直接修改模型材质，收益低且会引入额外缩放与像素清晰度风险，v1.0 保留原版。

## 四、后续低优先级内容

### 6. 自定义关卡编辑器

如果后续覆盖自定义对战关卡功能，需要检查：

- 编辑器工具名称
- 放置、删除和保存提示
- 地图未完成警告
- 自定义关卡选择界面

### 7. 成就与 Steam 信息

程序集能检索到 `UNLOCK ACHIEVEMENT`、`Achievement Status` 和统计保存信息，但其中多数可能只是日志。只翻译实际显示给玩家的成就解锁提示，不处理开发日志和 Steam 状态日志。

### 8. 第三方模组元数据

模组名称、作者和说明由各模组开发者提供，汉化模组不应全局强制翻译。

## v1.0 收尾口径

- 主线冒险、菜单、对话、背包、仓库、零件、技能、商店和常用状态界面作为正式交付范围。
- 图片式文字、模型材质、自定义关卡编辑器、Steam 日志与第三方模组元数据不阻塞 v1.0。
- 后续仅修复实际游玩发现的错译、漏译、排版或崩溃问题，不再为 v1.0 扩展新界面范围。

至此主线冒险模式和常用系统界面的汉化进入维护阶段。
