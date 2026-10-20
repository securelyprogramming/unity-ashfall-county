# Legacy Frontier chapter game

Ashfall County was rebuilt on 2026-10-19 as an open-county survival game (`Game/Assets/Ashfall`).
The previous three-chapter Frontier runtime that used to live in `Game/Assets/Frontier` is kept here,
outside the Unity project, so it no longer compiles or boots in the game. Nothing in it was deleted,
including uncommitted presentation work. The shared source of truth for that runtime is still
`game-workbench/frontier-runtime`; no shared contract was changed.

- `Frontier/`: the former game-local copy of the Frontier runtime (Core, Networking, Unity view, QA probe).
- `Editor/PrepareFrontierPurchasedArt.cs`: the former purchased-art prep step.
- `Editor/PrepareFrontier.cs.txt`: the build entry point as it was before the rebuild.
