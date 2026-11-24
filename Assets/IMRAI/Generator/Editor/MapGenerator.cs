// IMRAI Map Generator v0.1 (spec sections 2-6): config + seed -> layout file.
// Pure C#: one System.Random(seed) drives every choice; no clock, no UnityEngine.Random.
// Tile (i, j): i along +X (east), j along +Z (north). Directions N=+Z, E=+X, S=-Z, W=-X.
// Rotation r (0/90/180/270) turns a piece clockwise seen from above, so N -> E -> S -> W.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

[Serializable] public class GeneratorConfig
{
    public int seed = 42;
    public string name = "city";  // terrain name: layout files are <name>_<seed>.json
    public string manifest = "kenney_city_v1";
    public string layout = "grid";
    public float[] arena_size_m = { 9.71f, 7.16f };
    public float[] arena_origin = { 0f, 0f, 0f };
    public float margin_m = 0.3f, tile_m = 1.0f, min_corridor_m = 1.0f;
    public int block_tiles = 2;
    public float road_drop = 0.2f, building_density = 0.7f, prop_density = 0.2f;
    public int road_obstacles = 2;
    public int[] start_tile = new int[0], goal_tile = new int[0];
    public int max_tries = 20;
    public float min_path_fraction = 0.5f;  // drivable path >= this x grid width (placeholder rule)
}

[Serializable] public class ManifestEntry
{
    public string id, prefab, category, collider, front, source;
    public int[] footprint; public float height, weight = 1f; public string[] connects;
}

public class GeneratedMap
{
    public GeneratorConfig config; public ManifestEntry[] manifest;
    public int W, H, tries; public bool valid; public string failure = "";
    public List<LayoutObject> objects = new List<LayoutObject>();
    public int[] start, goal; public int pathTiles; public bool pathFound;
    public List<string> checkResults = new List<string>();
    public string json, hash;
}

public static class MapGenerator
{
    public const string Version = "0.1";
    static readonly string[] Dirs = { "N", "E", "S", "W" };
    static readonly int[] DI = { 0, 1, 0, -1 }, DJ = { 1, 0, -1, 0 };

    public static int GridCount(float arena, float margin, float tile) => Mathf.FloorToInt((arena - 2 * margin) / tile + 1e-4f);

    // Directions a piece's connects point to after turning it by `rot` degrees.
    public static HashSet<int> Rotated(string[] connects, int rot) =>
        new HashSet<int>(connects.Select(c => (Array.IndexOf(Dirs, c) + rot / 90) % 4));

    public static GeneratedMap Generate(GeneratorConfig c, ManifestEntry[] manifest)
    {
        var rng = new System.Random(c.seed);
        var map = new GeneratedMap { config = c, manifest = manifest };
        map.W = GridCount(c.arena_size_m[0], c.margin_m, c.tile_m);
        map.H = GridCount(c.arena_size_m[1], c.margin_m, c.tile_m);
        for (int t = 1; t <= c.max_tries; t++)
        {
            map.tries = t;
            map.objects.Clear();
            map.checkResults.Clear();
            map.failure = "";
            bool built = c.name == "warehouse" ? WarehouseGenerator.TryOnce(c, manifest, rng, map) : TryOnce(c, manifest, rng, map);
            if (built && RunChecks(map)) { map.valid = true; break; }
        }
        map.json = ToJson(map);
        map.hash = Sha256(map.json);
        return map;
    }

