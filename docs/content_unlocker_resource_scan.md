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
- 文件名明确标记不可获取/未使用：**1**
- Boss/皇帝命名候选：**47**
- 未发现静态引用的其他部件：**0**

部件主要通过 `Resources.Load("Parts/BodyParts/" + name)` 动态加载，因此“未发现静态引用”不能直接证明不可获取；必须结合敌人掉落表、商店、存档收藏和运行时测试。

### 明确的不可获取命名

- `RangedBossHead2_unobtainable.prefab`：说明 ID 116，说明名 `Overclocked R.A.M. Head`。

### Boss/皇帝部件候选

- `DrillBossArm.prefab` → #14 Electrocrush Dyna-arm
- `DrillBossArmDrill.prefab` → #78 Drill Arm
- `DrillBossChest.prefab` → #15 Big Core
- `DrillBossHead.prefab` → #16 Rhintro Head
- `DrillBossHip.prefab` → #17 Armored Dyna-hip
- `DrillBossLeg.prefab` → #18 Drill Leg
- `DrillBossNewHead.prefab` → #89 Bugga Head
- `DrillBossShld.prefab` → #19 Aero Pipes
- `IceBossArmClaw.prefab` → #92 Scythe Claw
- `IceBossArmMiniRocket.prefab` → #93 Missile Revolver
- `IceBossChest.prefab` → #94 Frostbyte Core
- `IceBossHead.prefab` → #95 Ice-Beak Head
- `IceBossHip.prefab` → #96 Frostbyte Hip
- `IceBossLeg.prefab` → #97 Frostbyte Leg
- `IceBossShld.prefab` → #98 Frostbyte Shoulder
- `PsychicBossArm.prefab` → #32 Kheprion Arm
- `PsychicBossChest.prefab` → #33 Kheprion Core
- `PsychicBossHead.prefab` → #34 Tri-eyed Kheprion Head
- `PsychicBossHip.prefab` → #35 Kheprion Hip
- `PsychicBossLeg.prefab` → #36 Kheprion Leg
- `PsychicBossShld.prefab` → #37 Kheprion Wing
- `RangedBossArm.prefab` → #40 Submachine Gun-arm
- `RangedBossChest.prefab` → #41 R.A.M. Core
- `RangedBossHead.prefab` → #42 R.A.M. Head
- `RangedBossHead2.prefab` → #116 Overclocked R.A.M. Head
- `RangedBossHip.prefab` → #43 R.A.M. Hip
- `RangedBossLeg.prefab` → #44 R.A.M. Leg
- `RangedBossShld.prefab` → #45 R.A.M. Wing
- `Skel_ValkEmperorArm.prefab` → #134 Valk Claw
- `Skel_ValkEmperorChest.prefab` → #135 Duplice
- `Skel_ValkEmperorHead.prefab` → #136 Valk Mask
- `Skel_ValkEmperorHip.prefab` → #137 Valk Hip
- `Skel_ValkEmperorShld.prefab` → #139 Emperors Eye
- `Skel_ValkEmperorTail.prefab` → #134 Valk Claw
- `Skel_ValkEmperorUpperArm.prefab` → #134 Valk Claw
- `SkellyBossArm.prefab` → #47 Subprogram Arm 2
- `SkellyBossChest.prefab` → #48 Khepriogram Core
- `SkellyBossHead.prefab` → #49 Khepriogram Head
- `SkellyBossHip.prefab` → #50 Khepriogram Hip
- `SkellyBossLeg.prefab` → #51 Khepriogram Leg
- `SkellyBossShld.prefab` → #52 Neomoog Wing
- `ValkEmperorArm.prefab` → #134 Valk Claw
- `ValkEmperorChest.prefab` → #135 Duplice
- `ValkEmperorHead.prefab` → #136 Valk Mask
- `ValkEmperorHip.prefab` → #137 Valk Hip
- `ValkEmperorLeg.prefab` → #138 Valk Leg
- `ValkEmperorShld.prefab` → #139 Emperors Eye

完整部件初筛数据：`part_resource_scan.csv`
