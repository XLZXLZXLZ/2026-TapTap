"""Export the authored world map without changing Unity assets. Requires PyYAML."""
from pathlib import Path
from collections import deque
import json
import re
import math
import xml.etree.ElementTree as ET
from html import escape
import yaml

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Design' / 'LevelStructure'


def load(path):
    text = path.read_text(encoding='utf-8-sig')
    text = re.sub(r'^%.*\n|^--- !u!\d+ &\d+.*\n', '', text, flags=re.M)
    return yaml.safe_load(text)['MonoBehaviour']


def xy(v):
    return v['x'], v['y']


def footprint(level, brushes):
    cells = set()
    refs = {r['rid']: r for r in level.get('references', {}).get('RefIds', [])}
    for p in level.get('Placements', []):
        if not p['Brush'].get('guid') or brushes[p['Brush']['guid']]['HandlerId'] == 'annotation':
            continue
        x, y = xy(p['Cell'])
        r = refs.get(p['Settings']['rid'], {})
        data = r.get('data', {}) or {}
        n = max(1, min(2048, data.get('MaxLength', 3))) if r.get('type', {}).get('class') == 'PistonPlacementSettings' else 1
        dx, dy = [(1, 0), (0, -1), (-1, 0), (0, 1)][data.get('QuarterTurns', 0) & 3]
        cells.update((x + i * dx, y + i * dy) for i in range(n))
    if level.get('UseCustomBoundary'):
        cells.update(xy(v) for v in level.get('BoundaryCells', []))
        if cells:
            return cells
    if not cells:
        x, y = xy(level.get('CanvasOrigin', {'x': 0, 'y': 0}))
        w, h = xy(level['Size'])
        return {(a, b) for a in range(x, x + w) for b in range(y, y + h)}
    x0, y0 = min(x for x, y in cells) - 1, min(y for x, y in cells) - 1
    x1, y1 = max(x for x, y in cells) + 1, max(y for x, y in cells) + 1
    exterior = {(x0, y0)}
    queue = deque(exterior)
    while queue:
        x, y = queue.popleft()
        for p in [(x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)]:
            a, b = p
            if x0 <= a <= x1 and y0 <= b <= y1 and p not in cells and p not in exterior:
                exterior.add(p)
                queue.append(p)
    return {(x, y) for x in range(x0 + 1, x1) for y in range(y0 + 1, y1) if (x, y) not in exterior}


def read_world():
    paths = {}
    for folder in ['Assets/Runtime/Levels', 'Assets/Runtime/LevelDesigner/Brushes', 'Assets/Runtime/LevelOutlines', 'Assets/Runtime/LevelStyles']:
        for p in (ROOT / folder).glob('*.asset'):
            guid = re.search(r'^guid: (\w+)', p.with_suffix('.asset.meta').read_text(), re.M)[1]
            paths[guid] = p
    brushes = {g: load(p) for g, p in paths.items() if '/Brushes/' in p.as_posix()}
    world = load(ROOT / 'Assets/Runtime/WorldMaps/World_Character.asset')
    modules = []
    for i, m in enumerate(world['Modules']):
        path = paths[m['Level']['guid']]
        level = load(path)
        px, py = xy(m['Position'])
        cells = {(x + px, y + py) for x, y in footprint(level, brushes)}
        modules.append(dict(sequence=m['Sequence'] or i + 1, id=m['Id'], name=level['m_Name'],
                            source=path.relative_to(ROOT).as_posix(), origin=[px, py], cells=cells,
                            bounds=[min(x for x,y in cells), min(y for x,y in cells), max(x for x,y in cells)+1, max(y for x,y in cells)+1],
                            custom=bool(level.get('UseCustomBoundary')), level=level))
    return world, sorted(modules, key=lambda m: m['sequence']), paths


