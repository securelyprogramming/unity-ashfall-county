using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mayo.Frontier.Presentation
{
    [Serializable] public sealed class FrontierPurchasedArtPlan
    {
        public int schemaVersion;
        public string gameId;
        public FrontierPurchasedModel[] models;
        public FrontierPurchasedBinding[] bindings;
        public FrontierPurchasedImage[] images;
    }
    [Serializable] public sealed class FrontierPurchasedModel
    {
        public string id, file, palette, idleClip;
        public bool character, singleBody, goblinLocomotion, genericRig, pitchedRoof, idleAbsolutePose;
        public float height = 1;
        public string[] renderers;
        public FrontierPurchasedSurface[] surfaces;
    }
    [Serializable] public sealed class FrontierPurchasedSurface { public string renderer; public FrontierPurchasedMaterial[] materials; }
    [Serializable] public sealed class FrontierPurchasedMaterial
    {
        public string id, texture;
        public float[] color;
        public float metallic, smoothness = .2f;
    }
    [Serializable] public sealed class FrontierPurchasedBinding { public string role, model; }
    [Serializable] public sealed class FrontierPurchasedImage { public string file; public float[] border; public float pixelsPerUnit = 100; }

    public sealed partial class FrontierWorldView
    {
        readonly Dictionary<string, GameObject> purchasedModels = new Dictionary<string, GameObject>();
        void LoadPurchasedArt()
        {
            purchasedModels.Clear();
            var text = Resources.Load<TextAsset>("FrontierPurchasedArt");
            if (text == null) return;
            var plan = JsonUtility.FromJson<FrontierPurchasedArtPlan>(text.text);
            if (plan.schemaVersion != 1 || plan.gameId != gameId) throw new InvalidOperationException("Purchased art plan belongs to a different Frontier game.");
            foreach (var binding in plan.bindings)
            {
                var model = Resources.Load<GameObject>("LicensedFrontier/Portfolio/Prepared/" + binding.model);
                if (model != null) purchasedModels.Add(binding.role, model);
            }
        }
        Transform Purchased(string role, Transform parent, Vector3 position, float scale = 1)
        {
            GameObject model;
            if (!purchasedModels.TryGetValue(role, out model)) return null;
            var instance = Instantiate(model, parent, false);
            instance.name = "Purchased " + role + " / " + model.name;
            instance.transform.localPosition = position;
            instance.transform.localScale *= scale;
            return instance.transform;
        }
        Transform PurchasedFit(string role, Transform parent, Vector3 position, Vector3 dimensions, float yaw = 0)
        {
            var model = Purchased(role,parent,position);
            if (model == null) return null;
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new InvalidOperationException("Purchased structural model has no renderer: " + role);
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            model.localScale = Vector3.Scale(model.localScale,new Vector3(dimensions.x/Mathf.Max(.001f,bounds.size.x),dimensions.y/Mathf.Max(.001f,bounds.size.y),dimensions.z/Mathf.Max(.001f,bounds.size.z)));
            model.localRotation = Quaternion.Euler(0,yaw,0);
            return model;
        }
        bool PurchasedHouse(Transform parent, Vector3 position, Vector3 dimensions)
        {
            if (!purchasedModels.ContainsKey("house-wall")) return false;
            var house = Group(parent,"Purchased modular building");house.localPosition = position;
            float w=dimensions.x,h=dimensions.y,d=dimensions.z;
            PurchasedFit("house-front",house,new Vector3(0,0,-d*.5f),new Vector3(w,h,.2f),180);
            PurchasedFit("house-window",house,new Vector3(0,0,d*.5f),new Vector3(w,h,.2f));
            PurchasedFit("house-wall",house,new Vector3(-w*.5f,0,0),new Vector3(d,h,.2f),90);
            PurchasedFit("house-window",house,new Vector3(w*.5f,0,0),new Vector3(d,h,.2f),270);
            // Original pack roof modules are single pitched slopes. Their prepared ridge
            // points along +Z, so a mirrored pair meets at the center of the footprint.
            float roofDepth = (d+.45f)*.5f;
            PurchasedFit("house-roof",house,new Vector3(0,h,-roofDepth*.5f),new Vector3(w+.45f,h*.6f,roofDepth));
            PurchasedFit("house-roof",house,new Vector3(0,h,roofDepth*.5f),new Vector3(w+.45f,h*.6f,roofDepth),180);
            return true;
        }
        bool BuildPurchasedCharacter(Visual v, string kind)
        {
            string role = kind.StartsWith("hero-") ? "hero" : kind.Contains("zombie") || kind == "brute" || kind == "space-brute" ? "zombie" : kind == "customer" ? "customer" : kind.Contains("orc") ? "goblin" : kind == "bot" ? "bot" : kind == "sentinel" || kind == "dungeon-guardian" || kind == "ember-lord" ? "guardian" : kind.Contains("raider") ? "raider" : kind == "healer" || kind == "elder" || kind == "villager" ? "civilian" : "";
            if (role.Length == 0) return false;
            Transform model = Purchased(role, v.model, Vector3.zero);
            if (model == null) return false;
            if (kind.Contains("brute") || kind == "dungeon-guardian" || kind == "ember-lord") model.localScale *= 1.4f;
            v.purchasedCharacter = model.GetComponent<FrontierPurchasedCharacter>();
            if (v.purchasedCharacter == null) throw new InvalidOperationException("Purchased character is missing its prepared rig.");
            v.purchasedCharacter.Initialize();
            v.head = v.purchasedCharacter.Head;
            v.leftArm = v.purchasedCharacter.LeftHand;
            v.rightArm = v.purchasedCharacter.RightHand;
            return true;
        }
    }

}
