using System;
using UnityEngine;

namespace TapTap
{
    public enum CameraLensMode { FieldOfView, Physical }

    [Serializable]
    public sealed class LevelCameraSettings
    {
        public bool HasPreset;
        public Vector3 LocalPosition = new Vector3(16f, 9f, -30f);
        public Vector3 LocalEulerAngles;
        public CameraLensMode LensMode;
        [Range(1f, 150f)] public float FieldOfView = 38f;
        [Min(0.1f)] public float FocalLength = 35f;
        public Vector2 SensorSize = new Vector2(36f, 24f);
        public Vector2 LensShift;
        public Camera.GateFitMode GateFit = Camera.GateFitMode.Vertical;
        [Min(0.01f)] public float NearClip = 0.1f;
        [Min(1f)] public float FarClip = 1000f;
        [Min(0f)] public float BlendDuration = 0.6f;
        public AnimationCurve BlendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        // Preserve the old serialized data for migration and the commented controller.
        [HideInInspector] public bool AutoSize = true;
        [HideInInspector] public float ViewSize = 9f;
        [HideInInspector] public bool FollowHorizontal = true;
        [HideInInspector] public bool FollowVertical = true;
        [HideInInspector] public Vector2 Offset = new Vector2(0f, 1.5f);
        [HideInInspector] public Vector2 DeadZone = new Vector2(0.1f, 0.15f);

        public float VerticalFieldOfView => LensMode == CameraLensMode.Physical
            ? Mathf.Clamp(Camera.FocalLengthToFieldOfView(Mathf.Max(0.1f, FocalLength),
                Mathf.Max(0.1f, SensorSize.y)), 1f, 150f)
            : Mathf.Clamp(FieldOfView, 1f, 150f);

        public Vector3 DefaultPosition(RectInt area, float aspect)
        {
            float halfHeight = Mathf.Max(area.height * 0.5f, area.width * 0.5f / Mathf.Max(0.1f, aspect));
            float distance = (halfHeight + 0.5f) * 1.08f
                / Mathf.Tan(VerticalFieldOfView * 0.5f * Mathf.Deg2Rad);
            return new Vector3(area.center.x, area.center.y, -Mathf.Max(1f, distance));
        }

        public void GenerateDefault(RectInt area, float aspect, float unit = 1f)
        {
            LocalPosition = DefaultPosition(area, aspect);
            LocalEulerAngles = Vector3.zero;
            FarClip = Mathf.Max(FarClip, (-LocalPosition.z + Mathf.Max(area.width, area.height) + 10f) * unit);
            HasPreset = true;
        }

        public void Capture(Camera camera, LevelRegion region)
        {
            float unit = Mathf.Max(0.0001f, region.UnitSize);
            LocalPosition = region.transform.InverseTransformPoint(camera.transform.position) / unit;
            LocalEulerAngles = (Quaternion.Inverse(region.transform.rotation) * camera.transform.rotation).eulerAngles;
            LensMode = camera.usePhysicalProperties ? CameraLensMode.Physical : CameraLensMode.FieldOfView;
            FieldOfView = camera.fieldOfView;
            FocalLength = camera.focalLength;
            SensorSize = camera.sensorSize;
            LensShift = camera.lensShift;
            GateFit = camera.gateFit;
            NearClip = camera.nearClipPlane;
            FarClip = camera.farClipPlane;
            HasPreset = true;
        }
    }

    public struct LevelCameraShot
    {
        public Vector3 Position, Focus;
        public Quaternion Rotation;
        public float FieldOfView, NearClip, FarClip;
        public bool Physical;
        public Vector2 SensorSize, LensShift;
        public Camera.GateFitMode GateFit;

