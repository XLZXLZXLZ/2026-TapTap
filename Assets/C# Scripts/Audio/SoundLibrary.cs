using System;
using UnityEngine;

namespace TapTap
{
    public enum SoundId
    {
        MagnetRelease, MagnetJoin, ImpactLight, ImpactHeavy, Bounce, RecallFailed,
        SwitchPress, PhaseChange, PistonMove, Death, Respawn,
        RewindStart, RewindEnd, BulletTimeOn, BulletTimeOff, Click, KeyPickup, Complete,
        MagnetHum, RewindLoop, ConveyorLoop
    }

    [Serializable]
    public sealed class SoundDefinition
    {
        public SoundId Id;
        public AudioClip[] Clips = Array.Empty<AudioClip>();
        [Range(0f, 1f)] public float Volume = 0.5f;
        public Vector2 Pitch = new Vector2(0.97f, 1.03f);
        [Min(0f)] public float MinimumInterval = 0.05f;
        [Range(1, 16)] public int MaxVoices = 3;
        [Range(0, 100)] public int Priority = 50;
        public bool Loop;
        public bool PauseWithGameplay = true;
        public bool AllowDuringRewind;
    }

    [CreateAssetMenu(menuName = "TapTap/Audio/Sound Library")]
    public sealed class SoundLibrary : ScriptableObject
    {
        public const string ResourcePath = "Audio/GameplaySoundLibrary";
        public SoundDefinition[] Sounds = Array.Empty<SoundDefinition>();

        public SoundDefinition Find(SoundId id)
        {
            foreach (SoundDefinition sound in Sounds)
                if (sound != null && sound.Id == id) return sound;
            return null;
        }
    }
}
