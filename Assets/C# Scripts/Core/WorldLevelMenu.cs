using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    public sealed class WorldLevelMenu : MonoBehaviour
    {
        public WorldMapDefinition World;
        private WorldMapAssemblyView view;
        private List<WorldMapModule> modules;
        private WorldMapModule selected;
        private GUIStyle detailStyle;
        private string notice;
        private int passedCount;
        private Vector2 lastPointerPosition;
        private bool pointerInitialized;
        public static WorldLevelMenu Current { get; private set; }
        public static bool IsOpen => Current != null;
        public WorldMapModule Selected => selected;
        public WorldMapAssemblyView View => view;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;

        private void Start()
        {
            Current = this;
            if (World == null) { notice = "请为选关入口配置大世界。"; return; }
            view = GetComponent<WorldMapAssemblyView>();
            if (view == null) view = gameObject.AddComponent<WorldMapAssemblyView>();
            modules = view.AvailableModules(World);
            Refresh();
            HashSet<string> passed = WorldMapProgressStore.Completed(World);
            Select(modules.Find(module => CanSelect(module) && !passed.Contains(module.Id))
                ?? modules.Find(CanSelect));
        }

        private void Refresh()
        {
            view.ShowSelection(World);
            HashSet<string> passed = WorldMapProgressStore.Completed(World);
            passedCount = modules.FindAll(module => passed.Contains(module.Id)).Count;
            if (selected!=null && !CanSelect(selected)) selected=null;
            view.SetMenuSelection(selected, notice ?? "");
        }

        public void UnlockAll()
        {
            if (World == null) return;
            WorldMapProgressStore.UnlockAll(World);
            Refresh();
            notice = "全部关卡已解锁";
            view.SetMenuSelection(selected, notice);
        }

        public void Select(WorldMapModule module)
        {
            if (module!=null && view.UsesNeonScreen && !CanSelect(module)) return;
            if (selected == module) return;
            selected = module;
            view.SetMenuSelection(selected, notice ?? "");
        }

        private bool CanSelect(WorldMapModule module) => view.UsesNeonScreen
            ? view.NeonScreen.CanSelect(module) : WorldMapProgressStore.IsUnlocked(World,module);

        public bool EnterSelected()
        {
            if (selected == null || WorldMapSceneRouter.IsLoading || PauseMenu.BlocksInput) return false;
            if (!CanSelect(selected))
            { notice = "先通过前一关，或按 P 解锁全部关卡"; view.SetMenuSelection(selected, notice); return false; }
            bool result = WorldMapSceneRouter.Instance.Enter(World, selected);
            notice = result ? "正在进入第 " + World.SequenceNumber(selected) + " 关…" : WorldMapSceneRouter.Instance.LastError;
            view.SetMenuSelection(selected, notice);
            return result;
        }

        private void Update()
        {
            if (modules == null || WorldMapSceneRouter.IsLoading || PauseMenu.BlocksInput) return;
            if (Input.GetKeyDown(KeyCode.P)) UnlockAll();
            int direction = Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D) ? 1
                : Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A) ? -1 : 0;
            if (direction != 0 && modules.Count > 0)
            {
                var choices=view.UsesNeonScreen?modules.FindAll(CanSelect):modules;
                if (choices.Count>0)
                {
                    int index = choices.IndexOf(selected);
                    Select(choices[(index + direction + choices.Count) % choices.Count]);
                }
            }
            Vector2 mouse = Input.mousePosition;
            bool pointerMoved=pointerInitialized && (mouse-lastPointerPosition).sqrMagnitude>1f;
            lastPointerPosition=mouse; pointerInitialized=true;
            if (view.TryPick(mouse, out WorldMapModule module))
            {
                if (pointerMoved) Select(module);
                if (Input.GetMouseButtonDown(0)) { Select(module); EnterSelected(); }
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) EnterSelected();
        }

        private void OnGUI()
        {
            if (view != null && view.UsesNeonScreen) return;
            if (PauseMenu.BlocksInput) return;
            if (detailStyle == null) detailStyle = new GUIStyle(GUI.skin.label)
                { fontSize = 16, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.7f, 0.83f, 0.88f) } };
            GUI.Label(new Rect(0f, 70f, Screen.width, 28f), "点击拼图进入关卡 · ← / → 选关 · Enter 进入 · P 解锁全部", detailStyle);
            if (World == null) { GUI.Label(new Rect(0f, 110f, Screen.width, 28f), notice, detailStyle); return; }
            string state = selected == null ? "请选择关卡" : "第 " + World.SequenceNumber(selected) + " 关 · " + selected.DisplayName
                + (WorldMapProgressStore.Completed(World).Contains(selected.Id) ? " · 已通过"
                    : WorldMapProgressStore.IsUnlocked(World, selected) ? " · 可进入" : " · 未解锁");
            GUI.Label(new Rect(0f, Screen.height - 134f, Screen.width, 28f), state, detailStyle);
            GUI.Label(new Rect(0f, Screen.height - 105f, Screen.width, 25f),
                "已通过 " + passedCount + " / " + (modules?.Count ?? 0) + " 关 · 红色：1–5   黄色：6–12   蓝色：13–20", detailStyle);
            bool enabled = GUI.enabled;
            GUI.enabled = !WorldMapSceneRouter.IsLoading && selected != null && WorldMapProgressStore.IsUnlocked(World, selected);
            if (GUI.Button(new Rect(Screen.width * 0.5f - 90f, Screen.height - 72f, 180f, 32f), "进入关卡")) EnterSelected();
            GUI.enabled = enabled;
            GUI.Label(new Rect(0f, Screen.height - 36f, Screen.width, 28f), notice ?? "", detailStyle);
        }

        private void OnDisable()
        {
            if (Current == this) Current = null;
            if (view != null) view.Hide();
        }
    }
}
