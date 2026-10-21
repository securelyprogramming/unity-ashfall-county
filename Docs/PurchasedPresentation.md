# Purchased presentation — ashfall-county

Each Frontier game supplies `Game/Assets/Resources/FrontierPurchasedArt.json` with reviewed role bindings, selected modular character parts, model heights and menu slicing. Its `ContentSource/Art/presentation-selection.json` pins every original archive and selected file by SHA-256 and byte count. POLY and Synty packs remain within a stylized low-poly presentation. Kenney Komplete's authored exclusive-zombie skin is used for the infected in Ashfall County, Dead Orbit and Havenfall Coalition; it is a purchased bundle source with a retained CC0 notice.

Structural model surface recipes follow the material slot order in the original purchased Unity prefabs. Original brick, plaster, glass, timber planks and roofing replace the incorrect single-palette mapping. The importer requires every active material slot and ignores unused trailing slots retained in some original prefabs. Surface albedo, color, metallic and smoothness values are adapted to the existing built-in Standard shader. Paired original pitched roof slopes meet at a normalized ridge; vendor shader code is not imported. Non-readable imported static meshes retain their own renderer instead of being disabled by the procedural mesh combiner.

`PrepareFrontierPurchasedArt` creates materials and normalized, selected-part prefabs under ignored `Game/Assets/Resources/LicensedFrontier/Portfolio`. It never imports or runs vendor scripts, packages or sample scenes. The read-only snapshot view instances these prefabs for heroes, customers, civilians, raiders, zombies and goblins where bound. The existing simulation still owns collision, movement, damage, interactions, rewards, progression and multiplayer authority. Trees, rocks, crates, retail shelves, kitchen counters and bread use corresponding selected source art where bound. Purchased Synty wall/window/door/roof modules replace shelter and house geometry within the existing authored footprints. Retail/kitchen room boundaries and shopfronts use purchased modules; Guild fortifications use carved Viking walls. Dead Orbit bulkheads use compatible purchased POLY industrial metal wall art. Treant bodies/crowns use the purchased tree with original animated roots/arms and glowing face. Authored terrain and unbound props remain in use.

Goblin enemies use purchased Synty Goblin Locomotion idle/walk clips with root motion disabled. The Fantasy ranger uses its original FemaleStudioIdle pose clip; the Sci-Fi engineer uses its original time-varying SciFiIdle. Only original skeleton transform tracks with matching paths are retained; customizer, renderer and actor tracks are excluded. Other human idle poses and all human/zombie walking retain snapshot-driven joint animation. Survival, Shops and Viking character sources contain no matching idle/walk set. The complete catalog archive search found goblin locomotion and no dedicated portable human walk/run source. Dedicated wolf art, role-specific purchased human locomotion, complete environment replacement and individual weapon/VFX/audio coverage remain follow-up work; the manifest is not evidence that those slots are finished.

Wood UI Elements supplies rural, fantasy, retail and kitchen panels/buttons. Space Assets supplies Dead Orbit and Rift Runners. Menus preserve the existing uGUI layout, controller focus, event bindings and settings. Native captures exposed a camera offset that obscured the local hero and incorrect source pixel density for the sliced wood buttons; the view now centers on the local player's real world position, cuts away opaque shelters while the local player stands inside, and imports each sprite with its reviewed density.

## Ashfall County selections (open-county rebuild)

Three reviewed manifests are restored, in order, into the same private cache and copied into each isolated build snapshot by `tools/playtest/build.py`. Each stays at or under 250 MB:

| Manifest | Files | Size | Contents |
|---|---|---|---|
| `ContentSource/Art/presentation-selection.json` | 21 | 31 MB | Wood UI, Shops structures, POLY Survival Character meshes and atlas A |
| `ContentSource/Art/zomboid-selection.json` | 584 | 227 MB | Apocalypse HUD, Cartoon Zombies (Drinker, Soldier, Medic, Fat), POLY survival props, melee and firearms, vehicles, nature, Warrior humanoid clips, atlases B to K, survival and zombie SFX, ambience and music |
| `ContentSource/Art/decay-selection.json` | 100 | 128 MB | Freaks, plague heart, military props, guns and throwables, and open-county audio (below) |

