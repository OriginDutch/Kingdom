using System;
using System.Collections.Generic;

namespace Kingdom.Core.World;

/// <summary>
/// Builds a <see cref="WorldMap"/> from a <see cref="Seed"/>. Deterministic: same seed + settings = same world.
///
/// Pipeline:
///  1. Island heightmap (shape noise x hill noise, forced ocean border)
///  2. Ocean = below sea level and connected to the border
///  3. Coast: beach ring + gentle ramp up from the sea
///  4. Priority-flood drainage: every land cell drains to the sea; deep depressions become lakes
///  5. Flow accumulation: cells with a large upstream area become rivers
///  6. Fresh-water distance, forests, resource clusters, village suitability
/// </summary>
public static class WorldGenerator
{
    // Independent random streams, so changing one feature doesn't reshuffle the others.
    private const ulong StreamIsland = 1, StreamHills = 2, StreamForest = 3, StreamResources = 4;

    private static readonly int[] Dx8 = { 1, -1, 0, 0, 1, 1, -1, -1 };
    private static readonly int[] Dz8 = { 0, 0, 1, -1, 1, -1, 1, -1 };

    public static WorldMap Generate(Seed seed, WorldSettings? settings = null)
    {
        var s = settings ?? WorldSettings.Default;
        int n = s.Size;
        int count = n * n;

        var height = new double[count];
        BuildIslandHeights(seed, s, height);

        var terrain = new TerrainType[count];
        var isOcean = FloodOcean(n, height);
        KeepLargestLandmass(n, height, isOcean);

        // True (Euclidean) distance to the sea in cells, used for the beach and the coastal ramp.
        var coastDist = DistanceTransform(n, isOcean);
        ApplyCoastRamp(s, height, isOcean, coastDist);

        // Drainage and lakes.
        var filled = new double[count];
        var parent = new int[count];
        var order = PriorityFlood(n, height, isOcean, filled, parent);
        var lakeLevel = FindLakes(n, s, height, filled, isOcean, out var isLake);
        for (int i = 0; i < count; i++)
        {
            // Small puddles are filled in, so the land drains cleanly.
            if (!isOcean[i] && !isLake[i] && filled[i] > height[i]) height[i] = filled[i];
        }

        // Rivers from upstream area.
        var waterLevel = new float[count];
        Array.Fill(waterLevel, float.NaN);
        var isRiver = BuildRivers(n, s, height, isOcean, isLake, parent, order, waterLevel);

        // Terrain classification.
        double landCells = 0;
        for (int i = 0; i < count; i++)
        {
            if (isOcean[i]) { terrain[i] = TerrainType.Ocean; waterLevel[i] = 0f; }
            else if (isLake[i]) { terrain[i] = TerrainType.Lake; waterLevel[i] = (float)lakeLevel[i]; }
            else if (isRiver[i]) terrain[i] = TerrainType.River;
            else if (coastDist[i] <= s.BeachWidthCells) terrain[i] = TerrainType.Beach;
            else terrain[i] = TerrainType.Land;

            if (terrain[i] is TerrainType.Beach or TerrainType.Land or TerrainType.River) landCells++;
        }
        double landAreaKm2 = landCells * s.CellSize * s.CellSize / 1_000_000.0;

        var heightF = new float[count];
        for (int i = 0; i < count; i++) heightF[i] = (float)height[i];

        var freshSource = new bool[count];
        for (int i = 0; i < count; i++) freshSource[i] = terrain[i] is TerrainType.Lake or TerrainType.River;
        var freshCells = DistanceTransform(n, freshSource);
        var freshDist = new float[count];
        for (int i = 0; i < count; i++) freshDist[i] = (float)(freshCells[i] * s.CellSize);

        var forestNoise = BuildForest(seed, s, terrain, out var forest);
        var resources = PlaceResources(seed, s, heightF, terrain, forest, forestNoise, landAreaKm2);
        var suitable = BuildSuitability(n, s, heightF, terrain, freshDist);

        return new WorldMap(seed, s, heightF, waterLevel, terrain, forest, freshDist, suitable, resources, landAreaKm2);
    }

