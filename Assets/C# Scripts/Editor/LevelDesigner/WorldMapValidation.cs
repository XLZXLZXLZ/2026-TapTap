using System;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap.Editor
{
    public static class WorldMapValidation
    {
        public static List<string> Validate(WorldMapDefinition map)
        {
            var errors = new List<string>();
            if (map == null) { errors.Add("请选择大地图。"); return errors; }
            if (map.Modules == null || map.Modules.Count == 0)
            { errors.Add("大地图还没有关卡模块。"); return errors; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var sequences = new HashSet<int>();
            var checkedLevels = new HashSet<LevelDefinition>();
            float? unit = null;
            for (int i = 0; i < map.Modules.Count; i++)
            {
                WorldMapModule module = map.Modules[i];
                if (module?.Level == null) { errors.Add("第 " + (i + 1) + " 个模块缺少关卡。"); continue; }
                if (string.IsNullOrWhiteSpace(module.Id) || !ids.Add(module.Id))
                    errors.Add(module.DisplayName + "：模块 ID 为空或重复。");
                if (module.Sequence < 0 || !sequences.Add(map.SequenceNumber(module)))
                    errors.Add(module.DisplayName + "：关卡序号为负数或重复。");
                if (!checkedLevels.Add(module.Level)) continue;
                foreach (string error in LevelPrefabBuilder.Validate(module.Level))
                    errors.Add(module.DisplayName + "：" + error);
                if (module.Level.Palette == null) continue;
                float current = module.Level.Palette.UnitSize;
                if (unit.HasValue && !Mathf.Approximately(unit.Value, current))
                    errors.Add("关卡单位长度不一致，不能按统一网格拼装。");
                if (!unit.HasValue) unit = current;
            }
            return errors;
        }

        public static void RequireValid(WorldMapDefinition map)
        {
            List<string> errors = Validate(map);
            if (errors.Count > 0)
                throw new InvalidOperationException("大地图未通过同步检查，请先处理以下问题：\n" + string.Join("\n", errors));
        }
    }
}
