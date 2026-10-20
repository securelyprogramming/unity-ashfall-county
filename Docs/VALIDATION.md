# Ashfall County survival rebuild: native Windows validation (2026-10-20)

The game was rebuilt as an open-county zombie survival game (`Game/Assets/Ashfall`). Everything below about the
2026-10-13 chapter game is history; that runtime now lives in `Legacy/` and is no longer built.

**Drivable cars (2026-10-20 12:39): [AshfallVehicles-WindowsPlayer-20261020.json](ValidationReports/AshfallVehicles-WindowsPlayer-20261020.json).** Native probe [AshfallSurvival-WindowsPlayer-20261020-123903.json](ValidationReports/AshfallSurvival-WindowsPlayer-20261020-123903.json) passed 28/28 checks. Twelve of them are new: entering with E, starting the engine, driving with W, the model following the car, the camera zooming out, the engine loop, braking, running down a zombie, steering, fuel use, parking on exit, and the car's position and fuel after save/reload. Core: 574 checks passed. The build came from an uncommitted working tree, 0 errors. Driving used the virtual keyboard only, so physical-controller driving is not proven. No owned vehicle audio exists, so the engine and crash sounds are synthesized.

**Farming, fishing, traits and skill books (2026-10-20 13:05): [AshfallSurvival-WindowsPlayer-20261020-130506.json](ValidationReports/AshfallSurvival-WindowsPlayer-20261020-130506.json) passed 46/46 native checks.** Build `20261020-080010-survival`, from an uncommitted working tree that includes the drivable-cars work: 0 errors, 5 warnings. Core: 574 checks passed, including farming, fishing, traits, skill books, survival save/load and refusal of version 1 saves.
- New native checks:
  - Trait picker on the New Game screen: pick within the 4-point budget, an over-budget pick refused, closing with the pad's Back. The chosen traits reach the host, which validates them again.
  - Traits applied: Strong capacity, Green Thumb Farming +1, Smoker cigarettes.
  - Farming: the dig action is offered and digs a plot on the faced grass tile; the plant action is offered and plants; the plot is watered from the can.
  - Growth over 2.5 in-game days. The probe fast-forwards with `Simulation.Tick` and keeps the soil watered (QA setup).
  - The ripe plot is rendered and can be harvested.
  - Fishing: the fish action is offered, a fish is caught and then cooked.
  - Skill book read (Carpentry ×3); the Health/Skills panel shows traits and the book bonus; smoking.
  - Save/reload restores the growing plot, traits, read books and Fishing level.
- Save version is now 2. Version 1 survivors are refused with an explanation, and the file is left untouched.
- Failed runs are kept as history:
  - `...-124450`: the survivor died to a horde during the town frame-time sample, which made every later check fail. The probe now keeps the survivor alive during that 9 s sample, recorded as QA setup.
  - `...-125619`: 45/46. The probe recorded the plot's growth before fishing instead of at save time.
  - `...-130219`: 38/46. The existing car engine-start step did not register E once. It passed on the same build in `...-130506` and in earlier runs, so it is intermittent, not fixed.
  - `...-125127` passed 46/46 before the traits overlay was made opaque.
- Not proven: crop growth over real-time play (the probe fast-forwards days), fishing catch rates and trait balance over long sessions, the trait picker on a physical controller.

**Earlier evidence: [AshfallSurvival-WindowsPlayer-20261020-043947.json](ValidationReports/AshfallSurvival-WindowsPlayer-20261020-043947.json). 16/16 native checks passed.**

- Build: Unity 6000.6.0f1 Windows development player, built by `tools/playtest/build.py` into a fresh directory outside the checkout. 0 errors.
  - Owned packs were restored by hash into the build snapshot only: 54 prepared art roles (4 animated zombie types, survivors, props, weapons, vehicles) and 47/47 audio cues.