def contours(cells, scale=1):
    edges = {}
    for x, y in cells:
        for a, b, neighbor in [((x,y),(x+1,y),(x,y-1)), ((x+1,y),(x+1,y+1),(x+1,y)),
                               ((x+1,y+1),(x,y+1),(x,y+1)), ((x,y+1),(x,y),(x-1,y))]:
            if neighbor not in cells:
                edges.setdefault(a, set()).add(b)
    loops = []
    while edges:
        start = min(edges)
        a = start
        b = min(edges[a])
        pts = [start]
        while True:
            edges[a].remove(b)
            if not edges[a]:
                del edges[a]
            if b == start:
                break
            pts.append(b)
            dx, dy = b[0]-a[0], b[1]-a[1]
            nxt = max(edges[b], key=lambda c: math.atan2(dx*(c[1]-b[1])-dy*(c[0]-b[0]), dx*(c[0]-b[0])+dy*(c[1]-b[1])))
            a, b = b, nxt
        pts = [p for i,p in enumerate(pts) if (p[0]-pts[i-1][0])*(pts[(i+1)%len(pts)][1]-p[1]) != (p[1]-pts[i-1][1])*(pts[(i+1)%len(pts)][0]-p[0])]
        loops.append([(x/scale,y/scale) for x,y in pts])
    return loops


def clean_structure():
    """Presentation geometry, deliberately separate from authored grid occupancy.

    Shared boundaries use identical endpoints. The left entrance is a solid L;
    the two middle baselines are straight and the connector ends flush with them.
    Preserve the four structural corner flanges; remove only incidental cell teeth.
    """
    def rect(x0,y0,x1,y1):
        return [(x0,y0),(x1,y0),(x1,y1),(x0,y1)]
    polygons = {
        1: rect(11,113.5,40.5,131),
        2: rect(40.5,95.5,53.5,122),
        3: rect(11,95.5,40.5,113.5),
        4: rect(40.5,122,85,131),
        5: rect(53.5,106.5,85,122),
        6: [(53.5,95.5),(73.5,95.5),(73.5,84.5),(85,84.5),(85,106.5),(53.5,106.5)],
        7: rect(22.5,84.5,73.5,95.5),
        8: [(57,73),(91,73),(91,79),(85,79),(85,84.5),(57,84.5)],
        9: [(5,73),(57,73),(57,84.5),(22.5,84.5),(22.5,95.5),(11,95.5),(11,79),(5,79)],
        10: [(54.5,51.5),(62.5,51.5),(62.5,48.5),(85,48.5),(85,60),(91,60),(91,66),(54.5,66)],
        11: [(11,36.5),(25.5,36.5),(25.5,66),(5,66),(5,60),(11,60)],
        12: rect(25.5,38.5,40.5,66),
        13: [(40.5,38.5),(69.5,38.5),(69.5,48.5),(62.5,48.5),(62.5,51.5),(54.5,51.5),(54.5,66),(40.5,66)],
        14: rect(11,25,25.5,36.5),
        15: rect(42.5,28.5,69.5,38.5),
        16: rect(11,4,25.5,25),
        17: rect(69.5,20.5,85,48.5),
        18: [(46.5,23.5),(55.5,23.5),(55.5,4),(85,4),(85,20.5),(69.5,20.5),(69.5,28.5),(46.5,28.5)],
        19: [(25.5,16.5),(55.5,16.5),(55.5,23.5),(46.5,23.5),(46.5,28.5),(42.5,28.5),(42.5,38.5),(25.5,38.5)],
        20: rect(25.5,4,55.5,16.5),
        21: rect(46,66,51,73),
        22: [(2,126),(11,126),(11,131),(8,131),(8,159),(2,159)],
    }
    return {n:[points] for n,points in polygons.items()}


def polygon_cells(loops):
    """Rasterize only for geometry validation; SVG uses the clean vector vertices."""
    result=set()
    for points in loops:
        for y in range(int(min(p[1] for p in points)*2),int(max(p[1] for p in points)*2)):
            for x in range(int(min(p[0] for p in points)*2),int(max(p[0] for p in points)*2)):
                cx,cy=(x+.5)/2,(y+.5)/2
                inside=False
                for a,b in zip(points,points[1:]+points[:1]):
                    if (a[1]>cy)!=(b[1]>cy) and cx<(b[0]-a[0])*(cy-a[1])/(b[1]-a[1])+a[0]:
                        inside=not inside
                if inside:
                    result.add((x,y))
    return result


