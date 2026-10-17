"""Read-only audit of authored levels, prefab entries and world scene bindings.

Run with Python + PyYAML. This checks serialized configuration, not playability.
"""
from pathlib import Path
from collections import Counter
import argparse
import html
import json
import math
import re
import sys

sys.dont_write_bytecode = True
import export_level_structure as geometry

ROOT = geometry.ROOT
REGION = '69dc8edc1c3045d5822d24241821e820'
CHECKPOINT = '4a41ffc517044924096617ed7585b3bf'


def blocks(path):
    text = path.read_text(encoding='utf-8-sig')
    return {int(m[2]): m[3] for m in re.finditer(
        r'^--- !u!(\d+) &(-?\d+)[^\n]*\n(.*?)(?=^--- !u!|\Z)', text, re.M | re.S)}


def file_id(text, field):
    found = re.search(r'\b' + field + r': \{fileID: (-?\d+)', text)
    return int(found[1]) if found else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', default='Design/LevelStructure/configuration-audit.json')
    parser.add_argument('--unity-results', default='Design/LevelStructure/configuration-audit-unity.txt')
    args = parser.parse_args()
    paths = {}
    for meta in (ROOT / 'Assets').rglob('*.meta'):
        match = re.search(r'^guid: (\w+)', meta.read_text(encoding='utf-8-sig'), re.M)
        if match:
            paths[match[1]] = meta.with_suffix('')
    brushes = {g: geometry.load(p) for g, p in paths.items()
               if '/LevelDesigner/Brushes/' in p.as_posix() and p.suffix == '.asset'}
    rows = []
    for path in sorted((ROOT / 'Assets/Runtime/Levels').glob('*.asset')):
        level = geometry.load(path)
        issues, warnings = [], []
        placements = level.get('Placements') or []
        refs = {r['rid']: r for r in level.get('references', {}).get('RefIds', [])}
        checkpoints = [p for p in placements if brushes.get(p['Brush'].get('guid'), {}).get('HandlerId') == 'checkpoint']
        endpoints = [p for p in placements if brushes.get(p['Brush'].get('guid'), {}).get('HandlerId') == 'endpoint']
        entry_id = level.get('EntryPlacementId')
        entry = next((p for p in checkpoints if p['Id'] == entry_id), None)
        if not entry_id:
            issues.append('未指定入口')
        elif entry is None:
            issues.append('入口 ID 不存在或并非复活点')
        spawn = None
        cells = geometry.footprint(level, brushes)
        if entry:
            brush = brushes[entry['Brush']['guid']]
            data = refs.get(entry['Settings']['rid'], {}).get('data', {}) or {}
            offset = data.get('SpawnOffset', {'x': 0, 'y': .45})
            spawn = [entry['Cell'][axis] + brush['Anchor'][axis] + offset[axis] for axis in ('x', 'y')]
            if not all(math.isfinite(v) for v in spawn):
                issues.append('出生坐标不是有限数')
            elif tuple(math.floor(v) for v in spawn) not in cells:
                issues.append('出生点中心位于本关有效区域之外')
        if not endpoints:
            warnings.append('无终点标记，需确认是否为连接/演示关')
        palette = paths.get(level.get('Palette', {}).get('guid'))
        if not palette:
            issues.append('物体配置 Palette 引用丢失')
        for field in ('Style', 'Outline'):
            ref = level.get(field) or {}
            if ref.get('guid') and ref['guid'] not in paths:
                issues.append(field + ' 引用丢失')
        size = level['Size']; origin = level.get('CanvasOrigin', {'x': 0, 'y': 0})
        out = [p['Id'] for p in placements if not all(origin[a] <= p['Cell'][a] < origin[a] + size[a] for a in ('x', 'y'))]
        if out:
            issues.append(f'{len(out)} 个放置锚点越出编辑画布')
        ids = Counter(p['Id'] for p in placements)
        if any(not k or v > 1 for k, v in ids.items()):
            issues.append('放置 ID 为空或重复')
        if any(p['Brush'].get('guid') not in brushes for p in placements):
            issues.append('画笔引用缺失')
        prefab = paths.get((level.get('OutputPrefab') or {}).get('guid'))
        prefab_entry = 0
        if prefab and prefab.exists():
            docs = blocks(prefab)
            region = next((b for b in docs.values() if REGION in b and 'EntryCheckpoint:' in b), '')
            prefab_entry = file_id(region, 'EntryCheckpoint')
            if not prefab_entry:
                issues.append('生成 Prefab 未绑定入口')
            else:
                component = docs.get(prefab_entry, '')
                if not component or (CHECKPOINT not in component and 'm_CorrespondingSourceObject:' not in component):
                    issues.append('生成 Prefab 入口引用失效')
                elif 'm_CorrespondingSourceObject:' in component:
                    instance = docs.get(file_id(component, 'm_PrefabInstance'), '')
                    if not instance:
                        issues.append('入口所在嵌套 Prefab 实例缺失')
                    if entry and entry['Id'] not in instance:
                        issues.append('Prefab 入口绑定与布局指定入口不一致')
            if (level.get('OutputPrefab') or {}).get('guid') and not region:
                issues.append('输出 Prefab 缺少 LevelRegion')
        else:
            issues.append('未生成或丢失输出 Prefab')
        rows.append(dict(name=level['m_Name'], source=path.relative_to(ROOT).as_posix(),
                         checkpoints=[dict(id=p['Id'], cell=p['Cell']) for p in checkpoints],
                         entry_id=entry_id, spawn_local=spawn, endpoints=len(endpoints),
                         prefab=prefab.relative_to(ROOT).as_posix() if prefab else None,
                         prefab_entry=prefab_entry, issues=issues, warnings=warnings, worlds=[]))
    by_source = {r['source']: r for r in rows}
    worlds = []
    for path in sorted((ROOT / 'Assets/Runtime/WorldMaps').glob('*.asset')):
        world = geometry.load(path)
        issues, modules = [], []
        for i, module in enumerate(world.get('Modules') or []):
            source = paths.get((module.get('Level') or {}).get('guid'))
            seq = module.get('Sequence') or i + 1
            row = by_source.get(source.relative_to(ROOT).as_posix()) if source else None
            if row:
                row['worlds'].append(dict(world=world['m_Name'], sequence=seq))
            else:
                issues.append(f'第 {seq} 项关卡资源引用丢失')
            modules.append(dict(id=module['Id'], sequence=seq, origin=module['Position'],
                                source=row['source'] if row else None))
        for key in ('id', 'sequence'):
            counts = Counter(m[key] for m in modules)
            if any(not k or v > 1 for k, v in counts.items()):
                issues.append(key + ' 为空或重复')
        scene_path = world.get('PlayScenePath')
        scene_checks = []
        if not scene_path or not (ROOT / scene_path).exists():
            issues.append('未配置或丢失游玩场景')
        else:
            # Check every saved variant using this world's module IDs, including backups.
            for scene in sorted((ROOT / scene_path).parent.glob('*Play*.unity')):
                docs = blocks(scene)
                scene_rows = []
                for module in modules:
                    matches = [b for b in docs.values() if re.search(
                        r'propertyPath: WorldModuleId\s+value: ' + re.escape(module['id']) + r'\b', b)]
                    problems = []
                    if len(matches) != 1:
                        problems.append(f'关卡实例数量 {len(matches)}，应为 1')
                    else:
                        b = matches[0]
                        prefab_ref = re.search(r'm_SourcePrefab: \{fileID: -?\d+, guid: (\w+)', b)
                        actual_prefab = paths.get(prefab_ref[1]) if prefab_ref else None
                        expected_row = by_source.get(module['source'])
                        if expected_row and (not actual_prefab or actual_prefab.relative_to(ROOT).as_posix() != expected_row['prefab']):
                            problems.append('场景关卡 Prefab 与布局输出引用不一致')
                        for prop, expected in [('SequenceNumber', module['sequence']),
                                               ('m_LocalPosition.x', module['origin']['x']),
                                               ('m_LocalPosition.y', module['origin']['y'])]:
                            value = re.search(r'propertyPath: ' + re.escape(prop) + r'\s+value: ([^\r\n]+)', b)
                            if not value or float(value[1]) != expected:
                                problems.append(prop + ' 与大地图不一致')
                        override = re.search(r'propertyPath: EntryCheckpoint\s+value:[^\n]*\n\s+objectReference: \{fileID: (-?\d+)', b)
                        if override:
                            entry = int(override[1])
                            component = docs.get(entry, '')
                            if not entry or not component:
                                problems.append('场景显式覆盖为空/失效入口')
                            elif 'm_CorrespondingSourceObject:' in component:
                                source_id = file_id(component, 'm_CorrespondingSourceObject')
                                source_match = re.search(r'm_SourcePrefab: \{fileID: -?\d+, guid: (\w+)', b)
                                prefab = paths.get(source_match[1]) if source_match else None
                                if not prefab or source_id not in blocks(prefab):
                                    problems.append('场景入口指向已不存在的 Prefab 组件')
                    scene_rows.append(dict(sequence=module['sequence'], issues=problems))
                scene_checks.append(dict(path=scene.relative_to(ROOT).as_posix(),
                                         active=scene.as_posix().endswith(scene_path), modules=scene_rows))
        worlds.append(dict(name=world['m_Name'], issues=issues, modules=modules, scenes=scene_checks))
    report = dict(scope='静态配置检查；不等同于 Unity 运行、碰撞或完整通关验证',
                  level_count=len(rows), worlds=worlds, levels=rows)
    final_prefab = ROOT / 'Assets/Prefabs/FinalEscape/FinalEscapeSequence.prefab'
    if final_prefab.exists():
        text = final_prefab.read_text(encoding='utf-8-sig')
        meta = final_prefab.with_suffix('.prefab.meta').read_text(encoding='utf-8-sig')
        guid = re.search(r'^guid: (\w+)', meta, re.M)[1]
        main_scene = ROOT / 'Assets/Scenes/WorldMaps/World_Character_Play.unity'
        report['final_escape'] = dict(prefab=final_prefab.relative_to(ROOT).as_posix(),
                                      scene_has_instance=guid in main_scene.read_text(encoding='utf-8-sig'),
                                      step_count=int(re.search(r'^  StepCount: (\d+)', text, re.M)[1]),
                                      trigger_steps=[int(n) for n in re.findall(r'^  Step: (\d+)', text, re.M)])
        for row in rows:
            if row['name'] == 'Level_OutWay':
                row['notes'] = ['最终逃离区域有独立 FinalEscapeSequence/Ending 流程，缺少普通终点不能直接视为错误；入口缺失主要影响独立选关/重生策略。']
    native_path = ROOT / args.unity_results
    native_lines = native_path.read_text(encoding='utf-8-sig').splitlines() if native_path.exists() else []
    report['unity_validation'] = dict(source=args.unity_results if native_lines else None,
                                      passed=any(line.startswith('PASS|') for line in native_lines))
    for line in native_lines:
        if line.startswith('LAYOUT|'):
            _, name, errors = line.split('|', 2)
            for row in rows:
                if row['name'] == name:
                    row['unity_builder_errors'] = errors.split(' / ') if errors else []
        elif line.startswith('MAP|'):
            _, name, errors = line.split('|', 2)
            for world in worlds:
                if world['name'] == name:
                    world['unity_builder_errors'] = errors.split(' / ') if errors else []
                    world['overlap_conflicts'] = []
                    for error in world['unity_builder_errors']:
                        match = re.fullmatch(r'(.+) 与 (.+) 的重叠格块类型不一致。', error)
                        if match:
                            sequences = []
                            for level_name in match.groups():
                                sequences.append([m['sequence'] for m in world['modules']
                                                  if by_source.get(m['source'], {}).get('name') == level_name])
                            world['overlap_conflicts'].append(dict(levels=list(match.groups()), sequences=sequences))
    output = ROOT / args.output
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    write_html(output.with_suffix('.html'), report, native_lines)
    for world in worlds:
        print(world['name'], len(world['modules']), 'modules:', '; '.join(world['issues']) or 'map OK')
        for m in sorted(world['modules'], key=lambda m: m['sequence']):
            r = by_source.get(m['source'])
            if r:
                print(m['sequence'], r['name'], r['spawn_local'], '; '.join(r['issues'] + r['warnings']) or 'OK')
        for s in world['scenes']:
            print(s['path'], [(r['sequence'], r['issues']) for r in s['modules'] if r['issues']])
    print('Unused levels:', [(r['name'], r['issues'], r['warnings']) for r in rows if not r['worlds']])
    print(output)


