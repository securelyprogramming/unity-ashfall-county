using Ashfall.Core;
using UnityEngine;

namespace Ashfall.View
{
    /// <summary>Low-poly humanoid with a procedural gait. Purchased rigs (when restored) replace the body by role;
    /// either way, the visual follows authoritative positions and never moves the simulated actor.</summary>
    public sealed class ActorView : MonoBehaviour
    {
        Transform body, head, torso, armL, armR, legL, legR, held;
        Animator purchasedAnimator; Transform purchased;
        Transform pArmL, pArmR, pLegL, pLegR, pSpine; Quaternion rArmL, rArmR, rLegL, rLegR, rSpine;
        float phase, lying, swing; Vector3 velocity; string heldId = ""; bool animated, wasAttacking;
        public bool zombie;
        public float Alpha = 1;
        static Mesh cube;
        Material skinMat, shirtMat, pantsMat, hairMat, heldMat;

        public static ActorView Create(Transform parent, bool zombie, int variant, bool female, string role)
        {
            var go = new GameObject(zombie ? "Zombie" : "Survivor"); go.transform.SetParent(parent, false);
            var a = go.AddComponent<ActorView>(); a.zombie = zombie; a.Build(variant, female, role); return a;
        }

        static readonly Color[] Shirts = { new Color(.42f, .2f, .18f), new Color(.2f, .28f, .42f), new Color(.5f, .48f, .42f), new Color(.24f, .34f, .22f), new Color(.6f, .55f, .3f), new Color(.3f, .3f, .32f), new Color(.55f, .25f, .32f), new Color(.15f, .15f, .18f) };
        static readonly Color[] Pants = { new Color(.18f, .22f, .32f), new Color(.3f, .26f, .2f), new Color(.2f, .2f, .22f), new Color(.38f, .34f, .28f) };

