# Ashfall County development

Use Unity 6000.6.0f1. Native project: `Game/`. Preserve user saves, previous builds and unrelated edits. Paid assets stay ignored under Game/Assets/Resources/LicensedFrontier; restore exact hashes from ContentSource/Art/selection.json and private cache. Source for shared systems is game-workbench/frontier-runtime; inspect all consumer games before changing shared contracts. Host owns simulation; clients send actions only. Never weaken compiler/runtime checks or trust client-owned progression/resources. Build with tools/playtest/build.py into a fresh output outside checkout. Record actual compile, native gameplay/controller and LAN evidence separately; no Steam readiness claims from source alone. Windows/Linux require native hosts/modules. Menus are uGUI/TextMeshPro with Input System. No extra engine installations.
<!-- BEGIN:system-design-workflow -->
## System design and acceptance

Read game-design.json and game-workbench.json before changing game systems. The former records this game's characters, movement, NPC interactions, story, playtime targets and acceptance gaps. The latter owns authored/procedural world settings, per-stage overrides and explicitly supported playtest scenarios.

- Keep authored stages and procedural generation distinct. Preserve story gates, routes, player/objective clearance and deterministic seeds; seas must exclude land-only spawns. Workbench plans require an engine adapter before they can change native gameplay.
- Prefer compatible purchased art, menus, characters, weapons, buildings, animations and audio. Preserve the game's mechanics, controls, collision, economy and saves. Hash-pin local restoration/provenance and keep licensed payloads out of public Git.
- Validate NPC movement/navigation, idle/walk/interaction animations, dialogue and quest/economy state in each applicable native stage. Do not infer runtime NPCs from campaign cast text.
- Keep supported launch arguments in playtest.scenarios. Unimplemented stage/free-play/LAN/items targets stay explicitly unavailable until a real adapter and tests exist. Isolate test saves and outputs.
- Record fresh native tests in Docs/ValidationReports. Track controller hardware, two-machine LAN, campaign completion, playtime, platform and Steam acceptance separately. A target of several hours is a design target, not a measured claim.
<!-- END:system-design-workflow -->
