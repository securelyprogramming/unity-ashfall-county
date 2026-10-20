using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ashfall.View
{
    /// <summary>Flat-shaded, vertex-coloured mesh builder for streamed county geometry.</summary>
    public sealed class MeshKit
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Vector3> n = new List<Vector3>();
        readonly List<Color32> c = new List<Color32>();
        readonly List<int> t = new List<int>();
        public int Vertices { get { return v.Count; } }
        public void Clear() { v.Clear(); n.Clear(); c.Clear(); t.Clear(); }

        public void Tri(Vector3 a, Vector3 b, Vector3 d, Color32 color)
        {
            var normal = Vector3.Cross(b - a, d - a).normalized; int i = v.Count;
            v.Add(a); v.Add(b); v.Add(d); n.Add(normal); n.Add(normal); n.Add(normal); c.Add(color); c.Add(color); c.Add(color);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }
        public void Quad(Vector3 a, Vector3 b, Vector3 d, Vector3 e, Color32 color)
        {
            var normal = Vector3.Cross(b - a, e - a).normalized; int i = v.Count;
            v.Add(a); v.Add(b); v.Add(d); v.Add(e);
            for (int k = 0; k < 4; k++) { n.Add(normal); c.Add(color); }
            t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }
        /// <summary>Axis-aligned (optionally yawed) box from its bottom-centre.</summary>
        public void Box(Vector3 bottom, Vector3 size, Color32 color, float yaw = 0, bool top = true, bool bottomFace = false)
        {
            var r = Quaternion.Euler(0, yaw, 0); float x = size.x * .5f, z = size.z * .5f, h = size.y;
            Vector3 P(float px, float py, float pz) { return bottom + r * new Vector3(px, py, pz); }
            var p0 = P(-x, 0, -z); var p1 = P(x, 0, -z); var p2 = P(x, 0, z); var p3 = P(-x, 0, z);
            var q0 = P(-x, h, -z); var q1 = P(x, h, -z); var q2 = P(x, h, z); var q3 = P(-x, h, z);
            Quad(p0, q0, q1, p1, color); Quad(p1, q1, q2, p2, Shade(color, .92f)); Quad(p2, q2, q3, p3, Shade(color, .85f)); Quad(p3, q3, q0, p0, Shade(color, .9f));
            if (top) Quad(q0, q3, q2, q1, Shade(color, 1.06f));
            if (bottomFace) Quad(p0, p1, p2, p3, color);
        }
        public void Pyramid(Vector3 bottom, float radius, float height, int sides, Color32 color, float yaw = 0)
        {
            var apex = bottom + Vector3.up * height;
            for (int i = 0; i < sides; i++)
            {
                float a0 = (yaw + i * 360f / sides) * Mathf.Deg2Rad, a1 = (yaw + (i + 1) * 360f / sides) * Mathf.Deg2Rad;
                var p0 = bottom + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius; var p1 = bottom + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
                Tri(p0, apex, p1, Shade(color, .85f + .15f * Mathf.Abs(Mathf.Sin(a0))));
                Tri(p1, bottom, p0, Shade(color, .7f));
            }
        }
        public void Prism(Vector3 bottom, float radius, float height, int sides, Color32 color)
        {
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2 / sides, a1 = (i + 1) * Mathf.PI * 2 / sides;
                var p0 = bottom + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius; var p1 = bottom + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
                Quad(p0, p0 + Vector3.up * height, p1 + Vector3.up * height, p1, Shade(color, .85f + .15f * Mathf.Abs(Mathf.Cos(a0))));
                Tri(bottom + Vector3.up * height, p1 + Vector3.up * height, p0 + Vector3.up * height, Shade(color, 1.05f));
            }
        }
        /// <summary>Low-poly blob for bushes and deciduous canopies.</summary>
        public void Blob(Vector3 center, Vector3 radius, Color32 color, int seed)
        {
            int rings = 3, sides = 6; var pts = new Vector3[rings + 2][];
            for (int r = 0; r <= rings + 1; r++)
            {
                pts[r] = new Vector3[sides];
                float phi = Mathf.PI * r / (rings + 1), y = Mathf.Cos(phi), s = Mathf.Sin(phi);
                for (int i = 0; i < sides; i++)
                {
                    float a = (i + (r % 2) * .5f) * Mathf.PI * 2 / sides; float j = 1 + (Hash(seed + r * 13 + i) - .5f) * .35f;
                    pts[r][i] = center + Vector3.Scale(new Vector3(Mathf.Cos(a) * s * j, y * j, Mathf.Sin(a) * s * j), radius);
                }
            }
            for (int r = 0; r <= rings; r++)
                for (int i = 0; i < sides; i++)
                {
                    int k = (i + 1) % sides; var col = Shade(color, .8f + .3f * Hash(seed + r * 7 + i * 3));
                    Quad(pts[r][i], pts[r][k], pts[r + 1][k], pts[r + 1][i], col);
                }
        }
        public static float Hash(int x) { uint h = (uint)x * 2654435761u; h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; return (h & 0xffff) / 65535f; }
        public static Color32 Shade(Color32 c, float f) { return new Color32((byte)Mathf.Clamp(c.r * f, 0, 255), (byte)Mathf.Clamp(c.g * f, 0, 255), (byte)Mathf.Clamp(c.b * f, 0, 255), c.a); }
        public static Color32 Mix(Color32 a, Color32 b, float t) { return Color32.Lerp(a, b, t); }

        public Mesh Build(string name, Mesh reuse = null)
        {
            var mesh = reuse ?? new Mesh(); mesh.Clear(); mesh.name = name;
            mesh.indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetColors(c); mesh.SetTriangles(t, 0); mesh.RecalculateBounds();
            return mesh;
        }

        public static GameObject Object(string name, Transform parent, Mesh mesh, Material material, bool shadows = true)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = material;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off; r.receiveShadows = true;
            return go;
        }
    }
}
