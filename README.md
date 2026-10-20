# Ashfall County

An open-county zombie survival game. Scavenge homes and stores, keep yourself fed, watered, rested and patched up, barricade a safehouse, and decide whether to make for the evacuation convoy at the Ash River bridge, or keep surviving after it has gone.

**Status: development.** This is an original game in the open-world zombie-survival genre. It is not a certified or finished Steam game. See `Docs/ValidationReports` for what has actually been verified natively.

## The county

Each new game generates a seeded county: 3 km × 3 km by default (2 km and 1 km options). It contains the Ash River and its highway bridge, the county highway and Route 9, the county seat Cedar Fork, a dozen or more towns and hamlets, farms with barns and fenced crop fields, forests and ponds, and roadside homes and abandoned cars. Every building can be entered and has furnished rooms and searchable containers. Zombies only spawn on land, with density by zone: dense on main streets, sparse in the woods. Only the area around you is simulated and drawn.

## Surviving

- **Needs:** hunger, thirst, fatigue, endurance, panic, pain, boredom, sickness, wetness and carry weight, shown as status cards ("moodles").
- **Wounds:** scratches, lacerations, bites and glass cuts on body parts. They bleed until bandaged (rags work too), and they can become infected. With infection on, a bite is a death sentence.
- **Noise:** footsteps, fighting, breaking glass, hammering and above all gunshots carry, even through walls. Sneak (Ctrl) to stay quiet.
- **Zombies:** they wander, investigate sounds, chase on sight, pound on doors, windows and barricades, and climb through open windows. Shove them (Space), knock them down and finish them on the ground.
- **Utilities:** the power and water fail on seeded days. Fridges stop keeping food, stoves go cold, and the taps run dry. After that, collect rain, boil river water or drain what is left in the pipes.
- **Crafting** (B): rip sheets into rags, saw logs into planks, and build spears, spiked bats, campfires, rain collectors and crates. You can also cook a stew. Barricade doors and windows with a hammer, a plank and nails.
- **Skills:** Melee, Aiming, Carpentry, Cooking, First Aid, Foraging, Sneaking and Fitness improve with use. Your profession gives you a head start.
- **Story:** Ada Pike's emergency band and Jonah Reed's convoy come through on your hand radio. The convoy waits on the configured day (default Day 5, 8 AM – 2 PM). Death is permanent.

## Controls

| Action | Keyboard / mouse | Controller |
|---|---|---|
| Move / aim | WASD / mouse | Left stick / right stick |
| Attack / aim mode | Left / right mouse | RT / LT |
| Shove | Space | LB |
| Run / sneak | Shift / Ctrl | L3 / R3 |
| Interact (focused) / all actions | E / Q | A / Y |
| Inventory / crafting / health / map / radio log | Tab or I / B / H / M / J | Select / d-pad left / d-pad right / d-pad down |
| Reload / flashlight / forage | R / F / G | X / d-pad up |
| Hotbar / next weapon | 1–6 | RB |
| Zoom / rotate view | Mouse wheel / Z, X | |
| Pause | Esc | Start |

Menus work with keyboard, mouse and controller. You can rebind keys in Controls. Saves autosave every 5 minutes and on quit, and live in the game's own data folder (`Survival/*.sav`). Saves from the earlier chapter game are untouched.

## Code layout

- `Game/Assets/Ashfall/Core`: the pure C# simulation (no Unity references). World generation, items and loot, survivors, zombies, combat, crafting, saves. It is host-authoritative: owners submit input and commands only.
- `Game/Assets/Ashfall/Unity`: chunk-streamed county view, characters, camera, lighting, audio, uGUI/TextMeshPro menus and HUD, and the native QA probe.
- `Game/Assets/Editor/PrepareAshfallArt.cs`: prepares the privately restored owned packs (zombies, survivors, props, weapons, vehicles, trees, HUD sprites, audio) into prefabs at build time. Without them the game uses procedural stand-ins.
- `tools/AshfallCoreTests`: fast simulation checks that run without Unity: `dotnet run --project tools/AshfallCoreTests -c Release`.
- `Legacy/`: the retired Frontier chapter-game sources, kept outside the Unity project.

Owned art and audio stay private. They are pinned by `ContentSource/Art/presentation-selection.json` and `ContentSource/Art/zomboid-selection.json`, and restored by `tools/playtest/build.py` into the isolated build snapshot only. LAN multiplayer is not implemented in the rebuilt game.

Native acceptance probe, which uses an isolated save directory:

```sh
Game.exe -ashfall-qa=/abs/out/ashfall-qa.json -ashfall-user-dir=/abs/out/GameWorkbenchQA
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
