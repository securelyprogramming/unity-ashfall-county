using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mayo.Frontier.Presentation
{
    /// <summary>First-party reusable faceted geometry. No licensed model bytes are embedded.</summary>
    internal sealed class FrontierMeshKit : IDisposable
    {
        private readonly List<Mesh> ownedMeshes = new List<Mesh>();
        private readonly Dictionary<uint, Material> materials = new Dictionary<uint, Material>();
        private readonly Dictionary<string, Mesh> shapes = new Dictionary<string, Mesh>();
        private readonly Material template;
        private readonly Shader fallback;
        private bool disposed;

        public FrontierMeshKit()
        {
            template = Resources.Load<Material>("FrontierBase");
            fallback = template != null ? template.shader : Shader.Find("Standard");
            if (fallback == null) throw new InvalidOperationException("FrontierBase material or Standard shader is required.");
        }
        public Material Material(Color color, bool glow = false)
        {
            Color32 c = color;
            uint key = (uint)c.r | ((uint)c.g << 8) | ((uint)c.b << 16) | (glow ? 0x80000000u : 0u);
            Material result;
            if (materials.TryGetValue(key, out result)) return result;
            result = template != null ? new Material(template) : new Material(fallback);
            result.name = "Frontier original " + key;
            result.color = color;
            if (result.HasProperty("_Glossiness")) result.SetFloat("_Glossiness", .12f);
            if (glow && result.HasProperty("_EmissionColor"))
            {
                result.EnableKeyword("_EMISSION"); result.SetColor("_EmissionColor", color * .65f);
            }
            materials.Add(key, result);
            return result;
        }
        public Transform Part(Transform parent, string shape, Vector3 position, Vector3 scale, Color color, Vector3 euler = default(Vector3), bool glow = false)
        {
            GameObject item = new GameObject(shape);
            Transform t = item.transform; t.SetParent(parent, false); t.localPosition = position; t.localScale = scale; t.localRotation = Quaternion.Euler(euler);
            item.AddComponent<MeshFilter>().sharedMesh = Shape(shape);
            MeshRenderer renderer = item.AddComponent<MeshRenderer>(); renderer.sharedMaterial = Material(color, glow);
            renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
            return t;
        }
        public Mesh Shape(string kind)
        {
            Mesh mesh;
            if (shapes.TryGetValue(kind, out mesh)) return mesh;
            if (kind == "box") mesh = Box();
            else if (kind == "roof") mesh = Roof();
            else if (kind == "ramp") mesh = Ramp();
            else if (kind == "hull") mesh = Hull();
            else if (kind == "ring") mesh = Ring();
            else if (kind == "crystal") mesh = Crystal();
            else if (kind == "round") mesh = Orb();
            else if (kind == "cone") mesh = Frustum(.5f, 0, 8);
            else if (kind == "trunk") mesh = Frustum(.45f, .31f, 7);
            else if (kind == "bevel") mesh = Bevel();
            else mesh = Frustum(.5f, .5f, kind == "hex" ? 6 : 12);
            mesh.name = "Original Frontier " + kind;
            shapes.Add(kind, mesh); ownedMeshes.Add(mesh);
            return mesh;
        }
        private static Mesh Make(List<Vector3> vertices, List<int> triangles)
        {
            Mesh mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private static void Face(List<Vector3> v, List<int> t, params Vector3[] face)
        {
            int start = v.Count; v.AddRange(face);
            for (int i = 1; i < face.Length - 1; i++) { t.Add(start); t.Add(start + i); t.Add(start + i + 1); }
        }
        private static Mesh Box()
        {
            List<Vector3> v = new List<Vector3>(); List<int> t = new List<int>();
            Vector3 a = new Vector3(-.5f, -.5f, -.5f), b = new Vector3(.5f, -.5f, -.5f), c = new Vector3(.5f, -.5f, .5f), d = new Vector3(-.5f, -.5f, .5f);
            Vector3 e = a + Vector3.up, f = b + Vector3.up, g = c + Vector3.up, h = d + Vector3.up;
            Face(v,t,a,b,c,d); Face(v,t,e,h,g,f); Face(v,t,a,e,f,b); Face(v,t,b,f,g,c); Face(v,t,c,g,h,d); Face(v,t,d,h,e,a);
            return Make(v,t);
        }
        private static Mesh Frustum(float bottom, float top, int sides)
        {
            List<Vector3> v = new List<Vector3>(); List<int> t = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides, b = (i + 1) * Mathf.PI * 2 / sides;
                Vector3 p = new Vector3(Mathf.Cos(a)*bottom,0,Mathf.Sin(a)*bottom), q = new Vector3(Mathf.Cos(b)*bottom,0,Mathf.Sin(b)*bottom);
                Vector3 r = new Vector3(Mathf.Cos(b)*top,1,Mathf.Sin(b)*top), s = new Vector3(Mathf.Cos(a)*top,1,Mathf.Sin(a)*top);
                Face(v,t,p,s,r,q); Face(v,t,Vector3.zero,p,q); if(top>0) Face(v,t,Vector3.up,r,s);
            }
            return Make(v,t);
        }
        private static Mesh Bevel()
        {
            List<Vector3> v = new List<Vector3>(); List<int> t = new List<int>();
            Vector2[] r = { new Vector2(-.32f,-.5f),new Vector2(.32f,-.5f),new Vector2(.5f,-.32f),new Vector2(.5f,.32f),new Vector2(.32f,.5f),new Vector2(-.32f,.5f),new Vector2(-.5f,.32f),new Vector2(-.5f,-.32f) };
            for(int i=0;i<r.Length;i++)
            {
                Vector2 p=r[i],q=r[(i+1)%r.Length];
                Vector3 a=new Vector3(p.x,-.5f,p.y),b=new Vector3(q.x,-.5f,q.y),c=b+Vector3.up,d=a+Vector3.up;
                Face(v,t,a,d,c,b); Face(v,t,new Vector3(0,-.5f,0),a,b); Face(v,t,new Vector3(0,.5f,0),c,d);
            }
            return Make(v,t);
        }
        private static Mesh Roof()
        {
            List<Vector3> v = new List<Vector3>(); List<int> t = new List<int>();
            Vector3 a=new Vector3(-.5f,0,-.5f),b=new Vector3(.5f,0,-.5f),c=new Vector3(0,1,-.5f),d=new Vector3(-.5f,0,.5f),e=new Vector3(.5f,0,.5f),f=new Vector3(0,1,.5f);
            Face(v,t,a,c,b); Face(v,t,d,e,f); Face(v,t,a,d,f,c); Face(v,t,b,c,f,e); Face(v,t,a,b,e,d); return Make(v,t);
        }
        private static Mesh Ramp()
        {
            List<Vector3> v = new List<Vector3>(); List<int> t = new List<int>();
            Vector3 a=new Vector3(-.5f,0,-.5f),b=new Vector3(.5f,0,-.5f),c=new Vector3(-.5f,0,.5f),d=new Vector3(.5f,0,.5f),e=c+Vector3.up,f=d+Vector3.up;
            Face(v,t,a,e,f,b);Face(v,t,c,d,f,e);Face(v,t,a,c,e);Face(v,t,b,f,d);Face(v,t,a,b,d,c);return Make(v,t);
        }
        private static Mesh Crystal()
        {
            List<Vector3> v=new List<Vector3>();List<int>t=new List<int>();
            for(int i=0;i<6;i++)
            {
                float a=i*Mathf.PI/3,b=(i+1)*Mathf.PI/3;
                Vector3 p=new Vector3(Mathf.Cos(a)*.5f,0,Mathf.Sin(a)*.5f),q=new Vector3(Mathf.Cos(b)*.5f,0,Mathf.Sin(b)*.5f);
                Face(v,t,p,Vector3.up*.7f,q);Face(v,t,p,q,Vector3.down*.4f);
            }
            return Make(v,t);
        }
        private static Mesh Orb()
        {
            List<Vector3> v=new List<Vector3>();List<int>t=new List<int>();
            int sides=10;float[] heights={-.5f,-.28f,.22f,.5f};float[] radii={0,.44f,.45f,0};
            for(int ring=0;ring<3;ring++) for(int i=0;i<sides;i++)
            {
                float a=i*Mathf.PI*2/sides,b=(i+1)*Mathf.PI*2/sides;
                Vector3 p=new Vector3(Mathf.Cos(a)*radii[ring],heights[ring],Mathf.Sin(a)*radii[ring]);
                Vector3 q=new Vector3(Mathf.Cos(b)*radii[ring],heights[ring],Mathf.Sin(b)*radii[ring]);
                Vector3 r=new Vector3(Mathf.Cos(b)*radii[ring+1],heights[ring+1],Mathf.Sin(b)*radii[ring+1]);
                Vector3 s=new Vector3(Mathf.Cos(a)*radii[ring+1],heights[ring+1],Mathf.Sin(a)*radii[ring+1]);Face(v,t,p,s,r,q);
            }
            return Make(v,t);
        }
        private static Mesh Ring()
        {
            List<Vector3>v=new List<Vector3>();List<int>t=new List<int>();int sides=32;
            for(int i=0;i<sides;i++)
            {
                float a=i*Mathf.PI*2/sides,b=(i+1)*Mathf.PI*2/sides;
                Vector3 ai=new Vector3(Mathf.Cos(a)*.43f,0,Mathf.Sin(a)*.43f),ao=new Vector3(Mathf.Cos(a)*.5f,0,Mathf.Sin(a)*.5f),bi=new Vector3(Mathf.Cos(b)*.43f,0,Mathf.Sin(b)*.43f),bo=new Vector3(Mathf.Cos(b)*.5f,0,Mathf.Sin(b)*.5f);
                Face(v,t,ai,bi,bo,ao);Face(v,t,ao,bo,bi,ai);
            }
            return Make(v,t);
        }
        private static Mesh Hull()
        {
            List<Vector3>v=new List<Vector3>();List<int>t=new List<int>();
            float[] z={-.55f,-.38f,0,.38f,.55f};float[] widths={.06f,.42f,.5f,.42f,.06f};float[] heights={.48f,.22f,.16f,.22f,.48f};
            for(int i=0;i<z.Length-1;i++)
            {
                Vector3 a=new Vector3(-widths[i],heights[i],z[i]),b=new Vector3(widths[i],heights[i],z[i]),c=new Vector3(widths[i+1],heights[i+1],z[i+1]),d=new Vector3(-widths[i+1],heights[i+1],z[i+1]);
                Vector3 e=new Vector3(-widths[i]*.45f,0,z[i]),f=new Vector3(widths[i]*.45f,0,z[i]),g=new Vector3(widths[i+1]*.45f,0,z[i+1]),h=new Vector3(-widths[i+1]*.45f,0,z[i+1]);
                Face(v,t,a,d,c,b);Face(v,t,e,h,d,a);Face(v,t,f,b,c,g);Face(v,t,e,f,g,h);
            }
            return Make(v,t);
        }
        public void CombineStatic(Transform root)
        {
            Dictionary<Material,List<CombineInstance>> groups=new Dictionary<Material,List<CombineInstance>>();
            MeshFilter[] filters=root.GetComponentsInChildren<MeshFilter>();
            for(int i=0;i<filters.Length;i++)
            {
                MeshRenderer renderer=filters[i].GetComponent<MeshRenderer>();
                if(renderer==null || !renderer.enabled || filters[i].sharedMesh==null || renderer.sharedMaterials.Length!=1)continue;
                List<CombineInstance> rows;Material material=renderer.sharedMaterial;
                if(!groups.TryGetValue(material,out rows)){rows=new List<CombineInstance>();groups.Add(material,rows);}
                rows.Add(new CombineInstance{mesh=filters[i].sharedMesh,transform=root.worldToLocalMatrix*filters[i].transform.localToWorldMatrix});
                renderer.enabled=false;
            }
            foreach(KeyValuePair<Material,List<CombineInstance>> group in groups)
            {
                Mesh mesh=new Mesh{name="Original Frontier scenery batch",indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(group.Value.ToArray(),true,true);ownedMeshes.Add(mesh);
                GameObject item=new GameObject("Scenery batch");item.transform.SetParent(root,false);item.AddComponent<MeshFilter>().sharedMesh=mesh;item.AddComponent<MeshRenderer>().sharedMaterial=group.Key;
            }
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            foreach(Material material in materials.Values) if(material!=null) UnityEngine.Object.Destroy(material);
            foreach(Mesh mesh in ownedMeshes)if(mesh!=null)UnityEngine.Object.Destroy(mesh);
            materials.Clear();ownedMeshes.Clear();shapes.Clear();
        }
    }
}
