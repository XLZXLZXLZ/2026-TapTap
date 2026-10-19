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
        private string notice;
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
            if (module != null && (view == null || !CanSelect(module))) return;
            if (selected == module) return;
            selected = module;
            if (module != null) AudioManager.Instance?.Play(SoundId.Click, null, 0.5f, 1.05f);
            view.SetMenuSelection(selected, notice ?? "");
        }

        private bool CanSelect(WorldMapModule module) => view != null && view.NeonScreen != null && view.NeonScreen.CanSelect(module);

        public bool EnterSelected()
        {
            if (selected == null || WorldMapSceneRouter.IsLoading || PauseMenu.BlocksInput) return false;
            if (!CanSelect(selected))
            { notice = "先通过前一关，或按 P 解锁全部关卡"; view.SetMenuSelection(selected, notice); return false; }
            bool result = WorldMapSceneRouter.Instance.Enter(World, selected);
            if (result) AudioManager.Instance?.Play(SoundId.Click);
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
                var choices = modules.FindAll(CanSelect);
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

        private void OnDisable()
        {
            if (Current == this) Current = null;
            if (view != null) view.Hide();
        }
    }
}
