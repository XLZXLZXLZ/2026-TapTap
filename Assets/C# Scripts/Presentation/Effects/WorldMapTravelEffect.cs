using System;
using System.Collections;
using DG.Tweening;
using UnityEngine;

namespace TapTap
{
    public sealed class WorldMapTravelEffect : MonoBehaviour
    {
        public RectTransform Orb { get; private set; }
        public WorldMapTravelGraphic Trail { get; private set; }
        public Vector2 Origin { get; private set; }
        public Vector2 Destination { get; private set; }
        private Sequence timeline;
        private float travelDuration;
        private float pauseDuration;
        private const float TrailLifetime = 0.24f;
        private bool emitting;

        public static WorldMapTravelEffect Create(RectTransform board, Transform origin, Transform destination,
            float size, float duration, float pause = 0.6f)
        {
            var root = new GameObject("Level Travel Orb", typeof(RectTransform), typeof(WorldMapTravelEffect));
            root.layer = board.gameObject.layer;
            var rect = (RectTransform)root.transform;
            rect.SetParent(board, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = board.rect.size;
            var effect = root.GetComponent<WorldMapTravelEffect>();
            effect.Origin = board.InverseTransformPoint(origin.position);
            effect.Destination = board.InverseTransformPoint(destination.position);
            effect.travelDuration = Mathf.Max(0.1f, duration);
            effect.pauseDuration = Mathf.Max(0f, pause);
            Material glow = Resources.Load<Material>("WorldMapSelection/TravelOrb");
            if (glow == null) throw new InvalidOperationException("Missing WorldMapSelection/TravelOrb material.");
            effect.Trail = effect.Graphic("White Trail", glow);
            // The trail's vertices are in board coordinates. Its bounds must also
            // cover the board so a scrolling viewport's RectMask2D will not cull it.
            effect.Trail.rectTransform.pivot = Vector2.zero;
            effect.Trail.rectTransform.sizeDelta = board.rect.size;
            effect.Trail.IsTrail = true;
            effect.Trail.TrailWidth = size * 0.52f;
            effect.Trail.Lifetime = TrailLifetime;
            WorldMapTravelGraphic orb = effect.Graphic("White Orb", glow);
            effect.Orb = orb.rectTransform;
            effect.Orb.sizeDelta = Vector2.one * size;
            effect.Orb.anchoredPosition = effect.Origin;
            effect.Orb.localScale = Vector3.zero;
            return effect;
        }

        private WorldMapTravelGraphic Graphic(string label, Material glow)
        {
            var node = new GameObject(label, typeof(RectTransform), typeof(WorldMapTravelGraphic));
            node.layer = gameObject.layer;
            var graphic = node.GetComponent<WorldMapTravelGraphic>();
            graphic.rectTransform.SetParent(transform, false);
            graphic.rectTransform.anchorMin = graphic.rectTransform.anchorMax = Vector2.zero;
            graphic.rectTransform.anchoredPosition = Vector2.zero;
            graphic.raycastTarget = false;
            graphic.material = glow;
            return graphic;
        }

        public IEnumerator Play(Action<NeonWorldMapScreen.TransitionPhase> phase)
        {
            phase(NeonWorldMapScreen.TransitionPhase.OrbAppearing);
            timeline = DOTween.Sequence().SetUpdate(true);
            timeline.Append(Orb.DOScale(Vector3.one, 0.22f).SetEase(Ease.OutBack));
            timeline.AppendCallback(() => phase(NeonWorldMapScreen.TransitionPhase.OrbHolding));
            timeline.AppendInterval(pauseDuration);
            timeline.AppendCallback(() =>
            {
                emitting = true;
                Trail.Record(Orb.anchoredPosition);
                phase(NeonWorldMapScreen.TransitionPhase.OrbTravelling);
            });
            timeline.Append(DOTween.To(() => Orb.anchoredPosition, value => Orb.anchoredPosition = value,
                Destination, travelDuration).SetEase(Ease.InOutQuad));
            timeline.AppendCallback(() =>
            {
                Trail.Record(Destination);
                emitting = false;
                phase(NeonWorldMapScreen.TransitionPhase.OrbDisappearing);
            });
            timeline.Append(Orb.DOScale(Vector3.zero, 0.14f).SetEase(Ease.InQuad));
            timeline.AppendCallback(() => phase(NeonWorldMapScreen.TransitionPhase.TrailFading));
            timeline.AppendInterval(TrailLifetime);
            yield return timeline.WaitForCompletion();
        }

        private void LateUpdate()
        {
            if (emitting) Trail.Record(Orb.anchoredPosition);
        }

        public void Cancel()
        {
            timeline?.Kill();
            timeline = null;
            emitting = false;
            gameObject.SetActive(false);
            Destroy(gameObject);
        }

        private void OnDisable() { timeline?.Kill(); timeline = null; emitting = false; }
    }
}
