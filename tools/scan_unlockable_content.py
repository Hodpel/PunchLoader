#!/usr/bin/env python3
"""Scan exported Megabyte Punch assets for color palettes and unlock paths."""

from __future__ import annotations

import argparse
import csv
import re
from collections import defaultdict
from pathlib import Path


GUID_RE = re.compile(r"guid:\s*([0-9a-f]{32})")
COLOR_ENTRY_RE = re.compile(
    r"^  - name: (.+?)\r?\n    mat: \{fileID: 2100000, guid: ([0-9a-f]{32}), type: 2\}",
    re.MULTILINE,
)


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8", errors="replace")


def build_guid_maps(assets: Path) -> tuple[dict[str, Path], dict[str, str]]:
    guid_to_asset: dict[str, Path] = {}
    asset_to_guid: dict[str, str] = {}
    for meta in assets.rglob("*.meta"):
        match = re.search(r"^guid:\s*([0-9a-f]{32})\s*$", read_text(meta), re.MULTILINE)
        if not match:
            continue
        asset = Path(str(meta)[:-5])
        rel = asset.relative_to(assets).as_posix()
        guid_to_asset[match.group(1)] = asset
        asset_to_guid[rel] = match.group(1)
    return guid_to_asset, asset_to_guid


def build_reference_map(assets: Path) -> dict[str, set[str]]:
    references: dict[str, set[str]] = defaultdict(set)
    searchable = {".prefab", ".unity", ".asset", ".mat", ".controller", ".anim"}
    for path in assets.rglob("*"):
        if not path.is_file() or path.suffix.lower() not in searchable:
            continue
        text = read_text(path)
        rel = path.relative_to(assets).as_posix()
        for guid in set(GUID_RE.findall(text)):
            references[guid].add(rel)
    return references


def shader_profile(shader_path: str) -> tuple[str, str]:
    name = Path(shader_path).name.lower()
    if name == "toony-lighted.shader":
        return "标准卡通调色板", "不透明 Toon/Lighted；常规三色/图案贴图"
    if name == "normal-glossy.shader":
        return "高光/金属质感", "不透明 Specular；适合金色、骨骼或高光表面"
    if name == "shader.shader":
        return "半透明动态边缘", "Rim/Shield 双纹理；包含透明度和滚动参数"
    return "其他渲染", shader_path or "无法解析 Shader"


def material_info(path: Path, guid_to_asset: dict[str, Path], assets: Path) -> dict[str, str]:
    text = read_text(path)
    shader_match = re.search(r"m_Shader:.*guid:\s*([0-9a-f]{32})", text)
    shader_guid = shader_match.group(1) if shader_match else ""
    shader_asset = guid_to_asset.get(shader_guid)
    shader_path = shader_asset.relative_to(assets).as_posix() if shader_asset else ""
    profile, note = shader_profile(shader_path)
    texture_guids = []
    for match in re.finditer(r"m_Texture:.*guid:\s*([0-9a-f]{32})", text):
        guid = match.group(1)
        if guid not in texture_guids:
            texture_guids.append(guid)
    textures = []
    for guid in texture_guids:
        target = guid_to_asset.get(guid)
        textures.append(target.relative_to(assets).as_posix() if target else guid)
    opacity = re.search(r"name: _Opacity\s*\r?\n\s*second:\s*([^\r\n]+)", text)
    color = re.search(r"name: _Color\s*\r?\n\s*second:\s*(\{[^\r\n]+\})", text)
    return {
        "shader": shader_path,
        "render_profile": profile,
        "render_note": note,
        "textures": "; ".join(textures),
        "opacity": opacity.group(1).strip() if opacity else "",
        "color": color.group(1).strip() if color else "",
    }


