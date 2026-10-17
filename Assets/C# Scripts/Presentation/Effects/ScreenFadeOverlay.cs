using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace TapTap
{
    public sealed class ScreenFadeOverlay : LazySingleton<ScreenFadeOverlay>
    {
        private static ScreenFadeOverlay active;
        public new static ScreenFadeOverlay Existing => active;
        public new static ScreenFadeOverlay Instance
        {
            get
            {
                if (active==null) active=FindObjectOfType<ScreenFadeOverlay>();
                if (active==null) active=new GameObject("Persistent Screen Cover").AddComponent<ScreenFadeOverlay>();
                return active;
            }
        }
        public float Opacity { get; private set; }
        public float BulletOpacity { get; private set; }
        public Canvas CoverCanvas { get; private set; }
        public Image Cover { get; private set; }
        private Image bulletTint;
        private int fadeVersion;
        private Material ownedMaterial;

        private void Awake()
        {
            if (active!=null && active!=this) { Destroy(gameObject); return; }
            active=this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            var canvasObject=new GameObject("Cover Canvas",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform,false);
            CoverCanvas=canvasObject.GetComponent<Canvas>();
            CoverCanvas.renderMode=RenderMode.ScreenSpaceOverlay;
            CoverCanvas.sortingOrder=32767;
            Material material=Resources.Load<Material>("WorldMapSelection/ScreenCover");
            if (material==null)
            {
                ownedMaterial=new Material(Shader.Find("TapTap/UI/Screen Cover"));
                material=ownedMaterial;
            }
            bulletTint=Layer("Bullet Tint",material);
            Cover=Layer("Black Cover",material);
            RefreshGraphics();
        }

        private Image Layer(string name,Material material)
        {
            var node=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
            RectTransform rect=node.GetComponent<RectTransform>();
            rect.SetParent(CoverCanvas.transform,false);
            rect.anchorMin=Vector2.zero; rect.anchorMax=Vector2.one;
            rect.offsetMin=rect.offsetMax=Vector2.zero;
            Image image=node.GetComponent<Image>(); image.material=material; image.raycastTarget=false;
            return image;
        }

        private void RefreshGraphics()
        {
            if (Cover==null) return;
            Cover.color=new Color(0f,0f,0f,Opacity); Cover.enabled=Opacity>0f;
            Cover.raycastTarget=Opacity>0f;
            bulletTint.color=new Color(0.45f,0.75f,1f,BulletOpacity); bulletTint.enabled=BulletOpacity>0f;
        }

        private void Update() => AdvanceBulletTint(Time.unscaledDeltaTime, GameDebugTime.BulletTimeOn);

        public void AdvanceBulletTint(float unscaledDt, bool active)
        {
            const float opacity = 0.10f;
            const float duration = 0.20f;
            BulletOpacity = Mathf.MoveTowards(BulletOpacity, active ? opacity : 0f,
                opacity * Mathf.Max(0f, unscaledDt) / duration);
            RefreshGraphics();
        }

        public void SetOpacity(float opacity) { fadeVersion++; ApplyOpacity(opacity); }
        private void ApplyOpacity(float opacity) { Opacity=Mathf.Clamp01(opacity); RefreshGraphics(); }

        public IEnumerator FadeTo(float opacity, float duration)
        {
            float target = Mathf.Clamp01(opacity);
            int version=++fadeVersion;
            float start = Opacity;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (version!=fadeVersion) yield break;
                elapsed += Mathf.Min(Time.unscaledDeltaTime,1f/30f);
                ApplyOpacity(Mathf.SmoothStep(start, target, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }
            if (version==fadeVersion) ApplyOpacity(target);
        }

        protected override void OnDestroy()
        {
            if (active==this) active=null;
            if (ownedMaterial!=null) Destroy(ownedMaterial);
            base.OnDestroy();
        }
    }
}