    static bool TryOnce(GeneratorConfig c, ManifestEntry[] manifest, System.Random rng, GeneratedMap map)
    {
        int W = map.W, H = map.H, step = c.block_tiles + 1;
        var road = new bool[W, H];
        for (int i = 0; i < W; i++) for (int j = 0; j < H; j++) road[i, j] = i % step == 0 || j % step == 0;

        // Road variety: drop whole segments between crossings, keeping every road tile connected.
        foreach (var seg in Segments(W, H, step))
        {
            if (rng.NextDouble() >= c.road_drop) continue;
            foreach (var (i, j) in seg) road[i, j] = false;
            if (!Connected(road, W, H)) foreach (var (i, j) in seg) road[i, j] = true;
        }
        // Dead ends (one neighbour) and lone tiles are removed, repeatedly.
        for (bool changed = true; changed;)
        {
            changed = false;
            for (int i = 0; i < W; i++) for (int j = 0; j < H; j++)
                if (road[i, j] && Neighbours(road, W, H, i, j).Count <= 1) { road[i, j] = false; changed = true; }
        }
        int roadCount = 0;
        foreach (var r in road) if (r) roadCount++;
        if (roadCount < 2 || !Connected(road, W, H)) { map.failure = "road network too small"; return false; }

        var byCat = manifest.GroupBy(m => m.category).ToDictionary(g => g.Key, g => g.ToArray());
        var roadRot = new Dictionary<(int, int), (ManifestEntry, int)>();
        // Road pieces: the neighbour pattern picks the piece; turn it until its connects match.
        for (int j = 0; j < H; j++) for (int i = 0; i < W; i++)
        {
            if (!road[i, j]) continue;
            var want = new HashSet<int>(Neighbours(road, W, H, i, j));
            var options = new List<(ManifestEntry, int)>();
            foreach (var e in byCat["road"])
                foreach (int rot in new[] { 0, 90, 180, 270 })
                    if (e.connects != null && Rotated(e.connects, rot).SetEquals(want)) options.Add((e, rot));
            if (options.Count == 0) { map.failure = $"no road piece for tile {i},{j}"; return false; }
            // Weighted pick of piece; for symmetric pieces every matching turn is equally likely.
            var pieces = options.Select(o => o.Item1).Distinct().ToList();
            var piece = Pick(rng, pieces, e => e.weight);
            var turns = options.Where(o => o.Item1 == piece).Select(o => o.Item2).ToList();
            int r0 = turns[rng.Next(turns.Count)];
            roadRot[(i, j)] = (piece, r0);
            Add(map, piece.id, i, j, r0);
        }

        // Lots: road-side tiles get a building with chance building_density, facing a road; the
        // rest get a prop with chance prop_density, else grass or pavement.
        for (int j = 0; j < H; j++) for (int i = 0; i < W; i++)
        {
            if (road[i, j]) continue;
            var roadSides = Neighbours(road, W, H, i, j);
            if (roadSides.Count > 0 && rng.NextDouble() < c.building_density)
            {
                var b = Pick(rng, byCat["building"], e => e.weight);
                int face = roadSides[rng.Next(roadSides.Count)];
                int rot = b.front == "any" || string.IsNullOrEmpty(b.front)
                    ? 90 * rng.Next(4) : (((face - Array.IndexOf(Dirs, b.front)) % 4 + 4) % 4) * 90;
                Add(map, b.id, i, j, rot);
            }
            else if (rng.NextDouble() < c.prop_density)
                Add(map, Pick(rng, byCat["prop"], e => e.weight).id, i, j, 90 * rng.Next(4));
            else
                Add(map, Pick(rng, byCat["ground"], e => e.weight).id, i, j, 0);
        }

        // Start and goal: given, or two road tiles as far apart (along the roads) as possible.
        var roadTiles = ChooseEndpoints(map, road, rng);

        // Road obstacles: barriers on straight road tiles, one at a time, each kept only if the
        // path check still passes. A barrier shares its tile with the road piece under it.
        var blocked = new HashSet<(int, int)>();
        var barrier = byCat.ContainsKey("road_obstacle") ? byCat["road_obstacle"] : new ManifestEntry[0];
        var straights = roadTiles.Where(t => IsStraight(roadRot[t].Item1)).ToList();
        for (int k = 0; k < c.road_obstacles && barrier.Length > 0; k++)
        {
            var candidates = straights.Where(t => !blocked.Contains(t) && !Same(t, map.start) && !Same(t, map.goal)).ToList();
            if (candidates.Count == 0) break;
            var t = candidates[rng.Next(candidates.Count)];
            blocked.Add(t);
            if (PathLength(road, blocked, W, H, map.start, map.goal) < MinPath(map)) { blocked.Remove(t); continue; }
            Add(map, Pick(rng, barrier, e => e.weight).id, t.Item1, t.Item2, roadRot[t].Item2);
        }
        map.pathTiles = PathLength(road, blocked, W, H, map.start, map.goal);
        map.pathFound = map.pathTiles >= 0;
        return true;
    }

    internal static List<(int, int)> ChooseEndpoints(GeneratedMap map, bool[,] road, System.Random rng)
    {
        var c = map.config;
        int W = map.W, H = map.H;
        var roadTiles = new List<(int, int)>();
        for (int j = 0; j < H; j++) for (int i = 0; i < W; i++) if (road[i, j]) roadTiles.Add((i, j));
        if (c.start_tile != null && c.start_tile.Length == 2 && c.goal_tile != null && c.goal_tile.Length == 2)
        {
            map.start = c.start_tile; map.goal = c.goal_tile;
        }
        else
        {
            int best = -1;
            var far = new List<((int, int), (int, int))>();
            foreach (var a in roadTiles)
            {
                var d = Bfs(road, null, W, H, a);
                foreach (var kv in d)
                {
                    if (kv.Value > best) { best = kv.Value; far.Clear(); }
                    if (kv.Value == best && CompareTile(a, kv.Key) < 0) far.Add((a, kv.Key));
                }
            }
            var pair = far[rng.Next(far.Count)];
            map.start = new[] { pair.Item1.Item1, pair.Item1.Item2 };
            map.goal = new[] { pair.Item2.Item1, pair.Item2.Item2 };
        }

        return roadTiles;
    }

