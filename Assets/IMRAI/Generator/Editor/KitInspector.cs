// Inspects the Kenney kits as imported: size of every model at rotation 0 (bounds), whether any
// material would render pink under URP, and pictures for checking road edges and building fronts.
// Directions: N = +Z, E = +X, S = -Z, W = -X (Unity world, rotation 0).
// Batch: Unity -batchmode -projectPath unity/IMRAISandbox -executeMethod KitInspector.Run -out DIR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class KitInspector
{
    const string KitRoot = "Assets/ThirdParty/Kenney";

    static string Arg(string name, string fallback)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    static Bounds RendererBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.zero);
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    [MenuItem("IMRAI/Audit generator assets")]
    public static void AuditPrefabs()
    {
        var output = Arg("-out", "AssetAudit");
        Directory.CreateDirectory(output);
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var cam = Camera.main;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.75f, 0.75f, 0.75f);
        var report = new List<string> { "manifest,asset,category,min_vertex_y,max_vertex_y,min_collider_y,valid,non_urp_materials" };
        int failed = 0;
        foreach (var file in Directory.GetFiles("Assets/IMRAI/Generator/Manifests", "*.json").OrderBy(f => f))
        {
            var manifest = Path.GetFileNameWithoutExtension(file);
            var registry = AssetDatabase.LoadAssetAtPath<PrefabRegistry>(PrefabBuilder.RegistryPath(manifest));
            foreach (var e in MapGeneratorCommands.LoadManifest(manifest))
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(registry.Find(e.id));
                var ys = go.GetComponentsInChildren<MeshFilter>().SelectMany(f => f.sharedMesh.vertices.Select(v => f.transform.TransformPoint(v).y)).ToArray();
                float min = ys.Min(), max = ys.Max();
                var colliders = go.GetComponentsInChildren<Collider>();
                float minCollider = colliders.Length == 0 ? 0 : colliders.Min(c => c.bounds.min.y);
                bool floor = e.category == "road" || e.category == "ground";
                int badMaterials = go.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials)
                    .Count(m => m == null || m.shader == null || !m.shader.name.StartsWith("Universal Render Pipeline", StringComparison.Ordinal));
                bool valid = min >= -1e-5f && minCollider >= -1e-5f
                    && badMaterials == 0 && (floor ? max <= 1e-5f && colliders.Length == 0 : !PrefabBuilder.IsTerrainModel(e.source));
                if (!valid) failed++;
                report.Add(FormattableString.Invariant($"{manifest},{e.id},{e.category},{min:F6},{max:F6},{minCollider:F6},{valid},{badMaterials}"));
                Shoot(cam, go, RendererBounds(go), new Vector3(1, 1.2f, -1), 240,
                    Path.Combine(output, $"{manifest}__{e.id}.png"), false);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
        File.WriteAllLines(Path.Combine(output, "assets.csv"), report);
        Debug.Log($"ASSET AUDIT {report.Count - 1} assets, {failed} failed");
        if (Application.isBatchMode) EditorApplication.Exit(failed == 0 ? 0 : 1);
    }

    [MenuItem("IMRAI/Inspect Kenney kits")]
    public static void Run()
    {
        var outDir = Arg("-out", "KitInspection");
        Directory.CreateDirectory(outDir);
        var ci = CultureInfo.InvariantCulture;
        var report = new List<string> { "kit,model,size_x,size_y,size_z,min_x,min_y,min_z,max_x,max_y,max_z,materials,non_urp_materials" };
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var cam = Camera.main;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
        // Even light from all sides so every face shows (for judging building fronts).
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.75f, 0.75f, 0.75f);
        foreach (var l in UnityEngine.Object.FindObjectsOfType<Light>()) { l.intensity = 0.4f; l.shadows = LightShadows.None; }

        foreach (var file in Directory.GetFiles(KitRoot, "*.fbx", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            var kit = Path.GetFileName(Path.GetDirectoryName(file));
            var name = Path.GetFileNameWithoutExtension(file);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(file.Replace('\\', '/'));
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var b = RendererBounds(go);
            var mats = go.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToList();
            var bad = mats.Where(m => m.shader == null || !m.shader.name.StartsWith("Universal Render Pipeline")).Select(m => $"{m.name}:{m.shader?.name}");
            report.Add(string.Join(",", new[] { kit, name }.Concat(new[] { b.size.x, b.size.y, b.size.z, b.min.x, b.min.y, b.min.z, b.max.x, b.max.y, b.max.z }
                .Select(v => v.ToString("F4", ci)))) + $",{mats.Count},{string.Join(" ", bad)}");

            bool road = (kit == "CityKitRoads" && (name.StartsWith("road-") || name.StartsWith("tile-")))
                        || (kit == "NatureKit" && name.StartsWith("ground_path"))
                        || (kit == "SpaceKit" && name.StartsWith("terrain_road"));
            bool building = kit.StartsWith("CityKit") && name.Contains("building");
            if (road) Shoot(cam, go, b, new Vector3(0, 1, 0), 256, Path.Combine(outDir, $"top_{kit}_{name}.png"), true);
            if (building)
                foreach (var (dir, label) in new[] { (Vector3.forward, "N"), (Vector3.right, "E"), (Vector3.back, "S"), (Vector3.left, "W") })
                    Shoot(cam, go, b, dir + Vector3.up * 0.35f, 200, Path.Combine(outDir, $"side_{kit}_{name}_{label}.png"), false);
            UnityEngine.Object.DestroyImmediate(go);
        }
        File.WriteAllLines(Path.Combine(outDir, "kit_bounds.csv"), report);
        Debug.Log($"INSPECT wrote {report.Count - 1} models to {Path.GetFullPath(outDir)}");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    // A picture of the model seen from `from` (direction from the model towards the camera).
    static void Shoot(Camera cam, GameObject go, Bounds b, Vector3 from, int px, string path, bool orthoTop)
    {
        float r = Mathf.Max(b.extents.x, b.extents.z, b.extents.y, 0.25f);
        cam.orthographic = orthoTop;
        if (orthoTop)
        {
            // Square view of exactly x, z in [-0.6, 0.6] around the tile centre, N (+Z) at the top.
            cam.orthographicSize = Mathf.Max(0.6f, r * 1.1f);
            cam.transform.position = new Vector3(0, 5, 0);
            cam.transform.rotation = Quaternion.Euler(90, 0, 0);
        }
        else
        {
            cam.fieldOfView = 35;
            cam.transform.position = b.center + from.normalized * r * 4.5f;
            cam.transform.LookAt(b.center);
        }
        var rt = new RenderTexture(px, px, 24);
        cam.targetTexture = rt;
        cam.Render();
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(px, px, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, px, px), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        cam.targetTexture = null;
        RenderTexture.active = null;
        UnityEngine.Object.DestroyImmediate(rt);
    }
}