- County: seed 777, 3072 × 3072 m, 17 settlements, 2,764 buildings, 11,534 zombies, generated in about 1 s.
- Checks, driven through real Input System devices (virtual keyboard and gamepad):
  - Gameplay: title menu, new game, keyboard movement, E interaction opening a container panel, eating, drinking, gamepad melee kill, zombie perception and wounding, bandaging, barricading and the map panel.
  - Sessions: survivor alive before the performance sample, quit to title, then save and reload with identical position, kills, barricade and zombie count.
- Performance in Cedar Fork with 229 zombies simulated: 16.7 ms average frame (v-sync locked). Simulation 2.1 ms, view 0.3 ms, UI 0.1 ms.
- QA-only setup is listed in the report (teleports, granted tools, spawned test zombies, health restored before the town sample). It happens only in the isolated GameWorkbenchQA save.
- Earlier runs are kept as history. `...-042428` failed the town frame-time check at 89 ms. It was caused by zombies re-running failed path searches every tick and by per-window/per-plank draw calls, and both were fixed.
- Pure simulation checks: `dotnet run --project tools/AshfallCoreTests -c Release` gives 349 checks passed. That covers generation of both sizes, determinism, needs, loot, melee and firearms, zombie attack and bandaging, barricades and doors, crafting, eating and drinking, pathfinding, save/load and noise.

Not proven yet:
- Physical controller hardware.
- Human playtime and a full evacuation run.
- Linux/macOS players.
- LAN multiplayer: not implemented in the rebuilt game, and no longer advertised.
- Long-session memory and save growth.
- Steam packaging.

Licence follow-up: confirm that the Leartes glass-break WAVs may be used outside Unreal before shipping.

Reproduce: `python tools/playtest/run_current.py --build-only`, then `python tools/playtest/ashfall_qa.py --player <build>/Game.exe`.

---

# Native development validation — 2026-10-13

Latest rebuilt-source retest: **112 native checks passed**, including three campaign chapters and a separate-process save/settings reload, with zero runtime errors. All 19 runtime C# hashes matched the actual app build before and after testing. See [latest native evidence](ValidationReports/native-retest-20261013.json). Earlier same-day evidence below is retained as history.

Unity 6000.6.0f1 built this project as a macOS development app outside Git. The build succeeded with zero errors and one upstream TMP shader deprecation warning. 98 native campaign checks completed all three chapters through 860 production commands; 14 separate-process reload checks passed. Runtime errors: **0**. The native player restored saved chapter unlocks, preferences and the remapped Interact binding after restarting.

The probe exercises the real uGUI menus and Input System with a virtual gamepad, confirms solo pause, checks corrupt-save backup recovery, and captures native gameplay. This does not establish physical controller ergonomics.

Evidence and exact runtime source hashes: [native-macos-20261013.json](ValidationReports/native-macos-20261013.json). Full private logs and PNGs remain in the GameWorkbenchQA diagnostic workspace. Failed earlier compile/import, binding-reload and viewport attempts were retained; their causes were fixed and the affected native builds/tests repeated.

## Reproduce

Build a fresh app with `./run.sh --build-only --output /absolute/fresh/output`. Launch that app twice using `-frontier-qa=campaign` then `-frontier-qa=read`, supplying the same `-frontier-user-dir=/absolute/GameWorkbenchQA/user` and distinct `-frontier-report=`/`-frontier-capture=` paths under GameWorkbenchQA. See the shared runtime QA guide for exact arguments. Real player saves are not used.

## Remaining release work

This is a three-chapter development prototype, **not a finished Steam release**. Finish art, pacing, audio, accessibility and performance; validate physical controllers, separate-machine LAN and Windows/Linux players; complete distribution signing and Steamworks/store/release review. LAN host chapter/retry presentation is reviewed but not accepted by the native LAN smoke test. Workbench maps remain planning drafts until a runtime adapter is implemented.

Game Builder entry: [Ashfall County](http://192.168.1.101:8090/game-builder/786198da-6a89-4bb7-99f1-521eec125c24).
