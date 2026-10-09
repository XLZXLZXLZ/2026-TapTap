using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace TapTap
{
    [RequireComponent(typeof(RespawnService))]
    public sealed class RespawnView : MonoBehaviour
    {
        private sealed class Presentation
        {
            public RespawnService.Journey Journey;
            public GameObject Dot;
            public TrailRenderer Trail;
            public Sequence Timeline;
            public Transform[] Visuals;
            public Vector3[] Scales;
            public Vector3[] Positions;
            public Vector3 OrbScale;
            public Vector3 OrbPosition;
        }
        [SerializeField] private Transform bodyOrb;
        [SerializeField] private Transform headOrb;
        private RespawnService service;
        private readonly List<Presentation> presentations = new List<Presentation>();
        public void ConfigureOrbs(Transform lower, Transform upper) { bodyOrb = lower; headOrb = upper; }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            service = GetComponent<RespawnService>();
            if (bodyOrb != null) bodyOrb.gameObject.SetActive(false);
            if (headOrb != null) headOrb.gameObject.SetActive(false);
            service.ReturnStarted += Begin;
            service.ReturnEnded += End;
        }

        private void Begin(RespawnService.Journey journey)
        {
            Transform orb = journey.Primary.Part == EntityPart.Head ? headOrb : bodyOrb;
            if (orb == null) return;
            var playerView = GetComponent<PlayerView>();
            if (playerView != null) { playerView.ResetVisual(journey.Primary); playerView.ResetVisual(journey.Companion); }
            var presentation = new Presentation { Journey = journey };
            presentation.Visuals = journey.Companion != null
                ? new[] { journey.Primary.Visual, journey.Companion.Visual } : new[] { journey.Primary.Visual };
            presentation.Scales = new Vector3[presentation.Visuals.Length];
            presentation.Positions = new Vector3[presentation.Visuals.Length];
            var dot = orb.gameObject;
            presentation.OrbScale = orb.localScale;
            presentation.OrbPosition = orb.localPosition;
            dot.SetActive(true);
            dot.transform.position = journey.Origin;
            dot.transform.localScale = Vector3.zero;
            TrailRenderer trail = dot.GetComponent<TrailRenderer>();
            if (trail != null) { trail.Clear(); trail.emitting = false; }
            presentation.Dot = dot; presentation.Trail = trail;
            Sequence sequence = DOTween.Sequence().SetAutoKill(false).Pause();
            for (int i = 0; i < presentation.Visuals.Length; i++)
            {
                Transform visual = presentation.Visuals[i];
                if (visual == null) continue;
                visual.DOKill();
                presentation.Scales[i] = visual.localScale;
                presentation.Positions[i] = visual.localPosition;
                Vector3 origin = presentation.Positions[i];
                sequence.Insert(0f, DOVirtual.Float(1f, 0f, journey.ShakeDuration, strength =>
                {
                    float phase = (1f - strength) * Mathf.PI * 8f;
                    visual.localPosition = origin + new Vector3(Mathf.Sin(phase), Mathf.Sin(phase * 1.37f), 0f) * (0.045f * strength);
                }).SetEase(Ease.Linear));
                sequence.Insert(journey.ShakeDuration, visual.DOScale(Vector3.zero, journey.CollapseDuration).SetEase(Ease.InCubic));
                sequence.Insert(journey.ArrivalTime + journey.DotDuration,
                    visual.DOScale(presentation.Scales[i], journey.RebuildDuration).From(Vector3.zero, false).SetEase(Ease.OutBack));
            }
            sequence.Insert(journey.ShakeDuration + journey.CollapseDuration,
                dot.transform.DOScale(presentation.OrbScale, journey.DotDuration).SetEase(Ease.OutBack));
            sequence.Insert(journey.FlightStart, DOVirtual.Float(0f, 1f, journey.FlightDuration,
                t => { if (dot != null) dot.transform.position = service.FlightPoint(journey, t); }).SetEase(Ease.InOutSine));
            sequence.Insert(journey.ArrivalTime, dot.transform.DOScale(Vector3.zero, journey.DotDuration).From(presentation.OrbScale, false).SetEase(Ease.InSine));
            sequence.AppendInterval(0f);
            presentation.Timeline = sequence;
            presentations.Add(presentation);
        }

        private void LateUpdate()
        {
            RenderPresentations(RewindManager.Rewinding);
        }

        public void RefreshAfterRestore()
        {
            for (int i = presentations.Count - 1; i >= 0; i--)
            {
                bool present = false;
                foreach (RespawnService.Journey journey in service.Journeys)
                    if (journey == presentations[i].Journey) { present = true; break; }
                if (present) continue;
                Dispose(presentations[i]);
                presentations.RemoveAt(i);
            }
            foreach (RespawnService.Journey journey in service.Journeys)
            {
                bool present = false;
                foreach (Presentation presentation in presentations)
                    if (presentation.Journey == journey) { present = true; break; }
                if (!present) Begin(journey);
            }
            RenderPresentations(true);
        }

        private void RenderPresentations(bool rewinding)
        {
            foreach (Presentation presentation in presentations)
            {
                float t = RespawnService.RenderTime(presentation.Journey);
                presentation.Timeline.Goto(t, false);
                if (presentation.Trail != null)
                {
                    if (rewinding) presentation.Trail.Clear();
                    presentation.Trail.emitting = !rewinding && t >= presentation.Journey.FlightStart && t < presentation.Journey.ArrivalTime;
                }
            }
        }

        private void End(RespawnService.Journey journey)
        {
            for (int i = presentations.Count - 1; i >= 0; i--)
            {
                if (presentations[i].Journey != journey) continue;
                Dispose(presentations[i]);
                presentations.RemoveAt(i);
            }
        }

        private static void Dispose(Presentation presentation)
        {
            presentation.Timeline?.Kill();
            for (int i = 0; i < presentation.Visuals.Length; i++)
            {
                Transform visual = presentation.Visuals[i];
                if (visual == null) continue;
                visual.localScale = presentation.Scales[i];
                visual.localPosition = presentation.Positions[i];
            }
            if (presentation.Dot != null)
            {
                if (presentation.Trail != null) { presentation.Trail.emitting = false; presentation.Trail.Clear(); }
                presentation.Dot.SetActive(false);
                presentation.Dot.transform.localScale = presentation.OrbScale;
                presentation.Dot.transform.localPosition = presentation.OrbPosition;
            }
        }

        private void OnDisable()
        {
            if (service != null) { service.ReturnStarted -= Begin; service.ReturnEnded -= End; }
            ClearPresentations();
        }

        public void ClearPresentations()
        {
            foreach (Presentation presentation in presentations) Dispose(presentation);
            presentations.Clear();
        }
    }
}
