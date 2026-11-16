using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Kingdom.Core;
using Kingdom.Core.World;

namespace Kingdom.Core.Tests;

/// <summary>
/// Minimal test runner without NuGet dependencies.
/// Add a test by adding a (name, action) entry to <see cref="Tests"/>.
/// </summary>
public static class Program
{
    private static readonly string[] TestSeeds = { "K7QX2M9A", "HELLO123", "Z" };
    /// <summary>Wider sweep for invariants that must hold for any seed.</summary>
    private static readonly string[] SweepSeeds = { "A", "B", "C", "4", "ROME", "QWERTY", "ASDF1234", "LONDON", "TEST9", "ZZZ999",
        "S10U593", "S20V550", "S118B500", "S12Q214", "S0K895", "S7T689", "S110B127", "OU402CV9" };
    private static readonly Dictionary<string, WorldMap> Maps = new();

    private static WorldMap Map(string seed)
    {
        if (!Maps.TryGetValue(seed, out var map))
        {
            map = WorldGenerator.Generate(Seed.Parse(seed));
            Maps[seed] = map;
        }
        return map;
    }

    private static readonly List<(string Name, Action Run)> Tests = new()
    {
        // --- GameInfo ---
        ("GameInfo.Version is a real MAJOR.MINOR.PATCH version", () =>
            Assert.True(
                Regex.IsMatch(GameInfo.Version, @"^\d+\.\d+\.\d+$") && GameInfo.Version != "0.0.0",
                $"unexpected version '{GameInfo.Version}'")),
        ("GameInfo.Title is set", () =>
            Assert.True(!string.IsNullOrWhiteSpace(GameInfo.Title), "Title is empty")),

        // --- Seeds ---
        ("Seed: letters and digits accepted, upper-cased, trimmed", () =>
        {
            Assert.True(Seed.TryParse("  ab12Cd ", out var s, out _), "valid seed rejected");
            Assert.Equal("AB12CD", s.Text);
        }),
        ("Seed: same text (any case) gives the same value", () =>
            Assert.Equal(Seed.Parse("kingdom1").Value, Seed.Parse("KINGDOM1").Value)),
        ("Seed: rejects empty, too long, and non-alphanumeric input", () =>
        {
            Assert.True(!Seed.TryParse("", out _, out _), "empty accepted");
            Assert.True(!Seed.TryParse("   ", out _, out _), "whitespace accepted");
            Assert.True(!Seed.TryParse(new string('A', Seed.MaxLength + 1), out _, out _), "too long accepted");
            Assert.True(!Seed.TryParse("AB-12", out _, out _), "dash accepted");
            Assert.True(!Seed.TryParse("ÄBC", out _, out _), "non-ASCII letter accepted");
        }),
        ("Seed: random seeds are 8 letters/digits", () =>
        {
            var rnd = new Random(42);
            for (int i = 0; i < 100; i++)
            {
                var s = Seed.NewRandom(rnd);
                Assert.True(Regex.IsMatch(s.Text, "^[A-Z0-9]{8}$"), $"bad random seed '{s.Text}'");
            }
        }),

        // --- World generation ---
        ("World: same seed generates an identical world", () =>
        {
            var a = Map("K7QX2M9A");
            var b = WorldGenerator.Generate(Seed.Parse("k7qx2m9a"));
            Assert.True(a.Height.SequenceEqual(b.Height), "heights differ");
            Assert.True(a.Terrain.SequenceEqual(b.Terrain), "terrain differs");
            Assert.True(a.Forest.SequenceEqual(b.Forest), "forest differs");
            Assert.True(a.WaterLevel.SequenceEqual(b.WaterLevel), "water levels differ");
            Assert.True(a.FreshWaterDistance.SequenceEqual(b.FreshWaterDistance), "fresh water distance differs");
            Assert.True(a.SiteSuitable.SequenceEqual(b.SiteSuitable), "site suitability differs");
            Assert.Equal(a.Resources.Count, b.Resources.Count);
            for (int i = 0; i < a.Resources.Count; i++)
                Assert.True(a.Resources[i].Nodes.SequenceEqual(b.Resources[i].Nodes), $"resource cluster {i} differs");
        }),
        ("World: different seeds generate different worlds", () =>
            Assert.True(!Map("K7QX2M9A").Height.SequenceEqual(Map("HELLO123").Height), "two seeds gave the same heights")),
        ("World: is 10 x 10 km", () =>
        {
            var m = Map("K7QX2M9A");
            Assert.Equal(1000, m.Size);
            Assert.Equal(10f, m.CellSize);
        }),
        ("World: the map border is ocean (island surrounded by sea)", () =>
        {
            foreach (var seed in TestSeeds)
            {
                var m = Map(seed);
                for (int i = 0; i < m.Size; i++)
                {
                    Assert.True(m.Terrain[m.Index(i, 0)] == TerrainType.Ocean, $"{seed}: top border not ocean");
                    Assert.True(m.Terrain[m.Index(i, m.Size - 1)] == TerrainType.Ocean, $"{seed}: bottom border not ocean");
                    Assert.True(m.Terrain[m.Index(0, i)] == TerrainType.Ocean, $"{seed}: left border not ocean");
                    Assert.True(m.Terrain[m.Index(m.Size - 1, i)] == TerrainType.Ocean, $"{seed}: right border not ocean");
                }
            }
        }),
        ("World: has a sensible amount of land (20-70 km2)", () =>
        {
            foreach (var seed in TestSeeds)
            {
                double a = Map(seed).LandAreaKm2;
                Assert.True(a > 20 && a < 70, $"{seed}: land area {a:0.0} km2");
            }
        }),
        ("World: outside cliff stretches, plain land never touches the ocean (beach ring)", () =>
        {
            foreach (var seed in TestSeeds)
            {
                var m = Map(seed);
                for (int z = 1; z < m.Size - 1; z++)
                for (int x = 1; x < m.Size - 1; x++)
                {
                    int li = m.Index(x, z);
                    if (m.Terrain[li] != TerrainType.Land || m.CliffCoast[li]) continue;
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        Assert.True(m.Terrain[m.Index(x + dx, z + dz)] != TerrainType.Ocean,
                            $"{seed}: non-cliff land at {x},{z} touches ocean");
                }
            }
        }),
        ("World: has rivers and lakes", () =>
        {
            foreach (var seed in TestSeeds)
            {
                var m = Map(seed);
                Assert.True(m.Terrain.Count(t => t == TerrainType.River) > 200, $"{seed}: almost no river cells");
                Assert.True(m.Terrain.Count(t => t == TerrainType.Lake) > 0, $"{seed}: no lakes");
            }
        }),
        ("World: water cells have a water level, others don't", () =>
        {
            var m = Map("HELLO123");
            for (int i = 0; i < m.Terrain.Length; i++)
            {
                bool water = m.IsWater(i);
                Assert.True(water != float.IsNaN(m.WaterLevel[i]), $"cell {i}: water={water} level={m.WaterLevel[i]}");
                if (water) Assert.True(m.WaterLevel[i] >= m.Height[i], $"cell {i}: water below the bed");
            }
        }),
        ("World: every river cell is connected to the sea or a lake", () =>
        {
            foreach (var seed in TestSeeds)
            {
                var m = Map(seed);
                // Flood from ocean + lakes through river cells; every river cell must be reached.
                var reached = new bool[m.Terrain.Length];
                var q = new Queue<int>();
                for (int i = 0; i < m.Terrain.Length; i++)
                    if (m.Terrain[i] is TerrainType.Ocean or TerrainType.Lake) { reached[i] = true; q.Enqueue(i); }
                while (q.Count > 0)
                {
                    int c = q.Dequeue();
                    int cx = c % m.Size, cz = c / m.Size;
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = cx + dx, z = cz + dz;
                        if (!m.InBounds(x, z)) continue;
                        int j = m.Index(x, z);
                        if (!reached[j] && m.Terrain[j] == TerrainType.River) { reached[j] = true; q.Enqueue(j); }
                    }
                }
                for (int i = 0; i < m.Terrain.Length; i++)
                    if (m.Terrain[i] == TerrainType.River)
                        Assert.True(reached[i], $"{seed}: river cell {i} is cut off");
            }
        }),
        ("World: exactly one landmass, for many seeds", () =>
        {
            foreach (var seed in TestSeeds.Concat(SweepSeeds))
            {
                var m = Map(seed);
                var seen = new bool[m.Terrain.Length];
                int components = 0;
                var stack = new Stack<int>();
                for (int i = 0; i < m.Terrain.Length; i++)
                {
                    if (seen[i] || m.Terrain[i] == TerrainType.Ocean) continue;
                    components++;
                    seen[i] = true;
                    stack.Push(i);
                    while (stack.Count > 0)
                    {
                        int c = stack.Pop();
                        int cx = c % m.Size, cz = c / m.Size;
                        foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            int x = cx + dx, z = cz + dz;
                            if (!m.InBounds(x, z)) continue;
                            int j = m.Index(x, z);
                            if (!seen[j] && m.Terrain[j] != TerrainType.Ocean) { seen[j] = true; stack.Push(j); }
                        }
                    }
                }
                Assert.Equal(1, components);
            }
        }),
        ("World: sweep — every seed has land in range, a river, a lake, and sites for player and bot", () =>
        {
            foreach (var seed in SweepSeeds)
            {
                var m = Map(seed);
                Assert.True(m.LandAreaKm2 > 20 && m.LandAreaKm2 < 70, $"{seed}: land {m.LandAreaKm2:0.0} km2");
                Assert.True(m.Terrain.Count(t => t == TerrainType.River) > 200, $"{seed}: almost no river");
                Assert.True(m.Terrain.Count(t => t == TerrainType.Lake) > 0, $"{seed}: no lake");
                var p = VillagePlacement.ChooseBotSite(m, Array.Empty<VillageSite>(), 0, "You");
                Assert.True(p != null, $"{seed}: no site for the player");
                Assert.True(VillagePlacement.ChooseBotSite(m, new[] { p! }, 1, "Bot") != null, $"{seed}: no site for the bot");
            }
        }),
        ("World: each lake has one flat water level", () =>
        {
            foreach (var seed in TestSeeds)
            {
                var m = Map(seed);
                for (int z = 0; z < m.Size - 1; z++)
                for (int x = 0; x < m.Size - 1; x++)
                {
                    int i = m.Index(x, z);
                    if (m.Terrain[i] != TerrainType.Lake) continue;
                    foreach (int j in new[] { m.Index(x + 1, z), m.Index(x, z + 1) })
                        if (m.Terrain[j] == TerrainType.Lake)
                            Assert.Equal(m.WaterLevel[i], m.WaterLevel[j]);
                }
            }
        }),
        ("World: beach ring is about 40 m wide measured in metres, also diagonally", () =>
        {
            var m = Map("K7QX2M9A");
            // A plain land cell must be more than 4 cells (40 m) in straight-line distance from any ocean cell.
            int r = m.Settings.BeachWidthCells;
            for (int z = r + 1; z < m.Size - r - 1; z += 3)
            for (int x = r + 1; x < m.Size - r - 1; x += 3)
            {
                if (m.Terrain[m.Index(x, z)] != TerrainType.Land) continue;
                bool nearCliff = false;
                for (int dz = -r; dz <= r && !nearCliff; dz++)
                for (int dx = -r; dx <= r; dx++)
                    if (m.CliffCoast[m.Index(x + dx, z + dz)]) { nearCliff = true; break; }
                if (nearCliff) continue;
                for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                    if (dx * dx + dz * dz <= r * r)
                        Assert.True(m.Terrain[m.Index(x + dx, z + dz)] != TerrainType.Ocean, $"land at {x},{z} within {r} cells of the sea");
            }
        }),
        ("World: rivers are 4-connected: every river piece reaches the sea or a lake via orthogonal steps", () =>
        {
            foreach (var seed in TestSeeds.Concat(SweepSeeds))
            {
                var m = Map(seed);
                var reached = new bool[m.Terrain.Length];
                var q = new Queue<int>();
                for (int i = 0; i < m.Terrain.Length; i++)
                    if (m.Terrain[i] is TerrainType.Ocean or TerrainType.Lake) { reached[i] = true; q.Enqueue(i); }
                while (q.Count > 0)
                {
                    int c = q.Dequeue();
                    int cx = c % m.Size, cz = c / m.Size;
                    foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int x = cx + dx, z = cz + dz;
                        if (!m.InBounds(x, z)) continue;
                        int j = m.Index(x, z);
                        if (!reached[j] && m.Terrain[j] == TerrainType.River) { reached[j] = true; q.Enqueue(j); }
                    }
                }
                for (int i = 0; i < m.Terrain.Length; i++)
                    if (m.Terrain[i] == TerrainType.River)
                        Assert.True(reached[i], $"{seed}: river cell {i % m.Size},{i / m.Size} is only connected diagonally");
            }
        }),
        ("World: river water never sits above the river's own ground level by more than the carve", () =>
        {
            foreach (var seed in TestSeeds)
            {
                var m = Map(seed);
                for (int i = 0; i < m.Terrain.Length; i++)
                    if (m.Terrain[i] == TerrainType.River)
                        Assert.True(m.WaterLevel[i] <= m.Height[i] + 1.0f + 1e-4f, $"{seed}: river cell {i} water {m.WaterLevel[i]} vs bed {m.Height[i]}");
            }
        }),
        ("Relief: highest point is a real mountain (at least 250 m), never above 600 m", () =>
        {
            foreach (var seed in TestSeeds.Concat(SweepSeeds))
            {
                float max = Map(seed).Height.Max();
                Assert.True(max >= 250 && max <= 600, $"{seed}: max height {max:0} m");
            }
        }),
        ("Relief: mountains cover at most 15% of the island", () =>
        {
            foreach (var seed in TestSeeds.Concat(SweepSeeds))
            {
                var m = Map(seed);
                double land = m.Terrain.Count(t => t != TerrainType.Ocean);
                double share = m.Mountain.Count(b => b) / land;
                Assert.True(share <= m.Settings.MountainMaxShare, $"{seed}: mountains {share:P1}");
            }
        }),
        ("Relief: every world has 1-3 volcanoes with a crater; crater lakes hold water", () =>
        {
            foreach (var seed in TestSeeds.Concat(SweepSeeds))
            {
                var m = Map(seed);
                Assert.True(m.Volcanoes.Count >= 1 && m.Volcanoes.Count <= 3, $"{seed}: {m.Volcanoes.Count} volcanoes");
                foreach (var v in m.Volcanoes)
                {
                    Assert.True(m.TryGetCell(v.CenterX, v.CenterZ, out int cx, out int cz), "volcano outside map");
                    // The crater centre is lower than the rim.
                    float centre = m.Height[m.Index(cx, cz)];
                    // Highest point of the rim (one direction may be the breach notch).
                    float rim = float.MinValue;
                    for (int k = 0; k < 8; k++)
                    {
                        double a = k * Math.PI / 4;
                        rim = MathF.Max(rim, m.GroundHeightAt(v.CenterX + (float)Math.Cos(a) * v.CraterRadius, v.CenterZ + (float)Math.Sin(a) * v.CraterRadius));
                    }
                    Assert.True(centre < rim, $"{seed}: no crater (centre {centre:0} m, rim {rim:0} m)");
                    if (v.HasCraterLake)
                        Assert.True(m.Terrain[m.Index(cx, cz)] == TerrainType.Lake, $"{seed}: crater lake missing");
                }
            }
        }),
        ("Relief: 2-5 plateaus with gently undulating tops (3-6 m) that are clearly raised", () =>
        {
            foreach (var seed in TestSeeds.Concat(SweepSeeds))
            {
                var m = Map(seed);
                Assert.True(m.Plateaus.Count >= 2 && m.Plateaus.Count <= 5, $"{seed}: {m.Plateaus.Count} plateaus");
                foreach (var p in m.Plateaus)
                {
                    Assert.True(p.Radius >= 210 && p.Radius <= 310, $"{seed}: plateau radius {p.Radius:0}");
                    Assert.True(p.Accesses >= 1 && p.Accesses <= 3, $"{seed}: {p.Accesses} accesses");
                    // Flat: the inner half of the top is level (dry cells; a river may cut through).
                    int flat = 0, samples = 0;
                    for (int k = 0; k < 24; k++)
                    {
                        double a = k * Math.PI / 12;
                        foreach (float f in new[] { 0f, 0.25f, 0.5f })
                        {
                            float px = p.CenterX + (float)Math.Cos(a) * p.Radius * f, pz = p.CenterZ + (float)Math.Sin(a) * p.Radius * f;
                            m.TryGetCell(px, pz, out int gx, out int gz);
                            if (m.IsWater(m.Index(gx, gz))) continue;
                            samples++;
                            if (MathF.Abs(m.GroundHeightAt(px, pz) - p.TopHeight) < 4f) flat++;
                        }
                    }
                    Assert.True(flat >= samples * 0.85, $"{seed}: plateau top not level ({flat}/{samples} level)");
                    // Gently undulating: 3-6 m between the highest and lowest point of the whole top
                    // (top cells away from the rim; ravines are not part of the top).
                    float lo = float.MaxValue, hi = float.MinValue;
                    int rc = (int)(p.Radius * 1.4f / m.CellSize);
                    m.TryGetCell(p.CenterX, p.CenterZ, out int pcx, out int pcz);
                    for (int z = pcz - rc; z <= pcz + rc; z++)
                    for (int x = pcx - rc; x <= pcx + rc; x++)
                    {
                        if (!m.InBounds(x, z)) continue;
                        int i = m.Index(x, z);
                        if (!m.PlateauTop[i] || m.SlopeClasses[i] != SlopeClass.Normal) continue;
                        lo = MathF.Min(lo, m.Height[i]); hi = MathF.Max(hi, m.Height[i]);
                    }
                    Assert.True(hi - lo >= 2.5f && hi - lo <= 6.5f, $"{seed}: plateau top varies {hi - lo:0.0} m");
                    // Raised: in many directions the ground just outside is clearly lower (flanks and mountainsides excepted).
                    int lower = 0;
                    for (int k = 0; k < 16; k++)
                    {
                        double a = k * Math.PI / 8;
                        float o = m.GroundHeightAt(p.CenterX + (float)Math.Cos(a) * p.Radius * 1.6f, p.CenterZ + (float)Math.Sin(a) * p.Radius * 1.6f);
                        if (p.TopHeight - o > 15) lower++;
                    }
                    Assert.True(lower >= 5, $"{seed}: plateau only clearly raised in {lower}/16 directions");
                }
            }
        }),
        ("Relief: plateaus can be climbed: from every plateau top there is a dry way down past the edge without a cliff", () =>
        {
            foreach (var seed in TestSeeds.Concat(SweepSeeds))
            {
                var m = Map(seed);
                foreach (var p in m.Plateaus)
                {
                    m.TryGetCell(p.CenterX, p.CenterZ, out int sx, out int sz);
                    // Walk from the plateau centre over non-impassable dry cells; must reach well outside the plateau.
                    var seen = new HashSet<int> { m.Index(sx, sz) };
                    var q = new Queue<int>(seen);
                    bool escaped = false;
                    // Far enough to be past the cliff edge and onto the land around the plateau.
                    double limit = p.Radius * 1.3 + 40;
                    while (q.Count > 0 && !escaped)
                    {
                        int c = q.Dequeue();
                        int cx = c % m.Size, cz = c / m.Size;
                        foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            int x = cx + dx, z = cz + dz;
                            if (!m.InBounds(x, z)) continue;
                            int j = m.Index(x, z);
                            // Water blocks (rivers need a bridge, GDD); impassable cells are cliffs.
                            if (seen.Contains(j) || m.IsWater(j) || m.SlopeClasses[j] == SlopeClass.Impassable) continue;
                            seen.Add(j);
                            float wx = x * m.CellSize - p.CenterX, wz = z * m.CellSize - p.CenterZ;
                            // Escaped = past the edge and clearly down off the top.
                            if (MathF.Sqrt(wx * wx + wz * wz) > limit && m.Height[j] < p.TopHeight - 15) { escaped = true; break; }
                            q.Enqueue(j);
                        }
                    }
                    Assert.True(escaped, $"{seed}: plateau at {p.CenterX:0},{p.CenterZ:0} has no walkable way down");
                }
            }
        }),
        ("Relief: about 2 in 3 plateaus lean against a mountain or volcano, with the mountainside as a way up", () =>
        {
            int near = 0, total = 0;
            foreach (var seed in TestSeeds.Concat(SweepSeeds))
            {
                var m = Map(seed);
                total += m.Plateaus.Count;
                near += m.Plateaus.Count(p => p.AgainstMountain);
                foreach (var p in m.Plateaus.Where(p => p.AgainstMountain))
                {
                    // Some mountain cell within ~1 km of the centre.
                    bool found = false;
                    for (int k = 0; k < 32 && !found; k++)
                    for (float d = p.Radius; d <= p.Radius + 900 && !found; d += 50)
                    {
                        double a = k * Math.PI / 16;
                        if (m.TryGetCell(p.CenterX + (float)Math.Cos(a) * d, p.CenterZ + (float)Math.Sin(a) * d, out int gx, out int gz))
                            found = m.Mountain[m.Index(gx, gz)];
                    }
                    Assert.True(found, $"{seed}: plateau marked as against a mountain has none nearby");
                    // The mountainside is a way up: from the top onto mountain ground 20 m higher, never dropping
                    // more than 5 m below the top, never crossing a cliff or water.
                    m.TryGetCell(p.CenterX, p.CenterZ, out int sx, out int sz);
                    var seen = new HashSet<int> { m.Index(sx, sz) };
                    var q = new Queue<int>(seen);
                    bool reached = false;
                    while (q.Count > 0 && !reached)
                    {
                        int c = q.Dequeue();
                        int cx = c % m.Size, cz = c / m.Size;
                        foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            int x = cx + dx, z = cz + dz;
                            if (!m.InBounds(x, z)) continue;
                            int j = m.Index(x, z);
                            if (seen.Contains(j) || m.IsWater(j) || m.SlopeClasses[j] == SlopeClass.Impassable) continue;
                            if (m.Height[j] < p.TopHeight - 6) continue;
                            seen.Add(j);
                            if (m.Mountain[j] && m.Height[j] >= p.TopHeight + 15) { reached = true; break; }
                            q.Enqueue(j);
                        }
                    }
                    Assert.True(reached, $"{seed}: no way from the plateau top up onto the mountainside");
                }
            }
            double share = (double)near / total;
            Assert.True(share >= 0.45 && share <= 0.85, $"{share:P0} of plateaus against a mountain");
        }),
        ("Relief: coastal cliffs are at most 35% of the coastline, and some exist", () =>
        {
            foreach (var seed in TestSeeds.Concat(SweepSeeds))
            {
                var m = Map(seed);
                int coast = 0, cliff = 0, steep = 0;
                for (int z = 1; z < m.Size - 1; z++)
                for (int x = 1; x < m.Size - 1; x++)
                {
                    int i = m.Index(x, z);
                    if (m.Terrain[i] is not (TerrainType.Beach or TerrainType.Land)) continue;
                    bool touches = false;
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (m.Terrain[m.Index(x + dx, z + dz)] == TerrainType.Ocean) touches = true;
                    if (!touches) continue;
                    coast++;
                    if (m.Terrain[i] == TerrainType.Land)
                    {
                        cliff++;
                        // Cells facing the sea directly (not just at a corner) must be a real cliff.
                        bool facing = false;
                        foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                            if (m.Terrain[m.Index(x + dx, z + dz)] == TerrainType.Ocean) facing = true;
                        if (!facing || m.SlopeClasses[i] == SlopeClass.Impassable) steep++;
                    }
                }
                double share = (double)cliff / coast;
                Assert.True(share > 0.05 && share <= m.Settings.CoastCliffMaxShare, $"{seed}: coastal cliffs {share:P0}");
                Assert.True(steep >= cliff * 0.8, $"{seed}: only {steep}/{cliff} cliff-coast cells are actually impassable");
            }
        }),
        ("Relief: slope classes follow the GDD thresholds", () =>
        {
            Assert.Equal(SlopeClass.Normal, WorldGenerator.Classify(0));
            Assert.Equal(SlopeClass.Normal, WorldGenerator.Classify(10));
            Assert.Equal(SlopeClass.Slow, WorldGenerator.Classify(10.1));
            Assert.Equal(SlopeClass.Slow, WorldGenerator.Classify(20));
            Assert.Equal(SlopeClass.Climb, WorldGenerator.Classify(20.1));
            Assert.Equal(SlopeClass.Climb, WorldGenerator.Classify(35));
            Assert.Equal(SlopeClass.Impassable, WorldGenerator.Classify(35.1));
            var m = Map("K7QX2M9A");
            for (int i = 0; i < m.Slope.Length; i += 97)
                Assert.Equal(WorldGenerator.Classify(m.Slope[i]), m.SlopeClasses[i]);
        }),
        ("Relief: stone sits mainly in the mountains", () =>
        {
            foreach (var seed in TestSeeds.Concat(SweepSeeds))
            {
                var m = Map(seed);
                var stone = m.Resources.Where(r => r.Type == ResourceType.Stone).ToList();
                int inMountains = stone.Count(c =>
                {
                    m.TryGetCell(c.CenterX, c.CenterZ, out int x, out int z);
                    return m.Mountain[m.Index(x, z)];
                });
                Assert.True(inMountains >= stone.Count * 0.6, $"{seed}: only {inMountains}/{stone.Count} stone clusters in the mountains");
            }
        }),
        ("Relief: no forest above the tree line or on cliffs", () =>
        {
            foreach (var seed in TestSeeds)
            {
                var m = Map(seed);
                for (int i = 0; i < m.Forest.Length; i++)
                    if (m.Forest[i]) Assert.True(m.Height[i] < m.Settings.TreeLine && m.Slope[i] < 30, $"{seed}: forest at {m.Height[i]:0} m, {m.Slope[i]:0} deg");
            }
        }),
        ("Relief: breached craters drain (no crater lake unless the volcano has one)", () =>
        {
            foreach (var seed in TestSeeds.Concat(SweepSeeds))
            {
                var m = Map(seed);
                foreach (var v in m.Volcanoes.Where(v => !v.HasCraterLake))
                {
                    m.TryGetCell(v.CenterX, v.CenterZ, out int x, out int z);
                    int rc = (int)(v.CraterRadius * 1.2f / m.CellSize), lakeCells = 0;
                    for (int dz = -rc; dz <= rc; dz++)
                    for (int dx = -rc; dx <= rc; dx++)
                        if (m.InBounds(x + dx, z + dz) && m.Terrain[m.Index(x + dx, z + dz)] == TerrainType.Lake) lakeCells++;
                    Assert.True(lakeCells < 20, $"{seed}: breached crater holds {lakeCells} lake cells");
                }
            }
        }),
        ("World: forest only grows on plain land", () =>
        {
            var m = Map("K7QX2M9A");
            for (int i = 0; i < m.Forest.Length; i++)
                if (m.Forest[i]) Assert.True(m.Terrain[i] == TerrainType.Land, $"forest on {m.Terrain[i]}");
            Assert.True(m.Forest.Count(f => f) > 1000, "almost no forest");
        }),
        ("World: has stone, berry and apple clusters, all on plain land", () =>
        {
            foreach (var seed in TestSeeds)
            {
                var m = Map(seed);
                foreach (ResourceType t in Enum.GetValues<ResourceType>())
                    Assert.True(m.Resources.Count(r => r.Type == t) >= 5, $"{seed}: fewer than 5 {t} clusters");
                foreach (var c in m.Resources)
                foreach (var (x, z) in c.Nodes)
                {
                    Assert.True(m.TryGetCell(x, z, out int gx, out int gz), "node outside the map");
                    Assert.True(m.Terrain[m.Index(gx, gz)] == TerrainType.Land, $"{seed}: {c.Type} node on {m.Terrain[m.Index(gx, gz)]}");
                }
            }
        }),
        ("World: raycast straight down hits the surface", () =>
        {
            var m = Map("K7QX2M9A");
            float x = 5000, z = 5000;
            Assert.True(m.Raycast(x, 500, z, 0, -1, 0, 2000, out var hx, out var hy, out var hz), "no hit");
            Assert.True(MathF.Abs(hy - m.SurfaceHeightAt(x, z)) < 0.05f, $"hit {hy} vs surface {m.SurfaceHeightAt(x, z)}");
            Assert.True(MathF.Abs(hx - x) < 0.01f && MathF.Abs(hz - z) < 0.01f, "hit moved sideways");
        }),

        ("World: oblique raycast hits the surface; rays to the sky miss; water returns the water level", () =>
        {
            var m = Map("K7QX2M9A");
            Assert.True(m.Raycast(1000, 800, 1000, 1, -0.5f, 1, 20000, out var hx, out var hy, out var hz), "oblique ray missed");
            Assert.True(MathF.Abs(hy - m.SurfaceHeightAt(hx, hz)) < 0.1f, "oblique hit not on the surface");
            Assert.True(!m.Raycast(5000, 500, 5000, 0.3f, 1, 0, 20000, out _, out _, out _), "ray to the sky hit something");
            Assert.True(m.Raycast(-500, 300, -500, 1, -0.4f, 1, 20000, out _, out _, out _), "ray from outside the map missed");
            Assert.True(m.Raycast(50, 200, 50, 0, -1, 0, 1000, out _, out var oy, out _), "ray at the sea missed");
            Assert.True(MathF.Abs(oy) < 0.01f, $"sea hit at {oy}, expected sea level 0");
        }),

        // --- Village placement ---
        ("Placement: water, beach and far-from-fresh-water cells are refused", () =>
        {
            var m = Map("K7QX2M9A");
            var none = Array.Empty<VillageSite>();
            Assert.True(!VillagePlacement.CheckSite(m, 0, 0, none, out _), "ocean accepted");
            int beach = Array.IndexOf(m.Terrain, TerrainType.Beach);
            Assert.True(!VillagePlacement.CheckSite(m, beach % m.Size, beach / m.Size, none, out _), "beach accepted");
            for (int i = 0; i < m.Terrain.Length; i++)
            {
                if (m.Terrain[i] == TerrainType.Land && m.FreshWaterDistance[i] > m.Settings.MaxFreshWaterDistance)
                {
                    Assert.True(!VillagePlacement.CheckSite(m, i % m.Size, i / m.Size, none, out var reason), "dry site accepted");
                    Assert.True(reason.Contains("fresh water"), $"unexpected reason '{reason}'");
                    break;
                }
            }
        }),
        ("Placement: bot picks a valid site at least 1.5 km from the player", () =>
        {
            foreach (var seed in TestSeeds)
            {
                var m = Map(seed);
                var player = VillagePlacement.ChooseBotSite(m, Array.Empty<VillageSite>(), VillagePlacement.PlayerId, "You");
                Assert.True(player != null, $"{seed}: no site at all");
                var villages = new List<VillageSite> { player! };
                var bot = VillagePlacement.ChooseBotSite(m, villages, 1, "Bot");
                Assert.True(bot != null, $"{seed}: bot found no site");
                Assert.True(VillagePlacement.CheckSite(m, bot!.CellX, bot.CellZ, villages, out var reason), $"{seed}: bot site invalid: {reason}");
                double d = VillagePlacement.Distance(m, player!.CellX, player.CellZ, bot.CellX, bot.CellZ);
                Assert.True(d >= m.Settings.MinVillageDistance, $"{seed}: bot only {d:0} m away");
            }
        }),
        ("Placement: accepted sites have a dry, level footprint (no water or beach where houses stand)", () =>
        {
            foreach (var seed in TestSeeds.Concat(SweepSeeds))
            {
                var m = Map(seed);
                var p = VillagePlacement.ChooseBotSite(m, Array.Empty<VillageSite>(), 0, "You")!;
                var b = VillagePlacement.ChooseBotSite(m, new[] { p }, 1, "Bot")!;
                foreach (var v in new[] { p, b })
                {
                    int r = m.Settings.SiteFootprintCells;
                    for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (dx * dx + dz * dz > r * r) continue;
                        var t = m.Terrain[m.Index(v.CellX + dx, v.CellZ + dz)];
                        Assert.True(t == TerrainType.Land, $"{seed}: {v.Name} footprint has {t}");
                    }
                }
            }
        }),
        ("Placement: steep and out-of-map sites are refused", () =>
        {
            var m = Map("K7QX2M9A");
            var none = Array.Empty<VillageSite>();
            Assert.True(!VillagePlacement.CheckSite(m, -1, 5, none, out var r1) && r1.Contains("outside"), "out of map accepted");
            Assert.True(!VillagePlacement.CheckSite(m, m.Size, 5, none, out _), "out of map accepted");
            for (int z = 1; z < m.Size - 1; z++)
            for (int x = 1; x < m.Size - 1; x++)
            {
                int i = m.Index(x, z);
                if (m.Terrain[i] != TerrainType.Land) continue;
                if (MathF.Abs(m.Height[m.Index(x + 1, z)] - m.Height[i]) > m.Settings.MaxSlope + 0.5)
                {
                    Assert.True(!VillagePlacement.CheckSite(m, x, z, none, out _), "steep site accepted");
                    return;
                }
            }
        }),
        ("Placement: a site too close to another village is refused with a reason", () =>
        {
            var m = Map("K7QX2M9A");
            var player = VillagePlacement.ChooseBotSite(m, Array.Empty<VillageSite>(), VillagePlacement.PlayerId, "You")!;
            Assert.True(!VillagePlacement.CheckSite(m, player.CellX, player.CellZ, new[] { player with { OwnerId = 1, Name = "Bot" } }, out var reason),
                "site on top of another village accepted");
            Assert.True(reason.Contains("Too close"), $"unexpected reason '{reason}'");
        }),
    };

    public static int Main()
    {
        int failed = 0;
        foreach (var (name, run) in Tests)
        {
            try
            {
                run();
                Console.WriteLine($"PASS  {name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"FAIL  {name}: {ex.Message}");
            }
        }
        Console.WriteLine($"{Tests.Count - failed}/{Tests.Count} passed");
        return failed == 0 ? 0 : 1;
    }
}

internal static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"expected '{expected}', got '{actual}'");
    }
}
