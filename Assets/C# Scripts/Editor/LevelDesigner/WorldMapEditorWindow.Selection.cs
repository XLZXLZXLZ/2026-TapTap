#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TapTap.Editor
{
    public sealed partial class WorldMapEditorWindow
    {
        [SerializeField] private List<string> selectedModuleIds = new List<string>();
        private bool selectingModules;
        private Vector2Int moduleSelectionAnchor;
        private RectInt moduleSelectionArea;

        private bool ModuleSelected(WorldMapModule module) => module != null &&
            (selectedModuleIds.Count > 0 ? selectedModuleIds.Contains(module.Id) : module == Selected);

        private List<WorldMapModule> SelectedModules() => map?.Modules?.FindAll(ModuleSelected) ?? new List<WorldMapModule>();

        private void SelectSingleModule(WorldMapModule module)
        {
            selectedModuleIds.Clear();
            selectedId = module?.Id;
            if (module != null) selectedModuleIds.Add(module.Id);
        }

        private void BeginModuleSelection(Vector2Int cell)
        {
            selectingModules = true;
            moduleSelectionAnchor = cell;
            moduleSelectionArea = WorldMapRegionCreator.Rectangle(cell, cell);
            selectedPassageId = null;
            GUIUtility.hotControl = canvasControlId;
        }

        private void SelectModulesInArea(RectInt area)
        {
            selectedModuleIds.Clear(); selectedId = null;
            foreach (WorldMapModule module in map.Modules)
            {
                if (module?.Level == null) continue;
                // Use the occupied shape rather than its bounding box, including L/S-shaped rooms.
                foreach (RectInt run in module.Level.Footprint.Runs)
                    if (area.Overlaps(new RectInt(run.position + module.Position, run.size)))
                    {
                        selectedModuleIds.Add(module.Id);
                        if (selectedId == null) selectedId = module.Id;
                        break;
                    }
            }
        }

        private bool TrySelectedModuleBounds(out RectInt bounds)
        {
            bounds = default; bool found = false;
            foreach (WorldMapModule module in SelectedModules())
            {
                if (module.Level == null) continue;
                RectInt next = DisplayBounds(module);
                if (!found) { bounds = next; found = true; }
                else
                {
                    Vector2Int min = Vector2Int.Min(bounds.min, next.min);
                    Vector2Int max = Vector2Int.Max(bounds.max, next.max);
                    bounds = new RectInt(min, max - min);
                }
            }
            return found;
        }

        private void MoveSelectedModules(Vector2Int delta)
        {
            List<WorldMapModule> modules = SelectedModules();
            if (modules.Count == 0 || delta == Vector2Int.zero) return;
            Record(modules.Count > 1 ? "整体移动关卡模块" : "移动关卡模块");
            foreach (WorldMapModule module in modules) module.Position += delta;
            Changed();
        }

        private void DrawModuleSelection()
        {
            if (designTool != DesignTool.Arrange || SelectedPassage != null || pendingLevel != null) return;
            RectInt area;
            if (selectingModules) area = moduleSelectionArea;
            else if (selectedModuleIds.Count <= 1 || !TrySelectedModuleBounds(out area)) return;
            Rect rect = CellRect(area);
            Color color = new Color(0.4f, 0.85f, 1f);
            if (selectingModules) EditorGUI.DrawRect(rect, new Color(color.r, color.g, color.b, 0.12f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 2f, rect.y, 2f, rect.height), color);
            GUI.Label(new Rect(rect.x, rect.y - 22f, 300f, 20f), selectingModules
                ? "框选模块 · 松开选择 · Esc 取消"
                : "已选 " + SelectedModules().Count + " 个模块 · 拖动整体移动", EditorStyles.whiteMiniLabel);
        }
    }
}
#endif
