using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    public sealed class WorldLevelMenu : MonoBehaviour
    {
        public WorldMapDefinition World;
        public string MainMenuScenePath = "Assets/Scenes/MainMenu/MainMenu.unity";
        private WorldMapAssemblyView view;
        private List<WorldMapModule> modules;
        private WorldMapModule selected;
        private string notice;
        private Vector2 lastPointerPosition;
        private bool pointerInitialized;
        private bool entering;
        private WorldMapModule pressedModule;
        private WorldMapModule keyboardPressedModule;
        private bool keyboardFocus;
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
                ?? modules.Find(CanSelect), true);
        }

        private void Refresh()
        {
            CancelPress();
            view.ShowSelection(World);
            if (selected!=null && !CanSelect(selected)) selected=null;
            view.SetMenuSelection(selected, notice ?? "");
        }

        public void Select(WorldMapModule module) => Select(module, false);

        private void Select(WorldMapModule module, bool focus)
        {
            if (module != null && (view == null || !CanSelect(module))) return;
            if (selected == module) return;
            selected = module;
            if (module != null) AudioManager.Instance?.Play(SoundId.Click, null, 0.5f, 1.05f);
            view.SetMenuSelection(selected, notice ?? "", focus);
        }

        private bool CanSelect(WorldMapModule module) => view != null && view.CanSelect(module);

        public void ReturnToMainMenu()
        {
            if (entering || WorldMapSceneRouter.IsLoading || PauseMenu.BlocksInput) return;
            CancelPress();
            if (WorldMapSceneRouter.Instance.Load(MainMenuScenePath))
                AudioManager.Instance?.PlayClick();
            else
            {
                notice = WorldMapSceneRouter.Instance.LastError;
                view?.SetMenuSelection(selected, notice);
                Debug.LogError(notice, this);
            }
        }

        public bool EnterSelected()
        {
            if (selected == null || entering || WorldMapSceneRouter.IsLoading || PauseMenu.BlocksInput) return false;
            if (!CanSelect(selected))
            { notice = "请先通过前一关"; view.SetMenuSelection(selected, notice); return false; }
            entering = true;
            StartCoroutine(ConfirmAndEnter(selected));
            return true;
        }

        private IEnumerator ConfirmAndEnter(WorldMapModule module)
        {
            try
            {
                if (view.ActiveBlueprint != null) yield return view.Confirm(module);
                bool result = WorldMapSceneRouter.Instance.Enter(World, module);
                if (result) AudioManager.Instance?.Play(SoundId.Click);
                notice = result ? "正在进入第 " + World.SequenceNumber(module) + " 关…" : WorldMapSceneRouter.Instance.LastError;
                view.SetMenuSelection(selected, notice);
            }
            finally { entering = false; }
        }

        public void PointerDown(Vector2 position)
        {
            if (entering || WorldMapSceneRouter.IsLoading || PauseMenu.BlocksInput) return;
            CancelPress();
            if (view.TryPick(position, out var module))
            {
                pressedModule = module;
                view.SetPressed(module);
            }
        }

        public bool PointerUp(Vector2 position)
        {
            if (entering || WorldMapSceneRouter.IsLoading || PauseMenu.BlocksInput) { CancelPress(); return false; }
            WorldMapModule pressed = pressedModule;
            bool accept = pressed != null && view.ActiveBlueprint != null && !view.ActiveBlueprint.DraggedThisGesture
                && view.TryPick(position, out var module) && module == pressed;
            CancelPress();
            if (!accept) return false;
            Select(pressed);
            return EnterSelected();
        }

        public void PointerMove(Vector2 position)
        {
            if (pressedModule == null) return;
            view.SetPressed(view.TryPick(position, out var module) && module == pressedModule ? pressedModule : null);
        }

        public void CancelPress()
        {
            pressedModule = keyboardPressedModule = null;
            view?.SetPressed(null);
        }

        private void Update()
        {
            if (modules == null || entering || WorldMapSceneRouter.IsLoading) return;
            if (PauseMenu.BlocksInput) { CancelPress(); return; }
            if (Input.GetKeyDown(KeyCode.Escape)) { ReturnToMainMenu(); return; }
            view.HandleNavigation();
            int direction = Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D) ? 1
                : Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A) ? -1 : 0;
            if (direction != 0 && modules.Count > 0)
            {
                var choices = modules.FindAll(CanSelect);
                if (choices.Count>0)
                {
                    keyboardFocus = true;
                    int index = choices.IndexOf(selected);
                    Select(choices[(index + direction + choices.Count) % choices.Count], true);
                }
            }
            Vector2 mouse = Input.mousePosition;
            bool pointerMoved=pointerInitialized && (mouse-lastPointerPosition).sqrMagnitude>1f;
            lastPointerPosition=mouse; pointerInitialized=true;
            if (pointerMoved || Mathf.Abs(Input.mouseScrollDelta.y) > 0f) keyboardFocus = false;
            bool hit = view.TryPick(mouse, out WorldMapModule module);
            view.SetHovered(keyboardFocus ? selected : hit ? module : null);
            if (view.ActiveBlueprint != null)
            {
                if (Input.GetMouseButtonDown(0)) PointerDown(mouse);
                if (view.ActiveBlueprint.DraggedThisGesture && pressedModule != null) CancelPress();
                if (pressedModule != null && Input.GetMouseButton(0)) PointerMove(mouse);
                if (Input.GetMouseButtonUp(0) && PointerUp(mouse)) return;
                bool confirmDown = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
                bool confirmUp = Input.GetKeyUp(KeyCode.Return) || Input.GetKeyUp(KeyCode.KeypadEnter);
                if (confirmDown && pressedModule == null && CanSelect(selected))
                {
                    keyboardPressedModule = selected;
                    view.SetPressed(selected);
                }
                if (keyboardPressedModule != null && keyboardPressedModule != selected) CancelPress();
                if (confirmUp && keyboardPressedModule != null)
                { CancelPress(); EnterSelected(); }
            }
            else if (hit)
            {
                if (pointerMoved) Select(module);
                if (Input.GetMouseButtonDown(0)) { Select(module); EnterSelected(); }
            }
            if (view.ActiveBlueprint == null && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))) EnterSelected();
        }

        private void OnApplicationFocus(bool focus) { if (!focus) CancelPress(); }

        private void OnDisable()
        {
            StopAllCoroutines();
            entering = false;
            CancelPress();
            if (Current == this) Current = null;
            if (view != null) view.Hide();
        }
    }
}
