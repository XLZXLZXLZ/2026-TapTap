using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    public enum MagnetInput { Press, Release }

    public sealed class PlayerInput : MonoBehaviour
    {
        public bool UseKeyboard = true;
        public float Horizontal { get; private set; }
        public bool SpaceHeld { get; private set; }
        public bool ConsumedSpaceHeld { get; private set; }
        private readonly Queue<MagnetInput> edges = new Queue<MagnetInput>();
        private bool waitForSpaceRelease;
        private float externalHorizontal;
        private bool externalHeld;

        private void Update()
        {
            if (RewindManager.Rewinding) { Horizontal = 0f; edges.Clear(); return; }
            if (LevelAnnotation.IsFeedbackOpen)
            {
                Horizontal = 0f;
                SpaceHeld = false;
                edges.Clear();
                waitForSpaceRelease = true;
                return;
            }
            if (!UseKeyboard) return;
            float horizontal = ReadKeyboardHorizontal();
            bool held = Input.GetKey(KeyCode.Space);
            if (waitForSpaceRelease)
            {
                if (!held) waitForSpaceRelease = false;
                held = false;
            }
            Sample(horizontal, held);
        }

        public void SetExternalInput(float horizontal, bool held)
        {
            UseKeyboard = false;
            externalHorizontal = horizontal;
            externalHeld = held;
            if (RewindManager.Rewinding) { edges.Clear(); return; }
            if (LevelAnnotation.IsFeedbackOpen) { Horizontal = 0f; SpaceHeld = false; edges.Clear(); return; }
            if (waitForSpaceRelease) { if (!held) waitForSpaceRelease = false; held = false; }
            Sample(horizontal, held);
        }

        private static float ReadKeyboardHorizontal()
        {
            float horizontal = 0f;
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) horizontal -= 1f;
            if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) horizontal += 1f;
            return horizontal;
        }

        internal void RestoreHeldState(bool held)
        {
            edges.Clear();
            Horizontal = 0f;
            SpaceHeld = held;
            ConsumedSpaceHeld = held;
        }

        internal void ResumeAfterRewind()
        {
            edges.Clear();
            bool held = UseKeyboard ? Application.isFocused && Input.GetKey(KeyCode.Space) : externalHeld;
            waitForSpaceRelease = !SpaceHeld && held;
            // A historically held action must receive Release when the real key is up.
            Sample(UseKeyboard ? ReadKeyboardHorizontal() : externalHorizontal,
                waitForSpaceRelease ? false : held);
        }

        private void Sample(float horizontal, bool held)
        {
            Horizontal = Mathf.Clamp(horizontal, -1f, 1f);
            if (held != SpaceHeld)
            {
                if (edges.Count < 32) edges.Enqueue(held ? MagnetInput.Press : MagnetInput.Release);
                SpaceHeld = held;
            }
        }

        public bool TryConsume(out MagnetInput edge)
        {
            if (edges.Count == 0) { edge = default; return false; }
            edge = edges.Dequeue();
            ConsumedSpaceHeld = edge == MagnetInput.Press;
            return true;
        }

        public void ClearMagnetBuffer() => edges.Clear();

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && UseKeyboard) Sample(0f, false);
        }
    }
}
