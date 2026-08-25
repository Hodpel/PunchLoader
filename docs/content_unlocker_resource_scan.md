# 内容解锁器：颜色资源扫描报告

> 本报告由 `tools/scan_unlockable_content.py` 从 Unity 导出资源静态生成。
> “存在获取引用”表示资源图中发现了引用，不等同于已经人工确认玩家一定能在正常流程取得。

## 扫描结论

- 调色板材质总数：**44**
- `ColorGUI` 正式登记颜色：**30**（存档数组也固定为 30 项）
- 正式登记但未发现拾取引用：**1**
- 未登记材质：**14**
- 未登记且未发现外部引用：**3**

## 渲染类型

| 类型 | Shader | 特征 |
|---|---|---|
| 标准卡通调色板 | `Toon/Lighted` | 不透明，使用颜色/图案贴图和 Toon Ramp |
| 高光/金属质感 | `Specular` | 不透明，具有高光；`Gold` 属于这一类 |
| 半透明动态边缘 | `Rim` | 双纹理、透明度和滚动参数；`WhiteTransparant` 属于这一类 |

## 正式登记的 30 种颜色

| ID | 游戏名称 | 材质 | 渲染类型 | 静态获取判断 |
|---:|---|---|---|---|
| 0 | Blue Cookie | `bluebeigeMat.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 1 | Blorange | `blueWhiteMat.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 2 | Electro Circus | `DrillBossMat.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 3 | Killer's Grey | `lightbluedarkblueMat.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 4 | Meteor Yellow | `orangeyellowMat.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 5 | Psychic Purple | `PsychicBossMat.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 6 | Fabulous Puncher | `purpleorangeMat.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 7 | Imperial Ember | `RangedBossMat.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 8 | Strawberry Strike | `redblueMat.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 9 | Beta Blue | `turqoiseorangeMat.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 10 | White Cloak | `WhiteTransparant.mat` | 半透明动态边缘 | 正式登记但未发现获取引用 |
| 11 | Swift Reptile | `ArmyGreenMat.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 12 | Mega Mint | `greenbeigeMat.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 13 | Valk Elite | `BlackGreenGrayMat.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 14 | Strong Battery | `yellowturqoiseMat.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 15 | Hydro Assault | `turqoiseblueMat.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 16 | Dark Conductivity | `BlackWater.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 17 | Midnight Shock | `NightShade.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 18 | Sweet Revenge | `Fruit.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 19 | Nuclear System | `Hutch.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 20 | Magma Volta | `Lava.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 21 | Venom Magnetizer | `Poison.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 22 | Lethal Impact | `rocket.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 23 | Red Circuitry | `RedCircuit.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 24 | Burocratica | `Grey.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 25 | Conquistador | `Donald.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 26 | New Jack Swing | `Kawaii.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 27 | Flavor No.1 | `Gold.mat` | 高光/金属质感 | 正式登记且存在获取引用 |
| 28 | Slick Samba | `MintLemon.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |
| 29 | Tesla Deluxe | `SkellyBossMat.mat` | 标准卡通调色板 | 正式登记且存在获取引用 |

## 正式登记但疑似无法正常取得

- **#10 White Cloak**：`WhiteTransparant.mat`；存在 `ColorN` 拾取物预制体，但未在场景、关卡或其他预制体中发现对该拾取物的引用。

## 未登记材质

| 材质 | 渲染类型 | 外部引用 | 初步分类 |
|---|---|---:|---|
| `ArmyGreenYellowMat.mat` | 标准卡通调色板 | 0 | 未登记且未发现外部引用 |
| `beigebordeauxMat.mat` | 标准卡通调色板 | 4 | 未登记的专用材质 |
| `beigedarkorangedarkMat.mat` | 标准卡通调色板 | 1 | 未登记的专用材质 |
| `brownorangeMat.mat` | 标准卡通调色板 | 2 | 未登记的专用材质 |
| `CyberBone.mat` | 高光/金属质感 | 1 | 未登记的专用材质 |
| `DrillAbilityColor.mat` | 标准卡通调色板 | 1 | 未登记的专用材质 |
| `FinalBossMat.mat` | 标准卡通调色板 | 0 | 未登记且未发现外部引用 |
| `greenblueMat.mat` | 标准卡通调色板 | 2 | 未登记的专用材质 |
| `lightgreenbeigeMat.mat` | 标准卡通调色板 | 1 | 未登记的专用材质 |
| `orangeyellow2Mat.mat` | 标准卡通调色板 | 1 | 未登记的专用材质 |
| `pinkbrownMat.mat` | 标准卡通调色板 | 0 | 未登记且未发现外部引用 |
| `purplelightblueMat.mat` | 标准卡通调色板 | 4 | 未登记的专用材质 |
| `salmonpurpleMat.mat` | 标准卡通调色板 | 4 | 未登记的专用材质 |
| `turqoisebeigeMat.mat` | 标准卡通调色板 | 2 | 未登记的专用材质 |

## 解锁器实施分类

1. **直接解锁候选**：正式登记但没有正常获取引用的颜色。它们已经具备名称、材质、仓库显示和存档槽。
2. **正常颜色补全**：正式登记且有获取引用，但因关卡、平台或流程问题实际无法取得的项目；需要运行时逐项验证。
3. **特殊材质预览**：Boss、技能、金属和半透明材质。先在独立预览角色上测试，再决定是否允许保存为玩家颜色。
4. **禁止直接开放**：只适用于特定模型、依赖特殊 UV/Shader 参数，或会造成透明、闪烁、材质滚动异常的资源。

## 下一步

- 制作运行时颜色预览器，按 ID 和材质名切换玩家全身材质。
- 逐项记录外观、透明度、动画、高光、部件兼容性和存档重载结果。
- 将验证结果回填到 CSV，形成内容解锁器白名单。
- 对 Boss 专属部件另做一轮 Prefab、掉落和收藏入口扫描。

完整逐项数据：`color_resource_scan.csv`

## 部件资源初筛

- 含 `BodyPartScript` 的部件预制体：**158**
- 正式收藏说明 ID：**150**（#0–#149）
- 仓库外重复资源变体：**8**
- 属于正式收藏项的 Boss/皇帝命名 prefab：**34**
- 未发现静态引用的其他部件：**0**

部件主要通过 `Resources.Load("Parts/BodyParts/" + name)` 动态加载，因此“未发现静态引用”不能直接证明不可获取。Boss/皇帝命名也不是隐藏判据；皇帝套等资源属于正常的 150 项收藏。

### 仓库外重复资源变体

- `RangedBossHead2_unobtainable.prefab`：说明 ID 116，说明名 `Overclocked R.A.M. Head`，分类 `仓库外重复变体（明确不可获取命名）`。
- `Skel_ValkEmperorArm.prefab`：说明 ID 134，说明名 `Valk Claw`，分类 `仓库外重复变体（最终Boss骨架）`。
- `Skel_ValkEmperorChest.prefab`：说明 ID 135，说明名 `Duplice`，分类 `仓库外重复变体（最终Boss骨架）`。
- `Skel_ValkEmperorHead.prefab`：说明 ID 136，说明名 `Valk Mask`，分类 `仓库外重复变体（最终Boss骨架）`。
- `Skel_ValkEmperorHip.prefab`：说明 ID 137，说明名 `Valk Hip`，分类 `仓库外重复变体（最终Boss骨架）`。
- `Skel_ValkEmperorShld.prefab`：说明 ID 139，说明名 `Emperors Eye`，分类 `仓库外重复变体（最终Boss骨架）`。
- `Skel_ValkEmperorTail.prefab`：说明 ID 134，说明名 `Valk Claw`，分类 `仓库外重复变体（最终Boss骨架）`。
- `Skel_ValkEmperorUpperArm.prefab`：说明 ID 134，说明名 `Valk Claw`，分类 `仓库外重复变体（最终Boss骨架）`。

其余 Boss/皇帝命名 prefab 均保留在 CSV 中，但不再列为隐藏候选。

完整部件初筛数据：`part_resource_scan.csv`
