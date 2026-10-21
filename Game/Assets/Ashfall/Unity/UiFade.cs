using UnityEngine;

namespace Ashfall.View
{
    /// <summary>Unscaled-time alpha fade for panels and banners.</summary>
    public sealed class UiFade : MonoBehaviour
    {
        CanvasGroup group; float t, duration;
        public void Play(float seconds) { if (group == null) { group = GetComponent<CanvasGroup>(); if (group == null) group = gameObject.AddComponent<CanvasGroup>(); } duration = Mathf.Max(.01f, seconds); t = 0; group.alpha = 0; enabled = true; }
        void Update() { if (group == null) { enabled = false; return; } t += Time.unscaledDeltaTime; group.alpha = Mathf.Clamp01(t / duration); if (t >= duration) enabled = false; }
    }
}
