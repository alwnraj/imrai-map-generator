// Builds a map from a layout file (IMRAI Map Generator Spec v0.1, section 5). The loader does no
// maths: each object goes to its stored pos and rot, scaled by config.tile_m (prefabs are built at
// 1 unit per tile), and is named <asset>_<uid> so collision logs name the exact object.
// Both IMRAI apps carry this script, the prefabs and the registry, and load the same file.
using System;
using System.IO;
using UnityEngine;

[Serializable] public class LayoutConfig
{
    public string layout;
    public float tile_m = 1f, margin_m = 0.3f, building_density, prop_density, road_drop, min_corridor_m;
    public int block_tiles, road_obstacles;
    public float[] arena_size_m;
}
[Serializable] public class LayoutObject { public int uid; public string asset; public int[] tile; public int rot; public float[] pos; }
[Serializable] public class LayoutChecks { public bool path_found; public int path_tiles, tries; }
[Serializable] public class LayoutFile
{
    public string generator_version, manifest; public int seed; public LayoutConfig config;
    public float[] arena_origin; public int[] grid, start_tile, goal_tile;
    public LayoutObject[] objects; public LayoutChecks checks;
}

public class LayoutLoader : MonoBehaviour
{
    [Tooltip("Layout file (Assets/IMRAI/Layouts/city_<seed>.json); or leave empty and set a StreamingAssets path")]
    public TextAsset layout;
    [Tooltip("Path under StreamingAssets, used when no TextAsset is set (the ML2 build reads from there)")]
    public string streamingAssetsPath = "";
    public PrefabRegistry registry;
    public bool loadOnStart = true;

    void Start()
    {
        if (loadOnStart && transform.childCount == 0) Load();
    }

    public LayoutFile Load()
    {
        string text = layout != null ? layout.text : File.ReadAllText(Path.Combine(Application.streamingAssetsPath, streamingAssetsPath));
        return Build(JsonUtility.FromJson<LayoutFile>(text), registry, transform);
    }

    public static LayoutFile Build(LayoutFile file, PrefabRegistry registry, Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            if (Application.isPlaying) Destroy(parent.GetChild(i).gameObject);
            else DestroyImmediate(parent.GetChild(i).gameObject);
        }
        float scale = file.config != null && file.config.tile_m > 0 ? file.config.tile_m : 1f;
        foreach (var o in file.objects)
        {
            var prefab = registry.Find(o.asset);
            if (prefab == null) { Debug.LogError($"Layout {file.seed}: no prefab for asset '{o.asset}'"); continue; }
            var go = Instantiate(prefab, parent);
            go.name = $"{o.asset}_{o.uid}";
            go.transform.localPosition = new Vector3(o.pos[0], o.pos[1], o.pos[2]);
            go.transform.localRotation = Quaternion.Euler(0, o.rot, 0);
            go.transform.localScale = Vector3.one * scale;
        }
        return file;
    }
}