        public static LevelCameraShot Evaluate(LevelRegion region, LevelCameraSettings fallback, float aspect)
        {
            var settings = region != null ? region.CameraSettings : fallback;
            settings = settings ?? new LevelCameraSettings();
            RectInt area = region != null && region.Source != null ? region.Source.Footprint.Bounds
                : new RectInt(Vector2Int.zero, region != null ? region.Size : new Vector2Int(32, 18));
            Vector3 local = settings.HasPreset ? settings.LocalPosition : settings.DefaultPosition(area, aspect);
            float unit = region != null ? Mathf.Max(0.0001f, region.UnitSize) : 1f;
            Quaternion localRotation = Quaternion.Euler(settings.HasPreset ? settings.LocalEulerAngles : Vector3.zero);
            Vector3 focus = new Vector3(area.center.x, area.center.y, 0f) * unit;
            return new LevelCameraShot
            {
                Position = region != null ? region.transform.TransformPoint(local * unit) : local * unit,
                Rotation = region != null ? region.transform.rotation * localRotation : localRotation,
                Focus = region != null ? region.transform.TransformPoint(focus) : focus,
                FieldOfView = settings.VerticalFieldOfView,
                Physical = settings.LensMode == CameraLensMode.Physical,
                SensorSize = new Vector2(Mathf.Max(0.1f, settings.SensorSize.x), Mathf.Max(0.1f, settings.SensorSize.y)),
                LensShift = settings.LensShift,
                GateFit = settings.GateFit,
                NearClip = Mathf.Max(0.01f, settings.NearClip),
                FarClip = Mathf.Max(settings.NearClip + 1f, settings.HasPreset ? settings.FarClip
                    : Mathf.Max(settings.FarClip, (-local.z + Mathf.Max(area.width, area.height) + 10f) * unit))
            };
        }

        public static LevelCameraShot Read(Camera camera)
        {
            return new LevelCameraShot
            {
                Position = camera.transform.position, Rotation = camera.transform.rotation,
                Focus = camera.transform.position + camera.transform.forward * 10f,
                FieldOfView = camera.fieldOfView, Physical = camera.usePhysicalProperties,
                SensorSize = camera.sensorSize, LensShift = camera.lensShift, GateFit = camera.gateFit,
                NearClip = camera.nearClipPlane, FarClip = camera.farClipPlane
            };
        }

        public static LevelCameraShot Blend(LevelCameraShot from, LevelCameraShot to, float t)
        {
            return new LevelCameraShot
            {
                Position = Vector3.Lerp(from.Position, to.Position, t),
                Rotation = Quaternion.Slerp(from.Rotation, to.Rotation, t),
                Focus = Vector3.Lerp(from.Focus, to.Focus, t),
                FieldOfView = Mathf.Lerp(from.FieldOfView, to.FieldOfView, t),
                Physical = to.Physical,
                SensorSize = Vector2.Lerp(from.SensorSize, to.SensorSize, t),
                LensShift = Vector2.Lerp(from.LensShift, to.LensShift, t),
                GateFit = to.GateFit,
                NearClip = Mathf.Lerp(from.NearClip, to.NearClip, t),
                FarClip = Mathf.Lerp(from.FarClip, to.FarClip, t)
            };
        }

        public void ApplyLens(Camera camera, float viewScale = 1f)
        {
            camera.orthographic = false;
            camera.usePhysicalProperties = Physical;
            camera.nearClipPlane = Mathf.Max(0.01f, NearClip);
            camera.farClipPlane = Mathf.Max(camera.nearClipPlane + 1f, FarClip);
            float fov = Mathf.Clamp(2f * Mathf.Atan(Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad)
                * Mathf.Clamp(viewScale, 0.1f, 1f)) * Mathf.Rad2Deg, 1f, 150f);
            camera.sensorSize = SensorSize;
            camera.lensShift = LensShift;
            camera.gateFit = GateFit;
            if (Physical) camera.focalLength = Camera.FieldOfViewToFocalLength(fov, Mathf.Max(0.1f, SensorSize.y));
            else camera.fieldOfView = fov;
        }
    }
}