def compact_refs(refs: set[str], excluded: set[str]) -> list[str]:
    return sorted(ref for ref in refs if ref not in excluded)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("assets", type=Path, help="ExportedProject/Assets directory")
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--csv", type=Path, required=True)
    parser.add_argument("--part-csv", type=Path)
    args = parser.parse_args()

    assets = args.assets.resolve()
    guid_to_asset, asset_to_guid = build_guid_maps(assets)
    references = build_reference_map(assets)

    gui_path = assets / "GameObject" / "ColorGUI.prefab"
    entries = COLOR_ENTRY_RE.findall(read_text(gui_path))
    registered_by_guid = {guid: (index, name) for index, (name, guid) in enumerate(entries)}

    pickup_rows = {}
    for index in range(30):
        prefab = assets / "GameObject" / f"Color{index}.prefab"
        rel = prefab.relative_to(assets).as_posix()
        guid = asset_to_guid.get(rel, "")
        refs = compact_refs(references.get(guid, set()), {rel}) if guid else []
        pickup_rows[index] = {"prefab": rel, "guid": guid, "references": refs}

    palette_dir = assets / "Resources" / "parts" / "partpalets"
    rows = []
    for material in sorted(palette_dir.glob("*.mat"), key=lambda p: p.name.lower()):
        rel = material.relative_to(assets).as_posix()
        guid = asset_to_guid.get(rel, "")
        registered = registered_by_guid.get(guid)
        info = material_info(material, guid_to_asset, assets)
        refs = compact_refs(references.get(guid, set()), {rel})
        if registered:
            index, display_name = registered
            pickup = pickup_rows[index]
            pickup_refs = pickup["references"]
            if pickup_refs:
                status = "正式登记且存在获取引用"
                recommendation = "正常内容；仍需运行时核对实际关卡入口"
            else:
                status = "正式登记但未发现获取引用"
                recommendation = "隐藏/不可正常获取候选；优先加入解锁器测试"
            color_index = str(index)
            pickup_prefab = pickup["prefab"]
            pickup_ref_text = "; ".join(pickup_refs)
        else:
            display_name = ""
            color_index = ""
            pickup_prefab = ""
            pickup_ref_text = ""
            if refs:
                status = "未登记的专用材质"
                recommendation = "先确认用途；可能属于 Boss、技能或特效，不应直接当颜色开放"
            else:
                status = "未登记且未发现外部引用"
                recommendation = "未实装/孤立资源候选；需要运行时预览"
        rows.append({
            "color_index": color_index,
            "display_name": display_name,
            "material": rel,
            "render_profile": info["render_profile"],
            "shader": info["shader"],
            "textures": info["textures"],
            "opacity": info["opacity"],
            "main_color": info["color"],
            "status": status,
            "pickup_prefab": pickup_prefab,
            "pickup_references": pickup_ref_text,
            "material_references": "; ".join(refs),
            "recommendation": recommendation,
        })

    args.csv.parent.mkdir(parents=True, exist_ok=True)
    with args.csv.open("w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)

    official = [row for row in rows if row["color_index"]]
    hidden = [row for row in official if "未发现" in row["status"]]
    extras = [row for row in rows if not row["color_index"]]
    orphaned = [row for row in extras if "未发现" in row["status"]]

    lines = [
        "# 内容解锁器：颜色资源扫描报告",
        "",
        "> 本报告由 `tools/scan_unlockable_content.py` 从 Unity 导出资源静态生成。",
        "> “存在获取引用”表示资源图中发现了引用，不等同于已经人工确认玩家一定能在正常流程取得。",
        "",
        "## 扫描结论",
        "",
        f"- 调色板材质总数：**{len(rows)}**",
        f"- `ColorGUI` 正式登记颜色：**{len(official)}**（存档数组也固定为 30 项）",
        f"- 正式登记但未发现拾取引用：**{len(hidden)}**",
        f"- 未登记材质：**{len(extras)}**",
        f"- 未登记且未发现外部引用：**{len(orphaned)}**",
        "",
        "## 渲染类型",
        "",
        "| 类型 | Shader | 特征 |",
        "|---|---|---|",
        "| 标准卡通调色板 | `Toon/Lighted` | 不透明，使用颜色/图案贴图和 Toon Ramp |",
        "| 高光/金属质感 | `Specular` | 不透明，具有高光；`Gold` 属于这一类 |",
        "| 半透明动态边缘 | `Rim` | 双纹理、透明度和滚动参数；`WhiteTransparant` 属于这一类 |",
        "",
        "## 正式登记的 30 种颜色",
        "",
        "| ID | 游戏名称 | 材质 | 渲染类型 | 静态获取判断 |",
        "|---:|---|---|---|---|",
    ]
    for row in sorted(official, key=lambda item: int(item["color_index"])):
        lines.append(
            f"| {row['color_index']} | {row['display_name']} | `{Path(row['material']).name}` | "
            f"{row['render_profile']} | {row['status']} |"
        )

    lines += [
        "",
        "## 正式登记但疑似无法正常取得",
        "",
    ]
    if hidden:
        for row in hidden:
            lines.append(
                f"- **#{row['color_index']} {row['display_name']}**：`{Path(row['material']).name}`；"
                "存在 `ColorN` 拾取物预制体，但未在场景、关卡或其他预制体中发现对该拾取物的引用。"
            )
    else:
        lines.append("- 未发现。所有正式颜色的拾取物都至少存在一个静态引用。")

    lines += [
        "",
        "## 未登记材质",
        "",
        "| 材质 | 渲染类型 | 外部引用 | 初步分类 |",
        "|---|---|---:|---|",
    ]
    for row in extras:
        ref_count = len([value for value in row["material_references"].split("; ") if value])
        lines.append(
            f"| `{Path(row['material']).name}` | {row['render_profile']} | {ref_count} | {row['status']} |"
        )

    lines += [
        "",
        "## 解锁器实施分类",
        "",
        "1. **直接解锁候选**：正式登记但没有正常获取引用的颜色。它们已经具备名称、材质、仓库显示和存档槽。",
        "2. **正常颜色补全**：正式登记且有获取引用，但因关卡、平台或流程问题实际无法取得的项目；需要运行时逐项验证。",
        "3. **特殊材质预览**：Boss、技能、金属和半透明材质。先在独立预览角色上测试，再决定是否允许保存为玩家颜色。",
        "4. **禁止直接开放**：只适用于特定模型、依赖特殊 UV/Shader 参数，或会造成透明、闪烁、材质滚动异常的资源。",
        "",
        "## 下一步",
        "",
        "- 制作运行时颜色预览器，按 ID 和材质名切换玩家全身材质。",
        "- 逐项记录外观、透明度、动画、高光、部件兼容性和存档重载结果。",
        "- 将验证结果回填到 CSV，形成内容解锁器白名单。",
        "- 对 Boss 专属部件另做一轮 Prefab、掉落和收藏入口扫描。",
        "",
        f"完整逐项数据：`{args.csv.name}`",
    ]
    args.report.write_text("\n".join(lines) + "\n", encoding="utf-8")

    # Body-part pass. The collection completion threshold is 150, while the
    # exported project contains 158 BodyPart prefabs. Description-ID duplicates
    # identify the eight extra resource variants; Boss/Emperor naming alone does
    # not imply that a part is hidden or unobtainable.
    body_script_meta = assets / "Scripts" / "Assembly-CSharp" / "BodyPartScript.cs.meta"
    body_script_guid_match = re.search(
        r"^guid:\s*([0-9a-f]{32})\s*$", read_text(body_script_meta), re.MULTILINE
    )
    body_script_guid = body_script_guid_match.group(1) if body_script_guid_match else ""
    description_path = assets / "TextAsset" / "partDescriptionData.txt"
    descriptions = {
        int(index): name.strip()
        for index, name in re.findall(r"^#(\d+)\s+(.+?)#\s*$", read_text(description_path), re.MULTILINE)
    }
    part_rows = []
    body_dir = assets / "Resources" / "parts" / "bodyparts"
    for prefab in sorted(body_dir.glob("*.prefab"), key=lambda path: path.name.lower()):
        text = read_text(prefab)
        if body_script_guid and body_script_guid not in text:
            continue
        index_match = re.search(
            rf"m_Script: .*guid: {re.escape(body_script_guid)}.*?\n(?:.*\n){{0,8}}?\s+index:\s*(-?\d+)",
            text,
        )
        index = int(index_match.group(1)) if index_match else -1
        rel = prefab.relative_to(assets).as_posix()
        guid = asset_to_guid.get(rel, "")
        refs = compact_refs(references.get(guid, set()), {rel}) if guid else []
        lower = prefab.stem.lower()
        obvious_internal = "unobtainable" in lower or "unused" in lower
        boss_named = "boss" in lower or "emperor" in lower
        if obvious_internal:
            category = "明确内部/不可获取命名"
        elif not refs:
            category = "未发现静态引用"
        else:
            category = "一般部件或已有引用"
        part_rows.append({
            "prefab": rel,
            "description_id": str(index) if index >= 0 else "",
            "description_name": descriptions.get(index, ""),
            "category": category,
            "static_reference_count": str(len(refs)),
            "static_references": "; ".join(refs),
            "_stem": prefab.stem,
            "_boss_named": boss_named,
        })

    description_counts = defaultdict(int)
    for row in part_rows:
        description_counts[row["description_id"]] += 1
    for row in part_rows:
        duplicate_id = bool(row["description_id"]) and description_counts[row["description_id"]] > 1
        stem_lower = row["_stem"].lower()
        if "unobtainable" in stem_lower or "unused" in stem_lower:
            row["category"] = "仓库外重复变体（明确不可获取命名）"
        elif duplicate_id and stem_lower.startswith("skel_"):
            row["category"] = "仓库外重复变体（最终Boss骨架）"
        elif duplicate_id:
            row["category"] = "正式收藏项（存在重复资源变体）"
        elif row["_boss_named"]:
            row["category"] = "正式收藏项（Boss命名）"
        row.pop("_stem")
        row.pop("_boss_named")

    if args.part_csv:
        args.part_csv.parent.mkdir(parents=True, exist_ok=True)
        with args.part_csv.open("w", encoding="utf-8-sig", newline="") as handle:
            writer = csv.DictWriter(handle, fieldnames=list(part_rows[0]))
            writer.writeheader()
            writer.writerows(part_rows)

    extra_variants = [row for row in part_rows if row["category"].startswith("仓库外重复变体")]
    boss_parts = [row for row in part_rows if row["category"] == "正式收藏项（Boss命名）"]
    no_ref_parts = [row for row in part_rows if row["category"] == "未发现静态引用"]
    with args.report.open("a", encoding="utf-8") as handle:
        handle.write("\n## 部件资源初筛\n\n")
        handle.write(f"- 含 `BodyPartScript` 的部件预制体：**{len(part_rows)}**\n")
        handle.write("- 正式收藏说明 ID：**150**（#0–#149）\n")
        handle.write(f"- 仓库外重复资源变体：**{len(extra_variants)}**\n")
        handle.write(f"- 属于正式收藏项的 Boss/皇帝命名 prefab：**{len(boss_parts)}**\n")
        handle.write(f"- 未发现静态引用的其他部件：**{len(no_ref_parts)}**\n\n")
        handle.write(
            "部件主要通过 `Resources.Load(\"Parts/BodyParts/\" + name)` 动态加载，因此“未发现静态引用”"
            "不能直接证明不可获取。Boss/皇帝命名也不是隐藏判据；皇帝套等资源属于正常的 150 项收藏。\n\n"
        )
        handle.write("### 仓库外重复资源变体\n\n")
        if extra_variants:
            for row in extra_variants:
                handle.write(
                    f"- `{Path(row['prefab']).name}`：说明 ID {row['description_id'] or '无'}，"
                    f"说明名 `{row['description_name'] or '无'}`，分类 `{row['category']}`。\n"
                )
        else:
            handle.write("- 未发现。\n")
        handle.write("\n其余 Boss/皇帝命名 prefab 均保留在 CSV 中，但不再列为隐藏候选。\n")
        if args.part_csv:
            handle.write(f"\n完整部件初筛数据：`{args.part_csv.name}`\n")

    print(f"materials={len(rows)} official={len(official)} hidden={len(hidden)} extras={len(extras)} orphaned={len(orphaned)}")
    print(f"parts={len(part_rows)} extra_variants={len(extra_variants)} formal_boss_named={len(boss_parts)} no_static_ref={len(no_ref_parts)}")
    print(args.report)
    print(args.csv)
    if args.part_csv:
        print(args.part_csv)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
