# Ashfall County

Scavenge a flooded rural county, rescue stranded neighbors and hold an evacuation safehouse.

**Status: development.** Three authored chapters with distinct objectives, original cast and live gameplay rules. This source is being built and playtested toward a premium Steam release; it is not a certified or finished Steam game.

## Story

### The Last Pharmacy

Mara reaches a shuttered pharmacy with one promise: nobody leaves the county alone.

### The Waterline

Ada hears families beyond the flooded road. Supplies must reach the refuge before dusk.

### County Evacuation

Jonah can start the convoy if the safehouse holds long enough to gather every survivor.

## Play and build

Unity 6000.6.0f1; project `Game/`. Native Mac: `python3 tools/playtest/build.py --platform macos --output /absolute/fresh/output`. Windows/Linux profiles require native hosts and their corresponding editor modules. Outputs remain outsideGit.

WASD/leftstick move, mouse/rightstick aim, E/A interact, LMB/RT/X attack, B/LB build, Q/RB craft, Escape/Start pause, J/Select journal. Menus accept keyboard, mouse and gamepad; Actions chooses structures, supplier goods, recipes and crafting. Audio/display/rebinding preferences and earned chapter unlocks save locally pergame.

1-4 player LAN sessions use an authoritative TCP host, optional UDP discovery and a controller-operable IP keypad. Clients cannot grant themselves resources or control host-only campaign actions. LANmenus do notpause the host; solo pause does. No internet relay, Steam SDK, host migration or online matchmaking is claimed.

Selected purchased models/textures remain private, pinned by ContentSource/Art/selection.json. Original game characters, story, simulation and view source are versioned. See Docs/VALIDATION.md for actual evidence and remaining acceptance gates.

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