        void Build(int variant, bool female, string role)
        {
            if (cube == null) { var k = new MeshKit(); k.Box(new Vector3(0, -.5f, 0), Vector3.one, new Color32(255, 255, 255, 255), 0, true, true); cube = k.Build("Actor cube"); }
            body = new GameObject("Body").transform; body.SetParent(transform, false);
            if (zombie) role = ArtLibrary.Variant("zombie", variant) ?? role;
            var p = ArtLibrary.Spawn(role, body);
            if (p != null)
            {
                purchased = p.transform; purchasedAnimator = p.GetComponentInChildren<Animator>();
                if (purchasedAnimator != null && purchasedAnimator.isHuman)
                {
                    purchasedAnimator.applyRootMotion = false;
                    pArmL = purchasedAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm); pArmR = purchasedAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                    pLegL = purchasedAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg); pLegR = purchasedAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                    pSpine = purchasedAnimator.GetBoneTransform(HumanBodyBones.Spine);
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
            skinMat = Mat(skin); shirtMat = Mat(shirt); pantsMat = Mat(pants); hairMat = Mat(female ? new Color(.35f, .22f, .12f) : new Color(.16f, .12f, .08f)); heldMat = Mat(new Color(.4f, .3f, .2f));
            float s = female ? .95f : 1;
            legL = Limb("LegL", body, new Vector3(-.11f, .92f, 0) * s, new Vector3(.15f, .9f, .17f) * s, pantsMat);
            legR = Limb("LegR", body, new Vector3(.11f, .92f, 0) * s, new Vector3(.15f, .9f, .17f) * s, pantsMat);
            torso = Part("Torso", body, new Vector3(0, 1.22f, 0) * s, new Vector3(.44f, .62f, .26f) * s, shirtMat);
            head = Part("Head", body, new Vector3(0, 1.68f, 0) * s, new Vector3(.24f, .27f, .26f) * s, skinMat);
            Part("Hair", head, new Vector3(0, .1f, -.02f), new Vector3(1.08f, female ? .55f : .35f, 1.08f), hairMat);
            if (female) Part("Ponytail", head, new Vector3(0, -.1f, -.55f), new Vector3(.5f, .9f, .3f), hairMat);
            armL = Limb("ArmL", body, new Vector3(-.29f, 1.5f, 0) * s, new Vector3(.12f, .66f, .13f) * s, shirtMat);
            armR = Limb("ArmR", body, new Vector3(.29f, 1.5f, 0) * s, new Vector3(.12f, .66f, .13f) * s, shirtMat);
            Part("HandL", armL, new Vector3(0, -.7f, 0), new Vector3(.9f, .15f, .9f), skinMat);
            Part("HandR", armR, new Vector3(0, -.7f, 0), new Vector3(.9f, .15f, .9f), skinMat);
            if (zombie && rng.Chance(.6f)) Part("Blood", torso, new Vector3(rng.Range(-.2f, .2f), rng.Range(-.2f, .2f), .51f), new Vector3(.45f, .35f, .02f), Mat(new Color(.35f, .05f, .04f)));
            held = new GameObject("Held").transform; held.SetParent(armR, false); held.localPosition = new Vector3(0, -.72f, .02f);
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

        public void SetClothes(Color shirt, Color pants) { if (shirtMat != null) { shirtMat.color = shirt; pantsMat.color = pants; } }

        /// <summary>Shows a simple prop for the equipped item.</summary>
        public void SetHeld(string itemId)
        {
            if (held == null || itemId == heldId) return; heldId = itemId;
            for (int i = held.childCount - 1; i >= 0; i--) Destroy(held.GetChild(i).gameObject);
            if (string.IsNullOrEmpty(itemId)) return;
            var role = "item/" + itemId; var p = ArtLibrary.Spawn(role, held); if (p != null) return;
            var def = ItemCatalog.Exists(itemId) ? ItemCatalog.Get(itemId) : null; if (def == null) return;
            var color = def.category == ItemCategory.Firearm ? new Color(.12f, .12f, .13f) : def.blade ? new Color(.7f, .72f, .75f) : def.id == "frying_pan" || def.id == "pipe" || def.id == "crowbar" ? new Color(.45f, .46f, .5f) : new Color(.5f, .36f, .22f);
            float len = def.category == ItemCategory.Firearm ? (def.twoHanded ? .9f : .3f) : Mathf.Clamp(def.range * .6f, .25f, 1.2f);
            var m = Mat(color);
            var shaft = Part("Shaft", held, new Vector3(0, 0, len * .5f), new Vector3(.05f, .05f, len), m);
            if (def.id == "axe") Part("Head", held, new Vector3(.06f, 0, len * .95f), new Vector3(.18f, .04f, .14f), Mat(new Color(.6f, .6f, .62f)));
            if (def.id == "shovel") Part("Blade", held, new Vector3(0, 0, len), new Vector3(.2f, .03f, .26f), Mat(new Color(.5f, .5f, .5f)));
        }

        /// <summary>Drives pose from simulation state each frame.</summary>
        public void Pose(Vector3 target, float facingDeg, float speed, bool attacking, bool knocked, bool dead, bool aiming, float dt)
        {
            transform.position = Vector3.SmoothDamp(transform.position, target, ref velocity, .06f, 50, dt);
            var want = Quaternion.Euler(0, facingDeg, 0);
            transform.rotation = Quaternion.Slerp(transform.rotation, want, 1 - Mathf.Exp(-dt * 14));
            phase += dt * Mathf.Clamp(speed, 0, 5) * 2.6f;
            lying = Mathf.MoveTowards(lying, knocked || dead ? 1 : 0, dt * 3);
            swing = attacking ? Mathf.Min(1, swing + dt * 7) : Mathf.Max(0, swing - dt * 4);
            float gait = Mathf.Sin(phase) * Mathf.Clamp01(speed / 2.5f);
            if (animated)
            {
                purchasedAnimator.SetFloat("Speed", speed, .1f, dt);
                purchasedAnimator.SetBool("Down", knocked || dead);
                if (attacking && !wasAttacking) purchasedAnimator.SetTrigger("Attack");
                wasAttacking = attacking;
                return;
            }
            body.localRotation = Quaternion.Euler(-88 * lying, 0, 0);
            body.localPosition = new Vector3(0, .12f * lying, 0);
            if (purchased != null) { PosePurchased(gait, aiming); return; }
            if (legL == null) return;
            legL.localRotation = Quaternion.Euler(gait * 32, 0, 0); legR.localRotation = Quaternion.Euler(-gait * 32, 0, 0);
            if (zombie)
            {
                float reach = -80 + Mathf.Sin(phase * .5f) * 6;
                armL.localRotation = Quaternion.Euler(reach - swing * 30, 0, -6); armR.localRotation = Quaternion.Euler(reach - swing * 30, 0, 6);
                torso.localRotation = Quaternion.Euler(10 + swing * 10, Mathf.Sin(phase * .5f) * 6, 0);
                head.localRotation = Quaternion.Euler(14, Mathf.Sin(phase * .33f) * 15, Mathf.Sin(phase * .21f) * 10);
            }
            else
            {
                armL.localRotation = Quaternion.Euler(-gait * 26 - (aiming ? 70 : 0), 0, 0);
                float strike = swing > 0 ? Mathf.Lerp(-120, 20, swing) : -gait * -26;
                armR.localRotation = Quaternion.Euler(aiming ? -85 : strike, 0, 0);
                torso.localRotation = Quaternion.Euler(0, swing * 18, 0);
                head.localRotation = Quaternion.identity;
            }
        }
        void PosePurchased(float gait, bool aiming)
        {
            if (purchasedAnimator != null && purchasedAnimator.enabled) { purchasedAnimator.SetFloat("Speed", Mathf.Abs(gait) * 3); return; }
            var right = transform.right;
            Rotate(pLegL, rLegL, gait * 30); Rotate(pLegR, rLegR, -gait * 30);
            if (zombie) { Rotate(pArmL, rArmL, -75); Rotate(pArmR, rArmR, -75 - swing * 30); }
            else { Rotate(pArmL, rArmL, -gait * 22 - (aiming ? 70 : 0)); Rotate(pArmR, rArmR, aiming ? -85 : swing > 0 ? Mathf.Lerp(-110, 20, swing) : gait * 22); }
        }
        void Rotate(Transform joint, Quaternion rest, float angle)
        {
            if (joint == null) return;
            var parent = joint.parent == null ? Quaternion.identity : joint.parent.rotation;
            joint.localRotation = Quaternion.Inverse(parent) * Quaternion.AngleAxis(angle, transform.right) * parent * rest;
        }

        public void Hit() { if (animated) purchasedAnimator.SetTrigger("Hit"); }
        /// <summary>Corpses jump straight to the end of the death pose.</summary>
        public void Settle() { if (animated) { purchasedAnimator.SetBool("Down", true); purchasedAnimator.Play("Down", 0, 1); purchasedAnimator.Update(0); } }
        public void SetVisible(bool visible) { if (gameObject.activeSelf != visible) gameObject.SetActive(visible); }
        public void Snap(Vector3 position, float facingDeg) { transform.position = position; transform.rotation = Quaternion.Euler(0, facingDeg, 0); velocity = Vector3.zero; }
    }
}
