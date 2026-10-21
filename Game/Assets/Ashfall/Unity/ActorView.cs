using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ashfall.View
{
    /// <summary>Low-poly humanoid with a procedural gait. Purchased rigs (when restored) replace the body by role;
    /// either way, the visual follows authoritative positions and never moves the simulated actor.
    /// Zombie kinds get a readable silhouette (bulk, tint, glowing eyes, riot gear) on both procedural and purchased
    /// bodies: decorations are child objects on the head/chest anchors and tints multiply the body's own materials.</summary>
    public sealed class ActorView : MonoBehaviour
    {
        Transform body, head, torso, armL, armR, legL, legR, held, belly, marker, vestObj, helmetObj;
        Animator purchasedAnimator; Transform purchased;
        Transform pArmL, pArmR, pLegL, pLegR, pSpine, pHead, pChest; Quaternion rArmL, rArmR, rLegL, rLegR, rSpine;
        float phase, lying, swing; Vector3 velocity, ground; string heldId = ""; bool animated, wasAttacking;
        public bool zombie;
        public float Alpha = 1;
        /// <summary>Pool key: views with the same key look identical and can be reused for each other.</summary>
        public string LookKey = "";
        public ZombieKind Kind { get; private set; }
        public bool Plague { get; private set; }
        /// <summary>Overall scale (juggernauts are bigger).</summary>
        public float Size { get; private set; }
        /// <summary>True on the frame a foot lands (juggernaut stomps, dust).</summary>
        public bool Footfall { get; private set; }
        public bool Dead { get { return lying > .5f && wasDead; } }
        static Mesh cube;
        Material skinMat, shirtMat, pantsMat, hairMat, bloodMat;
        readonly List<Material> owned = new List<Material>(); readonly List<Color> ownedBase = new List<Color>(); readonly List<bool> ownedEmission = new List<bool>();
        Renderer[] renderers; int lod; bool flashing, wasDead;
        float hitClock, screamClock, chargeClock, pounceClock, grabClock, fallSign = -1, lean, hop; int lastStep, markerKind = -1;
        Color shirtNow = new Color(-1, 0, 0), pantsNow;
        const float HitTime = .26f, PounceTime = .5f;
        static int seeds; readonly int seed = ++seeds;   // per-view phase offset and fall direction
        static readonly int EmissionId = Shader.PropertyToID("_Emission"), ColorId = Shader.PropertyToID("_Color");

        public static ActorView Create(Transform parent, bool zombie, int variant, bool female, string role)
        {
            var go = new GameObject(zombie ? "Zombie" : "Survivor"); go.transform.SetParent(parent, false);
            var a = go.AddComponent<ActorView>(); a.zombie = zombie;
            if (zombie) role = ArtLibrary.Variant("zombie", variant) ?? role;
            a.Build(variant, female, role); a.Finish(); return a;
        }

        /// <summary>A zombie of a bestiary kind: art role "zombie/&lt;kindId&gt;/&lt;n&gt;", then "zombie/&lt;n&gt;", then procedural.</summary>
        public static ActorView CreateZombie(Transform parent, ZombieKind kind, bool plague, int variant, bool female)
        {
            var go = new GameObject("Zombie " + kind); go.transform.SetParent(parent, false);
            var a = go.AddComponent<ActorView>(); a.zombie = true;
            var def = KindDef(kind); string id = def != null ? def.id : kind.ToString().ToLowerInvariant();
            string role = ArtLibrary.Variant("zombie/" + id, variant) ?? ArtLibrary.Variant("zombie", variant) ?? (female ? "zombie_female" : "zombie");
            a.Build(variant, female, role); a.Decorate(kind, plague, def); a.LookKey = ZombieKey(kind, plague, variant, female); a.Finish(); return a;
        }
        public static string ZombieKey(ZombieKind kind, bool plague, int variant, bool female) { return "z" + (int)kind + (plague ? "p" : "") + "/" + variant + (female ? "f" : "m"); }
        public static ZombieKindDef KindDef(ZombieKind kind) { try { return Bestiary.Get(kind); } catch (System.Exception) { return null; } }

        /// <summary>A human: role is already resolved by the caller (character/, human/group/, survivor_*); tint multiplies purchased materials.</summary>
        public static ActorView CreateHuman(Transform parent, string role, int variant, bool female, Color tint)
        {
            var go = new GameObject("Survivor"); go.transform.SetParent(parent, false);
            var a = go.AddComponent<ActorView>(); a.zombie = false; a.Build(variant, female, role);
            if (a.purchased != null && tint != Color.white) a.TintPurchased(tint);
            a.Finish(); return a;
        }

        static readonly Color[] Shirts = { new Color(.42f, .2f, .18f), new Color(.2f, .28f, .42f), new Color(.5f, .48f, .42f), new Color(.24f, .34f, .22f), new Color(.6f, .55f, .3f), new Color(.3f, .3f, .32f), new Color(.55f, .25f, .32f), new Color(.15f, .15f, .18f) };
        static readonly Color[] Pants = { new Color(.18f, .22f, .32f), new Color(.3f, .26f, .2f), new Color(.2f, .2f, .22f), new Color(.38f, .34f, .28f) };

        void Build(int variant, bool female, string role)
        {
            Size = 1;
            if (cube == null) { var k = new MeshKit(); k.Box(new Vector3(0, -.5f, 0), Vector3.one, new Color32(255, 255, 255, 255), 0, true, true); cube = k.Build("Actor cube"); }
            body = new GameObject("Body").transform; body.SetParent(transform, false);
            var p = string.IsNullOrEmpty(role) ? null : ArtLibrary.Spawn(role, body);
            if (p != null)
            {
                purchased = p.transform; purchasedAnimator = p.GetComponentInChildren<Animator>();
                if (purchasedAnimator != null && purchasedAnimator.isHuman)
                {
                    purchasedAnimator.applyRootMotion = false; purchasedAnimator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                    pArmL = purchasedAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm); pArmR = purchasedAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                    pLegL = purchasedAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg); pLegR = purchasedAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                    pSpine = purchasedAnimator.GetBoneTransform(HumanBodyBones.Spine); pHead = purchasedAnimator.GetBoneTransform(HumanBodyBones.Head);
                    pChest = purchasedAnimator.GetBoneTransform(HumanBodyBones.Chest) ?? pSpine;
                    if (purchasedAnimator.runtimeAnimatorController == null) purchasedAnimator.enabled = false;
                    if (pArmL) rArmL = pArmL.localRotation; if (pArmR) rArmR = pArmR.localRotation; if (pLegL) rLegL = pLegL.localRotation; if (pLegR) rLegR = pLegR.localRotation; if (pSpine) rSpine = pSpine.localRotation;
                    held = new GameObject("Held").transform; held.SetParent(purchasedAnimator.GetBoneTransform(HumanBodyBones.RightHand) ?? body, false);
                }
                else { held = new GameObject("Held").transform; held.SetParent(body, false); held.localPosition = new Vector3(.3f, 1.0f, .2f); }
                if (purchasedAnimator != null && purchasedAnimator.runtimeAnimatorController != null) { purchasedAnimator.enabled = true; animated = true; }
                return;
            }
            var rng = new Rng((ulong)(variant * 7919 + (zombie ? 13 : 0) + (female ? 3 : 0)), 1);
            var skin = zombie ? Color.Lerp(new Color(.5f, .56f, .44f), new Color(.62f, .6f, .52f), rng.Float()) : Color.Lerp(new Color(.86f, .7f, .56f), new Color(.45f, .3f, .2f), rng.Float());
            var shirt = Shirts[rng.Range(0, Shirts.Length)]; var pants = Pants[rng.Range(0, Pants.Length)];
            if (zombie) { shirt = Color.Lerp(shirt, new Color(.3f, .22f, .16f), .45f); pants = Color.Lerp(pants, new Color(.25f, .2f, .15f), .4f); }
            skinMat = Mat(skin); shirtMat = Mat(shirt); pantsMat = Mat(pants); hairMat = Mat(female ? new Color(.35f, .22f, .12f) : new Color(.16f, .12f, .08f));
            float s = female ? .95f : 1;
            legL = Limb("LegL", body, new Vector3(-.11f, .92f, 0) * s, new Vector3(.15f, .9f, .17f) * s, pantsMat);
            legR = Limb("LegR", body, new Vector3(.11f, .92f, 0) * s, new Vector3(.15f, .9f, .17f) * s, pantsMat);
            torso = Part("Torso", body, new Vector3(0, 1.22f, 0) * s, new Vector3(.44f, .62f, .26f) * s, shirtMat);
            head = Part("Head", body, new Vector3(0, 1.68f, 0) * s, new Vector3(.24f, .27f, .26f) * s, skinMat);
            Part("Hair", head, new Vector3(0, .1f, -.02f), new Vector3(1.08f, female ? .55f : .35f, 1.08f), hairMat);
            if (female) Part("Ponytail", head, new Vector3(0, -.1f, -.55f), new Vector3(.5f, .9f, .3f), hairMat);
            armL = Limb("ArmL", body, new Vector3(-.29f, 1.5f, 0) * s, new Vector3(.12f, .66f, .13f) * s, shirtMat);
            armR = Limb("ArmR", body, new Vector3(.29f, 1.5f, 0) * s, new Vector3(.12f, .66f, .13f) * s, shirtMat);
            Part("HandL", armL, new Vector3(0, -.7f, 0), new Vector3(.11f, .12f, .12f) * s, skinMat);
            Part("HandR", armR, new Vector3(0, -.7f, 0), new Vector3(.11f, .12f, .12f) * s, skinMat);
            if (zombie && rng.Chance(.6f)) Part("Blood", torso, new Vector3(rng.Range(-.2f, .2f), rng.Range(-.2f, .2f), .51f), new Vector3(.45f, .35f, .02f), bloodMat = Mat(new Color(.35f, .05f, .04f)));
            held = new GameObject("Held").transform; held.SetParent(armR, false); held.localPosition = new Vector3(0, -.72f, .02f);
            Own(skinMat); Own(shirtMat); Own(pantsMat); Own(hairMat);
        }
        void Own(Material m) { if (m == null) return; owned.Add(m); ownedBase.Add(m.HasProperty(ColorId) ? m.color : Color.white); ownedEmission.Add(m.HasProperty(EmissionId)); }
        void SetOwnedColor(Material m, Color c) { m.color = c; int i = owned.IndexOf(m); if (i >= 0) ownedBase[i] = c; }
        void Rebase() { for (int i = 0; i < owned.Count; i++) if (owned[i].HasProperty(ColorId)) ownedBase[i] = owned[i].color; }
        void Finish()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            if (Size != 1) transform.localScale = Vector3.one * Size;
        }

        static Material Mat(Color c) { var m = new Material(ArtLibrary.Vertex); m.color = c; return m; }
        Transform Part(string name, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = cube; go.AddComponent<MeshRenderer>().sharedMaterial = m;
            return go.transform;
        }
        Transform Limb(string name, Transform parent, Vector3 pivot, Vector3 size, Material m)
        {
            var p = new GameObject(name).transform; p.SetParent(parent, false); p.localPosition = pivot;
            Part(name + " mesh", p, new Vector3(0, -size.y * .5f, 0), size, m);
            return p;
        }

        // ---------------------------------------------------------------- kinds, tints and gear
        static Material redEyes, amberEyes, riotGear, visorGlass, plateGear, bloatFlesh, pusFlesh, mouthFlesh, humpFlesh;
        static Material Shared(ref Material m, Color c, float emission = 0)
        {
            if (m == null) { m = new Material(ArtLibrary.Vertex) { color = c }; if (emission > 0) m.SetFloat(EmissionId, emission); }
            return m;
        }

        void Decorate(ZombieKind kind, bool plague, ZombieKindDef def)
        {
            Kind = kind; Plague = plague;
            if (kind == ZombieKind.Juggernaut) Size = Mathf.Clamp(def != null && def.scale > 0 ? def.scale : 1.7f, 1.2f, 2.4f);
            else if (kind == ZombieKind.Bloater) Size = 1.05f;
            else if (kind == ZombieKind.Screamer) Size = 1.04f;
            else if (kind == ZombieKind.Feral) Size = .96f;
            if (purchased == null) ShapeProcedural(kind);
            else TintPurchased(KindTint(kind));
            if (plague) { MultiplyOwned(new Color(1.12f, .78f, .76f)); }
            bool eyes = plague || kind == ZombieKind.Feral;
            if (eyes) Eyes(plague ? Shared(ref redEyes, new Color(1, .12f, .06f), 1.6f) : Shared(ref amberEyes, new Color(1, .62f, .12f), 1.4f));
            if (kind == ZombieKind.Armored) RiotGear(true, true);
            if (kind == ZombieKind.Bloater) Belly();
            if (kind == ZombieKind.Juggernaut) Hump();
            Rebase();
        }

        static Color KindTint(ZombieKind kind)
        {
            switch (kind)
            {
                case ZombieKind.Juggernaut: return new Color(.55f, .5f, .52f);
                case ZombieKind.Bloater: return new Color(.82f, 1.0f, .55f);
                case ZombieKind.Feral: return new Color(.5f, .47f, .45f);
                case ZombieKind.Screamer: return new Color(1.25f, 1.25f, 1.2f);
                case ZombieKind.Armored: return new Color(.75f, .78f, .9f);
                default: return Color.white;
            }
        }

        void ShapeProcedural(ZombieKind kind)
        {
            if (torso == null) return;
            switch (kind)
            {
                case ZombieKind.Juggernaut:
                    skinMat.color = new Color(.42f, .38f, .4f); shirtMat.color = new Color(.16f, .14f, .13f); pantsMat.color = new Color(.14f, .13f, .12f);
                    Shape(new Vector3(1.5f, 1.12f, 1.55f), 1.65f, .82f, 1.5f); head.localPosition += new Vector3(0, -.06f, .06f); break;
                case ZombieKind.Bloater:
                    skinMat.color = new Color(.6f, .68f, .34f); shirtMat.color = Color.Lerp(shirtMat.color, new Color(.48f, .5f, .2f), .55f);
                    Shape(new Vector3(1.45f, 1.04f, 1.6f), 1.3f, 1, 1.32f); break;
                case ZombieKind.Feral:
                    skinMat.color = new Color(.34f, .33f, .31f); shirtMat.color = new Color(.12f, .11f, .1f); pantsMat.color = new Color(.13f, .12f, .11f); hairMat.color = new Color(.06f, .05f, .05f);
                    Shape(new Vector3(.9f, .95f, .9f), .8f, .95f, .95f); break;
                case ZombieKind.Screamer:
                    skinMat.color = new Color(.82f, .82f, .76f); shirtMat.color = new Color(.6f, .58f, .54f); pantsMat.color = new Color(.4f, .4f, .42f);
                    Shape(new Vector3(.78f, 1.06f, .8f), .74f, 1.05f, .85f);
                    Part("Mouth", head, new Vector3(0, -.28f, .51f), new Vector3(.38f, .26f, .04f), Shared(ref mouthFlesh, new Color(.12f, .03f, .03f))); break;
                case ZombieKind.Armored:
                    shirtMat.color = new Color(.14f, .16f, .22f); pantsMat.color = new Color(.13f, .14f, .18f); break;
            }
        }
        void Shape(Vector3 torsoMul, float limbThick, float headMul, float shoulderMul)
        {
            torso.localScale = Vector3.Scale(torso.localScale, torsoMul);
            foreach (var limb in new[] { armL, armR, legL, legR })
            {
                if (limb == null || limb.childCount == 0) continue;
                var m = limb.GetChild(0); m.localScale = new Vector3(m.localScale.x * limbThick, m.localScale.y, m.localScale.z * limbThick);
            }
            armL.localPosition = new Vector3(armL.localPosition.x * shoulderMul, armL.localPosition.y, armL.localPosition.z);
            armR.localPosition = new Vector3(armR.localPosition.x * shoulderMul, armR.localPosition.y, armR.localPosition.z);
            legL.localPosition = new Vector3(legL.localPosition.x * Mathf.Sqrt(shoulderMul), legL.localPosition.y, legL.localPosition.z);
            legR.localPosition = new Vector3(legR.localPosition.x * Mathf.Sqrt(shoulderMul), legR.localPosition.y, legR.localPosition.z);
            head.localScale *= headMul;
        }

        /// <summary>Multiplies purchased materials (instanced once for this view) by a tint.</summary>
        void TintPurchased(Color tint)
        {
            if (purchased == null) return;
            foreach (var r in purchased.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                var mats = r.materials;   // per-view instances
                foreach (var m in mats)
                {
                    if (m == null) continue;
                    if (m.HasProperty(ColorId)) m.color = Mul(m.color, tint);
                    else if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Mul(m.GetColor("_BaseColor"), tint));
                    if (!owned.Contains(m)) Own(m);
                }
            }
        }
        void MultiplyOwned(Color tint) { foreach (var m in owned) if (m.HasProperty(ColorId)) m.color = Mul(m.color, tint); }
        static Color Mul(Color a, Color b) { return new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a); }

        /// <summary>Head centre, chest centre and body height in actor space (procedural parts or purchased bones/bounds).</summary>
        void Anchors(out Transform headAnchor, out Vector3 headPos, out Transform chestAnchor, out Vector3 chestPos, out float height)
        {
            height = 1.75f;
            if (purchased == null)
            {
                headAnchor = head; headPos = head.position; chestAnchor = torso; chestPos = torso.position; return;
            }
            var bounds = new Bounds(transform.position + Vector3.up, Vector3.zero); bool any = false;
            foreach (var r in purchased.GetComponentsInChildren<Renderer>(true)) { if (r is ParticleSystemRenderer) continue; if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds); }
            height = any ? Mathf.Clamp(bounds.max.y - transform.position.y, .8f, 3.5f) : 1.75f;
            headAnchor = pHead != null ? pHead : body; headPos = pHead != null ? pHead.position + Vector3.up * height * .03f : transform.position + Vector3.up * height * .9f;
            chestAnchor = pChest != null ? pChest : body; chestPos = pChest != null ? pChest.position : transform.position + Vector3.up * height * .68f;
        }
        Transform Attach(string name, Transform anchor, Vector3 worldPos, Vector3 worldSize, Mesh mesh, Material m)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            go.transform.position = worldPos; go.transform.localScale = worldSize;
            go.AddComponent<MeshFilter>().sharedMesh = mesh; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
            go.transform.SetParent(anchor, true);
            return go.transform;
        }

        void Eyes(Material m)
        {
            Transform ha, ca; Vector3 hp, cp; float h; Anchors(out ha, out hp, out ca, out cp, out h);
            float headSize = purchased == null ? .25f : h * .13f;
            for (int side = -1; side <= 1; side += 2)
            {
                var e = Attach("Eye", ha, hp + new Vector3(side * headSize * .24f, headSize * .08f, headSize * .52f), new Vector3(headSize * .2f, headSize * .11f, headSize * .06f), cube, m);
                e.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        /// <summary>Riot helmet and vest boxes (armored zombies, and survivors wearing armour).</summary>
        void RiotGear(bool vest, bool helmet)
        {
            Transform ha, ca; Vector3 hp, cp; float h; Anchors(out ha, out hp, out ca, out cp, out h);
            float k = h / 1.75f;
            if (helmet && helmetObj == null)
            {
                float hs = purchased == null ? .26f : h * .14f;
                helmetObj = Attach("Helmet", ha, hp + new Vector3(0, hs * .2f, -hs * .02f), new Vector3(hs * 1.25f, hs * .78f, hs * 1.22f), cube, Shared(ref riotGear, new Color(.13f, .15f, .2f)));
                Attach("Visor", helmetObj, hp + new Vector3(0, -hs * .02f, hs * .6f), new Vector3(hs * 1.08f, hs * .34f, hs * .08f), cube, Shared(ref visorGlass, new Color(.25f, .32f, .38f)));
            }
            if (vest && vestObj == null)
            {
                Vector3 size = purchased == null ? Vector3.Scale(torso.lossyScale, new Vector3(1.14f, .8f, 1.32f)) : new Vector3(.5f, .5f, .3f) * k;
                vestObj = Attach("Vest", ca, cp + Vector3.up * .02f * k, size, cube, Shared(ref plateGear, new Color(.2f, .23f, .2f)));
                Attach("Plate", vestObj, cp + new Vector3(0, .06f * k, size.z * .5f + .01f), new Vector3(size.x * .7f, size.y * .55f, .03f * k), cube, Shared(ref riotGear, new Color(.13f, .15f, .2f)));
            }
        }
        /// <summary>Shows or hides armour on a survivor (vest/helmet slots), created on first use.</summary>
        public void SetArmor(bool vest, bool helmet)
        {
            if (vest && vestObj == null || helmet && helmetObj == null) { RiotGear(vest, helmet); renderers = GetComponentsInChildren<Renderer>(true); }
            if (vestObj != null && vestObj.gameObject.activeSelf != vest) vestObj.gameObject.SetActive(vest);
            if (helmetObj != null && helmetObj.gameObject.activeSelf != helmet) helmetObj.gameObject.SetActive(helmet);
        }

        void Belly()
        {
            Transform ha, ca; Vector3 hp, cp; float h; Anchors(out ha, out hp, out ca, out cp, out h);
            float k = h / 1.75f; var flesh = Shared(ref bloatFlesh, new Color(.55f, .62f, .3f));
            belly = Attach("Belly", ca, cp + new Vector3(0, -.08f * k, .12f * k), new Vector3(.66f, .62f, .6f) * k, ArtLibrary.Blob, flesh);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 1.7f; var pos = belly.position + new Vector3(Mathf.Cos(a) * .22f * k, Mathf.Sin(a * 1.3f) * .18f * k, .26f * k);
                Attach("Pustule", belly, pos, Vector3.one * (.1f + i * .02f) * k, ArtLibrary.Blob, Shared(ref pusFlesh, new Color(.78f, .72f, .3f), .25f));
            }
            bellyBase = belly.localScale;
        }
        Vector3 bellyBase;
        void Hump()
        {
            Transform ha, ca; Vector3 hp, cp; float h; Anchors(out ha, out hp, out ca, out cp, out h);
            float k = h / 1.75f;
            Attach("Hump", ca, cp + new Vector3(0, .26f * k, -.12f * k), new Vector3(.7f, .4f, .48f) * k, ArtLibrary.Blob, skinMat ?? Shared(ref humpFlesh, new Color(.3f, .26f, .28f)));
        }

        // ---------------------------------------------------------------- clothes, held items and markers
        public void SetClothes(Color shirt, Color pants)
        {
            if (shirtMat == null || shirt == shirtNow && pants == pantsNow) return;
            shirtNow = shirt; pantsNow = pants; SetOwnedColor(shirtMat, shirt); SetOwnedColor(pantsMat, pants);
        }

        /// <summary>Shows a simple prop for the equipped item.</summary>
        public void SetHeld(string itemId)
        {
            itemId = itemId ?? "";
            if (held == null || itemId == heldId) return; heldId = itemId;
            for (int i = held.childCount - 1; i >= 0; i--) Destroy(held.GetChild(i).gameObject);
            if (string.IsNullOrEmpty(itemId)) return;
            var role = "item/" + itemId; var p = ArtLibrary.Spawn(role, held); if (p != null) return;
            var def = ItemCatalog.Exists(itemId) ? ItemCatalog.Get(itemId) : null; if (def == null) return;
            bool gun = def.category == ItemCategory.Firearm;
            var color = gun ? new Color(.12f, .12f, .13f) : def.blade ? new Color(.7f, .72f, .75f) : def.id == "frying_pan" || def.id == "pipe" || def.id == "crowbar" ? new Color(.45f, .46f, .5f) : new Color(.5f, .36f, .22f);
            float len = gun ? (def.twoHanded ? (itemId.Contains("hunting") || itemId.Contains("sniper") ? 1.15f : .9f) : itemId == "smg" ? .5f : .3f) : Mathf.Clamp(def.range * .6f, .25f, 1.2f);
            var m = HeldMat(color); bool thrown = def.blastRadius > 0 || def.tags != null && System.Array.IndexOf(def.tags, "throwable") >= 0;
            if (thrown) { Part("Body", held, new Vector3(0, 0, .06f), new Vector3(.09f, .12f, .09f), HeldMat(new Color(.3f, .34f, .22f))); return; }
            Part("Shaft", held, new Vector3(0, 0, len * .5f), new Vector3(.05f, .05f, len), m);
            if (gun && def.twoHanded) Part("Stock", held, new Vector3(0, -.04f, .04f), new Vector3(.06f, .12f, .2f), HeldMat(new Color(.3f, .22f, .14f)));
            if (gun && def.magazine > 8) Part("Magazine", held, new Vector3(0, -.08f, len * .45f), new Vector3(.04f, .14f, .06f), m);
            if (def.id == "axe") Part("Head", held, new Vector3(.06f, 0, len * .95f), new Vector3(.18f, .04f, .14f), HeldMat(new Color(.6f, .6f, .62f)));
            if (def.id == "shovel") Part("Blade", held, new Vector3(0, 0, len), new Vector3(.2f, .03f, .26f), HeldMat(new Color(.5f, .5f, .5f)));
        }

        static readonly Dictionary<Color, Material> heldMats = new Dictionary<Color, Material>();
        static Material HeldMat(Color c) { Material m; if (!heldMats.TryGetValue(c, out m) || m == null) heldMats[c] = m = Mat(c); return m; }
        static Material[] markerMats;
        /// <summary>Ground ring under a human: 0 none, 1 community, 2 hostile, 3 controlled, 4 neutral.</summary>
        public void SetMarker(int kind)
        {
            if (kind == markerKind) return; markerKind = kind;
            if (markerMats == null) markerMats = new[] { null, Fx.Tinted(new Color(.3f, .7f, 1, .45f), false), Fx.Tinted(new Color(1, .2f, .12f, .55f), false), Fx.Tinted(new Color(.85f, 1, .95f, .7f), false), Fx.Tinted(new Color(1, .85f, .3f, .35f), false) };
            if (kind <= 0 || kind >= markerMats.Length) { if (marker != null) marker.gameObject.SetActive(false); return; }
            if (marker == null)
            {
                var go = MeshKit.Object("Marker", transform, Fx.Ring, markerMats[kind], false); go.GetComponent<MeshRenderer>().receiveShadows = false;
                marker = go.transform; marker.localPosition = new Vector3(0, .05f, 0); marker.localScale = new Vector3(.55f, 1, .55f);
            }
            marker.GetComponent<MeshRenderer>().sharedMaterial = markerMats[kind]; marker.gameObject.SetActive(true);
        }

        // ---------------------------------------------------------------- reactions
        public void Hit() { hitClock = HitTime; if (animated) purchasedAnimator.SetTrigger("Hit"); }
        public void Scream() { screamClock = 1.3f; }
        public void Charge() { chargeClock = 1.6f; }
        public void Grab() { grabClock = 1.1f; }
        public void Pounce() { pounceClock = PounceTime; }
        public bool Charging { get { return chargeClock > 0; } }
        /// <summary>Approximate muzzle position in world space.</summary>
        public Vector3 Muzzle { get { return transform.position + transform.forward * .6f * Size + Vector3.up * 1.3f * Size; } }
        public Vector3 Chest { get { return transform.position + Vector3.up * 1.2f * Size; } }

        /// <summary>0 = full detail, 1 = no shadows and half-rate limbs, 2 = no shadows and no limb animation.</summary>
        public void SetLod(int level)
        {
            if (level == lod || renderers == null) return;
            bool shadows = level == 0; bool had = lod == 0; lod = level;
            if (shadows != had) foreach (var r in renderers) if (r != null && !(r is ParticleSystemRenderer) && r.transform != marker && r.gameObject.name != "Eye") r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (purchasedAnimator != null) purchasedAnimator.cullingMode = level >= 2 ? AnimatorCullingMode.CullCompletely : AnimatorCullingMode.CullUpdateTransforms;
        }

        /// <summary>Drives pose from simulation state each frame.</summary>
        public void Pose(Vector3 target, float facingDeg, float speed, bool attacking, bool knocked, bool dead, bool aiming, float dt)
        {
            if (dead && !wasDead) fallSign = (seed & 1) == 0 ? -1 : 1;
            wasDead = dead;
            ground = Vector3.SmoothDamp(ground, target, ref velocity, Kind == ZombieKind.Juggernaut ? .09f : .06f, 50, dt);
            hitClock = Mathf.Max(0, hitClock - dt); screamClock = Mathf.Max(0, screamClock - dt); chargeClock = Mathf.Max(0, chargeClock - dt);
            grabClock = Mathf.Max(0, grabClock - dt); pounceClock = Mathf.Max(0, pounceClock - dt);
            if (dead) { screamClock = chargeClock = grabClock = pounceClock = 0; }
            float pt = pounceClock > 0 ? 1 - pounceClock / PounceTime : 0;
            hop = pounceClock > 0 ? Mathf.Sin(pt * Mathf.PI) * 1.15f : 0;
            transform.position = hop > 0 ? ground + Vector3.up * hop : ground;
            var want = Quaternion.Euler(0, facingDeg, 0);
            transform.rotation = Quaternion.Slerp(transform.rotation, want, 1 - Mathf.Exp(-dt * (Kind == ZombieKind.Juggernaut ? 7 : 14)));
            float rate = !zombie ? 2.6f : Kind == ZombieKind.Juggernaut ? 2.6f / Size : Kind == ZombieKind.Feral ? 3.1f : 2.6f;
            phase += dt * Mathf.Clamp(speed, 0, 6) * rate;
            int step = (int)(phase / Mathf.PI); Footfall = step != lastStep && speed > .4f && !dead && !knocked; lastStep = step;
            lying = Mathf.MoveTowards(lying, knocked || dead ? 1 : 0, dt * (dead ? 3.2f : 3));
            swing = attacking ? Mathf.Min(1, swing + dt * 7) : Mathf.Max(0, swing - dt * 4);
            float gait = Mathf.Sin(phase) * Mathf.Clamp01(speed / 2.5f);
            float flinch = hitClock > 0 ? Mathf.Sin(hitClock / HitTime * Mathf.PI) : 0;
            // Kind lean: ferals hunt hunched; juggernauts lean into a charge; screamers throw their chest back.
            float wantLean = 0;
            if (zombie && lying < .5f)
            {
                if (Kind == ZombieKind.Feral) wantLean = 20 + Mathf.Clamp01(speed / 4) * 14;
                if (chargeClock > 0) wantLean = 30; if (grabClock > 0) wantLean = 14; if (pounceClock > 0) wantLean = 40; if (screamClock > 0) wantLean = -12;
            }
            lean = Mathf.Lerp(lean, wantLean, 1 - Mathf.Exp(-dt * 10));
            float bob = Kind == ZombieKind.Juggernaut ? Mathf.Abs(Mathf.Sin(phase)) * .06f * Mathf.Clamp01(speed) : 0;
            UpdateFlash();
            if (belly != null && lod < 2) { float pulse = 1 + Mathf.Sin(Time.time * 3.3f + seed) * (dead ? 0 : .07f); belly.localScale = Vector3.Scale(bellyBase, new Vector3(pulse, 2 - pulse, pulse)); }
            if (animated)
            {
                purchasedAnimator.SetFloat("Speed", speed, .1f, dt);
                purchasedAnimator.SetBool("Down", knocked || dead);
                if (attacking && !wasAttacking) purchasedAnimator.SetTrigger("Attack");
                wasAttacking = attacking;
                body.localRotation = Quaternion.Euler(lean - flinch * 14, 0, 0); body.localPosition = new Vector3(0, bob, 0);
                return;
            }
            body.localRotation = Quaternion.Euler(88 * lying * fallSign + lean * (1 - lying) - flinch * 16, 0, 0);
            body.localPosition = new Vector3(0, .12f * lying + bob, 0);
            if (lod >= 2 || lod == 1 && ((Time.frameCount + seed) & 1) == 0) return;
            if (purchased != null) { PosePurchased(gait, aiming); return; }
            if (legL == null) return;
            if (!zombie) { PoseHuman(gait, aiming, flinch); return; }
            float stride = Kind == ZombieKind.Feral ? 40 : Kind == ZombieKind.Juggernaut ? 24 : 32;
            legL.localRotation = Quaternion.Euler(gait * stride, 0, 0); legR.localRotation = Quaternion.Euler(-gait * stride, 0, 0);
            float reach = -80 + Mathf.Sin(phase * .5f) * 6;
            float torsoPitch = 10 + swing * 10, torsoYaw = Mathf.Sin(phase * .5f) * 6, torsoRoll = 0, headPitch = 14;
            float armLx = reach - swing * 30, armRx = reach - swing * 30, armLz = -6, armRz = 6;
            switch (Kind)
            {
                case ZombieKind.Feral:
                    float run = Mathf.Clamp01(speed / 3);
                    armLx = Mathf.Lerp(-30, 55, run) + gait * 25 - swing * 90; armRx = Mathf.Lerp(-30, 55, run) - gait * 25 - swing * 90; armLz = -14; armRz = 14;
                    torsoPitch = 18 + swing * 10; headPitch = -18; break;
                case ZombieKind.Juggernaut:
                    armLx = -12 + gait * 14 - swing * 70; armRx = -12 - gait * 14 - swing * 70; armLz = -18; armRz = 18;
                    torsoYaw = Mathf.Sin(phase) * 10; torsoPitch = 12; headPitch = 8; break;
                case ZombieKind.Bloater:
                    armLx = -45 + gait * 8; armRx = -45 - gait * 8; armLz = -28; armRz = 28; torsoRoll = Mathf.Sin(phase) * 8; torsoPitch = -4; break;
            }
            if (chargeClock > 0) { armLx = armRx = -70; armLz = -26; armRz = 26; torsoPitch = 18; }
            if (grabClock > 0) { armLx = armRx = -95; armLz = 14; armRz = -14; torsoPitch = 12; }
            if (pounceClock > 0) { armLx = armRx = -160; armLz = -12; armRz = 12; legL.localRotation = Quaternion.Euler(35, 0, 0); legR.localRotation = Quaternion.Euler(45, 0, 0); }
            if (screamClock > 0) { float k = Mathf.Clamp01(screamClock * 3); armLx = armRx = -30 * k; armLz = -75 * k; armRz = 75 * k; torsoPitch = -16 * k; headPitch = -40 * k + Mathf.Sin(Time.time * 40) * 3; }
            armL.localRotation = Quaternion.Euler(armLx, 0, armLz); armR.localRotation = Quaternion.Euler(armRx, 0, armRz);
            torso.localRotation = Quaternion.Euler(torsoPitch - flinch * 10, torsoYaw, torsoRoll);
            head.localRotation = Quaternion.Euler(headPitch, screamClock > 0 ? 0 : Mathf.Sin(phase * .33f) * 15, Mathf.Sin(phase * .21f) * 10);
        }
        void PoseHuman(float gait, bool aiming, float flinch)
        {
            legL.localRotation = Quaternion.Euler(gait * 32, 0, 0); legR.localRotation = Quaternion.Euler(-gait * 32, 0, 0);
            armL.localRotation = Quaternion.Euler(-gait * 26 - (aiming ? 70 : 0), 0, 0);
            float strike = swing > 0 ? Mathf.Lerp(-120, 20, swing) : -gait * -26;
            armR.localRotation = Quaternion.Euler(aiming ? -85 : strike, 0, 0);
            torso.localRotation = Quaternion.Euler(-flinch * 12, swing * 18, 0);
            head.localRotation = Quaternion.Euler(-flinch * 20, 0, 0);
        }
        void PosePurchased(float gait, bool aiming)
        {
            if (purchasedAnimator != null && purchasedAnimator.enabled) { purchasedAnimator.SetFloat("Speed", Mathf.Abs(gait) * 3); return; }
            Rotate(pLegL, rLegL, gait * 30); Rotate(pLegR, rLegR, -gait * 30);
            if (zombie)
            {
                float reach = Kind == ZombieKind.Juggernaut ? -20 : Kind == ZombieKind.Feral ? 30 : -75;
                if (chargeClock > 0 || grabClock > 0) reach = -80; if (pounceClock > 0) reach = -150; if (screamClock > 0) reach = -20;
                Rotate(pArmL, rArmL, reach); Rotate(pArmR, rArmR, reach - swing * 30);
            }
            else { Rotate(pArmL, rArmL, -gait * 22 - (aiming ? 70 : 0)); Rotate(pArmR, rArmR, aiming ? -85 : swing > 0 ? Mathf.Lerp(-110, 20, swing) : gait * 22); }
        }
        void Rotate(Transform joint, Quaternion rest, float angle)
        {
            if (joint == null) return;
            var parent = joint.parent == null ? Quaternion.identity : joint.parent.rotation;
            joint.localRotation = Quaternion.Inverse(parent) * Quaternion.AngleAxis(angle, transform.right) * parent * rest;
        }

        /// <summary>Hit flash: procedural materials glow briefly, purchased ones brighten toward a hot red-white.</summary>
        void UpdateFlash()
        {
            if (hitClock <= 0 && !flashing) return;
            float k = hitClock / HitTime; flashing = hitClock > 0;
            for (int i = 0; i < owned.Count; i++)
            {
                var m = owned[i]; if (m == null) continue;
                if (ownedEmission[i]) m.SetFloat(EmissionId, k * .9f);
                else if (m.HasProperty(ColorId)) m.color = Color.Lerp(ownedBase[i], new Color(1, .75f, .7f, ownedBase[i].a), k * .7f);
            }
        }

        /// <summary>Corpses jump straight to the end of the death pose.</summary>
        public void Settle()
        {
            lying = 1; wasDead = true;
            if (animated) { purchasedAnimator.SetBool("Down", true); purchasedAnimator.Play("Down", 0, 1); purchasedAnimator.Update(0); }
        }
        /// <summary>Clears reactions when a pooled view is reused.</summary>
        public void ResetPose()
        {
            lying = 0; swing = 0; hitClock = screamClock = chargeClock = grabClock = pounceClock = 0; lean = 0; hop = 0; wasDead = false; fallSign = -1;
            flashing = true; UpdateFlash();
            if (animated) purchasedAnimator.SetBool("Down", false);
        }
        public void SetVisible(bool visible) { if (gameObject.activeSelf != visible) gameObject.SetActive(visible); }
        public void Snap(Vector3 position, float facingDeg) { ground = position; transform.position = position; transform.rotation = Quaternion.Euler(0, facingDeg, 0); velocity = Vector3.zero; }

        void OnDestroy()
        {
            // Materials made for this view only (shared kind/eye/gear materials are static and kept).
            foreach (var m in owned) if (m != null) Destroy(m);
            if (bloodMat != null) Destroy(bloodMat);
        }
    }
}