    // A straight piece connects two opposite edges.
    static bool IsStraight(ManifestEntry e) =>
        e.connects != null && e.connects.Length == 2 && (Array.IndexOf(Dirs, e.connects[0]) + 2) % 4 == Array.IndexOf(Dirs, e.connects[1]);

    internal static int MinPath(GeneratedMap map) => Mathf.CeilToInt(map.config.min_path_fraction * map.W);
    internal static bool Same((int, int) t, int[] a) => a != null && t.Item1 == a[0] && t.Item2 == a[1];
    static int CompareTile((int, int) a, (int, int) b) => a.Item2 != b.Item2 ? a.Item2.CompareTo(b.Item2) : a.Item1.CompareTo(b.Item1);

    // The first four checks of spec section 6, run on every try.
    static bool RunChecks(GeneratedMap map)
    {
        var c = map.config;
        bool ok = true;
        void Check(bool pass, string name, string detail)
        {
            map.checkResults.Add($"{(pass ? "ok" : "FAIL")} {name}: {detail}");
            if (!pass) { ok = false; if (map.failure == "") map.failure = name; }
        }
        // One object per tile (a road barrier may share its road tile).
        var counts = new Dictionary<(int, int), int>();
        var cat = map.manifest.ToDictionary(m => m.id, m => m.category);
        foreach (var o in map.objects)
        {
            if (cat[o.asset] == "road_obstacle") continue;
            var t = (o.tile[0], o.tile[1]);
            counts[t] = counts.TryGetValue(t, out int n) ? n + 1 : 1;
        }
        int over = counts.Values.Count(n => n > 1);
        Check(over == 0 && counts.Count == map.W * map.H, "one object per tile", $"{counts.Count} of {map.W * map.H} tiles filled, {over} with more than one");
        // Inside the arena minus the margin.
        float x0 = c.arena_origin[0] + c.margin_m, x1 = c.arena_origin[0] + c.arena_size_m[0] - c.margin_m;
        float z0 = c.arena_origin[2] + c.margin_m, z1 = c.arena_origin[2] + c.arena_size_m[1] - c.margin_m;
        int outside = map.objects.Count(o => o.pos[0] - c.tile_m / 2 < x0 - 1e-4f || o.pos[0] + c.tile_m / 2 > x1 + 1e-4f ||
                                             o.pos[2] - c.tile_m / 2 < z0 - 1e-4f || o.pos[2] + c.tile_m / 2 > z1 + 1e-4f);
        Check(outside == 0, "inside the arena", $"{outside} tiles outside [{x0:F2}, {x1:F2}] x [{z0:F2}, {z1:F2}]");
        // Drivable path over road tiles, at least min_path_fraction of the grid width.
        Check(map.pathFound && map.pathTiles >= MinPath(map), "drivable path", $"start {map.start?[0]},{map.start?[1]} goal {map.goal?[0]},{map.goal?[1]}: {map.pathTiles} tiles (need >= {MinPath(map)})");
        Check(c.tile_m >= c.min_corridor_m, "corridor width", $"tile_m {c.tile_m} >= min_corridor_m {c.min_corridor_m}");
        if (c.name == "warehouse") Check(WarehouseGenerator.CheckLayout(map), "warehouse layout", c.layout);
        return ok;
    }

    internal static void Add(GeneratedMap map, string asset, int i, int j, int rot)
    {
        var c = map.config;
        map.objects.Add(new LayoutObject
        {
            uid = map.objects.Count + 1, asset = asset, tile = new[] { i, j }, rot = ((rot % 360) + 360) % 360,
            pos = new[] { R(c.arena_origin[0] + c.margin_m + (i + 0.5f) * c.tile_m), R(c.arena_origin[1]), R(c.arena_origin[2] + c.margin_m + (j + 0.5f) * c.tile_m) }
        });
    }

    static float R(float v) => (float)Math.Round(v, 4);

    internal static T Pick<T>(System.Random rng, IList<T> items, Func<T, float> weight)
    {
        double total = items.Sum(x => (double)weight(x));
        double r = rng.NextDouble() * total;
        foreach (var x in items) { r -= weight(x); if (r < 0) return x; }
        return items[items.Count - 1];
    }

    static List<int> Neighbours(bool[,] road, int W, int H, int i, int j)
    {
        var n = new List<int>();
        for (int d = 0; d < 4; d++)
        {
            int a = i + DI[d], b = j + DJ[d];
            if (a >= 0 && a < W && b >= 0 && b < H && road[a, b]) n.Add(d);
        }
        return n;
    }

