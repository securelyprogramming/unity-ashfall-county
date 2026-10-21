# Ashfall County

An open-world zombie survival game. You lead a small community of survivors through a quarantined county several kilometres across. Pick where your group sets down, scavenge rucksacks of supplies for your home base, build it up, recruit named survivors, and decide which fights to take: walkers are easy, juggernauts and plague hearts are not. Rival crews loot the same towns, raiders hunt you, and Guard airdrops and artillery reshape the map. Destroy every plague heart to cleanse the county.

**Status: development.** This is an original game. It is not a certified or finished Steam game. See `Docs/ValidationReports` for what has actually been verified natively, and `Docs/DESIGN.md` for the design.

## The county

Each new game generates a seeded county: 4 km × 4 km by default (2, 3 and 6 km options). It contains the Ash River and its highway bridge, highways and county roads, Cedar Fork and a dozen or more towns, farms, forests and ponds. Landmarks include Fort Ashby (a fenced military base), the regional airfield, the County Hospital, the KAFC radio station, Reed's Garage and the Red Talon Yard raider compound. On the **drop map** you choose one of 8–12 named landing zones, and the community claims a home site nearby. Only the area around your survivors is simulated in detail and drawn.

## Your community

- **Members:** you start with three survivors. Each has health (capped by injuries), stamina, fatigue, traits and skills (Cardio, Wits, Fighting, Shooting, plus specialties). Control one at a time; switch at base. If your survivor dies, control passes to another member. The game ends only when nobody is left.
- **Companion:** bring one member along (Follow, Hold or Attack with C). Some fights you only win together.
- **Base:** stock of Food, Meds, Ammo, Materials and Fuel; members eat every morning. Build facilities in your base's slots: Infirmary, Workshop, Watchtower, Garden, Kitchen and Radio Room, upgraded with Materials over in-game hours.
- **Rucksacks:** heavy packs of supplies found by building type. Carry them home, or fill a car trunk and deposit at base.
- **Influence and morale:** earned by deliveries, kills and missions; spent on recruits, trades and outposts. Shortages and deaths hurt morale.
- **Missions:** rescues, supply runs, infestations, freak hunts and base defence come over the radio and from enclaves.

## Threats

| Kind | Tier | What to do |
|---|---|---|
| Walker, crawler | 1 | Melee, shove, avoid crowds |
| Screamer | 2 | Kill it first; its scream pulls everything nearby |
| Bloater | 2 | Kill it from range; it bursts into toxic gas |
| Armored | 2 | Knock it down or hit it from behind |
| Feral | 3 | Fast and pouncing, more at night; bring a gun or a friend |
| Juggernaut | 4 | Explosives, several shooters or a car; otherwise run |
| Plague heart | 5 | A group fight; destroying every heart wins |

Plague zombies (red eyes) spread **blood plague**. It is fatal within about a day unless cured with a plague cure, which you can find in airdrops or make at the infirmary from plague samples.

## Other people

Every human runs the same rules as you: rival crews loot, fight zombies, race to airdrops and sometimes rob the weak. Enclaves trade and give missions. The Red Talons and Kade's deserters hunt you on sight. You'll meet Ada Pike (radio station), Jonah Reed (garage), Doc Hollis (hospital), Wren Calder, Tess Monroe, Marshal Kade, "Big Sal" Varga and the Pilgrim. Talk, trade, recruit or fight. A PUBG-style kill feed and a "survivors alive" counter keep score.

## World events

- **Airdrops:** the Guard announces a drop, a plane passes overhead, and a crate parachutes down in red smoke with rare and military loot. Everyone nearby will want it.
- **Red zones:** artillery shells a warned circle. Get out.

## Controls

