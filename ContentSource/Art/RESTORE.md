# Restore reviewed private art

The current manifest selects exactly three files from the owner's purchased `Spline_Builder_Plugin.unitypackage`: `SM_StoneFoot.fbx`, `SM_StoneFootConnector.fbx`, and `Polygon_Texture2.png` (344,563 bytes total). It does not claim that newly purchased Synty characters or whole environments have been downloaded or integrated.

Keep the original archive and purchase/entitlement records privately. The manifest pins the exact archive SHA-256 and each selected file's source path, byte count and SHA-256. Do not commit the archive, extracted licensed sources, `.meta` sidecars generated for them, or purchase records.

From this repository:

```sh
python3 tools/playtest/restore_selection.py --archive /private/path/Spline_Builder_Plugin.unitypackage
python3 tools/playtest/restore_selection.py --verify-only
```

Windows can use `python` or `py -3` instead of `python3`. The default cache is `~/code/game_asset_cache/frontier-synty`. Set `FRONTIER_ASSET_CACHE` or pass `--cache /private/cache` to choose another directory outside the checkout. A copied helper resolves `ContentSource/Art/selection.json` relative to this repository; `--manifest` can explicitly select that same reviewed manifest when invoking the shared source helper.

Restoration verifies the complete original archive hash before scanning. Tar traversal, absolute paths, links, special files, ambiguous names, oversized members/pathnames and expanded archives are rejected. Reads and the selected byte count are bounded. Only the three reviewed art files are staged; package scripts, editor tooling and unselected assets are never extracted. All selected hashes and existing destinations are checked before writing. Different existing originals are preserved and cause an error, while identical files are reused. Cache writes use complete temporary files and atomic no-overwrite hard links; the private filesystem must support hard links (APFS, NTFS and normal Linux filesystems do).

`--verify-only` changes no files. Add `--archive /private/path/Spline_Builder_Plugin.unitypackage` to that command to verify both the original archive and the existing cache. The restoration helper has no network download, login, installation or unpinning flag. New selections require explicit review before extending these pins.

The existing native build wrapper copies the verified selection into its isolated build snapshot. `Game/Assets/Resources/LicensedFrontier/` remains ignored. It does not import the purchased package's code. Run `./run.sh`, `.\run.ps1`, or the documented native build wrapper after restoration; the launcher keeps native outputs outside the checkout.
