#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TapTap.Editor
{
    public sealed partial class LevelEditorWindow
    {
        private LevelPlacement pistonDraft;
        private LevelPlacement resizingPiston;
        private bool pistonDirectionLocked;

        private bool CanEditPlacement(LevelPlacement original, LevelPlacement draft)
        {
            var ignored = new HashSet<LevelPlacement>();
            if (original != null) ignored.Add(original);
            return LevelPlacementGeometry.Fits(draft, layout.CanvasBounds) && !LevelGridEditing.CrossLayerConflict(layout, draft, ignored)
                && !layout.Placements.Exists(item => item != null && item != original && item.Brush?.LayerId == draft.Brush?.LayerId
                    && LevelPlacementGeometry.Bounds(item).Overlaps(LevelPlacementGeometry.Bounds(draft)));
        }

        private Rect PlacementRect(LevelPlacement placement)
        {
            RectInt bounds = LevelPlacementGeometry.Bounds(placement);
            Rect first = CellRect(new Vector2Int(bounds.xMin, bounds.yMax - 1));
            return new Rect(first.position, (Vector2)bounds.size * cellPixels);
        }

        private LevelPlacement PistonAtMouse(Vector2Int cell, Vector2 mouse)
        {
            var settings = paintSettings is PistonPlacementSettings piston
                ? (PistonPlacementSettings)CloneSettings(piston) : new PistonPlacementSettings();
            if (layout.Find(cell, "geometry") != null)
            {
                Vector2 offset = (mouse - CellRect(cell).center) / cellPixels;
                settings.QuarterTurns = Mathf.Abs(offset.x) >= Mathf.Abs(offset.y) ? offset.x >= 0 ? 0 : 2 : offset.y >= 0 ? 1 : 3;
                cell += settings.Direction;
            }
            return new LevelPlacement { Cell = cell, Brush = Brush, Settings = settings };
        }

        private Rect PistonHandle(LevelPlacement placement)
        {
            var settings = (PistonPlacementSettings)placement.Settings;
            Rect tip = CellRect(placement.Cell + settings.Direction * (settings.MaxLength - 1));
            float size = Mathf.Clamp(cellPixels * 0.32f, 7f, 16f);
            return new Rect(tip.center - Vector2.one * size * 0.5f, Vector2.one * size);
        }

        private void DrawPistonOverlay(Rect canvas)
        {
            if (pistonDraft != null)
            {
                DrawPlacement(pistonDraft, CellRect(pistonDraft.Cell), true);
                Color color = CanEditPlacement(resizingPiston, pistonDraft) ? new Color(0.4f, 1f, 0.65f) : new Color(1f, 0.35f, 0.25f);
                Outline(PlacementRect(pistonDraft), color, 2f);
                return;
            }
            LevelPlacement selected = SelectedPlacement();
            if (!SelectionMode || !(selected?.Settings is PistonPlacementSettings settings)) return;
            Rect handle = PistonHandle(selected);
            EditorGUI.DrawRect(handle, new Color(1f, 0.82f, 0.24f));
            handle.position += canvas.position;
            EditorGUIUtility.AddCursorRect(handle, settings.Direction.x != 0 ? MouseCursor.ResizeHorizontal : MouseCursor.ResizeVertical);
        }

        private bool HandlePistonInput(Rect canvas)
        {
            if (layout == null) return false;
            Event e = Event.current;
            Vector2 mouse = e.mousePosition - canvas.position;
            Vector2Int cell = MouseToCell(mouse);
            if (pistonDraft != null)
            {
                if (e.type == EventType.MouseDrag)
                {
                    var settings = (PistonPlacementSettings)pistonDraft.Settings;
                    Vector2Int delta = ClampCell(cell) - pistonDraft.Cell;
                    if (!pistonDirectionLocked && delta != Vector2Int.zero)
                        settings.QuarterTurns = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) ? delta.x >= 0 ? 0 : 2 : delta.y < 0 ? 1 : 3;
                    settings.MaxLength = Mathf.Clamp(delta.x * settings.Direction.x + delta.y * settings.Direction.y + 1, 1, PistonPlacementSettings.MaximumLength);
                    e.Use(); Repaint(); return true;
                }
                if (e.type == EventType.MouseUp && e.button == 0)
                {
                    if (CanEditPlacement(resizingPiston, pistonDraft))
                    {
                        if (resizingPiston != null)
                        {
                            RecordLayoutUndo("调整活塞长度");
                            resizingPiston.Settings = CloneSettings(pistonDraft.Settings);
                            selectionArea = LevelPlacementGeometry.Bounds(resizingPiston);
                            Changed();
                        }
                        else if (LevelGridEditing.Apply(layout, new[] { pistonDraft.Cell }, pistonDraft.Brush, pistonDraft.Settings,
                            pistonDraft.Brush.LayerId, false, false, () => RecordLayoutUndo("放置活塞"))) Changed();
                    }
                    else ShowNotification(new GUIContent("活塞范围越界或与其他物体重叠"));
                    EndStroke(); GUIUtility.hotControl = 0;
                    e.Use(); Repaint(); return true;
                }
                return false;
            }
            if (e.type != EventType.MouseDown || e.button != 0 || !canvas.Contains(e.mousePosition) || !layout.Contains(cell)
                || e.alt || spacePan || panning || selecting || movingSelection) return false;
            LevelPlacement selected = SelectedPlacement();
            if (SelectionMode && selected?.Settings is PistonPlacementSettings && PistonHandle(selected).Contains(mouse))
            {
                BeginStroke(0);
                resizingPiston = selected;
                pistonDraft = new LevelPlacement { Id = selected.Id, Cell = selected.Cell, Brush = selected.Brush, Settings = CloneSettings(selected.Settings) };
                pistonDirectionLocked = true;
            }
            else if (tool == GridTool.Brush && Brush?.HandlerId == "phase-piston")
            {
                LevelPlacement draft = PistonAtMouse(cell, mouse);
                BeginStroke(0);
                pistonDraft = draft;
                pistonDirectionLocked = draft.Cell != cell;
            }
            else return false;
            GUIUtility.keyboardControl = 0;
            GUIUtility.hotControl = gridControlId;
            e.Use(); Repaint(); return true;
        }

        private void DrawPiston(LevelPlacement placement, Rect first, PistonPlacementSettings settings, bool ghost)
        {
            Vector2 step = new Vector2(settings.Direction.x, -settings.Direction.y) * first.width;
            Color color = placement.Brush.Color;
            if (ghost) color.a *= 0.45f;
            bool extended = settings.ExtendedWhenActive == previewPhaseActive;
            int length = Mathf.Clamp(settings.MaxLength, 1, PistonPlacementSettings.MaximumLength);
            for (int i = 0; i < length; i++)
            {
                Rect segment = new Rect(first.position + step * i, first.size);
                if (settings.Direction.x != 0) { segment.y += segment.height * 0.12f; segment.height *= 0.76f; }
                else { segment.x += segment.width * 0.12f; segment.width *= 0.76f; }
                Color fill = i == settings.MaxLength - 1 ? new Color(0.95f, 0.67f, 0.26f, color.a) : color;
                if (!extended) fill.a *= 0.2f;
                EditorGUI.DrawRect(segment, fill);
                Outline(segment, color, Mathf.Max(1f, first.width * 0.04f));
            }
            Vector2 origin = first.center - step * 0.5f;
            Rect plate = settings.Direction.x != 0
                ? new Rect(origin.x - 2f, origin.y - first.height * 0.45f, 4f, first.height * 0.9f)
                : new Rect(origin.x - first.width * 0.45f, origin.y - 2f, first.width * 0.9f, 4f);
            EditorGUI.DrawRect(plate, new Color(0.85f, 0.9f, 0.95f, color.a));
            if (first.width >= 14f)
                GUI.Label(new Rect(first.position + step * (settings.MaxLength - 1), first.size), new[] { "→", "↓", "←", "↑" }[settings.QuarterTurns & 3], CenterStyle());
        }

        private void DrawPlatformCaps(LevelPlacement placement, Rect shape, bool ghost)
        {
            PlatformPart part = layout != null ? PlatformLevelBrushHandler.Part(layout, placement) : PlatformPart.Single;
            Color color = new Color(0.78f, 1f, 0.93f, ghost ? 0.45f : 1f);
            float width = Mathf.Max(2f, shape.width * 0.08f);
            if (part == PlatformPart.Single || part == PlatformPart.Left)
                EditorGUI.DrawRect(new Rect(shape.x, shape.y, width, shape.height), color);
            if (part == PlatformPart.Single || part == PlatformPart.Right)
                EditorGUI.DrawRect(new Rect(shape.xMax - width, shape.y, width, shape.height), color);
        }

        private void DrawConveyorCaps(LevelPlacement placement, Rect shape, ConveyorPlacementSettings settings, bool ghost)
        {
            ConveyorPart? part = layout != null ? ConveyorTileRules.Part(layout, placement) : null;
            Color color = new Color(0.87f, 0.79f, 1f, ghost ? 0.45f : 1f);
            float width = Mathf.Max(2f, shape.width * 0.08f);
            if (part == ConveyorPart.Left) EditorGUI.DrawRect(new Rect(shape.x, shape.y, width, shape.height), color);
            if (part == ConveyorPart.Right) EditorGUI.DrawRect(new Rect(shape.xMax - width, shape.y, width, shape.height), color);
            if (!part.HasValue) Outline(shape, new Color(1f, 0.5f, 0.25f, ghost ? 0.45f : 1f), 2f);
            if (shape.width >= 14f && (part == ConveyorPart.Middle || !part.HasValue))
                GUI.Label(shape, part.HasValue ? settings.FacingLeft ? "<<" : ">>" : "!", CenterStyle());
        }
    }
}
#endif
