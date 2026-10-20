# Ashfall County survival rebuild: native Windows validation (2026-10-20)

The game was rebuilt as an open-county zombie survival game (`Game/Assets/Ashfall`). Everything below about the
2026-10-13 chapter game is history; that runtime now lives in `Legacy/` and is no longer built.

**Latest evidence: [AshfallSurvival-WindowsPlayer-20261020-043947.json](ValidationReports/AshfallSurvival-WindowsPlayer-20261020-043947.json). 16/16 native checks passed.**

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
