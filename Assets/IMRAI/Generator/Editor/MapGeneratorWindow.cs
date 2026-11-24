using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public class MapGeneratorWindow : EditorWindow
{
    enum Terrain { City, Forest, Planet, Rubble, Warehouse }
    enum WarehouseLayout { StorageAisles, BlockedAisles, ReceivingStaging }

    [SerializeField] Terrain terrain = Terrain.Forest;
    [SerializeField] int seed = 42;
    [SerializeField] GeneratorConfig rules;
    [SerializeField] Terrain rulesTerrain;
    [SerializeField] WarehouseLayout warehouseLayout;
    string message;
    MessageType messageType;

    [MenuItem("IMRAI/Map Generator")]
    public static void Open()
    {
        var window = GetWindow<MapGeneratorWindow>("Map Generator");
        window.minSize = new Vector2(420, 440);
        window.Show();
    }

    void OnGUI()
    {
        EditorGUILayout.Space(8);
        terrain = (Terrain)EditorGUILayout.EnumPopup("Terrain", terrain);
        EnsureRules();
        if (terrain == Terrain.Warehouse)
        {
            warehouseLayout = (WarehouseLayout)EditorGUILayout.Popup("Layout", (int)warehouseLayout,
                new[] { "Storage aisles", "Blocked aisles", "Receiving/staging" });
            ApplyWarehouseLayout();
        }
        seed = EditorGUILayout.IntField("Seed", seed);
        EditorGUILayout.Space(8);
        rules.building_density = EditorGUILayout.Slider(new GUIContent("Large obstacle density", "Trees and tall stones in forest; large obstacles such as buildings in other terrains."), rules.building_density, 0, 1);
        rules.prop_density = EditorGUILayout.Slider(new GUIContent("Small prop density", "Chance of smaller props on lots not occupied by a large obstacle."), rules.prop_density, 0, 1);
        int width = MapGenerator.GridCount(rules.arena_size_m[0], rules.margin_m, rules.tile_m);
        int height = MapGenerator.GridCount(rules.arena_size_m[1], rules.margin_m, rules.tile_m);
        string spacing = terrain != Terrain.Warehouse ? "Lot tiles between roads"
            : warehouseLayout == WarehouseLayout.ReceivingStaging ? "Staging cluster size" : "Storage row depth";
        rules.block_tiles = EditorGUILayout.IntSlider(spacing, rules.block_tiles, 1, Mathf.Max(1, Mathf.Min(width, height) - 2));
        if (terrain != Terrain.Warehouse) rules.road_drop = EditorGUILayout.Slider("Road removal chance", rules.road_drop, 0, 1);
        int minimumBarriers = terrain == Terrain.Warehouse && warehouseLayout == WarehouseLayout.BlockedAisles ? 1 : 0;
        rules.road_obstacles = EditorGUILayout.IntSlider(new GUIContent("Maximum barriers", "Placement attempts; barriers that block the required path are skipped."), rules.road_obstacles, minimumBarriers, width * height);
        if (GUILayout.Button("Reset rules")) ResetRules();
        EditorGUILayout.Space(8);
        EditorGUILayout.HelpBox("Same terrain, seed and rules reproduce the same map. Barrier placements must keep a route open.", MessageType.Info);
        bool busy = EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling;
        if (busy) EditorGUILayout.HelpBox("Stop Play mode and wait for compilation before generating.", MessageType.Info);
        using (new EditorGUI.DisabledScope(busy))
        {
            if (GUILayout.Button("Generate & Open", GUILayout.Height(32)))
                GenerateSelected();
        }
        if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, messageType);
    }

    string ConfigPath => $"Assets/IMRAI/Generator/Configs/{terrain.ToString().ToLowerInvariant()}_v01.json";

    void EnsureRules()
    {
        if (rules == null || rulesTerrain != terrain) ResetRules();
    }

    void ResetRules()
    {
        rules = MapGeneratorCommands.LoadConfig(ConfigPath);
        rulesTerrain = terrain;
        if (terrain == Terrain.Warehouse) warehouseLayout = (WarehouseLayout)Math.Max(0, Array.IndexOf(WarehouseGenerator.Layouts, rules.layout));
        message = null;
    }

    void ApplyWarehouseLayout()
    {
        rules.layout = WarehouseGenerator.Layouts[(int)warehouseLayout];
        if (warehouseLayout == WarehouseLayout.BlockedAisles) rules.road_obstacles = Math.Max(1, rules.road_obstacles);
    }

    void GenerateSelected()
    {
        try
        {
            EnsureRules();
            if (terrain == Terrain.Warehouse) ApplyWarehouseLayout();
            rules.seed = seed;
            var path = MapGeneratorCommands.GenerateAndOpen(rules);
            message = path == null ? "Generation cancelled." : $"Opened {Path.GetFileNameWithoutExtension(path)}.\nLayout saved to {path}";
            messageType = path == null ? MessageType.Info : MessageType.None;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            messageType = MessageType.Error;
            Debug.LogException(ex);
        }
    }
}