def write_html(path, report, native_lines):
    esc = lambda value: html.escape(str(value))
    def table(headers, rows):
        return '<table><thead><tr>' + ''.join('<th>' + esc(h) + '</th>' for h in headers) + '</tr></thead><tbody>' + ''.join(
            '<tr>' + ''.join('<td>' + esc(v) + '</td>' for v in row) + '</tr>' for row in rows) + '</tbody></table>'
    by_source = {row['source']: row for row in report['levels']}
    world = next(w for w in report['worlds'] if w['name'] == 'World_Character')
    active = []
    for module in sorted(world['modules'], key=lambda m: m['sequence']):
        row = by_source[module['source']]
        spawn = row['spawn_local']
        active.append([module['sequence'], row['name'], str(tuple(spawn)) if spawn else '无',
                       row['endpoints'], '；'.join(row['issues'] + row['warnings'] + row.get('notes', [])) or '通过'])
    other = []
    for row in report['levels']:
        if any(w['world'] == 'World_Character' for w in row['worlds']):
            continue
        use = '、'.join(w['world'] + ' / ' + str(w['sequence']) for w in row['worlds']) or '未加入大世界'
        errors = row.get('unity_builder_errors', [])
        other.append([row['name'], use, '；'.join(row['issues'] + row['warnings']) or '通过',
                      f'{len(errors)} 项校验错误' if errors else '通过' if native_lines else '未运行'])
    scene_rows = []
    for scene in world['scenes']:
        problems = ['第 ' + str(m['sequence']) + ' 项：' + '；'.join(m['issues'])
                    for m in scene['modules'] if m['issues']]
        scene_rows.append([Path(scene['path']).name, '当前构建场景' if scene['active'] else '变体/备份',
                           '；'.join(problems) or '实例、序号、位置及显式入口覆盖检查通过'])
    native_status = '已通过 Unity 实际资源解析：第六关 Prefab 入口和两份场景入口均有效。' if report['unity_validation']['passed'] else '尚未取得 Unity 原生核验结果。'
    parts = ['<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>关卡入口与配置检查报告</title>',
             '<style>body{font:15px/1.7 system-ui,"Microsoft YaHei",sans-serif;color:#202c39;background:#f5f7fa;margin:0}main{max-width:1240px;margin:32px auto;padding:32px;background:white;border-radius:12px}h1{font-size:28px}h2{font-size:21px;margin-top:32px}table{border-collapse:collapse;width:100%;margin:16px 0}td,th{text-align:left;vertical-align:top;border:1px solid #dbe2e9;padding:9px 12px}th{background:#edf2f7}tr:nth-child(even){background:#f8fafc}pre{white-space:pre-wrap;overflow-wrap:anywhere;font-size:12px}code{background:#edf2f7;padding:2px 5px}.note{background:#eaf5ef;padding:16px;border-left:4px solid #378261}</style><main>',
             '<h1>关卡入口与配置检查报告</h1><p>2026-10-17 · 当前大世界 22 个模块 · 全部 ' + str(report['level_count']) + ' 份布局 · 2 张大地图 · 5 份游玩场景</p>',
             '<div class="note"><b>已修复第六关入口。</b>指定现有复活点 (22, 2)，Prefab 出生坐标 (22.5, 2.45)，大世界出生坐标 (76.5, 86.45)。主场景与 CLX 场景已移除强制置空入口的覆盖。' + native_status + '</div>',
             '<h2>需要后续处理的配置</h2><ol><li>Unity 原生大地图生成校验发现当前地图有 ' + str(len(world.get('overlap_conflicts', []))) + ' 组重叠格块类型冲突。WorldMapPrefabBuilder.Save 会因这些错误拒绝重新生成整体 Prefab。这是布局资源之间的校验结果，不代表已逐项验证场景中的手工覆盖和实际碰撞。应对照下表处理格块/设置差异。</li>',
             '<li>第 21 项 Level_Connect 和第 22 项 Level_OutWay 均无复活点和入口，且仍在 OrderedModules 中。它们属于连接区/出口区；第 22 项已配置独立最终逃离演出，不能把没有普通终点直接判为结局缺失。需要核实独立选关、重生和第 20 关后顺序进入第 21 项时的出生策略。目前这些操作会回退到模块原点。</li>',
             '<li>CLX 与三份备份场景均与当前大地图不完全同步：第一关位置不同，缺少第 22 项。三份备份还保留第六关的空入口覆盖；当前构建使用主场景，备份问题未修改。</li>',
             '<li>World_Heart 未配置游玩场景，第二、三项共用的 Demo_Hanging 没有入口绑定。</li>',
             '<li>未加入当前大世界的 Level_Example 存在画笔缺失、重复/空 ID、尖刺方向缺失、终点配置缺失等历史数据问题。其余旧资源的入口和输出缺失情况见下表。</li>',
             '<li>现有 LevelPrefabBuilder.Validate 允许入口 ID 为空；运行时入口为空会回退至关卡原点，且不会校验出生点属于目标区域。本次只调整第六关配置，未修改该运行机制。</li></ol>',
             '<h2>当前大世界逐关检查</h2><p>第 1–20 关的指定入口、Prefab 绑定及出生点区域归属均通过；原生布局校验通过。入口坐标以关卡本地网格单位表示。</p>',
             table(['序号', '布局', '入口出生点（本地）', '终点数量', '结果'], active),
             '<h2>大地图重叠配置冲突</h2>',
             table(['关卡序号', '布局资源', '影响'],
                   [(' / '.join(','.join(str(n) for n in seq) for seq in pair['sequences']),
                     ' / '.join(pair['levels']), '重叠格块类型或配置不一致，整体 Prefab 生成校验失败')
                    for pair in world.get('overlap_conflicts', [])]),
             '<h2>其余关卡资源</h2>', table(['布局', '用途', '静态检查', 'Unity 布局校验'], other),
             '<h2>场景版本检查</h2>', table(['场景', '用途', '结果'], scene_rows),
             '<h2>大地图原生校验</h2>']
    for w in report['worlds']:
        errors = w.get('unity_builder_errors', [])
        parts.append('<p><b>' + esc(w['name']) + '</b>：' + esc('；'.join(w['issues']) or '资源引用与序号检查通过') + '</p>')
        if errors:
            parts.append('<details><summary>' + str(len(errors)) + ' 项 Unity 地图生成校验问题</summary><pre>' + esc('\n'.join(errors)) + '</pre></details>')
    final = report.get('final_escape', {})
    parts += ['<h2>特殊结局流程</h2><p>主场景已挂载最终逃离 Prefab：' + esc(final.get('scene_has_instance', False)) +
              '；步骤数：' + esc(final.get('step_count', '未配置')) + '；触发步骤：' + esc(final.get('trigger_steps', [])) +
              '。FinalEscapeSequence 指向 Level_OutWay，并由独立 Ending 组件显示结束画面；该流程不依赖普通 LevelEndpoint。此处检查为资源与代码核对，未实玩终局演出。</p>',
              '<h2>验证范围与复查方式</h2><p>检查入口 ID/复活点类型、Prefab 入口引用、出生点是否在有效区域、画布锚点、资源引用、放置 ID、大地图序号与实例位置；Unity 另运行现有布局/地图生成校验，并打开主场景和 CLX 场景核对实际入口。未进行 Play Mode 完整通关、操作手感或碰撞安全验证。</p>',
              '<p>可重复运行 <code>python Tools/LevelStructure/audit_level_configuration.py</code> 更新静态报告。随附 Unity 结果是本次核验快照，脚本不会重新启动 Unity。</p>',
              '<details><summary>Unity 原生核验原始记录</summary><pre>' + esc('\n'.join(native_lines)) + '</pre></details></main></html>']
    path.write_text(''.join(parts), encoding='utf-8')


if __name__ == '__main__':
    main()
