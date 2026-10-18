using UnityEngine;

namespace TapTap
{
    [DefaultExecutionOrder(600), DisallowMultipleComponent]
    public sealed class FinalEscapeSequence : MonoBehaviour
    {
        public LevelDefinition Level;
        public Transform TriggerRoot;
        public FinalCameraTarget CameraTarget;
        public FinalEscapeEndingTransition Ending;
        [Min(1)] public int StepCount = 8;
        [Min(0.01f)] public float CameraStepDuration = 0.65f;
        [Min(0.01f)] public float RevealDuration = 0.85f;
        private LevelRegion region;
        private WorldMapRoot world;
        private DemoCamera rig;
        private RegionRevealView[] reveals;
        private LevelCameraShot initialShot, renderedShot, blendFrom;
        private float blendElapsed, revealElapsed;
        private bool started, ownsCamera, endingStarted;
        public int CurrentStep { get; private set; }
        public PlayerController Player { get; private set; }
        public bool CanTrigger => region != null && Player != null && !endingStarted
            && !WorldMapProgression.InputBlocked && !RewindManager.Rewinding
            && Time.timeScale > 0f;

        private void Start()
        {
            foreach (LevelRegion candidate in FindObjectsOfType<LevelRegion>())
                if (candidate.Source == Level && candidate.gameObject.scene == gameObject.scene)
                { region = candidate; break; }
            foreach (PlayerController candidate in FindObjectsOfType<PlayerController>())
                if (candidate.gameObject.scene == gameObject.scene) { Player = candidate; break; }
            foreach (DemoCamera candidate in FindObjectsOfType<DemoCamera>())
                if (candidate.gameObject.scene == gameObject.scene) { rig = candidate; break; }
            world = GetComponentInParent<WorldMapRoot>();
            if (world == null) world = FindObjectOfType<WorldMapRoot>();
            if (region == null || rig == null || Player == null || CameraTarget == null)
            {
                Debug.LogError("最终逃离演出缺少关卡、玩家、镜头或目标配置。", this);
                enabled = false;
                return;
            }
            if (TriggerRoot != null)
            {
                TriggerRoot.SetPositionAndRotation(region.transform.position, region.transform.rotation);
                TriggerRoot.localScale = region.transform.lossyScale * region.UnitSize;
            }
            reveals = world != null ? world.GetComponentsInChildren<RegionRevealView>()
                : region.GetComponentsInChildren<RegionRevealView>();
            Physics2D.SyncTransforms();
        }

        public void Advance(int step)
        {
            if (!CanTrigger || step <= CurrentStep || (!started && step != 1)) return;
            if (!started)
            {
                started = true;
                GameplayActionLocks.Acquire(this, GameplayAction.Rewind | GameplayAction.DebugCamera);
                // A trigger never marks regions discovered or writes progression saves.
            }
            CurrentStep = Mathf.Clamp(step, 1, StepCount);
            blendFrom = renderedShot;
            blendElapsed = 0f;
        }

        private void LateUpdate()
        {
            if (!started || rig == null || CameraTarget == null) return;
            if (!ownsCamera)
            {
                if (!rig.TryAcquireControl(this)) return;
                ownsCamera = true;
                initialShot = renderedShot = blendFrom = LevelCameraShot.Read(rig.ViewCamera);
            }
            float dt = Time.timeScale > 0f ? Time.unscaledDeltaTime : 0f;
            revealElapsed += dt;
            float reveal = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(revealElapsed / RevealDuration));
            if (reveals != null)
                foreach (RegionRevealView view in reveals)
                    if (view != null) view.SetVisibilityOverride(this, reveal);
            float progress = (float)CurrentStep / Mathf.Max(1, StepCount);
            Vector3 focus = world != null && world.Source != null ? world.WorldBounds.center : region.WorldBounds.center;
            LevelCameraShot target = LevelCameraShot.Blend(initialShot, CameraTarget.Evaluate(initialShot, focus), progress * progress);
            blendElapsed += dt;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(blendElapsed / CameraStepDuration));
            renderedShot = LevelCameraShot.Blend(blendFrom, target, t);
            renderedShot.ApplyLens(rig.ViewCamera);
            rig.ViewCamera.transform.SetPositionAndRotation(renderedShot.Position, renderedShot.Rotation);
            if (!endingStarted && CurrentStep == StepCount && t >= 1f && Ending != null)
            {
                endingStarted = true;
                Ending.Play();
            }
        }

        private void OnDisable()
        {
            if (endingStarted && Ending != null) Ending.Cancel();
            GameplayActionLocks.Release(this);
            if (reveals != null)
                foreach (RegionRevealView view in reveals)
                    if (view != null) view.SetVisibilityOverride(this, 0f);
            if (ownsCamera && rig != null) rig.ReleaseControl(this);
            ownsCamera = false;
            started = endingStarted = false;
            CurrentStep = 0;
            revealElapsed = blendElapsed = 0f;
        }
    }
}
