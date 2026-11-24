# IMRAI Map Generator

Unity Editor tool for repeatable flat-floor map generation in the IMRAI mocap cage. This is a Unity Editor project, not a standalone desktop application.

## Open and generate a map

Clone this repository or choose **Code → Download ZIP** on GitHub and extract it before opening the project.

1. Install Unity Hub and Unity **2022.3.62f3**. Keep this editor version; do not upgrade the project or its packages during setup.
2. In Unity Hub, choose **Add → Add project from disk** and select this folder, containing `Assets`, `Packages` and `ProjectSettings`.
3. Open with **2022.3.62f3** and wait for package resolution and asset import. The first open needs more time than subsequent opens. Internet access may be needed to restore the exact package versions pinned in `Packages/manifest.json` and `Packages/packages-lock.json`.
4. Choose **IMRAI → Map Generator**. Select a terrain and seed, adjust the rule controls, then click **Generate & Open**. Leave Play mode off.
5. Select **Map** in the Hierarchy, hover over Scene and press **F** to frame it. On Mac, **Option + click-drag** orbits the view.

Python, the original scan datasets and the old CageMaps project are not required. If the menu is missing, wait for compilation, choose **Assets → Refresh**, and check the Console for errors.

## Terrains and rules

City, Forest, Planet, Rubble and Warehouse are included. Warehouse offers Storage aisles, Blocked aisles and Receiving/staging. Blocked aisles retains a detour around at least one crate blocker.

Density controls set placement probabilities, not exact object counts. In Forest, large-obstacle density includes trees and tall stones. Warehouse uses row depth or staging-cluster size instead of road spacing. **Reset rules** restores terrain defaults. The selected terrain, seed and rules reproduce the same layout.

The window saves layout JSON in `Assets/IMRAI/Layouts/` and scenes in `Assets/IMRAI/Scenes/`. Warehouse filenames include the layout choice. Reusing the same terrain/layout/seed replaces its saved layout and scene. Source configuration files remain unchanged.

The floor is flat. Ponds, craters, raised terrain and geometry below the floor are excluded; buildings, trees, rocks, crates and barriers are above-floor obstacles.

## Portability and sharing

Keep `Assets` and all associated `.meta` files, `Packages`, `ProjectSettings`, this README and `THIRD_PARTY.md` together. Unity recreates the ignored `Library`, `Temp`, `Logs` and `UserSettings` folders locally. Do not distribute account credentials or add Unity's generated caches to Git.

Package versions are pinned. Registry dependencies are Burst **1.8.9**, Mathematics **1.2.1** and Searcher **4.9.2**; URP **14.0.12** is supplied by the specified editor. Do not replace these with latest versions during installation.

This is a sandbox. The arena is currently 9.71 × 7.16 m with a placeholder origin and corridor clearance. Alignment and loading in the actual Magic Leap 2/spectator apps still require lab calibration and integration. See `THIRD_PARTY.md` for asset licences.

## Developer checks

The IMRAI menu includes **Verify layouts** and **Audit generator assets**. Batch methods are also available with Unity's `-executeMethod` option: `MapGeneratorCommands.GenerateBatch`, `MapGeneratorCommands.Verify`, `MapGeneratorCommands.CollisionCheck` and `KitInspector.AuditPrefabs`. Close this project before starting a separate batch process against it.

For generation/verification, pass `-config Assets/IMRAI/Generator/Configs/<terrain>_v01.json -seeds 1-20`; generate the batch before verifying it. Warehouse also takes `-layout storage_aisles`, `blocked_aisles` or `receiving_staging`. Collision checks use `-seed 1`. `PrefabBuilder.Run` rebuilds the included prefabs and manifests from their source lists if required.
