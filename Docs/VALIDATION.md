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
