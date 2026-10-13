# Application icon

This game uses its own application artwork. The adjacent manifest records the source provenance, canonical asset hashes and engine input paths. Assets are installed locally, so building this repository does not require the Workbench checkout.

The shared generation, installation and audit tool lives at [game-workbench/scripts/game-icons.py](https://github.com/securelyprogramming/game-workbench/blob/main/scripts/game-icons.py). Run `python3 scripts/game-icons.py audit --games ashfall-county` from that checkout with these repositories as siblings.

The Editor-only `GameApplicationIconBuildHook` imports `Assets/Editor/Branding/GameAppIcon.png` and verifies all default and Standalone icon slots before a player build. Runtime code, application identifiers and save paths are unaffected.

To brand an already-accepted Mac app, use the shared tool's `package` command with a fresh output path. It preserves bundle IDs/executable names, verifies native code/data and managed payloads, and leaves the original app intact. Distribution signing, live OS icon acceptance, Windows/Linux builds and store capsule art have separate validation requirements.

[Native macOS icon verification](../../Docs/ValidationReports/application-icon-20261013.json) records the accepted live application icon, identity preservation, payload hashes and development signature scope.
