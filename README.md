# IMRAI Map Generator

Generate seeded maps with flat ground and above-ground obstacles in Unity. Includes City, Forest, Planet, Rubble and Warehouse.

## Setup

1. Clone this repository or download and extract its ZIP.
2. Install Unity Hub and Unity **2022.3.62f3**.
3. In Unity Hub, choose **Add → Add project from disk** and select the folder containing `Assets`, `Packages` and `ProjectSettings`.
4. Open the project and wait for import. Unity restores the pinned packages; the first import may need internet access.

## Generate a map

1. Open **IMRAI → Map Generator** with Play mode off.
2. Choose a terrain and seed. Adjust density, road layout and barriers as needed. Warehouse offers storage aisles, blocked aisles and receiving/staging layouts.
3. Click **Generate & Open**.
4. Select **Map** in the Hierarchy, hover over Scene and press **F**. On Mac, **Option + click-drag** orbits the view.

The same terrain, seed and rules reproduce the same layout. Density sets placement probability; **Reset rules** restores terrain defaults.

Layout JSON saves to `Assets/IMRAI/Layouts/`; scenes save to `Assets/IMRAI/Scenes/`. Generating the same terrain/layout/seed again overwrites those files.

If the menu is missing, wait for compilation, choose **Assets → Refresh** and check the Console for errors.

## Load a layout in a host app

Both apps must load the same layout file.

**Prerequisites**

- Unity 2022.3 with URP (this project uses 2022.3.62f3).
- Tag `Obstacle` and layer `VirtualObjects` in the host project.
- A collision component that reports contacts with `Obstacle` objects.
- Calibrated values: arena origin and axes, floor size (default 9.71 × 7.16 m), minimum corridor width.

**Steps**

1. Set `arena_origin`, `arena_size_m`, `min_corridor_m` and `tile_m` in the terrain config (`Assets/IMRAI/Generator/Configs/`). Positions in the layout are final metres and the loader does no maths, so a wrong value misplaces the map.
2. Generate in **IMRAI → Map Generator**, then run **Verify layouts**. It must report 0 failures. The output is `Assets/IMRAI/Layouts/<terrain>_<seed>.json`.
3. Copy these to the host project with their `.meta` files: `Assets/IMRAI/Loader/`, the terrain's folder in `Assets/IMRAI/Prefabs/` with its `*_registry.asset`, `Assets/IMRAI/Materials/`, and `Assets/ThirdParty/Kenney/` (keep each kit's licence file).
4. Make the layout readable. In the editor, assign the JSON to the loader's `layout` field. In a device build, copy it to `Assets/StreamingAssets/` and set `streamingAssetsPath` to its file name.
5. In the host scene, create an empty GameObject with `LayoutLoader`, assign the terrain's `PrefabRegistry`, and parent it to the node at the cage's calibrated origin.

**Check**

- [ ] No `no prefab for asset` errors.
- [ ] One child per layout object, named `<asset>_<uid>`.
- [ ] Obstacles have the tag, the layer and a trigger collider; floor tiles have no collider.
- [ ] Both apps show the same map.
- [ ] A contact logs the object's `<asset>_<uid>`.
- [ ] A marker at a known cage corner matches the map corner, with no mirroring or offset.
- [ ] The vehicle can drive from the start tile to the goal tile.

**Limits:** flat floor only, no moving obstacles, and collision has been tested only with a stand-in script. Files and code are confidential; do not post them publicly.

## Project notes

- Keep `Assets` (including `.meta` files), `Packages` and `ProjectSettings` together. Keep the pinned editor and package versions. Unity recreates ignored caches locally.
- **IMRAI → Verify layouts** checks generated maps; **Audit generator assets** checks the asset library.
- The arena is 9.71 × 7.16 m. Cage alignment, clearance calibration and Magic Leap 2/spectator integration remain unfinished.