    // ---------------------------------------------------------------- 1. heights

    private static void BuildIslandHeights(Seed seed, WorldSettings s, double[] height)
    {
        int n = s.Size;
        var islandNoise = new Noise(Rng.For(seed.Value, StreamIsland));
        var hillNoise = new Noise(Rng.For(seed.Value, StreamHills));
        double borderCells = s.BorderOceanMeters / s.CellSize;

        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            double wx = x * s.CellSize, wz = z * s.CellSize;
            double nx = x / (n - 1.0) * 2 - 1, nz = z / (n - 1.0) * 2 - 1;
            double d = Math.Sqrt(nx * nx + nz * nz);

            // Ragged coastline: distance from centre pushed in/out by low-frequency noise.
            double shape = islandNoise.Fbm(wx / 3000.0, wz / 3000.0, 4);
            double mask = 1 - SmoothStep(0.58, 0.97, d + 0.25 * shape);

            // Light hills: more lowland than highland.
            double hills = (hillNoise.Fbm(wx / 2200.0, wz / 2200.0, 5) + 1) * 0.5;
            hills = Math.Pow(Math.Clamp(hills, 0, 1), 1.6);
            double landH = s.LandBaseHeight + s.HillHeight * hills;

            double h = s.SeaFloorDepth + (landH - s.SeaFloorDepth) * mask;

            double edge = Math.Min(Math.Min(x, z), Math.Min(n - 1 - x, n - 1 - z));
            if (edge < borderCells) h = Math.Min(h, -20);

            height[z * n + x] = h;
        }
    }

    // ---------------------------------------------------------------- 2. ocean

    private static bool[] FloodOcean(int n, double[] height)
    {
        var ocean = new bool[n * n];
        var queue = new Queue<int>();
        for (int i = 0; i < n; i++)
        {
            Seed(i, 0); Seed(i, n - 1); Seed(0, i); Seed(n - 1, i);
        }
        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            int cx = c % n, cz = c / n;
            for (int k = 0; k < 4; k++)
            {
                int x = cx + Dx8[k], z = cz + Dz8[k];
                if (x < 0 || z < 0 || x >= n || z >= n) continue;
                int i = z * n + x;
                if (!ocean[i] && height[i] < 0) { ocean[i] = true; queue.Enqueue(i); }
            }
        }
        return ocean;

        void Seed(int x, int z)
        {
            int i = z * n + x;
            if (!ocean[i] && height[i] < 0) { ocean[i] = true; queue.Enqueue(i); }
        }
    }

    /// <summary>The noisy coast can leave small islets; keep only the main island (GDD: one island).</summary>
    private static void KeepLargestLandmass(int n, double[] height, bool[] isOcean)
    {
        int count = n * n;
        var label = new int[count];
        var sizes = new List<int> { 0 };
        var stack = new Stack<int>();
        for (int i = 0; i < count; i++)
        {
            if (isOcean[i] || label[i] != 0) continue;
            int id = sizes.Count, size = 0;
            label[i] = id;
            stack.Push(i);
            while (stack.Count > 0)
            {
                int c = stack.Pop();
                size++;
                int cx = c % n, cz = c / n;
                for (int k = 0; k < 4; k++)
                {
                    int x = cx + Dx8[k], z = cz + Dz8[k];
                    if (x < 0 || z < 0 || x >= n || z >= n) continue;
                    int j = z * n + x;
                    if (!isOcean[j] && label[j] == 0) { label[j] = id; stack.Push(j); }
                }
            }
            sizes.Add(size);
        }
        int keep = 0;
        for (int id = 1; id < sizes.Count; id++)
            if (sizes[id] > sizes[keep]) keep = id;
        for (int i = 0; i < count; i++)
        {
            if (isOcean[i] || label[i] == keep) continue;
            isOcean[i] = true;
            height[i] = Math.Min(height[i], -3);
        }
    }

    // ---------------------------------------------------------------- 3. coast

    private static void ApplyCoastRamp(WorldSettings s, double[] height, bool[] isOcean, double[] coastDist)
    {
        for (int i = 0; i < height.Length; i++)
        {
            if (isOcean[i]) continue;
            double d = coastDist[i];
            double max = d <= s.BeachWidthCells
                ? 0.4 + 0.45 * d
                : 0.4 + 0.45 * s.BeachWidthCells + (d - s.BeachWidthCells) * 1.2;
            if (height[i] > max) height[i] = max;
        }
    }

    // ---------------------------------------------------------------- 4. drainage + lakes

    /// <summary>
    /// Priority-flood (Barnes et al.): fills depressions so every land cell has a downhill path to the ocean.
    /// parent[i] = the cell water flows to from i (an ocean cell at the coast). Returns cells in processing order.
    /// </summary>
    private static int[] PriorityFlood(int n, double[] height, bool[] isOcean, double[] filled, int[] parent)
    {
        const double eps = 1e-4;
        int count = n * n;
        var closed = new bool[count];
        var order = new List<int>(count);
        // (height, index) priority: equal heights are processed in a fixed order on every machine.
        var pq = new PriorityQueue<int, (double, int)>();

        for (int i = 0; i < count; i++)
        {
            parent[i] = -1;
            if (isOcean[i]) { closed[i] = true; filled[i] = height[i]; }
        }
        // Seed with land cells touching the ocean.
        for (int i = 0; i < count; i++)
        {
            if (!isOcean[i]) continue;
            int cx = i % n, cz = i / n;
            for (int k = 0; k < 8; k++)
            {
                int x = cx + Dx8[k], z = cz + Dz8[k];
                if (x < 0 || z < 0 || x >= n || z >= n) continue;
                int j = z * n + x;
                if (closed[j]) continue;
                closed[j] = true;
                filled[j] = Math.Max(height[j], eps);
                parent[j] = i;
                pq.Enqueue(j, (filled[j], j));
            }
        }
        while (pq.TryDequeue(out int c, out _))
        {
            order.Add(c);
            int cx = c % n, cz = c / n;
            for (int k = 0; k < 8; k++)
            {
                int x = cx + Dx8[k], z = cz + Dz8[k];
                if (x < 0 || z < 0 || x >= n || z >= n) continue;
                int j = z * n + x;
                if (closed[j]) continue;
                closed[j] = true;
                filled[j] = Math.Max(height[j], filled[c] + eps);
                parent[j] = c;
                pq.Enqueue(j, (filled[j], j));
            }
        }
        return order.ToArray();
    }

    /// <summary>
    /// Every depression the flood had to fill is one connected component. Large, deep enough components become
    /// a lake as a whole, with one water level per lake, so there is no dry rim at water level.
    /// Returns the water level per cell (NaN outside lakes).
    /// </summary>
    private static double[] FindLakes(int n, WorldSettings s, double[] height, double[] filled, bool[] isOcean, out bool[] lake)
    {
        // Filled cells drift up by eps per step; anything above this counts as "under water".
        const double minFill = 0.01;
        int count = n * n;
        lake = new bool[count];
        var level = new double[count];
        Array.Fill(level, double.NaN);
        var seen = new bool[count];
        var component = new List<int>();
        var stack = new Stack<int>();
        for (int i = 0; i < count; i++)
        {
            if (seen[i] || isOcean[i] || filled[i] - height[i] <= minFill) continue;
            component.Clear();
            double maxDepth = 0, surface = double.MinValue;
            stack.Push(i);
            seen[i] = true;
            while (stack.Count > 0)
            {
                int c = stack.Pop();
                component.Add(c);
                maxDepth = Math.Max(maxDepth, filled[c] - height[c]);
                surface = Math.Max(surface, filled[c]);
                int cx = c % n, cz = c / n;
                for (int k = 0; k < 8; k++)
                {
                    int x = cx + Dx8[k], z = cz + Dz8[k];
                    if (x < 0 || z < 0 || x >= n || z >= n) continue;
                    int j = z * n + x;
                    if (!seen[j] && !isOcean[j] && filled[j] - height[j] > minFill) { seen[j] = true; stack.Push(j); }
                }
            }
            if (component.Count < s.LakeMinCells || maxDepth < s.LakeMinDepth) continue;
            foreach (int c in component)
            {
                lake[c] = true;
                level[c] = surface;
            }
        }
        return level;
    }

    // ---------------------------------------------------------------- 5. rivers

    private static bool[] BuildRivers(int n, WorldSettings s, double[] height, bool[] isOcean, bool[] isLake,
        int[] parent, int[] order, float[] waterLevel)
    {
        int count = n * n;
        var acc = new int[count];
        for (int i = 0; i < count; i++) acc[i] = isOcean[i] ? 0 : 1;

        // Upstream cells were processed later in the flood, so walk the order backwards.
        for (int k = order.Length - 1; k >= 0; k--)
        {
            int c = order[k];
            int p = parent[c];
            if (p >= 0 && !isOcean[p]) acc[p] += acc[c];
        }

        var river = new bool[count];
        var bed = new double[count];
        Array.Fill(bed, double.NaN);

        // Downstream first, so a wide stretch never raises a river cell that was already carved.
        foreach (int c in order)
        {
            if (isLake[c] || acc[c] < s.RiverMinCells) continue;
            float level = (float)(height[c] - 0.4);
            MarkRiver(c, level);

            // A diagonal step only touches at a corner: add an orthogonal cell so the river is 4-connected
            // (continuous water, and later units can't slip through diagonally).
            int pp = parent[c];
            if (pp >= 0)
            {
                int cx0 = c % n, cz0 = c / n, px0 = pp % n, pz0 = pp / n;
                int ddx = px0 - cx0, ddz = pz0 - cz0;
                if (ddx != 0 && ddz != 0)
                {
                    int a = cz0 * n + (cx0 + ddx), b = (cz0 + ddz) * n + cx0;
                    int bridge = height[a] <= height[b] ? a : b;
                    if (!isOcean[bridge] && !isLake[bridge] && !river[bridge])
                    {
                        float pLevel = river[pp] ? waterLevel[pp] : level;
                        // Same rule as other river cells: water 0.4 m below the cell's own ground.
                        MarkRiver(bridge, Math.Min((level + pLevel) * 0.5f, (float)(height[bridge] - 0.4)));
                    }
                }
            }

            if (acc[c] >= s.RiverWideCells)
            {
                int cx = c % n, cz = c / n;
                for (int k = 0; k < 4; k++)
                {
                    int x = cx + Dx8[k], z = cz + Dz8[k];
                    if (x < 0 || z < 0 || x >= n || z >= n) continue;
                    int j = z * n + x;
                    if (isOcean[j] || isLake[j] || river[j]) continue;
                    MarkRiver(j, level);
                }
            }
        }
        for (int i = 0; i < count; i++)
            if (river[i]) height[i] = bed[i];
        return river;

        void MarkRiver(int i, float level)
        {
            river[i] = true;
            waterLevel[i] = level;
            bed[i] = Math.Min(height[i], level - 1.0);
        }
    }

    // ---------------------------------------------------------------- 6. forest, resources, suitability

    private static float[] BuildForest(Seed seed, WorldSettings s, TerrainType[] terrain, out bool[] forest)
    {
        int n = s.Size;
        var noise = new Noise(Rng.For(seed.Value, StreamForest));
        var values = new float[n * n];
        forest = new bool[n * n];
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            int i = z * n + x;
            double v = noise.Fbm(x * s.CellSize / 700.0, z * s.CellSize / 700.0, 4);
            values[i] = (float)v;
            forest[i] = terrain[i] == TerrainType.Land && v > s.ForestThreshold;
        }
        return values;
    }

    private static List<ResourceCluster> PlaceResources(Seed seed, WorldSettings s, float[] height, TerrainType[] terrain,
        bool[] forest, float[] forestNoise, double landAreaKm2)
    {
        int n = s.Size;
        var result = new List<ResourceCluster>();

        // Height threshold for "hilly" land: stone prefers the top 35% of land heights.
        var landHeights = new List<float>();
        for (int i = 0; i < height.Length; i++)
            if (terrain[i] == TerrainType.Land) landHeights.Add(height[i]);
        landHeights.Sort();
        float hillCut = landHeights.Count > 0 ? landHeights[(int)(landHeights.Count * 0.65)] : 0;

        var stoneCandidates = new List<int>();
        var berryCandidates = new List<int>();
        var appleCandidates = new List<int>();
        for (int i = 0; i < height.Length; i++)
        {
            if (terrain[i] != TerrainType.Land) continue;
            if (height[i] >= hillCut && !forest[i]) stoneCandidates.Add(i);
            // Berries along forest edges: just below the forest threshold, or forest close to its edge.
            float f = forestNoise[i];
            if (f > s.ForestThreshold - 0.12 && f < s.ForestThreshold + 0.04) berryCandidates.Add(i);
            if (!forest[i]) appleCandidates.Add(i);
        }

        Place(ResourceType.Stone, stoneCandidates, s.StoneClustersPerKm2, minSpacing: 450, nodesMin: 4, nodesMax: 9, radius: 40, allowForest: false);
        Place(ResourceType.Berries, berryCandidates, s.BerryClustersPerKm2, minSpacing: 350, nodesMin: 6, nodesMax: 13, radius: 30, allowForest: true);
        Place(ResourceType.Apples, appleCandidates, s.AppleClustersPerKm2, minSpacing: 450, nodesMin: 4, nodesMax: 11, radius: 40, allowForest: false);
        return result;

        void Place(ResourceType type, List<int> candidates, double perKm2, double minSpacing, int nodesMin, int nodesMax, double radius, bool allowForest)
        {
            if (candidates.Count == 0) return;
            // One stream per type: tuning one resource doesn't reshuffle the others.
            var rng = Rng.For(seed.Value, StreamResources * 16 + (ulong)type);
            int target = (int)Math.Round(landAreaKm2 * perKm2);
            int placed = 0;
            for (int attempt = 0; attempt < target * 60 && placed < target; attempt++)
            {
                int c = candidates[rng.NextInt(0, candidates.Count)];
                float cx = (c % n) * s.CellSize, cz = (c / n) * s.CellSize;
                bool tooClose = false;
                foreach (var other in result)
                {
                    if (other.Type != type) continue;
                    float ddx = other.CenterX - cx, ddz = other.CenterZ - cz;
                    if (ddx * ddx + ddz * ddz < minSpacing * minSpacing) { tooClose = true; break; }
                }
                if (tooClose) continue;

                int want = rng.NextInt(nodesMin, nodesMax + 1);
                var nodes = new List<(float, float)>(want);
                for (int t = 0; t < want * 8 && nodes.Count < want; t++)
                {
                    double ang = rng.Range(0, Math.PI * 2);
                    double r = radius * Math.Sqrt(rng.NextDouble());
                    float px = cx + (float)(Math.Cos(ang) * r);
                    float pz = cz + (float)(Math.Sin(ang) * r);
                    int gx = (int)MathF.Round(px / s.CellSize), gz = (int)MathF.Round(pz / s.CellSize);
                    if (gx < 0 || gz < 0 || gx >= n || gz >= n) continue;
                    int gi = gz * n + gx;
                    if (terrain[gi] != TerrainType.Land) continue;
                    if (!allowForest && forest[gi]) continue;
                    nodes.Add((px, pz));
                }
                if (nodes.Count < nodesMin) continue;
                result.Add(new ResourceCluster(type, cx, cz, nodes));
                placed++;
            }
        }
    }

    private static bool[] BuildSuitability(int n, WorldSettings s, float[] height, TerrainType[] terrain, float[] freshDist)
    {
        var ok = new bool[n * n];
        int r = s.SiteFootprintCells;
        for (int z = r; z < n - r; z++)
        for (int x = r; x < n - r; x++)
        {
            int i = z * n + x;
            if (terrain[i] != TerrainType.Land) continue;
            if (freshDist[i] > s.MaxFreshWaterDistance) continue;

            // Slope at the centre.
            bool good = true;
            for (int k = 0; k < 8 && good; k++)
            {
                int j = (z + Dz8[k]) * n + (x + Dx8[k]);
                if (Math.Abs(height[j] - height[i]) > s.MaxSlope) good = false;
            }
            // Whole footprint (where the houses stand) must be plain land and fairly level.
            float lo = height[i], hi = height[i];
            for (int dz = -r; dz <= r && good; dz++)
            for (int dx = -r; dx <= r && good; dx++)
            {
                if (dx * dx + dz * dz > r * r) continue;
                int j = (z + dz) * n + (x + dx);
                if (terrain[j] != TerrainType.Land) { good = false; break; }
                lo = Math.Min(lo, height[j]);
                hi = Math.Max(hi, height[j]);
            }
            if (good && hi - lo > s.SiteMaxHeightSpread) good = false;
            ok[i] = good;
        }
        return ok;
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Exact Euclidean distance (in cells) from every cell to the nearest source cell
    /// (Felzenszwalb &amp; Huttenlocher). Cells far from any source get a large value.
    /// </summary>
    private static double[] DistanceTransform(int n, bool[] source)
    {
        const double inf = 1e12;
        int count = n * n;
        var g = new double[count];
        for (int i = 0; i < count; i++) g[i] = source[i] ? 0 : inf;

        var f = new double[n];
        var d = new double[n];
        var v = new int[n];
        var zb = new double[n + 1];

        // Columns, then rows (squared distances).
        for (int x = 0; x < n; x++)
        {
            for (int z = 0; z < n; z++) f[z] = g[z * n + x];
            Edt1D(f, d, v, zb, n);
            for (int z = 0; z < n; z++) g[z * n + x] = d[z];
        }
        for (int z = 0; z < n; z++)
        {
            for (int x = 0; x < n; x++) f[x] = g[z * n + x];
            Edt1D(f, d, v, zb, n);
            for (int x = 0; x < n; x++) g[z * n + x] = Math.Sqrt(d[x]);
        }
        return g;
    }

    private static void Edt1D(double[] f, double[] d, int[] v, double[] zb, int n)
    {
        int k = 0;
        v[0] = 0;
        zb[0] = double.NegativeInfinity;
        zb[1] = double.PositiveInfinity;
        for (int q = 1; q < n; q++)
        {
            double s = Intersect(f, q, v[k]);
            while (s <= zb[k])
            {
                k--;
                s = Intersect(f, q, v[k]);
            }
            k++;
            v[k] = q;
            zb[k] = s;
            zb[k + 1] = double.PositiveInfinity;
        }
        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (zb[k + 1] < q) k++;
            double dq = q - v[k];
            d[q] = dq * dq + f[v[k]];
        }

        static double Intersect(double[] f, int q, int p) =>
            ((f[q] + (double)q * q) - (f[p] + (double)p * p)) / (2.0 * q - 2.0 * p);
    }

    private static double SmoothStep(double edge0, double edge1, double x)
    {
        double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
