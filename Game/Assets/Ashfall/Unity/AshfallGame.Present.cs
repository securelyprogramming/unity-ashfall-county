using System.Collections;
using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.View
{
    /// <summary>Presentation half of the game root: sim events to audio/effects, camera, lighting and actor views.
    /// Every living human near the camera (members, residents, crews, raiders, enclaves) gets an ActorView; zombies
    /// get kind-specific views from a look-keyed pool; plague hearts, airdrops, red zones and effects have their own
    /// views. All text (log, radio, kill feed, banners) belongs to AshfallUI.OnEvent.</summary>
    public sealed partial class AshfallGame
    {
        public const float ActorRadius = 60, BodyRadius = 45, HumanSightLinger = 1.5f, ShellFallSeconds = 1.0f;
        public const int MaxZombieViews = 90, MaxCorpseViews = 70, MaxPooledActors = 160, MaxHumanLights = 3;
        Fx fx; ViewHearts heartViews; ViewWorldEvents eventViews; ViewGrade grade;
        readonly Dictionary<int, ActorView> humanViews = new Dictionary<int, ActorView>();
        readonly Dictionary<int, float> humanSeen = new Dictionary<int, float>();
        readonly Dictionary<string, Stack<ActorView>> actorPool = new Dictionary<string, Stack<ActorView>>();
        readonly List<int> scratchIds = new List<int>();
        readonly HashSet<int> keepIds = new HashSet<int>();
        sealed class Dying { public ActorView view; public Vector3 pos; public float yaw, time; }
        readonly List<Dying> dying = new List<Dying>();
        sealed class Shell { public Vector3 at; public float t; public bool cancelled; }
        readonly List<Shell> shells = new List<Shell>();
        readonly List<Vector4> recentBlasts = new List<Vector4>();   // xyz = position, w = Time.time
        readonly Light[] humanLights = new Light[MaxHumanLights];
        int pooledActors, viewedPlayer = -1; float switchPan, humanSightClock, sweepClock;
        /// <summary>Views drawn this frame, for diagnostics.</summary>
        public int DrawnHumans { get; private set; }
        public int DrawnZombies { get { return zombieViews.Count; } }

        /// <summary>Creates the views for a freshly entered session.</summary>
        void BeginPresentation(World w)
        {
            fx = new Fx(transform);
            heartViews = new ViewHearts(w, transform, fx);
            eventViews = new ViewWorldEvents(w, transform, fx);
            grade = ViewGrade.Attach(Cam);
            for (int i = 0; i < MaxHumanLights; i++)
            {
                if (humanLights[i] != null) continue;
                var l = new GameObject("Survivor flashlight").AddComponent<Light>(); l.transform.SetParent(transform, false);
                l.type = LightType.Spot; l.range = 16; l.spotAngle = 50; l.intensity = 2; l.color = new Color(1, .94f, .8f); l.shadows = LightShadows.None; l.enabled = false; humanLights[i] = l;
            }
            viewedPlayer = -1; switchPan = 0;
            var s = Player;
            if (s != null) { playerView = HumanView(s); playerView.Snap(Pos(s.pos), Yaw(s.facing)); viewedPlayer = s.id; }
        }

        /// <summary>Destroys every session view (called when leaving a session).</summary>
        void ClearPresentation()
        {
            foreach (var z in zombieViews.Values) if (z != null) Destroy(z.gameObject); zombieViews.Clear();
            foreach (var z in zombiePool) if (z != null) Destroy(z.gameObject); zombiePool.Clear();
            foreach (var c in corpseViews.Values) if (c != null) Destroy(c.gameObject); corpseViews.Clear();
            foreach (var h in humanViews.Values) if (h != null) Destroy(h.gameObject); humanViews.Clear(); humanSeen.Clear();
            foreach (var st in actorPool.Values) foreach (var a in st) if (a != null) Destroy(a.gameObject); actorPool.Clear(); pooledActors = 0;
            foreach (var d in dying) if (d.view != null) Destroy(d.view.gameObject); dying.Clear();
            playerView = null;   // it is one of humanViews
            if (fx != null) fx.Clear(); fx = null;
            if (heartViews != null) heartViews.Clear(); heartViews = null;
            if (eventViews != null) eventViews.Clear(); eventViews = null;
            foreach (var l in humanLights) if (l != null) l.enabled = false;
            shells.Clear(); recentBlasts.Clear();
        }

        // ---------------------------------------------------------------- events: audio, effects and camera feedback
        void Events()
        {
            var s = Player;
            var list = World.events;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (UI != null) UI.OnEvent(e);
                if (s != null && fx != null) EventFx(e, s);
            }
            list.Clear();
        }

        void Kick(float amount) { if (amount > shake) shake = amount; }
        void Flash(float strength, Color color) { if (grade != null) grade.Flash(strength, color); }
        static readonly Color HurtRed = new Color(.62f, 0, 0), BlastWhite = new Color(1, .85f, .6f);

        /// <summary>Plays a cue that should carry further than the mixer's 38 m falloff (guns, blasts, the plane):
        /// the source is pulled toward the listener and the volume follows the real distance.</summary>
        void PlayFar(string key, Vec2 at, float range, float volume = 1)
        {
            var me = Player; if (me == null) return;
            float d = Vec2.Distance(at, me.pos); if (d > range) return;
            if (d <= 8) { Audio.Play(key, at, volume); return; }
            var p = me.pos + (at - me.pos) * (8f / d);
            Audio.Play(key, p, Mathf.Clamp01(volume * (1 - d / range) * 1.35f));
        }

        static string GunKey(string weapon)
        {
            weapon = weapon ?? "";
            if (weapon.Contains("shotgun")) return "shotgun";
            if (weapon.Contains("smg")) return "smg";
            if (weapon.Contains("hunting") || weapon.Contains("sniper")) return "sniper";
            if (weapon.Contains("rifle")) return "rifle";
            return "gunshot";   // pistols and anything unknown
        }

        ActorView SurvivorView(int id) { ActorView a; return id >= 0 && humanViews.TryGetValue(id, out a) && a != null && a.gameObject.activeSelf ? a : null; }
        ActorView ZombieView(int id) { ActorView a; return id >= 0 && zombieViews.TryGetValue(id, out a) && a != null ? a : null; }

        void EventFx(SimEvent e, Survivor me)
        {
            bool mine = e.survivor == me.id;
            float dist = Vec2.Distance(e.pos, me.pos);
            var at = Pos(e.pos);
            switch (e.kind)
            {
                case EventKind.Message: case EventKind.Evacuated: case EventKind.Rain: break;
                case EventKind.Radio: if (mine) Audio.Play("radio", e.pos); break;
                case EventKind.LevelUp: if (mine) Audio.Play("levelup", e.pos); break;
                case EventKind.Hurt:
                    {
                        bool blocked = e.text == "blocked"; var v = SurvivorView(e.survivor);
                        if (!blocked) { if (v != null) v.Hit(); if (dist < ActorRadius) fx.Blood(v != null ? v.Chest : at + Vector3.up * 1.2f, Vector3.zero, e.text == "glass" ? 3 : 8); }
                        if (mine) { Kick(blocked ? .3f : .6f); if (!blocked) Flash(.5f, HurtRed); Audio.Play(blocked ? "block" : "hurt", e.pos); }
                        else Audio.Play(blocked ? "block" : "human-hurt", e.pos, .8f);
                        break;
                    }
                case EventKind.Bite:
                    {
                        var v = SurvivorView(e.survivor); if (v != null) v.Hit();
                        fx.Blood(v != null ? v.Chest : at + Vector3.up * 1.2f, Vector3.zero, 14); Audio.Play("bite", e.pos);
                        if (mine) { Kick(1.2f); Flash(.85f, HurtRed); }
                        break;
                    }
                case EventKind.Death:
                    {
                        var v = SurvivorView(e.survivor);
                        fx.Blood(v != null ? v.Chest : at + Vector3.up, Vector3.zero, 18); fx.Scorch(at, 1.4f, new Color(.28f, .02f, .02f, .7f), 240);
                        if (!mine) Audio.Play("human-death", e.pos); else Flash(1, HurtRed);
                        break;
                    }
                case EventKind.Break: Audio.Play("break", e.pos); if (dist < 40) fx.Dust(at + Vector3.up, .6f, 4); break;
                case EventKind.Craft: case EventKind.Barricade: Audio.Play("hammer", e.pos); break;
                case EventKind.Crash: if (mine) Kick(e.text == "hard" ? 1.2f : .5f); if (dist < 60) fx.Dust(at, 2, 8); Audio.Event(e, me); break;
                case EventKind.Gunshot:
                    {
                        string key = GunKey(e.text);
                        PlayFar(key, e.pos, key == "sniper" ? 150 : 100);
                        if (dist < 90)
                        {
                            var v = SurvivorView(e.survivor); var shooter = World.survivors.Count > e.survivor && e.survivor >= 0 ? World.survivors[e.survivor] : null;
                            var muzzle = v != null ? v.Muzzle : at + Vector3.up * 1.3f;
                            var dir = v != null ? v.transform.forward : shooter != null ? new Vector3(Mathf.Cos(shooter.facing), 0, Mathf.Sin(shooter.facing)) : Vector3.forward;
                            fx.Muzzle(muzzle, dir, key == "shotgun" || key == "sniper" ? 1.3f : key == "gunshot" ? .8f : 1);
                        }
                        if (mine) Kick(key == "shotgun" ? .7f : key == "sniper" ? .8f : key == "rifle" ? .45f : key == "smg" ? .3f : .4f);
                        break;
                    }
                case EventKind.Tracer:
                    {
                        var to = Pos(e.pos, 1.1f); var v = SurvivorView(e.survivor);
                        var shooter = e.survivor >= 0 && e.survivor < World.survivors.Count ? World.survivors[e.survivor] : null;
                        var from = v != null ? v.Muzzle : shooter != null ? Pos(shooter.pos, 1.3f) : to;
                        if ((from - to).sqrMagnitude > .04f && (dist < 90 || shooter != null && Vec2.Distance(shooter.pos, me.pos) < 90)) fx.Tracer(from, to);
                        break;
                    }
                case EventKind.Hit:
                    {
                        var zv = ZombieView(e.zombie); var target = zv != null ? zv.Chest : at + Vector3.up;
                        if (zv != null) zv.Hit();
                        if (e.text == "armor") { fx.Trail(target, new Color32(255, 220, 160, 255)); fx.Trail(target, new Color32(255, 220, 160, 255)); Audio.Play("block", e.pos, .8f); break; }
                        var by = SurvivorView(e.survivor);
                        if (dist < ActorRadius) fx.Blood(target, by != null ? target - by.transform.position : Vector3.zero, e.text == "blade" ? 10 : 6);
                        Audio.Event(e, me); break;
                    }
                case EventKind.Kill:
                    if (dist < ActorRadius) { fx.Blood(at + Vector3.up * .8f, Vector3.zero, 16); fx.Scorch(at, 1.1f, new Color(.25f, .02f, .02f, .65f), 150); }
                    Audio.Event(e, me); break;
                case EventKind.Footstep:
                    if (e.text == "juggernaut")
                    {
                        // Juggernaut stomps: heavy thud, dust and a camera bump up close.
                        Audio.Play("juggernaut-step", e.pos, .9f); if (dist < 40) fx.Puff(at, 1.4f);
                        if (dist < 16) Kick(.28f * (1 - dist / 16));
                        break;
                    }
                    Audio.Event(e, me); break;
                case EventKind.Thump:
                    if (e.text == "charge") { fx.Dust(at, 2, 12); Audio.Play("break", e.pos); if (dist < 25) Kick(.6f * (1 - dist / 25)); }
                    Audio.Event(e, me); break;
                case EventKind.Scream:
                    {
                        var zv = ZombieView(e.zombie); if (zv != null) zv.Scream();
                        fx.Shockwave(at, 14, new Color(.85f, .9f, 1, .5f), .6f);
                        PlayFar("scream", e.pos, 80); if (dist < 16) Kick(.35f);
                        break;
                    }
                case EventKind.Explosion: Explosion(e, at, dist); break;
                case EventKind.Gas: fx.GasBurst(at, 3.5f); PlayFar("gas", e.pos, 50); break;
                case EventKind.Pounce:
                    {
                        var zv = ZombieView(e.zombie); var v = SurvivorView(e.survivor);
                        if (e.text == "hit") { if (v != null) v.Hit(); fx.Blood(v != null ? v.Chest : at + Vector3.up, Vector3.zero, 10); Audio.Play("pounce", e.pos); if (e.survivor == me.id) { Kick(1.1f); Flash(.7f, HurtRed); } }
                        else { if (zv != null) zv.Pounce(); PlayFar("feral-screech", e.pos, 50); }
                        break;
                    }
                case EventKind.Charge:
                    {
                        var zv = ZombieView(e.zombie);
                        if (e.text == "hit")
                        {
                            var v = SurvivorView(e.survivor); if (v != null) v.Hit(); fx.Dust(at, 1.5f, 10); Audio.Play("charge", e.pos);
                            if (e.survivor == me.id) { Kick(1.4f); Flash(.8f, HurtRed); } else if (dist < 20) Kick(.5f);
                        }
                        else { if (zv != null) zv.Charge(); PlayFar("juggernaut-roar", e.pos, 60); Audio.Play("charge", e.pos, .7f); fx.Dust(at, 1.2f, 8); if (dist < 25) Kick(.4f * (1 - dist / 25)); }
                        break;
                    }
                case EventKind.Grab:
                    {
                        var zv = ZombieView(e.zombie); var v = SurvivorView(e.survivor); bool me2 = e.survivor == me.id;
                        switch (e.text)
                        {
                            case "start": if (zv != null) zv.Grab(); Audio.Play("juggernaut-roar", e.pos, .7f); if (me2) { Kick(.9f); Flash(.5f, HurtRed); } break;
                            case "slam": case "kill": if (v != null) v.Hit(); fx.Blood(v != null ? v.Chest : at + Vector3.up, Vector3.zero, e.text == "kill" ? 24 : 12); fx.Dust(at, 1.5f, 10); Audio.Play("zombie-attack", e.pos); if (me2) { Kick(1.4f); Flash(1, HurtRed); } else if (dist < 20) Kick(.5f); break;
                            case "break": Audio.Play("shove", e.pos); break;
                            default: if (zv != null) zv.Grab(); Audio.Play("zombie-attack", e.pos); break;
                        }
                        break;
                    }
                case EventKind.Heart:
                    if (heartViews != null) heartViews.OnEvent(e);
                    if (e.text == "pulse") Audio.Play("heart-pulse", e.pos, .6f);
                    else if (e.text == "scream") { PlayFar("heart-scream", e.pos, 80); if (dist < 20) Kick(.5f); }
                    else Audio.Play("hit-blade", e.pos, .9f);
                    break;
                case EventKind.HeartDestroyed:
                    if (heartViews != null) heartViews.OnEvent(e);
                    PlayFar("heart-destroyed", e.pos, 90); if (dist < 30) Kick(1.2f * (1 - dist / 30));
                    break;
                case EventKind.Plague:
                    if (e.text == "turned") Audio.Play("chase", e.pos);
                    else if (mine && e.text == "infected") { Audio.Play("bite", e.pos, .6f); Flash(.45f, new Color(.4f, 0, .05f)); }
                    else if (mine && e.text == "cured") Audio.Play("heal", e.pos);
                    break;
                case EventKind.Airdrop:
                    if (eventViews != null) eventViews.OnAirdrop(e);
                    switch (e.text)
                    {
                        case "announce": Audio.Ui("radio"); break;
                        case "plane": Audio.Ui("plane"); break;   // a fly-over is heard county-wide
                        case "land": PlayFar("airdrop-land", e.pos, 120); PlayFar("flare", e.pos, 80, .7f); break;
                        case "open": Audio.Play("loot", e.pos); break;
                    }
                    break;
                case EventKind.Artillery:
                    switch (e.text)
                    {
                        case "warn": Audio.Ui("artillery-siren"); break;
                        case "shell": ShellFired(at); PlayFar("artillery-whistle", e.pos, 160); break;
                    }
                    break;
                case EventKind.KillFeed: Audio.Ui("kill-feed"); break;
                case EventKind.Recruit: Audio.Ui("recruit"); break;
                case EventKind.Facility: Audio.Ui(e.text == "start" ? "hammer" : "facility-built"); break;
                case EventKind.Mission: Audio.Ui("mission"); break;
                case EventKind.Morale: Audio.Ui(e.text == "down" ? "morale-down" : "levelup"); break;
                case EventKind.Influence: Audio.Ui("influence"); break;
                case EventKind.Switch: Audio.Ui("click"); break;
                case EventKind.Talk: Audio.Play("radio-chatter", e.pos, .4f); break;
                case EventKind.Trade: Audio.Ui("loot"); break;
                case EventKind.Siege: Audio.Ui(e.text == "start" ? "music-combat" : "music-base"); if (e.text == "start") Audio.Ui("human-shout"); break;
                case EventKind.Legacy: Audio.Ui("music-base"); break;
                default: Audio.Event(e, me); break;   // swings, groans, doors, glass, engines, shoves...
            }
        }

        void Explosion(SimEvent e, Vector3 at, float dist)
        {
            string cause = e.text ?? "";
            foreach (var sh in shells) if ((sh.at - at).sqrMagnitude < 36) sh.cancelled = true;   // the sim's own blast replaces the synthesized one
            recentBlasts.Add(new Vector4(at.x, at.y, at.z, Time.time));
            if (cause.Contains("bloater") || cause.Contains("gas")) { fx.GasBurst(at, 3.5f); PlayFar("bloater-burst", e.pos, 70); if (dist < 12) Kick(.5f); return; }
            if (cause.Contains("molotov")) { fx.FireBurst(at, 2.5f); PlayFar("molotov", e.pos, 70); if (dist < 12) Kick(.4f); return; }
            bool artillery = cause.Contains("artillery") || cause.Contains("shell");
            fx.Explosion(at, artillery ? 1.4f : cause.Contains("pipe") ? 1.15f : 1);
            PlayFar(artillery ? "artillery-impact" : "explosion", e.pos, 180);
            float k = Mathf.Clamp01(1 - dist / 50); Kick(k * k * 2.2f);
            if (dist < 14) Flash(.35f * (1 - dist / 14), BlastWhite);
        }

        /// <summary>A shell is on its way: a falling spark streak, and an explosion if the sim does not report one first.</summary>
        void ShellFired(Vector3 at)
        {
            for (int i = recentBlasts.Count - 1; i >= 0; i--)
            {
                var b = recentBlasts[i]; if (Time.time - b.w > .4f) continue;
                if ((new Vector3(b.x, b.y, b.z) - at).sqrMagnitude < 36) return;   // already exploded this frame
            }
            if (shells.Count < 24) shells.Add(new Shell { at = at });
        }
        void UpdateShells(float dt)
        {
            for (int i = recentBlasts.Count - 1; i >= 0; i--) if (Time.time - recentBlasts[i].w > 1) recentBlasts.RemoveAt(i);
            for (int i = shells.Count - 1; i >= 0; i--)
            {
                var sh = shells[i]; sh.t += dt;
                if (sh.cancelled) { shells.RemoveAt(i); continue; }
                float k = Mathf.Clamp01(1 - sh.t / ShellFallSeconds);
                var p = sh.at + new Vector3(8, 60, 5) * k;
                fx.Trail(p, new Color32(255, 170, 90, 255)); if (Random.value < .5f) fx.Smoke(p, .5f, 1.2f, new Color32(90, 86, 80, 120));
                if (sh.t < ShellFallSeconds) continue;
                {
                    fx.Explosion(sh.at, 1.4f);
                    var me = Player; var v = new Vec2(sh.at.x, sh.at.z);
                    PlayFar("artillery-impact", v, 180);
                    if (me != null) { float d = Vec2.Distance(v, me.pos); float kk = Mathf.Clamp01(1 - d / 50); Kick(kk * kk * 2.2f); }
                }
                shells.RemoveAt(i);
            }
        }

        // ---------------------------------------------------------------- presentation
        void Present(float dt)
        {
            var s = Player; if (s == null) return;
            if (fx == null) BeginPresentation(World);
            shake = Mathf.Max(0, shake - dt * 2.5f);
            // Switching members pans the camera across the county instead of cutting.
            if (viewedPlayer != s.id) { if (viewedPlayer >= 0) switchPan = 1; viewedPlayer = s.id; }
            switchPan = Mathf.Max(0, switchPan - dt * .8f);
            var target = Pos(s.pos);
            var car = World.VehicleOf(s); float size = zoom; var camTarget = target;
            // Driving pulls the camera back and ahead of the car so there is time to react.
            if (car != null) { camTarget += new Vector3(Mathf.Cos(car.heading), 0, Mathf.Sin(car.heading)) * car.speed * .55f; size = zoom + 5 + Mathf.Abs(car.speed) * .45f; }
            float follow = switchPan > 0 ? Mathf.Lerp(8, 2.4f, switchPan) : 8;
            camFocus = Vector3.Lerp(camFocus, camTarget, 1 - Mathf.Exp(-dt * follow));
            PlaceCamera(camFocus, size);
            View.LoadRadius = Cam.orthographicSize > 22 ? 3 : 2;
            View.Refresh(camFocus, -CameraForward(), switchPan > 0 ? 4 : 2);
            SyncHumans(s, dt);
            ActorView pv; playerView = humanViews.TryGetValue(s.id, out pv) ? pv : null;
            SyncZombies(s, dt);
            SyncCorpses(s, dt);
            heartViews.Sync(camFocus, dt);
            eventViews.Update(camFocus, dt);
            UpdateShells(dt);
            Throwables();
            fx.Hazards(World, camFocus, dt);
            fx.Update(dt);
            bool indoors = World.map.Indoors(s.pos);
            Lighting(World.Daylight, indoors, target);
            flashlight.enabled = s.lightOn && s.alive && !s.inVehicle;
            if (flashlight.enabled) { var dir = new Vector3(Mathf.Cos(s.facing), -.25f, Mathf.Sin(s.facing)); flashlight.transform.SetPositionAndRotation(target + Vector3.up * 1.4f, Quaternion.LookRotation(dir)); }
            playerGlow.transform.position = target + Vector3.up * 2.5f;
            var emission = rain.emission; emission.rateOverTime = World.raining ? 2600 * World.rainIntensity : 0;
            if (World.raining && !rain.isPlaying) rain.Play(); if (!World.raining && rain.isPlaying) rain.Stop();
            rain.transform.position = target + Vector3.up * 16;
            if (grade != null) { grade.Night = 1 - World.Daylight; grade.Danger = s.alive && s.health < 30 ? (30 - s.health) / 30f : 0; }
            Audio.Ambience(World, s, indoors);
        }

        static Color ClothColor(string id)
        {
            switch (id)
            {
                case "hoodie": return new Color(.32f, .36f, .3f);
                case "tshirt": return new Color(.7f, .68f, .6f);
                case "leather_jacket": return new Color(.22f, .15f, .1f);
                case "police_vest": return new Color(.15f, .17f, .22f);
                case "jeans": return new Color(.2f, .26f, .4f);
                default: return id != null && id.StartsWith("vest") ? new Color(.22f, .25f, .2f) : new Color(.4f, .4f, .4f);
            }
        }

        void Lighting(float daylight, bool indoors, Vector3 focus)
        {
            float h = World != null ? World.HourOfDay : 15;
            float sunAngle = (h - 6) / 15f * 180;
            sun.transform.rotation = Quaternion.Euler(Mathf.Lerp(18, 62, Mathf.Sin(Mathf.Clamp01(daylight) * Mathf.PI * .5f)), sunAngle - 60, 0);
            var dayColor = new Color(1, .95f, .86f); var duskColor = new Color(1, .58f, .38f); var night = new Color(.26f, .32f, .55f);
            bool dusk = h > 17.5f && h < 21.5f || h > 5 && h < 7.5f;
            sun.color = daylight > .05f ? Color.Lerp(night, dusk ? duskColor : dayColor, daylight) : night;
            // Nights are dark: ferals hunt by night, and a flashlight or a lit base matters.
            sun.intensity = Mathf.Lerp(.045f, 1.12f, daylight) * (World != null && World.raining ? .7f : 1);
            var ambient = Color.Lerp(new Color(.07f, .08f, .13f), new Color(.5f, .52f, .56f), daylight);
            if (dusk && daylight > .05f) ambient = Color.Lerp(ambient, new Color(.42f, .32f, .3f), .25f * daylight);
            if (indoors) ambient *= .75f;
            RenderSettings.ambientLight = ambient;
            float fog = World == null ? 0 : World.fog + (World.raining ? .25f * World.rainIntensity : 0); float dark = 1 - daylight;
            var fogDay = dusk ? new Color(.58f, .5f, .48f) : new Color(.6f, .64f, .68f);
            RenderSettings.fogColor = Color.Lerp(new Color(.022f, .028f, .048f), fogDay, daylight);
            // The camera sits 120 m from the focus, so the fog band starts just past it and thickens toward the top of the screen.
            RenderSettings.fogStartDistance = Mathf.Lerp(Mathf.Lerp(104, 110, dark), 84, fog);
            RenderSettings.fogEndDistance = Mathf.Lerp(Mathf.Lerp(230, 185, dark), 145, fog);
            Cam.backgroundColor = RenderSettings.fogColor;
            playerGlow.intensity = Mathf.Lerp(.8f, 0, daylight * 1.6f);
        }

        // ---------------------------------------------------------------- actor pool
        ActorView AcquireZombie(ZombieKind kind, bool plague, int variant, bool female)
        {
            var key = ActorView.ZombieKey(kind, plague, variant, female);
            Stack<ActorView> st;
            while (actorPool.TryGetValue(key, out st) && st.Count > 0)
            {
                var a = st.Pop(); pooledActors--; if (a == null) continue;
                a.ResetPose(); a.SetVisible(true); return a;
            }
            return ActorView.CreateZombie(transform, kind, plague, variant, female);
        }
        void Release(ActorView a)
        {
            if (a == null) return;
            if (pooledActors >= MaxPooledActors || string.IsNullOrEmpty(a.LookKey)) { Destroy(a.gameObject); return; }
            a.SetVisible(false);
            Stack<ActorView> st; if (!actorPool.TryGetValue(a.LookKey, out st)) actorPool[a.LookKey] = st = new Stack<ActorView>();
            st.Push(a); pooledActors++;
        }
        static int Lod(float distance) { return distance < 22 ? 0 : distance < 40 ? 1 : 2; }

        // ---------------------------------------------------------------- humans
        static readonly Color[] RivalShirts = { new Color(.62f, .42f, .12f), new Color(.38f, .22f, .5f), new Color(.14f, .46f, .46f), new Color(.52f, .52f, .55f), new Color(.5f, .45f, .1f), new Color(.6f, .3f, .4f) };
        /// <summary>Art group for a human: community, enclave, rival, raider, soldier (deserters) or trader.</summary>
        string Group(Survivor s)
        {
            if (s.faction == 0) return "community";
            if (s.ai == AiRole.Trader) return "trader";
            var f = World.FactionOf(s); var role = f != null ? f.role : s.ai;
            switch (role)
            {
                case AiRole.Enclave: return "enclave";
                case AiRole.Raider: return "raider";
                case AiRole.Hunter: return "soldier";
                case AiRole.Trader: return "trader";
                case AiRole.Resident: case AiRole.Companion: return "community";
                default: return "rival";
            }
        }
        static void GroupColors(Survivor s, string group, out Color shirt, out Color pants)
        {
            switch (group)
            {
                case "community": shirt = new Color(.2f, .36f, .64f); pants = new Color(.2f, .22f, .3f); break;
                case "enclave": shirt = new Color(.5f, .4f, .25f); pants = new Color(.36f, .3f, .22f); break;
                case "raider": shirt = new Color(.56f, .09f, .07f); pants = new Color(.09f, .09f, .1f); break;
                case "soldier": shirt = new Color(.32f, .37f, .2f); pants = new Color(.27f, .3f, .19f); break;
                case "trader": shirt = new Color(.58f, .46f, .18f); pants = new Color(.3f, .25f, .2f); break;
                default:
                    {
                        int squad = s.squad >= 0 ? s.squad : s.faction; shirt = RivalShirts[(squad % RivalShirts.Length + RivalShirts.Length) % RivalShirts.Length];
                        pants = new Color(.16f, .16f, .18f); break;
                    }
            }
        }
        /// <summary>Role lookup: character/&lt;key&gt;, then human/&lt;group&gt;/&lt;n&gt;, then survivor_male/female, then procedural.</summary>
        ActorView HumanView(Survivor s)
        {
            ActorView a; if (humanViews.TryGetValue(s.id, out a) && a != null) return a;
            string group = Group(s), role = null;
            if (!string.IsNullOrEmpty(s.character) && ArtLibrary.Has("character/" + s.character)) role = "character/" + s.character;
            if (role == null) role = ArtLibrary.Variant("human/" + group, s.id);
            if (role == null) role = s.female ? "survivor_female" : "survivor_male";
            Color shirt, pants; GroupColors(s, group, out shirt, out pants);
            a = ActorView.CreateHuman(transform, role, s.id, s.female, Color.Lerp(Color.white, shirt * 1.7f, .35f));
            a.Snap(Pos(s.pos), Yaw(s.facing)); humanViews[s.id] = a; return a;
        }

        float SightRange(Survivor me)
        {
            float light = World.Daylight, range = 9 + 22 * light;
            if (me.lightOn) range = Mathf.Max(range, 18);
            if (World.fog > 0) range *= 1 - World.fog * .45f;
            return Mathf.Max(range, 4);
        }

        void SyncHumans(Survivor me, float dt)
        {
            float now = Time.time; bool look = (humanSightClock -= dt) <= 0; if (look) humanSightClock = .2f;
            float range = SightRange(me); int lights = 0, drawn = 0; bool dark = World.Daylight < .6f;
            foreach (var s in World.survivors)
            {
                bool controlled = s.id == me.id;
                float d = Vec2.Distance(s.pos, me.pos);
                bool show;
                if (!s.alive) show = d < BodyRadius && !s.zombieInfected;   // bodies stay where they fell (the infected get up again)
                else if (controlled) show = true;
                else if (s.offscreen || d > ActorRadius) show = false;
                else if (s.faction == 0) show = true;
                else
                {
                    // Other humans are seen like zombies: in sight range with a clear line, or by their flashlight at night.
                    if (look)
                    {
                        float reach = s.lightOn && dark ? Mathf.Max(range, 45) : range;
                        if (d < 3 || d < reach && World.map.LineOfSight(me.pos, s.pos)) humanSeen[s.id] = now;
                    }
                    float t; show = humanSeen.TryGetValue(s.id, out t) && now - t < HumanSightLinger;
                }
                ActorView a;
                if (!show) { if (humanViews.TryGetValue(s.id, out a) && a != null) a.SetVisible(false); continue; }
                a = HumanView(s);
                bool fresh = !a.gameObject.activeSelf; a.SetVisible(!s.inVehicle);
                if (fresh) { a.Snap(Pos(s.pos), Yaw(s.facing)); if (!s.alive) a.Settle(); }
                if (s.inVehicle) continue;
                drawn++;
                string group = Group(s); Color gs, gp; GroupColors(s, group, out gs, out gp);
                var primary = s.Primary; a.SetHeld(primary == null ? "" : primary.def);
                var shirt = s.Worn(WearSlot.Torso); var pants = s.Worn(WearSlot.Legs); var hat = s.Worn(WearSlot.Head);
                a.SetClothes(Color.Lerp(shirt == null ? new Color(.75f, .72f, .66f) : ClothColor(shirt.def), gs, .6f), Color.Lerp(pants == null ? new Color(.7f, .6f, .5f) : ClothColor(pants.def), gp, .45f));
                a.SetArmor(shirt != null && shirt.Def.armor > 0, hat != null && hat.Def.armor > 0);
                a.SetMarker(!s.alive ? 0 : controlled ? 3 : s.faction == 0 ? 1 : World.Hostile(me, s) ? 2 : 0);
                a.SetLod(controlled ? 0 : Lod(d));
                a.Pose(Pos(s.pos), Yaw(s.facing), s.speed, s.swingClock > 0, s.knockedDown > 0 || s.sleeping, !s.alive, s.aiming, dt);
                if (!controlled && s.alive && s.lightOn && dark && lights < MaxHumanLights)
                {
                    var l = humanLights[lights++]; l.enabled = true;
                    l.transform.SetPositionAndRotation(a.transform.position + Vector3.up * 1.4f, Quaternion.LookRotation(new Vector3(Mathf.Cos(s.facing), -.25f, Mathf.Sin(s.facing))));
                }
            }
            for (int i = lights; i < MaxHumanLights; i++) if (humanLights[i] != null && humanLights[i].enabled) humanLights[i].enabled = false;
            DrawnHumans = drawn;
            // Hidden views of humans far away are dropped so a long session does not keep every face it met.
            if ((sweepClock -= dt) <= 0)
            {
                sweepClock = 3; scratchIds.Clear();
                foreach (var kv in humanViews)
                {
                    if (kv.Value == null) { scratchIds.Add(kv.Key); continue; }
                    if (kv.Value.gameObject.activeSelf || kv.Key == me.id) continue;
                    var s = kv.Key < World.survivors.Count ? World.survivors[kv.Key] : null;
                    if (s == null || Vec2.Distance(s.pos, me.pos) > 120) scratchIds.Add(kv.Key);
                }
                foreach (var id in scratchIds) { var a = humanViews[id]; if (a != null) Destroy(a.gameObject); humanViews.Remove(id); humanSeen.Remove(id); }
            }
        }

        // ---------------------------------------------------------------- zombies and corpses
        void SyncZombies(Survivor s, float dt)
        {
            HashSet<int> vis; Sim.visible.TryGetValue(s.id, out vis);
            float now = Time.time;
            if (vis != null) foreach (int id in vis) lastSeen[id] = now;
            keepIds.Clear(); int drawn = 0;
            bool burnTick = (Time.frameCount & 3) == 0;
            foreach (var kv in lastSeen)
            {
                if (kv.Key < 0 || kv.Key >= World.zombies.Count) continue;
                var z = World.zombies[kv.Key];
                if (!z.Alive || now - kv.Value > 1.2f) continue;
                float d = Vec2.Distance(z.pos, s.pos);
                if (d > ActorRadius || drawn >= MaxZombieViews) continue;
                keepIds.Add(kv.Key); drawn++;
                ActorView a;
                if (!zombieViews.TryGetValue(kv.Key, out a) || a == null || a.Kind != z.kind || a.Plague != z.plague)
                {
                    if (a != null) Release(a);
                    a = AcquireZombie(z.kind, z.plague, z.variant, z.female);
                    a.Snap(Pos(z.pos), Yaw(z.facing)); zombieViews[kv.Key] = a;
                }
                float moving = z.state == ZombieState.Chase || z.state == ZombieState.Lunge ? z.speed : z.state == ZombieState.Wander ? z.speed * .45f : z.state == ZombieState.Investigate ? z.speed * .8f : 0;
                a.SetLod(Lod(d));
                a.Pose(Pos(z.pos), Yaw(z.facing), moving, z.state == ZombieState.Attack || z.state == ZombieState.Thump, z.state == ZombieState.Knocked || z.crawler, false, false, dt);
                if (a.Charging && Random.value < dt * 14) fx.Puff(a.transform.position, 1.1f);   // juggernaut charge dust
                if (burnTick && d < 45 && Burning(z)) for (int i = 0; i < 3; i++) fx.Trail(a.Chest + Random.insideUnitSphere * .4f, new Color32(255, (byte)Random.Range(120, 200), 40, 255));
            }
            scratchIds.Clear(); foreach (var kv in zombieViews) if (!keepIds.Contains(kv.Key)) scratchIds.Add(kv.Key);
            foreach (var id in scratchIds)
            {
                var a = zombieViews[id]; zombieViews.Remove(id);
                var z = id < World.zombies.Count ? World.zombies[id] : null;
                // A zombie that just died keeps its view while it falls, so the corpse that appears can adopt it.
                if (a != null && z != null && !z.Alive && a.gameObject.activeSelf && dying.Count < 40) dying.Add(new Dying { view = a, pos = a.transform.position, yaw = a.transform.eulerAngles.y, time = now });
                else Release(a);
            }
            scratchIds.Clear(); foreach (var kv in lastSeen) if (now - kv.Value > 5 && !keepIds.Contains(kv.Key)) scratchIds.Add(kv.Key);
            foreach (var id in scratchIds) lastSeen.Remove(id);
            for (int i = dying.Count - 1; i >= 0; i--)
            {
                var d = dying[i];
                if (d.view == null) { dying.RemoveAt(i); continue; }
                d.view.Pose(d.pos, d.yaw, 0, false, false, true, false, dt);
                if (now - d.time > 3) { Release(d.view); dying.RemoveAt(i); }
            }
        }
        ActorView AdoptDying(Vector3 at)
        {
            for (int i = 0; i < dying.Count; i++)
            {
                var d = dying[i]; if (d.view == null) continue;
                var p = d.pos; if ((p.x - at.x) * (p.x - at.x) + (p.z - at.z) * (p.z - at.z) > 1.2f * 1.2f) continue;
                dying.RemoveAt(i); return d.view;
            }
            return null;
        }

        void SyncCorpses(Survivor s, float dt)
        {
            keepIds.Clear(); int drawn = 0;
            foreach (var c in World.corpses)
            {
                if (!c.zombie) continue;   // human bodies are drawn from dead survivors
                if (Vec2.Distance(c.pos, s.pos) > BodyRadius || drawn >= MaxCorpseViews) continue;
                keepIds.Add(c.id); drawn++;
                ActorView a;
                if (!corpseViews.TryGetValue(c.id, out a) || a == null)
                {
                    a = AdoptDying(Pos(c.pos));
                    if (a == null)
                    {
                        ZombieKind kind; bool plague; CorpseLook(c, out kind, out plague);
                        a = AcquireZombie(kind, plague, c.variant, c.female); a.Snap(Pos(c.pos), Yaw(c.facing)); a.Settle();
                    }
                    corpseViews[c.id] = a;
                }
                a.SetLod(Mathf.Max(1, Lod(Vec2.Distance(c.pos, s.pos))));
                a.Pose(Pos(c.pos), Yaw(c.facing), 0, false, false, true, false, dt);
            }
            if (corpseViews.Count == keepIds.Count) return;
            scratchIds.Clear(); foreach (var kv in corpseViews) if (!keepIds.Contains(kv.Key)) scratchIds.Add(kv.Key);
            foreach (var id in scratchIds) { Release(corpseViews[id]); corpseViews.Remove(id); }
        }

        static void CorpseLook(Corpse c, out ZombieKind kind, out bool plague) { kind = c.kind; plague = c.plague; }
        static bool Burning(Zombie z) { return z.burning > 0; }

        /// <summary>Thrown grenades/molotovs/pipe bombs: in flight they follow a parabola from the thrower to the landing
        /// point (Throwable.Arc); once landed they sit on the ground with a fuse spark.</summary>
        void Throwables()
        {
            fx.BeginThrown();
            var list = World.throwables;
            for (int i = 0; i < list.Count && i < Fx.MaxThrown; i++)
            {
                var t = list[i]; float k = t.Arc;
                var pos = Vector2.Lerp(new Vector2(t.from.x, t.from.y), new Vector2(t.pos.x, t.pos.y), k);
                float span = Vec2.Distance(t.from, t.pos), arc = Mathf.Clamp(span * .25f, .6f, 4);
                float y = t.landed ? .12f : 1.3f + 4 * arc * k * (1 - k) - 1.15f * k;
                fx.Thrown(new Vector3(pos.x, y, pos.y), Time.time + i);
            }
            fx.EndThrown();
        }
    }
}