def validate_clean_structure(polygons):
    cells={n:polygon_cells(loops) for n,loops in polygons.items()}
    union=set.union(*cells.values())
    expected=set()
    for x0,y0,x1,y1 in [(11,73,85,131),(11,4,85,66),(46,66,51,73),(2,126,8,159),(8,126,11,131),
                           (5,73,11,79),(85,73,91,79),(5,60,11,66),(85,60,91,66)]:
        expected.update((x,y) for x in range(x0*2,x1*2) for y in range(y0*2,y1*2))
    assert union==expected, 'Presentation contains a gap or an unwanted protrusion'
    assert sum(map(len,cells.values()))==len(union), 'Presentation regions overlap'
    return cells


def px(p):
    return 88+(p[0]-2)*8, 198+(159-p[1])*8


def path(loops, rounded=False):
    pieces = []
    for pts in loops:
        if rounded:
            # The two lower authored corners are 1-cell stair steps. Replace
            # these display-only steps with the requested continuous arcs.
            clean = []
            seen = set()
            for p in pts:
                corner = (11,4) if p[0]<=13 and p[1]<=6 else (85,4) if p[0]>=83 and p[1]<=6 else None
                if corner:
                    if corner not in seen:
                        clean.append(corner); seen.add(corner)
                else:
                    clean.append(p)
            pts = clean
        # Only the three requested outer silhouette corners are rounded.
        radii = {(85,131):4, (85,4):4, (11,4):4} if rounded else {}
        incoming, outgoing = [], []
        for i,p in enumerate(pts):
            before, after = pts[i-1], pts[(i+1)%len(pts)]
            r = min(radii.get(p,0), math.dist(p,before)/2, math.dist(p,after)/2)
            incoming.append(tuple(p[k]+(before[k]-p[k])*r/math.dist(p,before) for k in [0,1]))
            outgoing.append(tuple(p[k]+(after[k]-p[k])*r/math.dist(p,after) for k in [0,1]))
        a,b = px(outgoing[-1])
        d = [f'M{a:g},{b:g}']
        for i,p in enumerate(pts):
            a,b = px(incoming[i]); d.append(f'L{a:g},{b:g}')
            if incoming[i] != outgoing[i]:
                c,e=px(outgoing[i]); r=math.dist(p,incoming[i])*8
                before,after=pts[i-1],pts[(i+1)%len(pts)]
                turn=(p[0]-before[0])*(after[1]-p[1])-(p[1]-before[1])*(after[0]-p[0])
                d.append(f'A{r:g},{r:g} 0 0 {0 if turn>0 else 1} {c:g},{e:g}')
        d.append('Z')
        pieces.append(' '.join(d))
    return ' '.join(pieces)


def label_position(cells):
    # The centre of the largest axis-aligned interior square keeps labels off thin appendages.
    distance = {p:0 for p in cells if any(q not in cells for q in [(p[0]-1,p[1]),(p[0]+1,p[1]),(p[0],p[1]-1),(p[0],p[1]+1)])}
    queue=deque(distance)
    while queue:
        x,y=queue.popleft()
        for p in [(x-1,y),(x+1,y),(x,y-1),(x,y+1)]:
            if p in cells and p not in distance:
                distance[p]=distance[(x,y)]+1; queue.append(p)
    cx=sum(x for x,y in cells)/len(cells); cy=sum(y for x,y in cells)/len(cells)
    p=max(cells,key=lambda p:(distance[p], -(p[0]-cx)**2-(p[1]-cy)**2))
    return p[0]+.5,p[1]+.5


def color(n):
    return '#e3a29b' if n<=5 else '#8bb7db' if n<=12 else '#e6cf7c' if n<=20 else '#c2c8c3'


