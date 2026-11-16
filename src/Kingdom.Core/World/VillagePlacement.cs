using System;
using System.Collections.Generic;

namespace Kingdom.Core.World;

/// <summary>A founded village on the map.</summary>
public sealed record VillageSite(int OwnerId, string Name, int CellX, int CellZ);

/// <summary>
/// Temporary village placement rule for milestone 1 (GDD 2026-11-16):
/// flat enough, on land but not beach, fresh water within ~300 m, at least ~1.5 km from another village.
/// </summary>
public static class VillagePlacement
{
    public const int PlayerId = 0;

    /// <summary>Checks a cell. On failure, <paramref name="reason"/> explains why in one short sentence.</summary>
    public static bool CheckSite(WorldMap map, int x, int z, IEnumerable<VillageSite> existing, out string reason,
        int? ignoreOwnerId = null)
    {
        if (!map.InBounds(x, z)) { reason = "That's outside the map."; return false; }
        int i = map.Index(x, z);
        switch (map.Terrain[i])
        {
            case TerrainType.Ocean: reason = "That's the sea."; return false;
            case TerrainType.Lake: reason = "That's a lake."; return false;
            case TerrainType.River: reason = "That's a river."; return false;
            case TerrainType.Beach: reason = "Too close to the sea: villages can't be built on the beach."; return false;
        }
        if (!map.SiteSuitable[i])
        {
            reason = map.FreshWaterDistance[i] > map.Settings.MaxFreshWaterDistance
                ? $"No fresh water within {map.Settings.MaxFreshWaterDistance:0} m (river or lake)."
                : "Not enough flat, dry land here for a village (too steep, or water or beach too close).";
            return false;
        }
        double minDist = map.Settings.MinVillageDistance;
        foreach (var v in existing)
        {
            if (ignoreOwnerId.HasValue && v.OwnerId == ignoreOwnerId.Value) continue;
            double d = Distance(map, x, z, v.CellX, v.CellZ);
            if (d < minDist)
            {
                reason = $"Too close to {v.Name} ({d:0} m, minimum is {minDist:0} m).";
                return false;
            }
        }
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Picks the best site for a bot: suitable, far enough from other villages, with food, stone, wood and water nearby.
    /// Deterministic for a given map and set of villages. Returns null if no site is left.
    /// </summary>
    public static VillageSite? ChooseBotSite(WorldMap map, IReadOnlyList<VillageSite> existing, int botId, string name)
    {
        const int step = 5; // evaluate every 50 m
        double bestScore = double.NegativeInfinity;
        int bestX = -1, bestZ = -1;
        double minDist = map.Settings.MinVillageDistance;

        for (int z = 1; z < map.Size - 1; z += step)
        for (int x = 1; x < map.Size - 1; x += step)
        {
            int i = map.Index(x, z);
            if (!map.SiteSuitable[i]) continue;
            bool farEnough = true;
            foreach (var v in existing)
            {
                if (Distance(map, x, z, v.CellX, v.CellZ) < minDist) { farEnough = false; break; }
            }
            if (!farEnough) continue;

            double score = ScoreSite(map, x, z);
            if (score > bestScore)
            {
                bestScore = score;
                bestX = x;
                bestZ = z;
            }
        }
        return bestX < 0 ? null : new VillageSite(botId, name, bestX, bestZ);
    }

    /// <summary>Higher is better. Food clusters weigh most, then forest (wood), stone and water distance.</summary>
    public static double ScoreSite(WorldMap map, int x, int z)
    {
        float wx = x * map.CellSize, wz = z * map.CellSize;
        int food = 0, stone = 0;
        foreach (var c in map.Resources)
        {
            float dx = c.CenterX - wx, dz = c.CenterZ - wz;
            float d2 = dx * dx + dz * dz;
            if (c.Type == ResourceType.Stone) { if (d2 < 1200f * 1200f) stone++; }
            else if (d2 < 1000f * 1000f) food++;
        }

        // Share of forest cells within ~400 m, sampled every 40 m.
        int forest = 0, samples = 0;
        for (int dz = -40; dz <= 40; dz += 4)
        for (int dx = -40; dx <= 40; dx += 4)
        {
            if (dx * dx + dz * dz > 40 * 40) continue;
            int sx = x + dx, sz = z + dz;
            if (!map.InBounds(sx, sz)) continue;
            samples++;
            if (map.Forest[map.Index(sx, sz)]) forest++;
        }
        double forestShare = samples == 0 ? 0 : (double)forest / samples;
        double waterMeters = map.FreshWaterDistance[map.Index(x, z)];

        return 3.0 * Math.Min(food, 4) + 2.0 * Math.Min(stone, 2) + 6.0 * forestShare - waterMeters / 150.0;
    }

    public static double Distance(WorldMap map, int ax, int az, int bx, int bz)
    {
        double dx = (ax - bx) * (double)map.CellSize, dz = (az - bz) * (double)map.CellSize;
        return Math.Sqrt(dx * dx + dz * dz);
    }
}