Every row pins the original archive (library `file_id`, byte count and SHA-256) and the selected member (source path, byte count and SHA-256). Only meshes, textures, animation clips and audio are restored. Vendor scripts, prefabs, materials, controllers and scenes never are.

### Packs used by `decay-selection.json`

| Pack (library key) | Licence note | Used for |
|---|---|---|
| Cartoon Zombies Pack, Unity (`item:851`, archive file 531197) | Fab Standard License; keep private | Tank (juggernaut), Monster (feral), Mask (walker), plus Drinker Shout (screamer), Fat Attack_Boom (bloater burst), Medic and Soldier Run_Z clips |
| Creatures Mega Pack, Unity (`item:853`, archive file 531916) | Fab Standard License; keep private | Cocoon mesh, idle clip, albedo and emissive (plague heart) |
| POLY - Weapons Kit (`item:997`, file 504682) | Animpic EULA via Humble POLY Game Dev Assets Collection #3; keep private | Assault rifle 5.56, army SMG, army sniper rifle, frag and smoke grenades, large ammo box (supply crate), wooden ammo box (military crate) |
| POLY - Survival Kit (`item:1000`, file 505598) | Animpic EULA (as above) | Tent, radio set, flare gun, energy drink |
| POLY - Survival Construction (`item:1002`, file 505592) | Animpic EULA (as above) | Sandbags, watchtower, single bed (medical bed), bunk bed |
| POLY - Survival Workshop (`item:994`, file 505629) | Animpic EULA (as above) | Diesel generator |
| PSX Horror Audio, Futuristic Heavy Duty Weapons, Combat Sounds Bundle, Footsteps, Weapon Sound Pack Vol 1 & 2, Survival Sound Effects, Dystopian Hellfire, Soundscapes of Chernobyl (`item:56/58/78/90/92/68/79/77`) | GameDev Market Pro licence via Humble "SFX and Music for Your Games"; keep private | Screams, explosions, heavy steps, rifle/SMG/sniper shots, fire ignite, hammer, combat and base music |
| Ultimate SFX for Games: Monsters Boss & Zombies, Alarms & Sirens, Shooter Radio Commands, War Strategy, Notification, Checkpoints, Ui Games Interface (`item:968/946/973/979/969/950/977`) | Humble bundle royalty-free SFX licence; keep private | Roars, bile, air-raid siren, radio chatter, human shouts, mission/recruit/influence/facility/morale/kill-feed cues |
| Voices: Wordless Expressions (Ovani Voice Vault, `item:940`) | Ovani Sound royalty-free licence; keep private | Human pain and death |

Existing zomboid-selection files are reused where they fit: the Weapons Kit ammo box (AmmoBox), the Vehicle Kit barrier (Barrier), the Molotov and pipe models, and the POLY Survival Character atlases for every human variant.

### Prepared roles (`Game/Assets/Editor/PrepareAshfallArt.cs`)