def draw(world, modules, display, raw=False):
    if raw:
        union=set.union(*(m['cells'] for m in modules))
        silhouette=path(contours(union))
    else:
        union=set.union(*(polygon_cells(loops) for loops in display.values()))
        silhouette=path(contours(union,2),True)
    s=['<svg xmlns="http://www.w3.org/2000/svg" xmlns:inkscape="http://www.inkscape.org/namespaces/inkscape" width="1440" height="1600" viewBox="0 0 1440 1600">',
       '<title>World Character · 22 关实际结构核对图</title>',
       '<desc>基于实际关卡关系整理的矢量结构图。展示边界规整化，消除半格小凸角，保留中段四处结构凸沿，左上入口归纳为连续 L 形。只对右上、右下、左下外角取圆角。原始占格另附对照图。</desc>',
       '<defs><pattern id="grid" width="80" height="80" patternUnits="userSpaceOnUse"><path d="M80 0H0V80" fill="none" stroke="#dddcd4" stroke-width=".7"/></pattern>',
       f'<clipPath id="silhouette-clip"><path d="{silhouette}" fill-rule="evenodd"/></clipPath>',
       '<marker id="arrow" markerWidth="7" markerHeight="7" refX="5" refY="3" orient="auto"><path d="M0,0 L6,3 L0,6" fill="none" stroke="#405a55" stroke-width="1.3"/></marker>',
       '</defs><style>text{font-family:"Microsoft YaHei","Noto Sans CJK SC",sans-serif;fill:#233831} .muted{fill:#748079} .mono{font-family:Consolas,monospace} .label{font-size:22px;font-weight:700;text-anchor:middle;dominant-baseline:central;paint-order:stroke;stroke:#faf8ee;stroke-width:3;stroke-linejoin:round}</style>',
       '<rect width="1440" height="1600" fill="#f7f5ed"/>',
       '<text x="80" y="65" font-size="14" letter-spacing="3" class="muted">TAPTAP / WORLD CHARACTER / STRUCTURE STUDY</text>',
       '<text x="80" y="122" font-size="42" font-weight="700">关卡结构 · '+('原始边界' if raw else '分区核对稿')+'</text>',
       '<text x="82" y="158" font-size="17" class="muted">22 个关卡 · '+('原始占格边界' if raw else '按实际分区关系规整绘制')+' · 2026.10.18</text>',
       '<rect x="64" y="182" width="770" height="1308" rx="16" fill="url(#grid)"/>',
       f'<path d="{silhouette}" fill="#e6cf7c" fill-rule="evenodd"/>',
       '<g id="level-regions" clip-path="url(#silhouette-clip)">']
    for m in modules:
        n=m['sequence']; loops=contours(m['cells']) if raw else display[n]
        s.append(f'<g id="level-{n:02}" data-module-id="{m["id"]}" data-source="{m["source"]}"><title>{n:02} · {escape(m["name"])} · 原点 {m["origin"]}</title><path d="{path(loops,not raw)}" fill="{color(n)}" fill-rule="evenodd" stroke="#53665e" stroke-width="{1.4 if raw else 1.6}" stroke-linejoin="miter"/></g>')
    s.append('</g>')
    s.append(f'<path id="world-silhouette" d="{silhouette}" fill="none" stroke="#304a3f" stroke-width="2.7" stroke-linejoin="round" fill-rule="evenodd"/>')
    s.append('<g id="level-numbers">')
    for m in modules:
        x,y=px(label_position(m['cells']))
        s.append(f'<text class="label" x="{x:g}" y="{y:g}">{m["sequence"]:02}</text>')
    s.append('</g>')
    s.extend(['<path d="M140 276 H208" fill="none" stroke="#7a8980" stroke-width="1.2"/><text x="220" y="270" font-size="20" font-weight="700">左上新增 / 22</text><text x="220" y="297" class="mono muted" font-size="15">Level_OutWay</text>',
              '<text x="96" y="1464" class="muted" font-size="14">'+('实际占格边界对照 · 网格线每 10 单位' if raw else '规整结构示意 · 保留区域位置与连接关系 · 网格线每 10 单位')+'</text>',
              '<line x1="869" y1="204" x2="869" y2="1468" stroke="#d7d9cf"/>',
              '<text x="905" y="222" font-size="24" font-weight="700">区域与关卡索引</text>'])
    for yy,fill,title in [(264,color(1),'一区 01–05 / 红'),(300,color(6),'二区 06–12 / 蓝'),(336,color(13),'三区 13–20 / 黄'),(372,color(21),'其它 21–22 / 灰')]:
        s.append(f'<rect x="905" y="{yy-17}" width="20" height="20" rx="4" fill="{fill}"/><text x="940" y="{yy}" font-size="17">{title}</text>')
    s.append('<text x="905" y="417" font-size="14" class="muted">编号使用 Sequence；文件名数字不是关卡顺序</text>')
    for i,m in enumerate(modules):
        y=457+i*33
        s.append(f'<circle cx="919" cy="{y-5}" r="12" fill="{color(m["sequence"])}"/><text x="919" y="{y-1}" font-size="12" text-anchor="middle" font-weight="700">{m["sequence"]:02}</text><text x="944" y="{y}" font-size="16" class="mono">{escape(m["name"])}</text>')
    notes = ['边界读取：图块占格 + 自定义范围',
             '共墙：保留原始重叠带与双方边线' if raw else '分界：共享端点，移除半格齿状凸角',
             '外角：保留原始阶梯轮廓，无圆角' if raw else '圆角：右上 / 右下 / 左下，半径 4 格',
             '左上：保留原始通道占格' if raw else '左上：归纳为连续 L 形入口',
             '通行箭头已停用' if raw else '中段：四角保留凸沿，横向基线齐平', '颜色：以关卡 Style 资源分区为准',
             '此图按实际占格绘制，供主图对照' if raw else '原始占格另附 SVG，可对照共墙范围']
    s.append('<line x1="905" y1="1211" x2="1355" y2="1211" stroke="#d7d9cf"/>')
    for i,t in enumerate(notes):
        s.append(f'<text x="905" y="{1248+i*29}" font-size="16" class="muted">{t}</text>')
    s.extend(['<line x1="80" y1="1520" x2="1360" y2="1520" stroke="#cdd1c6"/>',
              '<text x="80" y="1556" font-size="15" class="muted">结构确认用 SVG · 各关卡独立分组，可后续拆分选关素材 · 本次未修改场景、关卡资源或选关逻辑</text>', '</svg>'])
    return '\n'.join(s)


