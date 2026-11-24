using System;
using System.Collections.Generic;
using System.Linq;

public static class WarehouseGenerator
{
    public static readonly string[] Layouts = { "storage_aisles", "blocked_aisles", "receiving_staging" };

    public static bool CheckLayout(GeneratedMap map)
    {
        var c = map.config;
        var categories = map.manifest.ToDictionary(e => e.id, e => e.category);
        var lanes = new bool[map.W, map.H];
        var blocked = new HashSet<(int, int)>();
        foreach (var o in map.objects)
        {
            if (categories[o.asset] == "road") lanes[o.tile[0], o.tile[1]] = true;
            if (categories[o.asset] == "road_obstacle") blocked.Add((o.tile[0], o.tile[1]));
        }
        if (c.layout == "receiving_staging")
        {
            for (int j = 0; j < map.H; j++) for (int i = 0; i < map.W; i++)
                if ((i == 0 || i == map.W - 1 || j == 0 || j == map.H - 1 || i == map.W / 2 || j == map.H / 2) && !lanes[i, j]) return false;
            return map.objects.Any(o => categories[o.asset] != "road" && categories[o.asset] != "road_obstacle");
        }
        for (int j = 0; j < map.H; j++) for (int i = 0; i < map.W; i++)
            if (lanes[i, j] != (j == 0 || j == map.H - 1 || i == map.W - 1 || i % (c.block_tiles + 1) == 0)) return false;
        return c.layout == "storage_aisles" || c.layout == "blocked_aisles" && blocked.Count > 0
            && map.pathTiles > MapGenerator.PathLength(lanes, null, map.W, map.H, map.start, map.goal);
    }

    public static bool TryOnce(GeneratorConfig c, ManifestEntry[] manifest, Random rng, GeneratedMap map)
    {
        int W = map.W, H = map.H;
        if (W < 5 || H < 4 || c.block_tiles < 1 || !Layouts.Contains(c.layout))
        { map.failure = "warehouse needs a grid at least 5x4, positive row depth and a supported layout"; return false; }
        bool receiving = c.layout == "receiving_staging", blockedLayout = c.layout == "blocked_aisles";
        if (blockedLayout && c.road_obstacles < 1)
        { map.failure = "blocked aisles requires at least one barrier"; return false; }

        var lanes = new bool[W, H];
        for (int j = 0; j < H; j++) for (int i = 0; i < W; i++)
            lanes[i, j] = receiving || j == 0 || j == H - 1 || i == W - 1 || i % (c.block_tiles + 1) == 0;
        if (receiving)
        {
            // Four staging clusters leave the central cross and perimeter available for travel.
            int cx = W / 2, cz = H / 2;
            foreach (var (x0, x1, z0, z1) in new[] { (1, cx - 1, 1, cz - 1), (cx + 1, W - 2, 1, cz - 1),
                         (1, cx - 1, cz + 1, H - 2), (cx + 1, W - 2, cz + 1, H - 2) })
            {
                int width = Math.Min(c.block_tiles, x1 - x0 + 1), depth = Math.Min(c.block_tiles, z1 - z0 + 1);
                if (width <= 0 || depth <= 0) continue;
                int x = rng.Next(x0, x1 - width + 2), z = rng.Next(z0, z1 - depth + 2);
                for (int j = z; j < z + depth; j++) for (int i = x; i < x + width; i++) lanes[i, j] = false;
            }
        }
        if (!MapGenerator.Connected(lanes, W, H)) { map.failure = "warehouse lanes disconnected"; return false; }
        var categories = manifest.GroupBy(e => e.category).ToDictionary(g => g.Key, g => g.ToArray());
        for (int j = 0; j < H; j++) for (int i = 0; i < W; i++)
            if (lanes[i, j]) MapGenerator.Add(map, categories["road"][0].id, i, j, 0);
        for (int j = 0; j < H; j++) for (int i = 0; i < W; i++)
        {
            if (lanes[i, j]) continue;
            string category = rng.NextDouble() < c.building_density ? "building"
                : rng.NextDouble() < c.prop_density ? "prop" : "ground";
            var asset = MapGenerator.Pick(rng, categories[category], e => e.weight);
            MapGenerator.Add(map, asset.id, i, j, category == "ground" ? 0 : 90 * rng.Next(4));
        }

        var roadTiles = MapGenerator.ChooseEndpoints(map, lanes, rng);
        if (blockedLayout && (c.start_tile == null || c.start_tile.Length == 0) && (c.goal_tile == null || c.goal_tile.Length == 0))
        {
            var interior = Enumerable.Range(1, W - 2).Where(i => i % (c.block_tiles + 1) == 0).ToArray();
            if (interior.Length == 0) { map.failure = "blocked aisles needs an interior aisle"; return false; }
            int aisle = interior[rng.Next(interior.Length)];
            map.start = new[] { aisle, 0 }; map.goal = new[] { aisle, H - 1 };
        }
        int baseline = MapGenerator.PathLength(lanes, null, W, H, map.start, map.goal);
        var blocked = new HashSet<(int, int)>();
        var candidates = roadTiles.Where(t => t.Item1 > 0 && t.Item1 < W - 1 && t.Item2 > 0 && t.Item2 < H - 1
            && !MapGenerator.Same(t, map.start) && !MapGenerator.Same(t, map.goal)).ToList();
        int placed = 0;
        if (blockedLayout)
        {
            var forced = candidates.ToList();
            while (forced.Count > 0)
            {
                var t = forced[rng.Next(forced.Count)]; forced.Remove(t);
                blocked.Add(t);
                int length = MapGenerator.PathLength(lanes, blocked, W, H, map.start, map.goal);
                if (length >= MapGenerator.MinPath(map) && length > baseline)
                {
                    MapGenerator.Add(map, MapGenerator.Pick(rng, categories["road_obstacle"], e => e.weight).id, t.Item1, t.Item2, 0);
                    placed = 1; break;
                }
                blocked.Remove(t);
            }
            if (placed == 0) { map.failure = "could not place an aisle blocker with a valid detour"; return false; }
        }
        for (int k = placed; k < c.road_obstacles && candidates.Count > 0; k++)
        {
            var t = candidates[rng.Next(candidates.Count)]; candidates.Remove(t);
            if (!blocked.Add(t)) continue;
            int length = MapGenerator.PathLength(lanes, blocked, W, H, map.start, map.goal);
            if (length < MapGenerator.MinPath(map)) { blocked.Remove(t); continue; }
            MapGenerator.Add(map, MapGenerator.Pick(rng, categories["road_obstacle"], e => e.weight).id, t.Item1, t.Item2, receiving ? rng.Next(2) * 90 : 0);
        }
        map.pathTiles = MapGenerator.PathLength(lanes, blocked, W, H, map.start, map.goal);
        map.pathFound = map.pathTiles >= 0;
        return true;
    }
}
