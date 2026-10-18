"""Export a text-free robot blueprint from the real 1-20 level placements.

Only reads Unity source assets. Adjacent wall cells are merged; thin platforms
retain their authored preview/collider rectangle. Robot appendages are inherited
from the approved diagram and are not invented gameplay geometry.
"""
from pathlib import Path
from collections import Counter
import argparse
import hashlib
import json
import re
import sys
import xml.etree.ElementTree as ET

sys.dont_write_bytecode = True
import export_level_structure as structure

ROOT = Path(__file__).resolve().parents[2]
CONCEPT = ROOT / 'Design/LevelStructure/RobotConcept'
NS = '{http://www.w3.org/2000/svg}'
WALL_BRUSHES = {'Terrain', 'SquarePlatform'}
PLATFORM_BRUSHES = {'Platform'}


def point(x, y):
    return 170 + (x - 11) * 8, 230 + (131 - y) * 8


def path(loops):
    parts = []
    for loop in loops:
        parts.append('M' + ' L'.join(f'{x:g},{y:g}' for x, y in (point(*p) for p in loop)) + ' Z')
    return ' '.join(parts)


def read_silhouette():
    source = CONCEPT / 'robot-level-map-white-v2.svg'
    svg = ET.parse(source).getroot()
    regions = []
    for item in svg.iter(NS + 'path'):
        if not item.get('id', '').startswith('level-'):
            continue
        values = [float(v) for v in re.findall(r'-?\d+(?:\.\d+)?', item.get('d'))]
        regions.append([((x - 170) / 8 + 11, 131 - (y - 230) / 8)
                        for x, y in zip(values[::2], values[1::2])])
    assert len(regions) == 20
    occupied = set.union(*(structure.polygon_cells([region]) for region in regions))
    return structure.contours(occupied, 2), source, regions


def merge_platforms(rectangles):
    """Join only touching horizontal strips of identical height and elevation."""
    by_row = {}
    for x, y, width, height in rectangles:
        by_row.setdefault((y, height), []).append((x, x + width))
    merged = []
    for (y, height), spans in sorted(by_row.items()):
        start, end = sorted(spans)[0]
        for left, right in sorted(spans)[1:]:
            if left <= end:
                end = max(end, right)
            else:
                merged.append((start, y, end - start, height))
                start, end = left, right
        merged.append((start, y, end - start, height))
    return merged


