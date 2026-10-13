# Ashfall County stage references

Open the primary Unity 6000.6.0f1 project in `Game/`. Playtest Hub reads `playtest.build.json` and `tools/playtest/build.py` for supported build profiles. Native players and paid assets stay outside Git.

Workbench displays three authored campaign chapters and verified native captures when available. The game's actual procedural layouts and gameplay live in `Game/Assets/Frontier/Core/Chapters.cs` and its adjacent rules. The Unity presentation reads those authoritative entities.

The Workbench map workshop creates independent planning drafts; editing a marker does not automatically modify this running game. Preserve existing drafts and implement a reviewed runtime adapter before claiming map changes are playable.

Run the shared integration command from the sibling Workbench checkout after native screenshots and their report have been copied to the private workspace:

```sh
node scripts/connect-frontier-previews.mjs /path/to/workspace --check
node scripts/connect-frontier-previews.mjs /path/to/workspace
```

Catalog: `tools/StageBuilder/stage-catalog.json`. References use workspace-relative paths and verified SHA-256 hashes. This is a development prototype targeting a future Steam release; menus, controller, LAN, platform and release evidence are separate acceptance gates.
