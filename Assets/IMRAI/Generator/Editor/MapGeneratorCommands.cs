// Editor commands for the IMRAI map generator.
//   IMRAI > Map Generator                     -> choose terrain/seed, generate and open a map
//   IMRAI > Generate batch of 20 maps          -> <terrain>_1..20.json + <terrain>_batch.csv (milestone M4)
// Terrains are configs in Generator/Configs (city_v01, forest_v01, planet_v01, rubble_v01); pass -config.
//   IMRAI > Verify layouts                     -> repeatability, distinct maps, loader round-trip
//   IMRAI > Open layout as scene...            -> a scene for inspection (the JSON is the source of truth)
// Batch (any of): Unity -batchmode -nographics -projectPath unity/IMRAISandbox -executeMethod
//   MapGeneratorCommands.GenerateOne  [-config PATH] [-seed N]
//   MapGeneratorCommands.GenerateBatch [-config PATH] [-seeds 1-20]
//   MapGeneratorCommands.Verify        [-seeds 1-20]
//   MapGeneratorCommands.CollisionCheck [-seed N]   (enters Play mode; drives a CarProxy into a building)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MapGeneratorCommands
{
    const string DefaultConfig = "Assets/IMRAI/Generator/Configs/city_v01.json";
    const string ManifestDir = "Assets/IMRAI/Generator/Manifests";
    const string LayoutDir = "Assets/IMRAI/Layouts";
    static PrefabRegistry Registry(string manifest) => AssetDatabase.LoadAssetAtPath<PrefabRegistry>(PrefabBuilder.RegistryPath(manifest));
    public static string LayoutPrefix(GeneratorConfig c) => c.name == "warehouse" ? $"warehouse_{c.layout}" : c.name;
    public static string LayoutPath(GeneratorConfig c) => $"{LayoutDir}/{LayoutPrefix(c)}_{c.seed}.json";

    [Serializable] class ManifestWrapper { public ManifestEntry[] items; }

    static string Arg(string name, string fallback)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    static List<int> Seeds(string text)
    {
        var seeds = new List<int>();
        foreach (var part in text.Split(','))
        {
            var ab = part.Split('-');
            int a = int.Parse(ab[0]), b = ab.Length > 1 ? int.Parse(ab[1]) : a;
            for (int s = a; s <= b; s++) seeds.Add(s);
        }
        return seeds;
    }

    public static GeneratorConfig LoadConfig(string path, int? seed = null)
    {
        var c = JsonUtility.FromJson<GeneratorConfig>(File.ReadAllText(path));
        if (c.name == "warehouse")
        {
            string layout = Arg("-layout", c.layout);
            if (layout != c.layout && layout == "blocked_aisles") c.road_obstacles = Math.Max(1, c.road_obstacles);
            c.layout = layout;
        }
        if (seed.HasValue) c.seed = seed.Value;
        return c;
    }

    public static ManifestEntry[] LoadManifest(string name) =>
        JsonUtility.FromJson<ManifestWrapper>("{\"items\":" + File.ReadAllText($"{ManifestDir}/{name}.json") + "}").items;

    public static GeneratedMap Run(GeneratorConfig c) => MapGenerator.Generate(c, LoadManifest(c.manifest));

    static string Write(GeneratedMap m)
    {
        Directory.CreateDirectory(LayoutDir);
        var path = LayoutPath(m.config);
        File.WriteAllText(path, m.json);
        return path;
    }

    static void Exit(int code) { if (Application.isBatchMode) EditorApplication.Exit(code); }

    public static void GenerateOne()
    {
        var c = LoadConfig(Arg("-config", DefaultConfig), int.TryParse(Arg("-seed", ""), out int s) ? s : (int?)null);
        var m = Run(c);
        var path = Write(m);
        AssetDatabase.Refresh();
        Debug.Log($"GENERATE seed {c.seed}: {(m.valid ? "valid" : "FAILED (" + m.failure + ")")} after {m.tries} tries, {m.objects.Count} objects -> {path}\n" + string.Join("\n", m.checkResults));
        Exit(m.valid ? 0 : 1);
    }

    public static string GenerateAndOpen(string configPath, int seed) => GenerateAndOpen(LoadConfig(configPath, seed));

    public static string GenerateAndOpen(GeneratorConfig config)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Stop Play mode and wait for compilation before generating.");
        var map = Run(config);
        if (!map.valid) throw new InvalidOperationException($"Seed {config.seed} failed: {map.failure}");
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return null;
        var path = Write(map);
        AssetDatabase.Refresh();
        OpenLayoutScene(path);
        Debug.Log($"GENERATE {map.config.name} seed {config.seed}: valid, {map.objects.Count} objects -> {path}");
        return path;
    }

    [MenuItem("IMRAI/Generate batch of 20 maps")]
    public static void GenerateBatch()
    {
        var configPath = Arg("-config", DefaultConfig);
        var seeds = Seeds(Arg("-seeds", "1-20"));
        var ci = CultureInfo.InvariantCulture;
        var csv = new List<string> { "seed,valid,tries,grid,road_tiles,buildings,props,ground,barriers,start_tile,goal_tile,path_tiles,failure,sha256" };
        int failed = 0;
        foreach (var seed in seeds)
        {
            var m = Run(LoadConfig(configPath, seed));
            Write(m);
            var cat = m.manifest.ToDictionary(e => e.id, e => e.category);
            int Count(string k) => m.objects.Count(o => cat[o.asset] == k);
            csv.Add(string.Join(",", seed, m.valid ? "yes" : "no", m.tries, $"{m.W}x{m.H}", Count("road"), Count("building"), Count("prop"),
                Count("ground"), Count("road_obstacle"), $"{m.start?[0]} {m.start?[1]}", $"{m.goal?[0]} {m.goal?[1]}", m.pathTiles, m.failure, m.hash));
            if (!m.valid) failed++;
        }
        var name = LayoutPrefix(LoadConfig(configPath));
        File.WriteAllLines($"{LayoutDir}/{name}_batch.csv", csv);
        AssetDatabase.Refresh();
        Debug.Log($"BATCH {name} {seeds.Count} seeds, {failed} failed -> {LayoutDir}/{name}_batch.csv\n" + string.Join("\n", csv));
        Exit(failed == 0 ? 0 : 1);
    }

    // Checks 5 and 6 of the spec in the sandbox: same seed gives the same file (hash), different
    // seeds give different maps, and the loader builds exactly the objects in each file.
    [MenuItem("IMRAI/Verify layouts")]
    public static void Verify()
    {
        var seeds = Seeds(Arg("-seeds", "1-20"));
        var lines = new List<string>();
        int failed = 0;
        void Check(bool ok, string text) { lines.Add($"{(ok ? "ok  " : "FAIL")} {text}"); if (!ok) failed++; }
        var configPath = Arg("-config", DefaultConfig);
        var registry = Registry(LoadConfig(configPath).manifest);
        var hashes = new Dictionary<string, int>();
        foreach (var seed in seeds)
        {
            var a = Run(LoadConfig(configPath, seed));
            var b = Run(LoadConfig(configPath, seed));
            Check(a.hash == b.hash, $"seed {seed}: generating twice gives the same hash {a.hash.Substring(0, 12)}");
            var path = LayoutPath(a.config);
            Check(File.Exists(path) && MapGenerator.Sha256(File.ReadAllText(path)) == a.hash, $"seed {seed}: saved file matches a fresh run");
            Check(a.valid, $"seed {seed}: valid ({string.Join("; ", a.checkResults)})");
            if (hashes.TryGetValue(a.hash, out int other)) Check(false, $"seed {seed}: same map as seed {other}");
            hashes[a.hash] = seed;

            // Loader round trip: same count, names, positions and rotations as the file.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Map").transform;
            var file = JsonUtility.FromJson<LayoutFile>(File.ReadAllText(path));
            LayoutLoader.Build(file, registry, root);
            bool same = root.childCount == file.objects.Length;
            for (int k = 0; same && k < file.objects.Length; k++)
            {
                var o = file.objects[k]; var t = root.GetChild(k);
                same &= t.name == $"{o.asset}_{o.uid}" && Vector3.Distance(t.localPosition, new Vector3(o.pos[0], o.pos[1], o.pos[2])) < 1e-4f
                        && Mathf.Abs(Mathf.DeltaAngle(t.localEulerAngles.y, o.rot)) < 0.01f;
            }
            Check(same, $"seed {seed}: loader built {root.childCount} objects matching the file ({file.objects.Length})");
            var obstacles = root.GetComponentsInChildren<BoxCollider>().Where(bc => bc.isTrigger).Select(bc => bc.gameObject).ToList();
            Check(obstacles.All(g => g.CompareTag("Obstacle") && g.layer == LayerMask.NameToLayer("VirtualObjects")),
                  $"seed {seed}: {obstacles.Count} obstacles tagged Obstacle on layer VirtualObjects");
            var floorCats = a.manifest.Where(e => e.category == "road" || e.category == "ground").Select(e => e.id).ToHashSet();
            var floor = file.objects.Count(o => floorCats.Contains(o.asset));
            Check(root.Cast<Transform>().Count(t => t.GetComponent<Collider>() == null) == floor, $"seed {seed}: {floor} floor tiles have no collider");
            bool flat = true;
            foreach (var t in root.Cast<Transform>().Where(t => floorCats.Contains(file.objects[t.GetSiblingIndex()].asset)))
            {
                flat &= t.GetComponentsInChildren<Collider>().Length == 0;
                foreach (var filter in t.GetComponentsInChildren<MeshFilter>())
                    foreach (var vertex in filter.sharedMesh.vertices)
                        flat &= Mathf.Abs(filter.transform.TransformPoint(vertex).y - file.arena_origin[1]) < 1e-5f;
            }
            Check(flat, $"seed {seed}: all road and ground vertices lie on the arena floor, with no colliders");
            bool above = root.GetComponentsInChildren<MeshFilter>().All(f => f.sharedMesh.vertices
                .All(v => f.transform.TransformPoint(v).y >= file.arena_origin[1] - 1e-5f));
            Physics.SyncTransforms();
            above &= root.GetComponentsInChildren<Collider>().All(c => c.bounds.min.y >= file.arena_origin[1] - 1e-5f);
            Check(above, $"seed {seed}: no mesh or collider extends below the arena floor");
            Check(a.manifest.All(e => floorCats.Contains(e.id) || !PrefabBuilder.IsTerrainModel(e.source)),
                $"seed {seed}: obstacle pool has no ponds, craters or other terrain-relief models");
            if (a.config.name == "warehouse") Check(WarehouseGenerator.CheckLayout(a), $"seed {seed}: {a.config.layout} structure and detour checks");
        }
        Debug.Log($"VERIFY {LoadConfig(configPath).name} {seeds.Count} seeds, {failed} failed\n" + string.Join("\n", lines));
        Exit(failed == 0 ? 0 : 1);
    }

    // Spec check 7 in the sandbox: drive a CarProxy (kinematic Rigidbody + Box Collider + collision
    // script) into a building of a generated map and check the log names that building's uid.
    public static void CollisionCheck()
    {
        int seed = int.Parse(Arg("-seed", "1"));
        var cfg = LoadConfig(Arg("-config", DefaultConfig), seed);
        var buildings = new HashSet<string>(LoadManifest(cfg.manifest).Where(e => e.category == "building").Select(e => e.id));
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var root = new GameObject("Map");
        var loader = root.AddComponent<LayoutLoader>();
        loader.layout = AssetDatabase.LoadAssetAtPath<TextAsset>(LayoutPath(cfg));
        loader.registry = Registry(cfg.manifest);
        var car = GameObject.CreatePrimitive(PrimitiveType.Cube);
        car.name = "CarProxy";
        car.transform.localScale = new Vector3(0.3f, 0.15f, 0.5f);
        var rb = car.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        car.AddComponent<CollisionReporterStandIn>();
        Directory.CreateDirectory("Assets/IMRAI/Scenes");
        EditorSceneManager.SaveScene(scene, "Assets/IMRAI/Scenes/CollisionCheck.unity");

        // Keep this callback alive into Play mode (no domain reload), and restore the setting after.
        bool hadOptions = EditorSettings.enterPlayModeOptionsEnabled;
        var options = EditorSettings.enterPlayModeOptions;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        void Finish(int code)
        {
            EditorSettings.enterPlayModeOptionsEnabled = hadOptions;
            EditorSettings.enterPlayModeOptions = options;
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(code);
        }
        // Timed in game seconds: in batch mode editor frames run much faster than physics steps.
        GameObject target = null; Vector3 from = Vector3.zero, to = Vector3.zero; float t0 = -1;
        const float speed = 0.8f;  // m/s
        EditorApplication.update += () =>
        {
            if (!EditorApplication.isPlaying) return;
            var proxy = GameObject.Find("CarProxy");
            if (target == null && Time.time > 0.5f)
            {
                target = GameObject.FindGameObjectsWithTag("Obstacle").Where(g => buildings.Contains(g.name.Substring(0, g.name.LastIndexOf('_')))).OrderBy(g => g.name).First();
                to = target.transform.position + new Vector3(0, 0.1f, 0);
                from = to + new Vector3(0, 0, -1.2f);
                proxy.transform.position = from;
                t0 = Time.time;
            }
            if (target == null) return;
            float travelled = speed * (Time.time - t0);
            proxy.GetComponent<Rigidbody>().MovePosition(Vector3.MoveTowards(from, to, travelled));
            if (travelled > 1.2f + 0.4f)
            {
                var hit = proxy.GetComponent<CollisionReporterStandIn>().lastHit;
                int uid = int.Parse(target.name.Substring(target.name.LastIndexOf('_') + 1));
                bool ok = hit == target.name;
                Debug.Log($"{(ok ? "ok  " : "FAIL")} collision with {target.name} logged as '{hit}' (uid {uid})");
                target = null; t0 = float.MaxValue;
                Finish(ok ? 0 : 1);
            }
        };
        EditorApplication.EnterPlaymode();
    }

    // Pictures of layouts (top-down and from the south-west), for checking by eye:
    // -executeMethod MapGeneratorCommands.RenderLayouts -seeds 1-6 -out DIR
    public static void RenderLayouts()
    {
        var outDir = Arg("-out", "LayoutRenders");
        Directory.CreateDirectory(outDir);
        var configPath = Arg("-config", DefaultConfig);
        var registry = Registry(LoadConfig(configPath).manifest);
        foreach (var seed in Seeds(Arg("-seeds", "1-6")))
        {
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.65f, 0.65f, 0.65f);
            var cfg = LoadConfig(configPath, seed);
            var file = JsonUtility.FromJson<LayoutFile>(File.ReadAllText(LayoutPath(cfg)));
            LayoutLoader.Build(file, registry, new GameObject("Map").transform);
            var arena = GameObject.CreatePrimitive(PrimitiveType.Cube);  // the capture volume floor
            float ax = file.config.arena_size_m[0], az = file.config.arena_size_m[1];
            arena.transform.localScale = new Vector3(ax, 0.01f, az);
            arena.transform.position = new Vector3(file.arena_origin[0] + ax / 2, -0.006f, file.arena_origin[2] + az / 2);
            var cam = Camera.main;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
            foreach (var (name, top) in new[] { ("top", true), ("view", false) })
            {
                cam.orthographic = top;
                if (top)
                {
                    cam.orthographicSize = az / 2 + 0.3f;
                    cam.transform.SetPositionAndRotation(new Vector3(ax / 2, 10, az / 2), Quaternion.Euler(90, 0, 0));
                }
                else
                {
                    cam.fieldOfView = 50;
                    cam.transform.position = new Vector3(ax / 2 - 3.5f, 6.5f, -3.2f);
                    cam.transform.LookAt(new Vector3(ax / 2, 0, az / 2));
                }
                int w = 1000, h = top ? Mathf.RoundToInt(1000 * (az + 0.6f) / (ax + 0.6f * ax / az)) : 700;
                var rt = new RenderTexture(w, h, 24) { antiAliasing = 4 };
                cam.targetTexture = rt;
                cam.aspect = (float)w / h;
                cam.Render();  // warm-up: the first frame after loading can sample textures before they are ready
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, $"{LayoutPrefix(cfg)}_{seed}_{name}.png"), tex.EncodeToPNG());
                cam.targetTexture = null;
                RenderTexture.active = null;
            }
        }
        Exit(0);
    }

    [MenuItem("IMRAI/Open layout as scene...")]
    static void OpenAsScene()
    {
        var path = EditorUtility.OpenFilePanel("Layout file", LayoutDir, "json");
        if (string.IsNullOrEmpty(path)) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        OpenLayoutScene(path);
    }

    static void OpenLayoutScene(string path)
    {
        var file = JsonUtility.FromJson<LayoutFile>(File.ReadAllText(path));
        if (Registry(file.manifest) == null) throw new InvalidOperationException($"Missing prefab registry for {file.manifest}");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var root = new GameObject("Map");
        var loader = root.AddComponent<LayoutLoader>();
        loader.layout = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        // Creating a scene can unload assets that are not yet referenced by a component.
        loader.registry = Registry(file.manifest);
        LayoutLoader.Build(file, loader.registry, root.transform);
        loader.loadOnStart = false;
        Directory.CreateDirectory("Assets/IMRAI/Scenes");
        var scenePath = $"Assets/IMRAI/Scenes/{Path.GetFileNameWithoutExtension(path)}.unity";
        if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new IOException($"Could not save scene: {scenePath}");
        Selection.activeGameObject = root;
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
    }
}