def export(name, room_outline_width=0):
    _, modules, paths = structure.read_world()
    brushes = {guid: structure.load(p) for guid, p in paths.items() if '/Brushes/' in p.as_posix()}
    silhouette, silhouette_source, regions = read_silhouette()
    walls, platforms, report, totals, omitted = set(), [], [], Counter(), Counter()
    for module in modules:
        if not 1 <= module['sequence'] <= 20:
            continue
        origin_x, origin_y = module['origin']
        included = Counter()
        for placement in module['level']['Placements']:
            brush = brushes.get(placement['Brush'].get('guid'))
            if brush is None:
                continue
            kind = brush['m_Name']
            x, y = structure.xy(placement['Cell'])
            x += origin_x
            y += origin_y
            if kind in WALL_BRUSHES:
                walls.add((x, y))
            elif kind in PLATFORM_BRUSHES:
                rect = brush['PreviewRect']
                platforms.append((x + rect['x'], y + rect['y'], rect['width'], rect['height']))
            else:
                omitted[kind] += 1
                continue
            included[kind] += 1
            totals[kind] += 1
        source = ROOT / module['source']
        report.append(dict(sequence=module['sequence'], source=module['source'], origin=module['origin'],
                           counts=dict(included), sha256=hashlib.sha256(source.read_bytes()).hexdigest()))
    assert len(report) == 20
    wall_loops = structure.contours(walls)
    # The merged path must preserve the exact source wall-cell union area.
    area = sum(sum(a[0] * b[1] - a[1] * b[0] for a, b in zip(loop, loop[1:] + loop[:1])) / 2
               for loop in wall_loops)
    assert area == len(walls), (area, len(walls))
    strips = merge_platforms(platforms)
    silhouette_d = path(silhouette)
    wall_d = path(wall_loops)
    svg = [
        '<svg xmlns="http://www.w3.org/2000/svg" width="2500" height="3450" viewBox="0 0 1000 1380">',
        '<title>Robot architectural section — actual level walls and platforms</title>',
        '<desc>Orthographic XY section extracted from the real 1–20 source level placements. No visible text. '
        'Wall tiles are merged without changing their positions; thin platforms use their authored 0.25-cell thickness. '
        'The robot outline retains its antenna, side tabs, four corner flanges and feet. '
        'This first structural study omits mechanisms, characters and material textures.</desc>',
        '<defs>',
        f'<clipPath id="robot-interior"><path d="{silhouette_d}" fill-rule="evenodd"/></clipPath>',
        '<pattern id="wall-section-hatch" width="5" height="5" patternUnits="userSpaceOnUse">'
        '<path d="M-1,1 L1,-1 M0,5 L5,0 M4,6 L6,4" stroke="white" stroke-width="0.55" fill="none"/></pattern>',
        '</defs>',
        '<rect width="1000" height="1380" fill="black"/>',
        '<g id="actual-geometry" clip-path="url(#robot-interior)">',
        f'<path id="actual-wall-union" d="{wall_d}" fill="url(#wall-section-hatch)" fill-rule="evenodd" '
        'stroke="white" stroke-width="1.25" stroke-linejoin="miter"/>',
        '<g id="actual-platforms" fill="white" stroke="white" stroke-width="0.45">',
    ]
    for x, y, width, height in strips:
        left, top = point(x, y + height)
        svg.append(f'<rect x="{left:g}" y="{top:g}" width="{width * 8:g}" height="{height * 8:g}"/>')
    svg.append('</g></g>')
    if room_outline_width > 0:
        svg.append(f'<g id="room-boundaries" fill="none" stroke="white" stroke-width="{room_outline_width:g}" stroke-linejoin="miter">')
        for index, region in enumerate(regions, 1):
            svg.append(f'<path id="room-boundary-{index:02}" d="{path([region])}"/>')
        svg.append('</g>')
    outer_width = max(3.2, room_outline_width)
    svg.extend([
        f'<path id="robot-outline" d="{silhouette_d}" fill="none" stroke="white" stroke-width="{outer_width:g}" '
        'stroke-linejoin="miter"/>',
        '<g id="antenna-outline" fill="none" stroke="white" stroke-width="3.2" stroke-linejoin="miter">'
        '<circle cx="100" cy="75" r="40"/><path d="M100 115 V260 H135 L170 290"/></g>',
        '</svg>',
    ])
    content = '\n'.join(svg)
    assert not ET.fromstring(content).findall('.//' + NS + 'text')
    CONCEPT.mkdir(parents=True, exist_ok=True)
    target = CONCEPT / (name + '.svg')
    target.write_text(content, encoding='utf-8')
    metadata = dict(
        world='Assets/Runtime/WorldMaps/World_Character.asset',
        outline_source=str(silhouette_source.relative_to(ROOT)).replace('\\', '/'),
        included_brush_counts=dict(totals), source_placement_count=sum(totals.values()),
        merged_wall_cell_count=len(walls), wall_contour_area=area, wall_contour_count=len(wall_loops),
        merged_platform_strip_count=len(strips), intentionally_omitted_counts=dict(omitted),
        coordinate_mapping='One uniform XY projection for all levels; no per-room distortion. Geometry is clipped to the approved robot silhouette.',
        wall_style='White boundaries and diagonal section hatching; thin platforms shown as solid white strips.',
        room_outline_width=room_outline_width, room_boundary_count=len(regions) if room_outline_width > 0 else 0,
        visible_text_count=0, levels=report,
    )
    target.with_suffix('.source.json').write_text(json.dumps(metadata, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'Exported {len(report)} real levels, {sum(totals.values())} placements, '
          f'{len(walls)} union wall cells and {len(strips)} platform strips.')
    print(target)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--name', default='robot-blueprint-v3')
    parser.add_argument('--room-outline-width', type=float, default=0,
                        help='Emphasize each room boundary with a white stroke in SVG viewBox units.')
    args = parser.parse_args()
    if not re.fullmatch(r'[a-zA-Z0-9_-]+', args.name):
        parser.error('Use a plain file stem for --name.')
    if not 0 <= args.room_outline_width <= 20:
        parser.error('Room outline width must be between 0 and 20.')
    export(args.name, args.room_outline_width)