    // Runs of road tiles between two crossings (and from the last crossing to the edge).
    static List<List<(int, int)>> Segments(int W, int H, int step)
    {
        var segs = new List<List<(int, int)>>();
        for (int j = 0; j < H; j += step)  // along rows
        {
            var cur = new List<(int, int)>();
            for (int i = 0; i < W; i++)
            {
                if (i % step == 0) { if (cur.Count > 0) segs.Add(cur); cur = new List<(int, int)>(); }
                else cur.Add((i, j));
            }
            if (cur.Count > 0) segs.Add(cur);
        }
        for (int i = 0; i < W; i += step)  // along columns
        {
            var cur = new List<(int, int)>();
            for (int j = 0; j < H; j++)
            {
                if (j % step == 0) { if (cur.Count > 0) segs.Add(cur); cur = new List<(int, int)>(); }
                else cur.Add((i, j));
            }
            if (cur.Count > 0) segs.Add(cur);
        }
        return segs;
    }

    static Dictionary<(int, int), int> Bfs(bool[,] road, HashSet<(int, int)> blocked, int W, int H, (int, int) from)
    {
        var dist = new Dictionary<(int, int), int> { [from] = 0 };
        var q = new Queue<(int, int)>();
        q.Enqueue(from);
        while (q.Count > 0)
        {
            var (i, j) = q.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                var n = (i + DI[d], j + DJ[d]);
                if (n.Item1 < 0 || n.Item1 >= W || n.Item2 < 0 || n.Item2 >= H || !road[n.Item1, n.Item2]) continue;
                if (blocked != null && blocked.Contains(n)) continue;
                if (dist.ContainsKey(n)) continue;
                dist[n] = dist[(i, j)] + 1;
                q.Enqueue(n);
            }
        }
        return dist;
    }

    internal static bool Connected(bool[,] road, int W, int H)
    {
        (int, int)? first = null; int count = 0;
        for (int i = 0; i < W; i++) for (int j = 0; j < H; j++) if (road[i, j]) { count++; first ??= (i, j); }
        return first == null || Bfs(road, null, W, H, first.Value).Count == count;
    }

    // Steps along the road from start to goal, or -1 if there is no path.
    public static int PathLength(bool[,] road, HashSet<(int, int)> blocked, int W, int H, int[] s, int[] g)
    {
        if (s == null || g == null || !road[s[0], s[1]] || !road[g[0], g[1]]) return -1;
        var d = Bfs(road, blocked, W, H, (s[0], s[1]));
        return d.TryGetValue((g[0], g[1]), out int n) ? n : -1;
    }

    // Layout file of spec section 5, written by hand so numbers and field order never vary.
    public static string ToJson(GeneratedMap m)
    {
        var ci = CultureInfo.InvariantCulture;
        string F(float v) => Math.Round(v, 4).ToString("0.0###", ci);
        string Arr(float[] a) => "[" + string.Join(", ", a.Select(F)) + "]";
        string IArr(int[] a) => a == null ? "[]" : "[" + string.Join(", ", a) + "]";
        var c = m.config;
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"generator_version\": \"{Version}\",\n");
        sb.Append($"  \"manifest\": \"{c.manifest}\",\n");
        sb.Append($"  \"seed\": {c.seed},\n");
        string layoutField = c.name == "warehouse" ? $"\"layout\": \"{c.layout}\", " : "";
        sb.Append($"  \"config\": {{{layoutField}\"tile_m\": {F(c.tile_m)}, \"margin_m\": {F(c.margin_m)}, \"block_tiles\": {c.block_tiles}, " +
                  $"\"building_density\": {F(c.building_density)}, \"prop_density\": {F(c.prop_density)}, \"road_drop\": {F(c.road_drop)}, " +
                  $"\"road_obstacles\": {c.road_obstacles}, \"min_corridor_m\": {F(c.min_corridor_m)}, " +
                  $"\"arena_size_m\": {Arr(c.arena_size_m)}}},\n");
        sb.Append($"  \"arena_origin\": {Arr(c.arena_origin)},\n");
        sb.Append($"  \"grid\": [{m.W}, {m.H}],\n");
        sb.Append($"  \"start_tile\": {IArr(m.start)},\n");
        sb.Append($"  \"goal_tile\": {IArr(m.goal)},\n");
        sb.Append("  \"objects\": [\n");
        sb.Append(string.Join(",\n", m.objects.Select(o =>
            $"    {{\"uid\": {o.uid}, \"asset\": \"{o.asset}\", \"tile\": [{o.tile[0]}, {o.tile[1]}], \"rot\": {o.rot}, \"pos\": {Arr(o.pos)}}}")));
        sb.Append("\n  ],\n");
        sb.Append($"  \"checks\": {{\"path_found\": {(m.pathFound ? "true" : "false")}, \"path_tiles\": {m.pathTiles}, \"tries\": {m.tries}, \"valid\": {(m.valid ? "true" : "false")}}}\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    public static string Sha256(string text)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
    }
}
