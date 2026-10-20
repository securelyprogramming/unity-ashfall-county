# unity-ashfall-county: Unity project context

<!-- LOCAL_WINDOWS_SETUP_20261017_START -->
## Local Windows validation — 2026-10-17

- Local project: `D:/Code/securelyprogramming/unity-ashfall-county/Game`.
- Exact editor: Unity `6000.6.0f1`. Windows player build and native gameplay probes passed on this PC.
- Validated executable: `D:/pt/r2/103/Game.exe`.
- A captured native frame was inspected for visible rendering and readable UI.
- [Dated build/runtime evidence](../ValidationReports/LocalWindows-20261017.json).

Use this dated local record for checks performed here. Earlier host-policy and validation sections below describe their recorded older builds; current online, separate-PC LAN, physical-controller and release acceptance are not established by this local setup.
<!-- LOCAL_WINDOWS_SETUP_20261017_END -->

<!-- unity-onboarding:generated:start -->
Analyzed October 17, 2026 on Windows. Source commit: `0972a4c51c532be7f72e2ff4d51f95bc91e3be46`.
Project root relative to this repository: `Game`.

Confirmed configuration: Unity **6000.6.0f1** (`ProjectSettings/ProjectVersion.txt`), Built-in or custom; no SRP package declared, Input System package declared (`Packages/manifest.json`).

Build scene paths from `ProjectSettings/EditorBuildSettings.asset`: `Assets/Scenes/Frontier.unity`. Missing scene files: none. The first listed scene is the configured startup candidate; native startup requires execution.

Source inventory: 24 C# files under Assets, 0 assembly definitions. Sampled files: `Assets\Editor\GameApplicationIconBuildHook.cs`, `Assets\Editor\PrepareFrontier.cs`, `Assets\StudioIntro\StudioIntro.cs`, `Assets\Frontier\Core\Actions.cs`. Observed namespaces: Mayo.Frontier, ProgrammingSecurely.Presentation. MonoBehaviour classes in this sample: StudioIntro.

Assembly definitions: none; default Unity assemblies apply.

Testing: Unity Test Framework not declared; source test candidates: `Assets\Frontier\Core\PlaytestDriver.cs`. Use repository AGENTS.md and `tools/playtest/build.py` for the canonical Windows build. Build into a fresh directory outside this checkout.

Tooling: Unity CLI and a Unity Personal license are available on this machine. No connected Unity Pipeline editor was detected at baseline. Required exact editors are being installed; an installed newer patch does not establish compatibility.

Constraints: preserve project version, scenes, saves, purchased-asset hashes, platform checks and unrelated changes. Restore private licensed caches using repository documentation. Networking, full campaigns, controllers and separate-machine LAN remain unverified by this static inspection. Do not treat earlier repository reports as evidence for this PC.

Machine build/startup evidence: `D:/Code/securelyprogramming/.local-setup/` and `D:/Code/playtest_temp/`. Current results must be read there; this context does not claim compilation or runtime success.
<!-- unity-onboarding:generated:end -->
