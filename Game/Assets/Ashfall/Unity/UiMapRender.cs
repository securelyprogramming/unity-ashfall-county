using System.Threading.Tasks;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.View
{
    /// <summary>Renders the county overview used by the drop map, the map panel and the HUD minimap.
    /// Rendering runs on a worker thread; the texture is uploaded on the main thread when it is ready.</summary>
    public static class UiMapRender
    {
        /// <summary>Colour per ground kind at one sample per <c>step</c> tiles; buildings are drawn as roofs, woods darker.</summary>
        public static Color32[] Render(Map m, int maxSize, out int tw, out int th)
        {
            int step = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(m.width, m.height) / (float)maxSize));
            tw = m.width / step; th = m.height / step;
            var px = new Color32[tw * th];
            for (int y = 0; y < th; y++)
                for (int x = 0; x < tw; x++)
                {
                    int tx = x * step, ty = y * step, i = m.Index(tx, ty);
                    Color32 c = Ground(m.ground[i]);
                    if (m.roomAt[i] >= 0) c = new Color32(196, 182, 152, 255);
                    else if (m.blockers.ContainsKey(i) && m.ground[i] == Ashfall.Core.Ground.Grass) c = new Color32(44, 68, 42, 255);
                    px[x + y * tw] = c;
                }
            return px;
        }

        static Color32 Ground(Ground g)
        {
            switch (g)
            {
                case Ashfall.Core.Ground.Water: return new Color32(40, 72, 92, 255);
                case Ashfall.Core.Ground.Road: case Ashfall.Core.Ground.Bridge: return new Color32(68, 68, 70, 255);
                case Ashfall.Core.Ground.Runway: return new Color32(88, 88, 90, 255);
                case Ashfall.Core.Ground.Gravel: return new Color32(118, 108, 88, 255);
                case Ashfall.Core.Ground.Sidewalk: case Ashfall.Core.Ground.Parking: case Ashfall.Core.Ground.Concrete: return new Color32(116, 116, 112, 255);
                case Ashfall.Core.Ground.MilitaryFloor: return new Color32(92, 98, 80, 255);
                case Ashfall.Core.Ground.Field: return new Color32(146, 126, 80, 255);
                case Ashfall.Core.Ground.Sand: return new Color32(176, 162, 118, 255);
                case Ashfall.Core.Ground.Mud: case Ashfall.Core.Ground.Dirt: return new Color32(104, 88, 62, 255);
                case Ashfall.Core.Ground.Grass: return new Color32(82, 106, 62, 255);
                default: return new Color32(96, 100, 84, 255);
            }
        }
    }

    /// <summary>The in-session county texture plus the exploration fog, shared by the map panel and the minimap.</summary>
    public sealed class UiCountyMap
    {
        public Texture2D texture, fog;
        public int mapWidth, mapHeight;
        Task<Color32[]> task; int tw, th, retries; Color32[] fogPixels; int fogFrame = -1; readonly Map map;

        public UiCountyMap(Map m)
        {
            map = m; mapWidth = m.width; mapHeight = m.height;
            int size = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(m.width, m.height) / 1024f));
            tw = m.width / size; th = m.height / size;
            texture = new Texture2D(tw, th, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "County map" };
            var blank = new Color32[tw * th]; for (int i = 0; i < blank.Length; i++) blank[i] = new Color32(20, 22, 24, 255);
            texture.SetPixels32(blank); texture.Apply();
            fog = new Texture2D(m.CellsX, m.CellsY, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "County fog" };
            fogPixels = new Color32[m.CellsX * m.CellsY];
            Start();
        }
        void Start() { var m = map; task = Task.Run(() => { int a, b; return UiMapRender.Render(m, 1024, out a, out b); }); }

        /// <summary>Uploads the rendered pixels once the worker finishes. Cheap to call every frame.</summary>
        public bool Poll()
        {
            if (task == null || !task.IsCompleted) return task == null;
            // The host may edit the map while the worker reads it; a rare torn read is simply retried.
            if (task.IsFaulted) { task = null; if (++retries < 3) Start(); return false; }
            if (task.Result.Length == tw * th) { texture.SetPixels32(task.Result); texture.Apply(); }
            task = null; return true;
        }

        /// <summary>Refreshes the exploration fog (at most once per frame; the explored grid only grows).</summary>
        public void RefreshFog(Map m)
        {
            if (fogFrame == Time.frameCount) return; fogFrame = Time.frameCount;
            var e = m.explored;
            for (int i = 0; i < fogPixels.Length && i < e.Length; i++) fogPixels[i] = e[i] ? new Color32(0, 0, 0, 0) : new Color32(6, 7, 9, 210);
            fog.SetPixels32(fogPixels); fog.Apply();
        }

        public void Destroy() { if (texture != null) Object.Destroy(texture); if (fog != null) Object.Destroy(fog); texture = null; fog = null; }
    }
}