def draw_wireframe(source):
    """Draw strokes inside each region so numbering never sets an overlap priority.

    A shared edge consists of two clipped half-strokes. Identical colors read as
    one line; different colors remain on their respective sides. A masked outer
    band restores the same apparent width along the outside silhouette.
    """
    ns='http://www.w3.org/2000/svg'
    ET.register_namespace('',ns)
    def tag(name):
        return '{'+ns+'}'+name
    def tint(n):
        return '#ba5d59' if n<=5 else '#4384b5' if n<=12 else '#ac8921' if n<=20 else '#6e8179'
    svg=ET.fromstring(source)
    svg.find(tag('title')).text='World Character · 彩色线框结构图'
    svg.find(tag('desc')).text='22 个规整关卡，以区域内侧描边绘制。同色共边合一，异色共边各占自己一侧，不使用关卡序号控制边界遮挡。'
    defs=svg.find(tag('defs'))
    regions=svg.find(f"{tag('g')}[@id='level-regions']")
    silhouette=svg.find(f"{tag('path')}[@id='world-silhouette']")
    silhouette_d=silhouette.get('d')
    svg.remove(silhouette)
    for p in list(svg.findall(tag('path'))):
        if p.get('d')==silhouette_d:
            svg.remove(p)
    mask=ET.SubElement(defs,tag('mask'),{'id':'outer-line-mask','maskUnits':'userSpaceOnUse','x':'0','y':'0','width':'1440','height':'1600'})
    ET.SubElement(mask,tag('path'),{'d':silhouette_d,'fill':'none','stroke':'white','stroke-width':'6','stroke-linejoin':'round'})
    outer=ET.Element(tag('g'),{'id':'outer-colored-lines','mask':'url(#outer-line-mask)','clip-path':'url(#silhouette-clip)'})
    svg.insert(list(svg).index(regions),outer)
    for room in regions:
        n=int(room.get('id').split('-')[1])
        p=room.find(tag('path'))
        d=p.get('d')
        clip_id=f'room-interior-{n:02}'
        clip=ET.SubElement(defs,tag('clipPath'),{'id':clip_id})
        ET.SubElement(clip,tag('path'),{'d':d,'fill-rule':'evenodd'})
        p.set('fill','none'); p.set('stroke',tint(n)); p.set('stroke-width','3')
        p.set('clip-path',f'url(#{clip_id})')
        ET.SubElement(outer,tag('path'),{'d':d,'fill':tint(n),'fill-rule':'evenodd'})
    labels=svg.find(f"{tag('g')}[@id='level-numbers']")
    for text in labels:
        text.set('style',f'fill:{tint(int(text.text))}')
    notes=['画法：不填色，边线沿区域内侧绘制', '同色共边：合并成一条线', '异色共边：双色并排，各占自己一侧',
           '序号：只表示关卡推进顺序', '边界归属：不依赖数字大小或绘制顺序', '左上 / 中段保持规整，三个外角圆角', '保留 22 个独立关卡分组，可继续拆分']
    for text in svg.findall(tag('text')):
        y=text.get('y')
        if y=='122':
            text.text='关卡结构 · 彩色线框稿'
        elif y=='158':
            text.text='22 个关卡 · 同色共边合一 / 异色共边并排 · 2026.10.18'
        elif text.get('x')=='905' and y and int(y) in range(1248,1423,29):
            text.text=notes[(int(y)-1248)//29]
    for rect in list(svg.findall(tag('rect'))):
        if rect.get('x')=='905':
            y=float(rect.get('y'))+10
            n={247:1,283:6,319:13,355:21}[int(rect.get('y'))]
            line=ET.Element(tag('path'),{'d':f'M905 {y:g} H925','stroke':tint(n),'stroke-width':'3','fill':'none'})
            svg.insert(list(svg).index(rect),line)
            svg.remove(rect)
    return ET.tostring(svg,encoding='unicode')


def main():
    world, modules, paths = read_world()
    OUT.mkdir(parents=True, exist_ok=True)
    display=clean_structure()
    validate_clean_structure(display)
    report=[]
    scene=(ROOT / world['PlayScenePath']).read_text(encoding='utf-8-sig')
    scene_modules={}
    for block in scene.split('PrefabInstance:')[1:]:
        match=re.search(r'propertyPath: WorldModuleId\s+value: (\w+)',block)
        if match:
            scene_modules[match[1]]={key:re.search(r'propertyPath: '+re.escape(key)+r'\s+value: ([^\r\n]+)',block)[1] for key in ['SequenceNumber','m_LocalPosition.x','m_LocalPosition.y']}
    for m in modules:
        saved=scene_modules[m['id']]
        assert int(saved['SequenceNumber'])==m['sequence']
        assert [float(saved['m_LocalPosition.x']),float(saved['m_LocalPosition.y'])]==m['origin']
        loops=contours(m['cells'])
        # Green's theorem verifies that contour extraction retained every authored cell.
        area=sum(sum(a[0]*b[1]-a[1]*b[0] for a,b in zip(loop,loop[1:]+loop[:1]))/2 for loop in loops)
        assert area==len(m['cells']), (m['sequence'],area,len(m['cells']))
        report.append({k:v for k,v in m.items() if k not in ['cells','level']} | dict(area=len(m['cells']), raw_contours=loops, display_contours=display[m['sequence']], scene_verified=True))
    overlaps=[]
    for i, a in enumerate(modules):
        for b in modules[i + 1:]:
            overlap = a['cells'] & b['cells']
            if overlap:
                overlaps.append(dict(levels=[a['sequence'],b['sequence']],cells=sorted(overlap)))
    for raw,name in [(False,'world-character-structure.svg'),(True,'world-character-raw-boundaries.svg')]:
        (OUT/name).write_text(draw(world,modules,display,raw),encoding='utf-8')
    (OUT/'world-character-wireframe.svg').write_text(draw_wireframe(draw(world,modules,display)),encoding='utf-8')
    (OUT/'structure-data.json').write_text(json.dumps(dict(source='Assets/Runtime/WorldMaps/World_Character.asset', modules=report, overlaps=overlaps, passages=[],display_policy='Presentation polygons are intentionally regularized: shared endpoints, no half-cell teeth, straight main sides X11/X85, middle baselines Y66/Y73 with four structural corner flanges, flush central connector, solid L-shaped upper-left entrance. Actual contours remain in raw_contours and the raw SVG. Outer corners (85,131), (85,4), (11,4) use visual radius 4. No Unity source data modified.'),ensure_ascii=False,indent=2),encoding='utf-8')
    print(f'Exported {len(modules)} modules; all scene origins and sequence numbers match; {len(overlaps)} shared-footprint pairs; contour areas verified.')
    print(OUT)


if __name__ == '__main__':
    main()
