using UnityEngine;

namespace Ashfall.View
{
    /// <summary>Full-screen colour grade on the game camera (built-in pipeline image effect): a slightly desaturated,
    /// cold-shadow / warm-highlight look, a vignette that deepens at night, and a red edge flash when the controlled
    /// survivor is hurt. Disables itself when the shader is missing or unsupported.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class ViewGrade : MonoBehaviour
    {
        Material material;
        float flash; Color flashColor = new Color(.6f, 0, 0);
        /// <summary>0 = full day, 1 = deep night.</summary>
        public float Night;
        /// <summary>0..1 persistent danger tint (low health).</summary>
        public float Danger;

        public static ViewGrade Attach(Camera cam)
        {
            if (cam == null) return null;
            var g = cam.GetComponent<ViewGrade>(); if (g != null) return g;
            var shader = Resources.Load<Shader>("Ashfall/AshfallGrade");
            if (shader == null || !shader.isSupported) return null;
            g = cam.gameObject.AddComponent<ViewGrade>(); g.material = new Material(shader) { name = "Ashfall grade", hideFlags = HideFlags.DontSave };
            return g;
        }

        /// <summary>Red (or any colour) flash at the screen edges; strength 0..1.</summary>
        public void Flash(float strength, Color color)
        {
            if (strength >= flash) flashColor = color;
            flash = Mathf.Clamp01(Mathf.Max(flash, strength));
        }

        void Update() { flash = Mathf.Max(0, flash - Time.unscaledDeltaTime * 1.8f); }

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (material == null) { Graphics.Blit(src, dst); return; }
            float n = Mathf.Clamp01(Night);
            material.SetColor("_Lift", new Color(.012f, .02f + .01f * n, .04f + .025f * n, 0));
            material.SetColor("_Gain", Color.Lerp(new Color(1.04f, 1.0f, .95f), new Color(.92f, .96f, 1.06f), n));
            material.SetFloat("_Saturation", Mathf.Lerp(.9f, .74f, n));
            material.SetFloat("_Contrast", Mathf.Lerp(1.07f, 1.12f, n));
            material.SetFloat("_Vignette", Mathf.Lerp(.32f, .55f, n) + Danger * .15f);
            float pulse = Danger > 0 ? Danger * (.18f + .08f * Mathf.Sin(Time.unscaledTime * 4)) : 0;
            float a = Mathf.Max(flash * .55f, pulse);
            var c = flash * .55f >= pulse ? flashColor : new Color(.45f, 0, 0);
            material.SetColor("_Flash", new Color(c.r, c.g, c.b, a));
            Graphics.Blit(src, dst, material);
        }

        void OnDestroy() { if (material != null) Destroy(material); }
    }
}