| Action | Keyboard / mouse | Controller |
|---|---|---|
| Move / aim | WASD / mouse | Left stick / right stick |
| Attack / aim mode | Left / right mouse | RT / LT |
| Shove | Space | LB |
| Run / sneak | Shift / Ctrl | L3 / R3 |
| Interact (focused) / all actions | E / Q | A / Y |
| Throw grenade, molotov or pipe bomb | G (at the cursor) | RB hold, release to throw (tap to cycle weapons) |
| Companion order | C | d-pad up |
| Inventory / community / missions / map | Tab or I / K / J / M | Select / d-pad left / d-pad right / d-pad down |
| Reload / flashlight | R / F | X |
| Hotbar | 1–6 | |
| Zoom / rotate view | Mouse wheel / Z, X | |
| Drive (in a car): throttle and reverse / steer / brake / engine / get out | W, S / A, D / Space / R or E / E | Left stick / left stick / LB / X / A |
| Pause | Esc | Start |

Menus work with keyboard, mouse and controller. You can rebind keys in Controls. Saves autosave every 5 minutes and on quit, and live in the game's own data folder (`Survival/*.sav`). Saves from the earlier survivor game (save version 2 or older) cannot be loaded; the load menu says so and leaves the file untouched.

## Code layout

- `Game/Assets/Ashfall/Core`: the pure C# host simulation (no Unity references). World generation and landmarks, items and loot, the community and base, the bestiary, AI humans and factions, combat, world events, vehicles and saves. Owners submit input and commands only; AI survivors use the same requests.
- `Game/Assets/Ashfall/Unity`: chunk-streamed county view, actors and effects, camera, lighting, audio, uGUI/TextMeshPro menus and HUD, and the native QA probe.
- `Game/Assets/Editor/PrepareAshfallArt.cs`: prepares the privately restored owned packs into prefabs at build time. Without them the game uses procedural stand-ins.
- `tools/AshfallCoreTests`: fast simulation checks without Unity: `dotnet run --project tools/AshfallCoreTests -c Release` (add `-- --quick` to skip the 3 km county).
- `tools/UnityCompileCheck`: a seconds-long type check of the Unity scripts against the installed Unity 6000.6 assemblies: `dotnet build tools/UnityCompileCheck -c Release` (needs a `Game/Library` from one editor import). It does not replace a native build.
- `Legacy/`: the retired Frontier chapter-game sources, kept outside the Unity project.

Owned art and audio stay private. They are pinned by `ContentSource/Art/presentation-selection.json`, `zomboid-selection.json` and `decay-selection.json`, and restored by `tools/playtest/build.py` into the isolated build snapshot only. LAN multiplayer is not implemented in the rebuilt game.

Native acceptance probe, which uses an isolated save directory (`--video` also records staged scenes to MP4):

```sh
python tools/playtest/ashfall_qa.py --player /abs/build/Game.exe --size 4096 --video
```

<!-- frontier-launcher-docs:begin -->
## Run current source

```sh
./run.sh
./run.sh --build-only
./run.sh -- -screen-width 1280 -screen-height 720
```

Windows: `.\run.ps1`; the same flags and native player arguments apply. Python 3 and the existing Unity 6000.6.0f1 editor plus native platform module are required. `UNITY_EDITOR` selects an existing editor; `UNITY_EDITORS_ROOT` can identify a nonstandard editor directory. No editor or toolchain is installed automatically.

Each run verifies the private art cache, builds current source into a fresh directory outside the checkout, then launches the native player. Previous builds and saves remain. `--output /absolute/fresh/directory` overrides the default cache location; `--check` performs prerequisite checks without building. `--build-only` skips launch.

Restore the reviewed purchased sources once if the private cache is missing:

```sh
python3 tools/playtest/restore_selection.py --archive /private/path/Spline_Builder_Plugin.unitypackage
python3 tools/playtest/restore_selection.py --verify-only
```

Set `FRONTIER_ASSET_CACHE` to another private directory outside the checkout if needed. See [art restoration instructions](ContentSource/Art/RESTORE.md). Only the hash-pinned two footing meshes and palette are restored; package scripts and other unreviewed content are excluded.
<!-- frontier-launcher-docs:end -->
