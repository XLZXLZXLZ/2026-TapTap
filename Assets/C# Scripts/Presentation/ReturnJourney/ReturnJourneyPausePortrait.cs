using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace TapTap
{
    [DisallowMultipleComponent, RequireComponent(typeof(PauseMenu))]
    public sealed class ReturnJourneyPausePortrait : MonoBehaviour
    {
        public Image OriginalArt;
        [Tooltip("暂停菜单左图下预先隐藏的返程图片。")]
        public Image ReturnArt;
        public Shader TearShader;
        [Min(0.01f)] public float TearDuration = 0.65f;
        private Material originalMaterial, returnMaterial;
        private Material originalSource, returnSource;
        private Coroutine reveal;
        private PauseMenu menu;
        private bool changed;
        public bool IsRevealing => reveal != null;
        public bool HasChanged => changed;

        private void Awake()
        {
            menu = GetComponent<PauseMenu>();
            if (OriginalArt != null) originalSource = OriginalArt.material;
            if (ReturnArt != null) returnSource = ReturnArt.material;
        }
        private void Start() => Apply(GameStageProgression.StageIn(gameObject.scene) >= GameStage.CarryingKey);
        public void Reveal()
        {
            if (changed || reveal != null || OriginalArt == null || ReturnArt == null) return;
            reveal = StartCoroutine(RevealSequence());
        }
        private IEnumerator RevealSequence()
        {
            while (menu.IsTransitioning && menu.Content.activeInHierarchy) yield return null;
            if (TearShader == null) TearShader = Resources.Load<Shader>("Shaders/ReturnPortraitTear");
            if (TearShader != null)
            {
                originalMaterial = new Material(TearShader) { hideFlags = HideFlags.HideAndDontSave };
                returnMaterial = new Material(TearShader) { hideFlags = HideFlags.HideAndDontSave };
                OriginalArt.material = originalMaterial;
                ReturnArt.material = returnMaterial;
            }
            float elapsed = 0f;
            while (elapsed < TearDuration && menu.Content.activeInHierarchy)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, TearDuration));
                float strength = Mathf.Sin(t * Mathf.PI);
                originalMaterial?.SetVector("_Tear", new Vector4(strength, elapsed, 0f, 0f));
                returnMaterial?.SetVector("_Tear", new Vector4(strength, elapsed, 0f, 0f));
                // Both slots move with LeftArt; swap at peak distortion, after the panel has arrived.
                Apply(t >= 0.5f);
                yield return null;
            }
            Apply(true);
            ReleaseMaterials();
            reveal = null;
        }
        private void Apply(bool value)
        {
            changed = value;
            // Keep the original RectTransform alive: PauseMenu animates it as the shared container.
            if (OriginalArt != null) OriginalArt.enabled = !value;
            if (ReturnArt != null) ReturnArt.gameObject.SetActive(value);
        }
        private void ReleaseMaterials()
        {
            if (OriginalArt != null) OriginalArt.material = originalSource;
            if (ReturnArt != null) ReturnArt.material = returnSource;
            if (originalMaterial != null) Destroy(originalMaterial);
            if (returnMaterial != null) Destroy(returnMaterial);
            originalMaterial = returnMaterial = null;
        }
        private void LateUpdate()
        {
            if (reveal != null || changed || GameStageProgression.StageIn(gameObject.scene) < GameStage.CarryingKey) return;
            // A menu instantiated after pickup restores the new art without replaying the one-off reveal.
            Apply(true);
        }
        private void OnDisable()
        {
            if (reveal != null) StopCoroutine(reveal);
            reveal = null;
            ReleaseMaterials();
        }
    }
}
