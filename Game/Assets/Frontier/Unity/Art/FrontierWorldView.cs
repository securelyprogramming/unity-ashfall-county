using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mayo.Frontier.Presentation
{
    /// <summary>Read-only 3D presentation of host snapshots; never owns gameplay or network authority.</summary>
    public sealed partial class FrontierWorldView : MonoBehaviour, IDisposable
    {
        private sealed class Visual
        {
            public Transform root, model, torso, head, leftArm, rightArm, leftLeg, rightLeg;
            public Transform ring, marker, hit, muzzle, carry, tiers, movingPart, stockRoot, statusItem;
            public MeshRenderer ringRenderer;
            public string kind, carryKind = "", stockKind = "";
            public int seen, level = -1, stock = -1;
            public float previousHp = -1, hitUntil, walkPhase, movement;
            public Vector3 previousPosition;
            public bool positioned, quadruped;
            public Transform[] effects;
        }
        private readonly Dictionary<long, Visual> entities = new Dictionary<long, Visual>();
        private readonly List<long> stale = new List<long>();
        private FrontierMeshKit kit;
        private Transform ownedRoot, scenery, entityRoot;
        private Camera viewCamera;
        private GameObject licensedFooting;
        private string gameId, setting;
        private Color accent, ground, stone, wood, leaf;
        private Color previousAmbient, previousFog;
        private AmbientMode previousAmbientMode;
        private bool previousFogEnabled, environmentOwned;
        private float previousFogDensity, previousAmbientIntensity;
        private int epoch, sceneryChapter;
        private float previousWorldClock = -1;
        private Vector3 cameraFocus;
        private bool cameraPositioned, initialized;
        private readonly Color ink = new Color(.055f,.085f,.105f), cream = new Color(.91f,.84f,.67f), copper = new Color(.68f,.34f,.16f);
        private readonly Color metal = new Color(.27f,.36f,.39f), skin = new Color(.72f,.48f,.32f), danger = new Color(.88f,.22f,.16f), signal = new Color(.33f,.86f,.65f);

        public Camera ViewCamera { get { return viewCamera; } }
        public int VisibleEntityCount { get { return entities.Count; } }

        public void Initialize(string gameId, string accent, string setting)
        {
            if (string.IsNullOrEmpty(gameId)) throw new ArgumentException("A Frontier game ID is required.");
            Dispose();
            this.gameId = gameId; this.setting = setting ?? "";
            if (!ColorUtility.TryParseHtmlString(accent, out this.accent)) this.accent = new Color(.52f,.72f,.65f);
            kit = new FrontierMeshKit();
            ownedRoot = Group(transform, "Frontier authored world");
            entityRoot = Group(ownedRoot, "Snapshot entities");
            licensedFooting = Resources.Load<GameObject>("LicensedFrontier/SplineBuilderFooting/SM_StoneFoot_Prepared");
            ConfigurePalette(); ConfigureLighting();
            Transform cameraRoot = Group(ownedRoot, "Isometric camera");
            viewCamera = cameraRoot.gameObject.AddComponent<Camera>();
            viewCamera.tag = "MainCamera"; viewCamera.orthographic = true; viewCamera.orthographicSize = 14.7f;
            viewCamera.nearClipPlane = .1f; viewCamera.farClipPlane = 160;
            viewCamera.clearFlags = CameraClearFlags.SolidColor;
            viewCamera.backgroundColor = gameId == "dead-orbit" ? new Color(.018f,.027f,.065f) : Color.Lerp(ground, ink, .6f);
            viewCamera.allowHDR = true; viewCamera.allowMSAA = true;
            cameraFocus = new Vector3(-6,0,-5); PlaceCamera(true);
            sceneryChapter = 1; BuildScenery(1); initialized = true;
        }
        private void ConfigurePalette()
        {
            ground = new Color(.24f,.31f,.27f); stone = new Color(.42f,.45f,.4f); wood = new Color(.39f,.25f,.16f); leaf = new Color(.24f,.43f,.25f);
            if (gameId == "dead-orbit") { ground = new Color(.12f,.19f,.23f); stone = new Color(.29f,.4f,.44f); wood = metal; leaf = new Color(.24f,.48f,.53f); }
            else if (gameId == "fjordbound") { ground = new Color(.57f,.67f,.67f); stone = new Color(.35f,.43f,.46f); leaf = new Color(.16f,.33f,.32f); }
            else if (gameId == "emberwake-rebuilders" || gameId == "gatebreakers-guild") { ground = new Color(.29f,.27f,.25f); stone = new Color(.42f,.4f,.36f); leaf = new Color(.38f,.41f,.21f); }
            else if (gameId == "market-day") { ground = new Color(.48f,.49f,.43f); stone = new Color(.7f,.68f,.56f); leaf = new Color(.38f,.55f,.3f); }
            else if (gameId == "hearth-rush") { ground = new Color(.4f,.28f,.22f); stone = new Color(.67f,.61f,.49f); }
            else if (gameId == "rift-runners") { ground = new Color(.23f,.32f,.32f); stone = new Color(.39f,.42f,.5f); leaf = new Color(.3f,.39f,.58f); }
        }
        private void ConfigureLighting()
        {
            previousAmbient = RenderSettings.ambientLight; previousAmbientMode = RenderSettings.ambientMode; previousAmbientIntensity = RenderSettings.ambientIntensity;
            previousFogEnabled = RenderSettings.fog; previousFog = RenderSettings.fogColor; previousFogDensity = RenderSettings.fogDensity; environmentOwned = true;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = gameId == "dead-orbit" ? new Color(.38f,.48f,.59f) : new Color(.62f,.66f,.64f); RenderSettings.ambientIntensity = 1;
            RenderSettings.fog = false;
            Light sun = Group(ownedRoot,"Warm directional light").gameObject.AddComponent<Light>();
            sun.type = LightType.Directional; sun.transform.rotation = Quaternion.Euler(48,-36,0); sun.color = gameId == "dead-orbit" ? new Color(.65f,.82f,1) : new Color(1,.9f,.74f); sun.intensity = 1.12f;
            sun.shadows = LightShadows.Soft; sun.shadowStrength = .6f; sun.shadowBias = .045f;
            Light fill = Group(ownedRoot,"Cool rim light").gameObject.AddComponent<Light>(); fill.type = LightType.Directional; fill.transform.rotation = Quaternion.Euler(35,130,0); fill.color = new Color(.49f,.68f,.85f); fill.intensity = .28f; fill.shadows = LightShadows.None;
        }
        public void Render(Mayo.Frontier.World state, int localPlayerId)
        {
            if (!initialized || state == null) return;
            if (sceneryChapter != state.chapter) { sceneryChapter = state.chapter; BuildScenery(state.chapter); }
            if (state.elapsed < previousWorldClock)
            {
                foreach (Visual old in entities.Values) { old.hitUntil = 0; old.previousHp = -1; old.positioned = false; old.walkPhase = old.movement = 0; }
            }
            previousWorldClock = state.elapsed;
            epoch++; float clock = state.elapsed;
            bool active = state.phase == "active";
            Vector3 focus = cameraFocus; bool localFound = false;
            foreach (Mayo.Frontier.Player player in state.players)
            {
                Visual v = Get(1,player.id,"hero-" + gameId);
                v.root.gameObject.SetActive(player.connected);
                if (!player.connected) continue;
                Position(v,player.x,player.z,player.faceX,player.faceZ);
                Animate(v,clock,active && !player.dead,player.attackCooldown,player.dead);
                v.ringRenderer.sharedMaterial = kit.Material(player.id == localPlayerId ? accent : signal,true);
                v.ring.localScale = Vector3.one * (player.id == localPlayerId ? 1.45f : 1.2f);
                Feedback(v,player.hp,clock);
                Carry(v,player.carried);
                if (v.muzzle != null) v.muzzle.gameObject.SetActive(active && !player.dead && player.attackCooldown > .31f && IsRangedHero());
                if (player.id == localPlayerId) { focus = new Vector3(player.x*.57f,0,player.z*.57f); localFound = true; }
            }
            foreach (Mayo.Frontier.Enemy enemy in state.enemies)
            {
                Visual v = Get(2,enemy.id,enemy.kind);
                Vector3 target = ClosestLivingPlayer(state,enemy.x,enemy.z);
                Position(v,enemy.x,enemy.z,enemy.alive ? target.x-enemy.x : 0,enemy.alive ? target.z-enemy.z : 0);
                Animate(v,clock,active && enemy.alive,enemy.attackCooldown, !enemy.alive);
                v.ring.gameObject.SetActive(enemy.alive); Feedback(v,enemy.hp,clock);
                if (enemy.kind == "customer") { v.ringRenderer.sharedMaterial = kit.Material(enemy.patience < 15 ? danger : accent,true); Carry(v,enemy.need); }
                if (!enemy.alive && v.hit != null) v.hit.gameObject.SetActive(false);
            }
            foreach (Mayo.Frontier.Structure structure in state.structures)
            {
                Visual v = Get(3,structure.id,structure.kind);
                v.root.localPosition = new Vector3(structure.x,0,structure.z);
                v.root.gameObject.SetActive(structure.hp > 0);
                if (structure.hp <= 0) continue;
                UpdateStructure(v,structure,clock,state);
                Feedback(v,structure.hp,clock);
            }
            foreach (Mayo.Frontier.Pickup pickup in state.pickups)
            {
                Visual v = Get(4,pickup.id,pickup.kind);
                v.root.gameObject.SetActive(pickup.active); v.root.localPosition = new Vector3(pickup.x,0,pickup.z);
                if (!pickup.active) continue;
                v.model.localPosition = new Vector3(0,.14f + Mathf.Sin(clock*2+pickup.id)*.07f,0);
                v.model.localRotation = Quaternion.Euler(0,clock*15 + pickup.id*31,0);
            }
            foreach (Mayo.Frontier.TaskState task in state.tasks)
            {
                if (task.kind == "order") continue; // Order management belongs to the actual HUD, not duplicate world text.
                Visual v = Get(5,task.id,task.kind);
                v.root.localPosition = new Vector3(task.x,0,task.z);
                bool complete = task.status == "complete";
                bool rescued = task.kind == "survivor" || task.kind == "resident" || task.kind == "passenger";
                v.root.gameObject.SetActive(!(complete && rescued));
                if (v.marker != null) { v.marker.gameObject.SetActive(!complete); v.marker.localPosition = new Vector3(0,2.65f+Mathf.Sin(clock*2+task.id)*.12f,0); v.marker.localRotation = Quaternion.Euler(0,clock*35,0); }
                v.ringRenderer.sharedMaterial = kit.Material(complete ? signal : accent,true);
                if (v.leftArm != null) Animate(v,clock,active,0,false);
                if (task.kind == "hull-breach" && v.statusItem != null) v.statusItem.gameObject.SetActive(!complete);
                if (task.kind == "thorn-vine") v.model.gameObject.SetActive(!complete);
            }
            stale.Clear();
            foreach (KeyValuePair<long,Visual> item in entities) if (item.Value.seen != epoch) stale.Add(item.Key);
            for (int i=0;i<stale.Count;i++) { Visual v=entities[stale[i]]; if(v.root!=null)Destroy(v.root.gameObject);entities.Remove(stale[i]); }
            if (localFound) cameraFocus = Vector3.Lerp(cameraFocus,focus,1-Mathf.Exp(-Time.unscaledDeltaTime*4));
            PlaceCamera(!cameraPositioned);
            if (stormRing != null) stormRing.localScale = new Vector3(state.zoneRadius*2,1,state.zoneRadius*2);
        }
        private Visual Get(int category, int id, string kind)
        {
            long key=((long)category<<32)|(uint)id; Visual v;
            if (entities.TryGetValue(key,out v) && v.kind != kind) { Destroy(v.root.gameObject); entities.Remove(key); v=null; }
            if (v == null)
            {
                v = new Visual { root=Group(entityRoot,category+":"+id+" "+kind),kind=kind };
                v.model=Group(v.root,"Original model");
                v.ring=kit.Part(v.root,"ring",new Vector3(0,.026f,0),Vector3.one*1.3f,category==2?danger:accent,glow:true);
                v.ringRenderer=v.ring.GetComponent<MeshRenderer>();
                v.hit=kit.Part(v.root,"ring",new Vector3(0,.035f,0),Vector3.one*1.7f,danger,glow:true);v.hit.gameObject.SetActive(false);
                if(category==1 || category==2) BuildCharacter(v,kind,id);
                else if(category==3) BuildStructure(v,kind);
                else if(category==4) { BuildItem(v.model,kind,1);v.ring.localScale=Vector3.one*.9f; }
                else BuildTask(v,kind,id);
                entities.Add(key,v);
            }
            v.seen=epoch; return v;
        }
        private void Position(Visual v,float x,float z,float faceX,float faceZ)
        {
            Vector3 p=new Vector3(x,0,z);
            if(v.positioned)
            {
                float distance = Vector3.Distance(p,v.previousPosition); v.walkPhase += distance*5;
                v.movement = Mathf.Lerp(v.movement,distance/Mathf.Max(Time.unscaledDeltaTime,.01f),1-Mathf.Exp(-Time.unscaledDeltaTime*12));
            }
            v.previousPosition=p;v.positioned=true;v.root.localPosition=p;
            if(faceX*faceX+faceZ*faceZ>.01f) v.root.localRotation=Quaternion.Euler(0,Mathf.Atan2(faceX,faceZ)*Mathf.Rad2Deg,0);
        }
        private void Animate(Visual v,float clock,bool active,float attack,bool dead)
        {
            v.model.localRotation = dead ? Quaternion.Euler(0,0,82) : Quaternion.identity;
            v.model.localPosition = dead ? new Vector3(0,.1f,0) : Vector3.zero;
            if(v.leftArm==null)return;
            float gait=active?Mathf.Sin(v.walkPhase)*Mathf.Clamp01(v.movement/1.5f):0;
            float strike=active?Mathf.Clamp01(attack/.55f):0;
            v.leftLeg.localRotation=Quaternion.Euler(gait*28,0,0);v.rightLeg.localRotation=Quaternion.Euler(-gait*28,0,0);
            if(v.quadruped)
            {
                v.leftArm.localRotation=Quaternion.Euler(-gait*28,0,0);v.rightArm.localRotation=Quaternion.Euler(gait*28,0,0);
            }
            else
            {
                bool zombie=v.kind.Contains("zombie") || v.kind.Contains("brute");
                v.leftArm.localRotation=Quaternion.Euler(zombie?-68:gait*-21,0,zombie?-8:4);
                bool ranged = v.kind.StartsWith("hero-") && IsRangedHero();
                v.rightArm.localRotation=Quaternion.Euler(ranged ? -strike*8 : strike>0?-75+Mathf.Sin(strike*Mathf.PI)*100:zombie?-73:gait*21,0,-4);
                if(v.head!=null) v.head.localRotation=Quaternion.Euler(zombie?14:Mathf.Sin(clock*1.1f)*2,0,zombie?10:0);
            }
        }
        private void Feedback(Visual v,float hp,float clock)
        {
            if(v.previousHp>=0 && hp<v.previousHp)v.hitUntil=clock+.18f;
            v.previousHp=hp;
            if(v.hit!=null)v.hit.gameObject.SetActive(clock<v.hitUntil);
        }
        private void Carry(Visual v,string item)
        {
            item=item??"";if(v.carryKind==item)return;v.carryKind=item;
            if(v.carry!=null)Destroy(v.carry.gameObject);
            if(item.Length==0)return;
            v.carry=Group(v.model,"Carried "+item);v.carry.localPosition=new Vector3(-.45f,1.05f,.42f);v.carry.localScale=Vector3.one*.48f;
            string shown=item.Contains("fish")?"fish":item.Contains("bread")?"bread":item.Contains("burnt")?"burnt":item.Contains("stew")?"stew":item=="tools"?"metal":item=="produce"?"food":item=="groceries"?"food":item;
            BuildItem(v.carry,shown,1);
        }
        private void PlaceCamera(bool snap)
        {
            if(viewCamera==null)return;
            Vector3 target=cameraFocus+new Vector3(23,30,-26);
            viewCamera.transform.position=snap?target:Vector3.Lerp(viewCamera.transform.position,target,1-Mathf.Exp(-Time.unscaledDeltaTime*8));
            viewCamera.transform.rotation=Quaternion.LookRotation(cameraFocus+Vector3.up*.4f-viewCamera.transform.position,Vector3.up);cameraPositioned=true;
        }
        private static Vector3 ClosestLivingPlayer(Mayo.Frontier.World state,float x,float z)
        {
            Vector3 result=Vector3.zero;float best=float.MaxValue;
            foreach(Mayo.Frontier.Player p in state.players)
            {
                if(!p.connected || p.dead)continue;float d=(p.x-x)*(p.x-x)+(p.z-z)*(p.z-z);
                if(d<best){best=d;result=new Vector3(p.x,0,p.z);}
            }
            return result;
        }
        private bool IsRangedHero() { return gameId=="ashfall-county" || gameId=="dead-orbit" || gameId=="havenfall-coalition" || gameId=="rift-runners" || gameId=="stormline-express"; }
        private static Transform Group(Transform parent,string name) { GameObject item=new GameObject(name);item.transform.SetParent(parent,false);return item.transform; }
        private Transform P(Transform root,string shape,float x,float y,float z,float sx,float sy,float sz,Color color,Vector3 euler=default(Vector3),bool glow=false) { return kit.Part(root,shape,new Vector3(x,y,z),new Vector3(sx,sy,sz),color,euler,glow); }
        private Transform Beam(Transform parent,Vector3 a,Vector3 b,float width,Color color)
        {
            Transform t=kit.Part(parent,"cylinder",a,new Vector3(width,(b-a).magnitude,width),color);t.localRotation=Quaternion.FromToRotation(Vector3.up,b-a);return t;
        }
        public void Dispose()
        {
            initialized=false;cameraPositioned=false;epoch=0;previousWorldClock=-1;entities.Clear();stale.Clear();
            if(ownedRoot!=null){ownedRoot.gameObject.SetActive(false);Destroy(ownedRoot.gameObject);ownedRoot=null;}
            viewCamera=null;scenery=null;entityRoot=null;stormRing=null;licensedFooting=null;
            if(kit!=null){kit.Dispose();kit=null;}
            if(environmentOwned)
            {
                RenderSettings.ambientLight=previousAmbient;RenderSettings.ambientMode=previousAmbientMode;RenderSettings.ambientIntensity=previousAmbientIntensity;
                RenderSettings.fog=previousFogEnabled;RenderSettings.fogColor=previousFog;RenderSettings.fogDensity=previousFogDensity;environmentOwned=false;
            }
        }
        private void OnDestroy() { Dispose(); }
    }
}
