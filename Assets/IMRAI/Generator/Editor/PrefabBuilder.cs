// Builds the IMRAI prefabs for every terrain from Generator/Editor/Sources/<terrain>.json, following
// the Getting Started sheet: an empty root at the origin named after the asset, the Kenney model as
// its child, centred on the tile with its base on the floor; obstacles get a trigger Box Collider on
// the root sized to the model's bounds, tag Obstacle and layer VirtualObjects (children too); floor
// tiles (road, ground) are flattened to y=0, with no collider and no tag. Then writes each terrain's manifest
// (Generator/Manifests/<manifest>.json, spec section 3) with footprints and heights measured from the
// prefabs, and the PrefabRegistry its loader uses (Prefabs/<manifest>_registry.asset).
// Batch: Unity -batchmode -nographics -projectPath unity/IMRAISandbox -executeMethod PrefabBuilder.Run [-terrain forest]
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class PrefabBuilder
{
    const string SourcesDir = "Assets/IMRAI/Generator/Editor/Sources";
    const string ManifestDir = "Assets/IMRAI/Generator/Manifests";
    const string MaterialDir = "Assets/IMRAI/Materials";
    const string MeshDir = "Assets/IMRAI/Meshes";

    [Serializable] public class Source
    {
        public string id, model, category, front, plain, row_axis = "z";
        public string[] connects;
        public float weight = 1, spacing, scale = 1, child_yaw, min_box_height;
        public int count = 1;
    }
    [Serializable] public class MaterialColor { public string name, color; }
    [Serializable] class Sources { public string manifest, folder; public Source[] items; public MaterialColor[] material_colors; }

    public static string RegistryPath(string manifest) => $"Assets/IMRAI/Prefabs/{manifest}_registry.asset";

    // Kit ground features must not enter the obstacle pool through the generic prop category.
    public static bool IsTerrainModel(string model)
    {
        var name = Path.GetFileName(model ?? "");
        return new[] { "ground_", "crater", "pond", "river", "hole", "trench", "cliff", "ramp", "slope", "hill", "stairs" }
            .Any(word => name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    static readonly Dictionary<string, Color> Plain = new Dictionary<string, Color>
    {
        { "grass", new Color(0.36f, 0.62f, 0.30f) }, { "gravel", new Color(0.55f, 0.53f, 0.50f) },
        { "dust", new Color(0.78f, 0.47f, 0.33f) },
        { "dirt", new Color(0.72f, 0.44f, 0.23f) },
        { "concrete", new Color(0.56f, 0.56f, 0.54f) }, { "aisle", new Color(0.32f, 0.34f, 0.35f) },
    };

    [MenuItem("IMRAI/Build prefabs and manifests (all terrains)")]
    public static void Run()
    {
        int obstacleLayer = LayerMask.NameToLayer("VirtualObjects");
        if (obstacleLayer < 0 || !UnityEditorInternal.InternalEditorUtility.tags.Contains("Obstacle"))
            throw new Exception("Run IMRAI > Sandbox setup first (needs tag Obstacle and layer VirtualObjects)");
        var only = Environment.GetCommandLineArgs().SkipWhile(a => a != "-terrain").Skip(1).FirstOrDefault();
        int built = 0;
        foreach (var file in Directory.GetFiles(SourcesDir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            if (only != null && Path.GetFileNameWithoutExtension(file) != only) continue;
            built += Build(JsonUtility.FromJson<Sources>(File.ReadAllText(file)), obstacleLayer);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"PREFABS built {built} prefabs");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static int Build(Sources set, int obstacleLayer)
    {
        var prefabDir = $"Assets/IMRAI/Prefabs/{set.folder}";
        Directory.CreateDirectory(prefabDir);
        Directory.CreateDirectory(ManifestDir);
        Directory.CreateDirectory(MaterialDir);
        var registry = AssetDatabase.LoadAssetAtPath<PrefabRegistry>(RegistryPath(set.manifest));
        if (registry == null) { registry = ScriptableObject.CreateInstance<PrefabRegistry>(); AssetDatabase.CreateAsset(registry, RegistryPath(set.manifest)); }
        registry.entries = new List<PrefabRegistry.Entry>();
        var ci = CultureInfo.InvariantCulture;
        var lines = new List<string>();

        foreach (var s in set.items)
        {
            if (s.category != "road" && s.category != "ground" && IsTerrainModel(s.model))
                throw new Exception($"{s.id}: terrain relief cannot be used as an obstacle on a flat arena");
            var root = new GameObject(s.id);
            if (!string.IsNullOrEmpty(s.plain)) AddPlainTile(root, s.plain);
            else
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/ThirdParty/Kenney/{s.model}.fbx");
                if (model == null) throw new Exception($"{set.manifest} {s.id}: model not found: {s.model}");
                for (int k = 0; k < Math.Max(1, s.count); k++)
                {
                    var child = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                    child.transform.localRotation = Quaternion.Euler(0, s.child_yaw, 0) * child.transform.localRotation;
                    child.transform.localScale *= s.scale;
                    float off = (k - (s.count - 1) / 2f) * s.spacing;  // a row of copies across the road
                    child.transform.localPosition = s.row_axis == "x" ? new Vector3(off, 0, 0) : new Vector3(0, 0, off);
                }
                ApplyMaterialColors(root, set);
                // Centre the model on the tile and put its base on the floor (some kits are offset).
                var b0 = Bounds(root);
                var shift = new Vector3(-b0.center.x, -b0.min.y, -b0.center.z);
                foreach (Transform c in root.transform) c.localPosition += shift;
            }
            if (s.category == "road" || s.category == "ground") FlattenFloor(root, set.folder);
            var b = Bounds(root);
            bool obstacle = s.category == "building" || s.category == "prop" || s.category == "road_obstacle";
            if (obstacle)
            {
                var box = root.AddComponent<BoxCollider>();
                box.isTrigger = true;
                float h = Mathf.Max(b.size.y, s.min_box_height);
                box.center = new Vector3(b.center.x, h / 2, b.center.z);
                box.size = new Vector3(b.size.x, h, b.size.z);
                root.tag = "Obstacle";
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = obstacleLayer;
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{prefabDir}/{s.id}.prefab");
            UnityEngine.Object.DestroyImmediate(root);
            registry.entries.Add(new PrefabRegistry.Entry { id = s.id, prefab = prefab });

            // Footprint in tiles (1 tile = 1 unit at the kits' scale) and height in tile units.
            int fx = Mathf.Max(1, Mathf.CeilToInt(b.size.x - 1e-3f)), fz = Mathf.Max(1, Mathf.CeilToInt(b.size.z - 1e-3f));
            var fields = new List<string>
            {
                $"\"id\": \"{s.id}\"", $"\"prefab\": \"{set.folder}/{s.id}\"", $"\"category\": \"{s.category}\"",
                $"\"footprint\": [{fx}, {fz}]", $"\"height\": {Math.Round(b.size.y, 3).ToString(ci)}",
                $"\"collider\": \"{(obstacle ? "trigger_box" : "none")}\"",
            };
            if (s.connects != null && s.connects.Length > 0) fields.Add($"\"connects\": [{string.Join(", ", s.connects.Select(c => $"\"{c}\""))}]");
            if (!string.IsNullOrEmpty(s.front)) fields.Add($"\"front\": \"{s.front}\"");
            fields.Add($"\"weight\": {s.weight.ToString(ci)}");
            fields.Add($"\"size\": [{Math.Round(b.size.x, 3).ToString(ci)}, {Math.Round(b.size.y, 3).ToString(ci)}, {Math.Round(b.size.z, 3).ToString(ci)}]");
            fields.Add($"\"source\": \"{(string.IsNullOrEmpty(s.model) ? "IMRAI plain " + s.plain + " tile" : "Kenney " + s.model)}\"");
            lines.Add("  {" + string.Join(", ", fields) + "}");
            if (fx > 1 || fz > 1) Debug.LogWarning($"{set.manifest} {s.id}: footprint {fx}x{fz} tiles; v0.1 expects 1x1");
        }
        File.WriteAllText($"{ManifestDir}/{set.manifest}.json", "[\n" + string.Join(",\n", lines) + "\n]\n");
        EditorUtility.SetDirty(registry);
        Debug.Log($"PREFABS {set.manifest}: {set.items.Length} prefabs in {prefabDir}");
        return set.items.Length;
    }

    // Keep upward-facing surfaces only: projecting undersides too would cause coplanar flicker.
    // Bake into separate meshes so the original kit models and obstacle geometry stay unchanged.
    static void FlattenFloor(GameObject root, string folder)
    {
        var meshDir = $"{MeshDir}/{folder}";
        Directory.CreateDirectory(meshDir);
        var originalChildren = root.transform.Cast<Transform>().ToArray();
        var filters = root.GetComponentsInChildren<MeshFilter>();
        for (int k = 0; k < filters.Length; k++)
        {
            var filter = filters[k];
            var source = filter.sharedMesh;
            var vertices = source.vertices.Select(v => root.transform.InverseTransformPoint(filter.transform.TransformPoint(v))).ToArray();
            var triangles = new List<int>[source.subMeshCount];
            int count = 0;
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                triangles[sub] = new List<int>();
                var indices = source.GetTriangles(sub);
                for (int i = 0; i < indices.Length; i += 3)
                {
                    int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                    if (Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).y <= 1e-8f) continue;
                    triangles[sub].AddRange(new[] { a, b, c });
                    count++;
                }
            }
            if (count == 0) continue;
            for (int i = 0; i < vertices.Length; i++) vertices[i].y = 0;
            var mesh = UnityEngine.Object.Instantiate(source);
            mesh.name = $"{root.name}_{k}";
            mesh.vertices = vertices;
            for (int sub = 0; sub < triangles.Length; sub++) mesh.SetTriangles(triangles[sub], sub);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            var path = $"{meshDir}/{mesh.name}.asset";
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved == null) { AssetDatabase.CreateAsset(mesh, path); saved = mesh; }
            else { EditorUtility.CopySerialized(mesh, saved); EditorUtility.SetDirty(saved); UnityEngine.Object.DestroyImmediate(mesh); }
            var surface = new GameObject($"flat_{k}");
            surface.transform.SetParent(root.transform, false);
            surface.AddComponent<MeshFilter>().sharedMesh = saved;
            surface.AddComponent<MeshRenderer>().sharedMaterials = filter.GetComponent<MeshRenderer>().sharedMaterials;
        }
        foreach (var child in originalChildren) UnityEngine.Object.DestroyImmediate(child.gameObject);
        if (root.GetComponentsInChildren<MeshFilter>().Length == 0) throw new Exception($"{root.name}: no upward-facing floor surfaces");
    }

    // Some kit FBX files store off-hue colours (Nature Kit grass is teal, water near white under URP).
    // Sources may list material_colors {name, "#rrggbb"}; matching materials are swapped for URP Lit
    // copies in Assets/IMRAI/Materials, leaving the kit files untouched.
    static void ApplyMaterialColors(GameObject root, Sources set)
    {
        if (set.material_colors == null || set.material_colors.Length == 0) return;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                var mc = set.material_colors.FirstOrDefault(m => m.name == mats[i].name);
                if (mc == null) continue;
                var path = $"{MaterialDir}/{set.folder}_{mc.name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
                ColorUtility.TryParseHtmlString(mc.color, out var c);
                mat.color = c;
                mat.SetFloat("_Smoothness", 0.1f);
                mats[i] = mat;
            }
            r.sharedMaterials = mats;
        }
    }

    // A plain ground tile made in Unity where a kit has none: 1 x 0.02 x 1, no collider.
    static void AddPlainTile(GameObject root, string kind)
    {
        var matPath = $"{MaterialDir}/{kind}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = Plain[kind] };
            AssetDatabase.CreateAsset(mat, matPath);
        }
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
        cube.name = kind;
        cube.transform.SetParent(root.transform, false);
        cube.transform.localScale = new Vector3(1f, 0.02f, 1f);
        cube.transform.localPosition = new Vector3(0, 0.01f, 0);
        cube.GetComponent<Renderer>().sharedMaterial = mat;
    }

    public static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.zero);
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    // Top-down pictures of every road prefab of every terrain (for checking connects by eye).
    // Batch: -executeMethod PrefabBuilder.RenderRoads -out DIR
    public static void RenderRoads()
    {
        var outDir = Environment.GetCommandLineArgs().SkipWhile(a => a != "-out").Skip(1).FirstOrDefault() ?? "RoadRenders";
        Directory.CreateDirectory(outDir);
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.8f, 0.8f, 0.8f);
        var cam = Camera.main;
        cam.orthographic = true;
        cam.orthographicSize = 0.5f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.transform.SetPositionAndRotation(new Vector3(0, 5, 0), Quaternion.Euler(90, 0, 0));
        foreach (var file in Directory.GetFiles(SourcesDir, "*.json"))
        {
            var set = JsonUtility.FromJson<Sources>(File.ReadAllText(file));
            foreach (var s in set.items.Where(i => i.category == "road" || i.category == "road_obstacle"))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/IMRAI/Prefabs/{set.folder}/{s.id}.prefab");
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var rt = new RenderTexture(240, 240, 24);
                cam.targetTexture = rt;
                cam.Render(); cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(240, 240, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 240, 240), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, $"{set.manifest}__{s.id}__{string.Join("", s.connects ?? new string[0])}.png"), tex.EncodeToPNG());
                cam.targetTexture = null; RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
