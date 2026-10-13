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

Unity6000.6.0f1; project `Game/`. Native Mac: `python3 tools/playtest/build.py --platform macos --output /absolute/fresh/output`. Windows/Linux profiles require native hosts and their corresponding editor modules. Outputs remain outsideGit.

WASD/leftstick move, mouse/rightstick aim, E/A interact, LMB/RT/X attack, B/LB build, Q/RB craft, Escape/Start pause, J/Select journal. Menus accept keyboard, mouse and gamepad; Actions chooses structures, supplier goods, recipes and crafting. Audio/display/rebinding preferences and earned chapter unlocks save locally pergame.

1-4 player LAN sessions use an authoritative TCP host, optional UDP discovery and a controller-operable IP keypad. Clients cannot grant themselves resources or control host-only campaign actions. LANmenus do notpause the host; solo pause does. No internet relay, Steam SDK, host migration or online matchmaking is claimed.

Selected purchased models/textures remain private, pinned by ContentSource/Art/selection.json. Original game characters, story, simulation and view source are versioned. See Docs/VALIDATION.md for actual evidence and remaining acceptance gates.
