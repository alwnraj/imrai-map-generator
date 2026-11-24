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

## Project notes

- Keep `Assets` (including `.meta` files), `Packages` and `ProjectSettings` together. Keep the pinned editor and package versions. Unity recreates ignored caches locally.
- **IMRAI → Verify layouts** checks generated maps; **Audit generator assets** checks the asset library.
- The arena is 9.71 × 7.16 m. Cage alignment, clearance calibration and Magic Leap 2/spectator integration remain unfinished.
