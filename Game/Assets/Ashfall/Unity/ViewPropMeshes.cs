using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.View
{
    /// <summary>Procedural low-poly meshes for the open-county prop kinds (military crates, sandbags, tents, towers,
    /// base furniture). Built for the prop's footprint in tiles, cached per kind and size, and used by WorldView
    /// before ArtLibrary.PropMesh. A restored purchased "prop/&lt;Kind&gt;" prefab still overrides them.</summary>
    public static class ViewPropMeshes
    {
        static readonly Dictionary<int, Mesh> cache = new Dictionary<int, Mesh>();
        static Color32 C(int r, int g, int b) { return new Color32((byte)r, (byte)g, (byte)b, 255); }
        static readonly Color32 Olive = C(84, 96, 58), DarkOlive = C(58, 66, 42), Tan = C(170, 150, 108), Canvas = C(106, 112, 78), ConcreteGrey = C(158, 156, 148),
            Steel = C(140, 146, 150), DarkSteel = C(66, 70, 74), Wood = C(122, 86, 58), DarkWood = C(84, 58, 40), Yellow = C(214, 176, 52), Black = C(30, 30, 32),
            White = C(222, 220, 212), Soil = C(70, 50, 34), Leaf = C(70, 130, 52), Blue = C(40, 92, 170), Red = C(170, 40, 34);

        /// <summary>Mesh for a prop, sized to its footprint (rotated props swap width and depth), or null for kinds this file does not draw.</summary>
        public static Mesh Get(Prop p)
        {
            if (p == null) return null;
            int w = Mathf.Max(1, p.w), h = Mathf.Max(1, p.h);
            bool turned = p.kind != PropKind.Car && (p.rotation & 1) == 1;
            return Get(p.kind, turned ? h : w, turned ? w : h);
        }

        public static Mesh Get(PropKind kind, int w = 1, int h = 1)
        {
            if (kind < PropKind.SupplyCrate || kind > PropKind.Bunk) return null;
            w = Mathf.Clamp(w, 1, 8); h = Mathf.Clamp(h, 1, 8);
            int key = (int)kind * 100 + w * 10 + h;
            Mesh mesh; if (cache.TryGetValue(key, out mesh) && mesh != null) return mesh;
            var k = new MeshKit(); float sx = w, sz = h; var o = Vector3.zero;
            switch (kind)
            {
                case PropKind.SupplyCrate:
                    // Guard airdrop crate on a pallet, strapped, with a yellow band.
                    k.Box(o, new Vector3(.96f * sx, .12f, .96f * sz), DarkWood);
                    k.Box(new Vector3(0, .12f, 0), new Vector3(.86f * sx, .72f, .86f * sz), Olive);
                    k.Box(new Vector3(0, .74f, 0), new Vector3(.9f * sx, .1f, .9f * sz), DarkOlive);
                    k.Box(new Vector3(0, .58f, 0), new Vector3(.87f * sx, .07f, .87f * sz), Yellow);
                    k.Box(new Vector3(-.22f * sx, .12f, 0), new Vector3(.06f, .74f, .88f * sz), Black);
                    k.Box(new Vector3(.22f * sx, .12f, 0), new Vector3(.06f, .74f, .88f * sz), Black);
                    break;
                case PropKind.MilitaryCrate:
                    k.Box(o, new Vector3(.92f * sx, .46f, .58f * sz), DarkOlive);
                    k.Box(new Vector3(0, .46f, 0), new Vector3(.95f * sx, .07f, .6f * sz), Olive);
                    for (int i = -1; i <= 1; i += 2) k.Box(new Vector3(i * .32f * sx, .3f, .29f * sz), new Vector3(.1f, .1f, .04f), Steel);
                    k.Box(new Vector3(0, .18f, .292f * sz), new Vector3(.36f * sx, .08f, .01f), Yellow);
                    break;
                case PropKind.Sandbags:
                    for (int row = 0; row < 3; row++)
                    {
                        int bags = Mathf.Max(2, Mathf.RoundToInt(sx * 2.6f)); float span = .96f * sx / bags;
                        for (int i = 0; i < bags; i++)
                        {
                            float x = -.48f * sx + span * (i + .5f) + (row % 2 == 1 ? span * .3f : 0); if (x > .48f * sx) continue;
                            var col = MeshKit.Shade(Tan, .88f + MeshKit.Hash(row * 31 + i) * .2f);
                            k.Blob(new Vector3(x, .09f + row * .16f, (row == 2 ? .02f : 0)), new Vector3(span * .55f, .1f, .24f * Mathf.Min(sz, 1.4f)), col, row * 17 + i);
                        }
                    }
                    break;
                case PropKind.Barrier:
                    // Concrete jersey barrier with a hazard stripe.
                    k.Box(o, new Vector3(.98f * sx, .28f, .6f * sz), ConcreteGrey);
                    k.Box(new Vector3(0, .28f, 0), new Vector3(.98f * sx, .36f, .38f * sz), MeshKit.Shade(ConcreteGrey, .95f));
                    k.Box(new Vector3(0, .64f, 0), new Vector3(.98f * sx, .22f, .24f * sz), MeshKit.Shade(ConcreteGrey, 1.05f));
                    for (int i = 0; i < Mathf.Max(2, (int)(sx * 3)); i++)
                        k.Box(new Vector3(-.42f * sx + i * .3f, .66f, .121f * sz), new Vector3(.14f, .16f, .01f), i % 2 == 0 ? Yellow : Black);
                    break;
                case PropKind.Tent: Tent(k, sx, sz); break;
                case PropKind.Watchtower: Watchtower(k, sx, sz); break;
                case PropKind.GardenBed:
                    k.Box(o, new Vector3(.96f * sx, .3f, .96f * sz), DarkWood, 0, false);
                    k.Box(new Vector3(0, .0f, 0), new Vector3(.86f * sx, .27f, .86f * sz), Soil);
                    for (int x = 0; x < w * 2; x++)
                        for (int z = 0; z < h * 2; z++)
                        {
                            var at = new Vector3(-.48f * sx + (x + .5f) * .48f, .27f, -.48f * sz + (z + .5f) * .48f);
                            float g = .7f + MeshKit.Hash(x * 7 + z * 13) * .5f;
                            if ((x + z) % 2 == 0) k.Pyramid(at, .12f * g, .38f * g, 5, MeshKit.Shade(Leaf, g), x * 40);
                            else k.Blob(at + Vector3.up * .08f, new Vector3(.13f, .09f, .13f) * g, MeshKit.Shade(C(96, 150, 70), g), x * 5 + z);
                        }
                    break;
                case PropKind.MedicalBed:
                    {
                        bool longX = sx > sz; float len = (longX ? sx : sz) * .94f, wid = .62f;
                        var size = longX ? new Vector3(len, 0, wid) : new Vector3(wid, 0, len);
                        foreach (var c in Corners(size * .46f)) k.Box(c, new Vector3(.05f, .55f, .05f), Steel);
                        k.Box(new Vector3(0, .45f, 0), new Vector3(size.x, .06f, size.z), Steel);
                        k.Box(new Vector3(0, .51f, 0), new Vector3(size.x * .97f, .12f, size.z * .97f), C(196, 214, 222));
                        var head = longX ? new Vector3(-len * .38f, .63f, 0) : new Vector3(0, .63f, -len * .38f);
                        k.Box(head, longX ? new Vector3(.24f, .08f, wid * .8f) : new Vector3(wid * .8f, .08f, .24f), White);
                        var pole = longX ? new Vector3(-len * .5f, 0, wid * .5f + .1f) : new Vector3(wid * .5f + .1f, 0, -len * .5f);
                        k.Box(pole, new Vector3(.04f, 1.7f, .04f), Steel); k.Box(pole + Vector3.up * 1.42f, new Vector3(.14f, .2f, .06f), C(170, 210, 230));
                        k.Box(head + new Vector3(0, .09f, 0), new Vector3(.12f, .01f, .03f), Red); k.Box(head + new Vector3(0, .09f, 0), new Vector3(.03f, .01f, .12f), Red);
                    }
                    break;
                case PropKind.RadioSet:
                    k.Box(new Vector3(0, .72f, 0), new Vector3(.95f * sx, .05f, .6f * sz), Wood);
                    foreach (var c in Corners(new Vector3(.42f * sx, 0, .26f * sz))) k.Box(c, new Vector3(.06f, .72f, .06f), DarkWood);
                    k.Box(new Vector3(-.1f, .77f, -.05f), new Vector3(.5f, .26f, .3f), DarkOlive);
                    k.Box(new Vector3(-.1f, .84f, .101f), new Vector3(.22f, .1f, .01f), C(90, 200, 120));
                    for (int i = 0; i < 3; i++) k.Box(new Vector3(.04f + i * .06f, .8f, .101f), new Vector3(.035f, .035f, .02f), Black);
                    k.Box(new Vector3(.12f, 1.03f, -.12f), new Vector3(.02f, 1.1f, .02f), Steel);
                    k.Box(new Vector3(.3f, .77f, .05f), new Vector3(.12f, .08f, .16f), Black);   // handset
                    break;
                case PropKind.Generator:
                    k.Box(o, new Vector3(.95f * sx, .08f, .7f * sz), DarkSteel);
                    k.Box(new Vector3(0, .08f, 0), new Vector3(.82f * sx, .5f, .56f * sz), C(196, 160, 48));
                    k.Box(new Vector3(-.12f * sx, .58f, 0), new Vector3(.4f * sx, .16f, .44f * sz), DarkSteel);
                    k.Box(new Vector3(.3f * sx, .58f, 0), new Vector3(.22f, .1f, .3f), C(176, 140, 40));
                    k.Box(new Vector3(.3f * sx, .68f, .08f), new Vector3(.06f, .05f, .06f), Red);
                    k.Box(new Vector3(-.38f * sx, .3f, .2f * sz), new Vector3(.06f, .6f, .06f), Black);   // exhaust
                    break;
                case PropKind.OutpostFlag:
                    k.Box(o, new Vector3(.5f, .18f, .5f), ConcreteGrey);
                    k.Box(new Vector3(0, .18f, 0), new Vector3(.07f, 4.1f, .07f), Steel);
                    k.Box(new Vector3(.62f, 3.45f, 0), new Vector3(1.1f, .7f, .03f), Blue);
                    k.Box(new Vector3(.62f, 3.73f, 0), new Vector3(1.1f, .1f, .035f), White);
                    k.Box(new Vector3(.62f, 3.5f, 0), new Vector3(.3f, .3f, .04f), White);
                    break;
                case PropKind.AmmoBox:
                    for (int i = 0; i < 2; i++)
                    {
                        var at = new Vector3(i == 0 ? -.12f : .14f, i * .0f, i == 0 ? 0 : .06f); float yaw = i * 12;
                        k.Box(at, new Vector3(.24f, .3f, .42f), i == 0 ? Olive : DarkOlive, yaw);
                        k.Box(at + Vector3.up * .3f, new Vector3(.26f, .04f, .44f), DarkOlive, yaw);
                        k.Box(at + Vector3.up * .34f, new Vector3(.04f, .04f, .2f), Steel, yaw);
                        k.Box(at + new Vector3(0, .14f, 0), new Vector3(.245f, .05f, .425f), Yellow, yaw);
                    }
                    break;
                case PropKind.Bunk:
                    {
                        bool longX = sx > sz; float len = (longX ? sx : sz) * .94f, wid = Mathf.Min(.9f, (longX ? sz : sx) * .9f);
                        var size = longX ? new Vector3(len, 0, wid) : new Vector3(wid, 0, len);
                        foreach (var c in Corners(size * .47f)) k.Box(c, new Vector3(.07f, 1.75f, .07f), DarkWood);
                        for (int level = 0; level < 2; level++)
                        {
                            float y = level == 0 ? .3f : 1.2f;
                            k.Box(new Vector3(0, y, 0), new Vector3(size.x, .08f, size.z), DarkWood);
                            k.Box(new Vector3(0, y + .08f, 0), new Vector3(size.x * .96f, .12f, size.z * .96f), level == 0 ? Canvas : C(96, 100, 110));
                            var pillow = longX ? new Vector3(-len * .38f, y + .2f, 0) : new Vector3(0, y + .2f, -len * .38f);
                            k.Box(pillow, longX ? new Vector3(.22f, .07f, wid * .7f) : new Vector3(wid * .7f, .07f, .22f), White);
                        }
                        k.Box(new Vector3(0, 1.38f, 0) + (longX ? new Vector3(0, 0, wid * .47f) : new Vector3(wid * .47f, 0, 0)), longX ? new Vector3(len, .05f, .04f) : new Vector3(.04f, .05f, len), DarkWood);
                    }
                    break;
            }
            mesh = k.Vertices > 0 ? k.Build("View prop " + kind + " " + w + "x" + h) : null;
            cache[key] = mesh; return mesh;
        }

        static Vector3[] Corners(Vector3 half) { return new[] { new Vector3(-half.x, 0, -half.z), new Vector3(half.x, 0, -half.z), new Vector3(-half.x, 0, half.z), new Vector3(half.x, 0, half.z) }; }

        static void Tent(MeshKit k, float sx, float sz)
        {
            bool alongX = sx >= sz; float len = (alongX ? sx : sz) * .96f, wid = (alongX ? sz : sx) * .96f, hgt = Mathf.Clamp(wid * .8f, 1.3f, 2.4f);
            var canvas = Canvas; var dark = MeshKit.Shade(Canvas, .8f);
            Vector3 A(float l, float y, float s) { return alongX ? new Vector3(l, y, s) : new Vector3(s, y, l); }
            float l0 = -len * .5f, l1 = len * .5f, s0 = -wid * .5f, s1 = wid * .5f;
            // Two slopes, both faces (the camera can see inside a cut-away), then the gable ends.
            k.Quad(A(l0, 0, s0), A(l0, hgt, 0), A(l1, hgt, 0), A(l1, 0, s0), canvas); k.Quad(A(l1, 0, s0), A(l1, hgt, 0), A(l0, hgt, 0), A(l0, 0, s0), dark);
            k.Quad(A(l1, 0, s1), A(l1, hgt, 0), A(l0, hgt, 0), A(l0, 0, s1), MeshKit.Shade(canvas, .9f)); k.Quad(A(l0, 0, s1), A(l0, hgt, 0), A(l1, hgt, 0), A(l1, 0, s1), dark);
            k.Tri(A(l0, 0, s1), A(l0, hgt, 0), A(l0, 0, s0), MeshKit.Shade(canvas, .85f)); k.Tri(A(l0, 0, s0), A(l0, hgt, 0), A(l0, 0, s1), dark);
            k.Tri(A(l1, 0, s0), A(l1, hgt, 0), A(l1, 0, s1), MeshKit.Shade(canvas, .85f)); k.Tri(A(l1, 0, s1), A(l1, hgt, 0), A(l1, 0, s0), dark);
            k.Tri(A(l1 + .01f, 0, s0 * .45f), A(l1 + .01f, hgt * .7f, 0), A(l1 + .01f, 0, s1 * .45f), C(40, 40, 32));   // door flap
            k.Box(A(l0 - .05f, 0, 0), new Vector3(.06f, hgt + .15f, .06f), DarkWood); k.Box(A(l1 + .05f, 0, 0), new Vector3(.06f, hgt + .15f, .06f), DarkWood);
        }

        static void Watchtower(MeshKit k, float sx, float sz)
        {
            float hx = sx * .5f - .15f, hz = sz * .5f - .15f, deck = 4f;
            foreach (var c in Corners(new Vector3(hx, 0, hz))) k.Box(c, new Vector3(.18f, deck + 2.1f, .18f), DarkWood);
            for (int i = 0; i < 2; i++)
            {
                float y = 1.4f + i * 1.3f;   // cross braces
                k.Box(new Vector3(0, y, -hz), new Vector3(hx * 2, .1f, .08f), Wood); k.Box(new Vector3(0, y, hz), new Vector3(hx * 2, .1f, .08f), Wood);
                k.Box(new Vector3(-hx, y, 0), new Vector3(.08f, .1f, hz * 2), Wood); k.Box(new Vector3(hx, y, 0), new Vector3(.08f, .1f, hz * 2), Wood);
            }
            k.Box(new Vector3(0, deck, 0), new Vector3(sx * .98f, .16f, sz * .98f), Wood, 0, true, true);
            k.Box(new Vector3(0, deck + .16f, -hz), new Vector3(hx * 2, .8f, .06f), MeshKit.Shade(Wood, .9f));
            k.Box(new Vector3(0, deck + .16f, hz), new Vector3(hx * 2, .8f, .06f), MeshKit.Shade(Wood, .9f));
            k.Box(new Vector3(-hx, deck + .16f, 0), new Vector3(.06f, .8f, hz * 2), MeshKit.Shade(Wood, .85f));
            k.Box(new Vector3(hx, deck + .16f, 0), new Vector3(.06f, .8f, hz * 2), MeshKit.Shade(Wood, .85f));
            k.Pyramid(new Vector3(0, deck + 2.1f, 0), Mathf.Max(sx, sz) * .78f, 1.1f, 4, C(70, 64, 58), 45);
            // Ladder on the south face.
            k.Box(new Vector3(-.22f, 0, -hz - .2f), new Vector3(.05f, deck + .4f, .05f), Wood); k.Box(new Vector3(.22f, 0, -hz - .2f), new Vector3(.05f, deck + .4f, .05f), Wood);
            for (int i = 0; i < 12; i++) k.Box(new Vector3(0, .3f + i * .33f, -hz - .2f), new Vector3(.46f, .04f, .04f), Wood);
            k.Box(new Vector3(hx * .5f, deck + .16f, hz * .4f), new Vector3(.5f, .5f, .5f), Tan);   // sandbag nest
        }
    }
}