- **Zombies**: `zombie/0..4` (Drinker, Soldier, Medic, Fat, Mask), `zombie/walker/0..2` (Drinker, Medic, Mask), `zombie/screamer/0` (pale Drinker, Shout), `zombie/bloater/0` (Fat, Attack_Boom), `zombie/armored/0` (Soldier), `zombie/feral/0` (Monster, Run and Jump), `zombie/juggernaut/0` (Tank, Move and Move_Z). Generic rigs, heights normalised to 1.8 m (the view applies the bestiary size). Animator parameters: `Speed`, `Down`, `Attack`, `Hit`, plus a `Special` trigger on the screamer (scream), bloater (burst), feral (pounce) and juggernaut (charge). Use `ArtLibrary.HasParameter` before setting it. There is no crawler rig: `zombie/crawler/*` falls back to the generic pool or the procedural crawler.
- **Humans**: POLY Survival Character customizer parts with fabric atlases A to K and three skin tones, retargeted to the Warrior humanoid clips. `human/community|enclave|rival|raider|soldier/0..3` and `human/trader/0..1`. Even variants are male and odd variants female; `ArtLibrary.HumanVariant(group, seed, female)` picks a matching one. Named characters: `character/ada_pike`, `jonah_reed`, `doc_hollis` (male), `wren_calder`, `tess_monroe`, `marshal_kade`, `big_sal` (1.92 m), `pilgrim`. `survivor_male` and `survivor_female` are kept.
- **Props**: all existing kinds, plus `prop/SupplyCrate`, `MilitaryCrate`, `AmmoBox`, `Sandbags`, `Barrier`, `Tent` (3 m), `Watchtower` (3 m by 5.5 m), `MedicalBed`, `Bunk`, `RadioSet` and `Generator`. Each has a fixed size and is centred on the prop footprint. `GardenBed` and `OutpostFlag` have no compatible owned model, so they stay procedural (`ViewPropMeshes`).
- **Items**: the existing weapons, plus `item/assault_rifle`, `smg`, `hunting_rifle`, `sniper_rifle`, `grenade`, `smoke_grenade`, `molotov`, `pipe_bomb`, `flare_gun` and `energy_drink`.
- **Effects**: `fx/plague_heart` is the cocoon, with an idle pulse and emissive glow. The library has no Unity aircraft or parachute (only Unreal helicopter sources), so `aircraft/plane`, `aircraft/helicopter` and `fx/parachute` stay procedural.

### Audio keys (`AshfallAudio.cs`)

Owned clips are indexed by file name under `LicensedFrontier/Portfolio/Zomboid/Audio` and `LicensedFrontier/Portfolio/Decay/Audio`. Every key, and any unknown key, has a deterministic synthesized stand-in, so nothing is silent or throws when art is not restored. These keys are synthesized only, because no fitting owned clip was found: `gas`, `heart-pulse` (lub-dub), `artillery-whistle`, `plane`, `helicopter`, and the vehicle keys. `SetMusicMood("explore" | "base" | "combat")` crossfades the music bed. `Play` or `Ui` with `music-combat`, `music-base` or `music-explore` does the same. Combat relaxes to explore after three minutes unless it is requested again. `GunshotKey(weaponId)` maps weapons to `rifle`, `smg`, `sniper`, `shotgun` or `gunshot`.

Restore and verify:

```powershell
python tools/playtest/restore_presentation.py            # every manifest; downloads only pinned originals from the LAN library
python tools/playtest/restore_presentation.py --verify-only
```

Native evidence, 2026-10-20: a Windows build succeeded with `ASHFALL_ART_PLAN roles=115` and no preparation failures. The built player logged `purchasedRoles=115/115` and `purchasedCues=78/89`. The new roles are not yet rendered in gameplay, because they are consumed by the unity-view rebuild. See [the decay art validation report](ValidationReports/AshfallDecayArt-WindowsPlayer-20261020.json).

Restore on a fresh machine with the bundled Python runtime or a real Python 3 installation:

```powershell
python tools/playtest/restore_presentation.py
python tools/playtest/build.py --platform windows --output D:/fresh-playtest-output
```

The default selected cache is `~/code/game_asset_cache/frontier-presentation/<game-id>`. `FRONTIER_PRESENTATION_CACHE` overrides the parent directory. Original archives use `~/code/game_asset_cache/archives/frontier-presentation`; `--archives` can select another private archive cache. `--offline` requires pinned original archives or an already verified selection. Builds require verified cached sources and create fresh isolated snapshots; they never silently download new art. Licensed payloads and prepared prefabs remain ignored. Keep proof of account entitlement privately for distribution.

Source-only checks, editor import, a Windows player build and native rendered gameplay are separate evidence levels. Physical controllers, all campaigns, separate-machine LAN, playtime, other platforms and Steam release acceptance remain unverified by this art work.

Dated evidence and exact remaining scope: [Purchased presentation validation](ValidationReports/PurchasedPresentation-20261018.json).
