using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Kingdom.Core.World;

namespace Kingdom.MapPreview;

public static class Program
{
    public static int Main(string[] args)
    {
        string seedText = args.Length > 0 ? args[0] : Seed.NewRandom().Text;
        string output = args.Length > 1 ? args[1] : $"map_{seedText}.ppm";
        var seed = Seed.Parse(seedText);

        var sw = Stopwatch.StartNew();
        var map = WorldGenerator.Generate(seed);
        sw.Stop();

        int n = map.Size;
        int Count(TerrainType t) => map.Terrain.Count(x => x == t);
        Console.WriteLine($"Seed {seed.Text}  generated in {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"Land {map.LandAreaKm2:0.0} km2 | beach {Count(TerrainType.Beach)} | river {Count(TerrainType.River)} | lake {Count(TerrainType.Lake)} cells");
        Console.WriteLine($"Forest {100.0 * map.Forest.Count(f => f) / Math.Max(1, Count(TerrainType.Land)):0}% of land | suitable cells {map.SiteSuitable.Count(b => b)}");
        Console.WriteLine($"Max height {map.Height.Max():0} m | mountain {100.0 * map.Mountain.Count(m => m) / Math.Max(1, map.Terrain.Count(t => t != TerrainType.Ocean)):0.0}% | volcanoes {map.Volcanoes.Count} (crater lakes {map.Volcanoes.Count(v => v.HasCraterLake)}) | plateaus {map.Plateaus.Count}");
        int coast = 0, cliff = 0;
        for (int z = 1; z < n - 1; z++)
        for (int x = 1; x < n - 1; x++)
        {
            int i = z * n + x;
            if (map.Terrain[i] is not (TerrainType.Beach or TerrainType.Land)) continue;
            bool touches = false;
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) if (map.Terrain[i + dz * n + dx] == TerrainType.Ocean) touches = true;
            if (!touches) continue;
            coast++;
            if (map.Terrain[i] == TerrainType.Land) cliff++;
        }
        Console.WriteLine($"Coast cliffs {100.0 * cliff / Math.Max(1, coast):0}% of coastline | impassable {100.0 * map.SlopeClasses.Count(c => c == SlopeClass.Impassable) / Math.Max(1, map.Terrain.Count(t => t != TerrainType.Ocean)):0.0}% of land");
        foreach (ResourceType t in Enum.GetValues<ResourceType>())
            Console.WriteLine($"  {t}: {map.Resources.Count(r => r.Type == t)} clusters");

        // Fake a player at the best site, then let a bot choose, to exercise placement.
        var player = VillagePlacement.ChooseBotSite(map, Array.Empty<VillageSite>(), 0, "Player");
        var villages = new System.Collections.Generic.List<VillageSite>();
        if (player != null) villages.Add(player);
        var bot = VillagePlacement.ChooseBotSite(map, villages, 1, "Bot");
        if (bot != null) villages.Add(bot);
        foreach (var v in villages)
            Console.WriteLine($"  {v.Name} at {v.CellX * map.CellSize:0},{v.CellZ * map.CellSize:0} m");

        using var fs = File.Create(output);
        var header = System.Text.Encoding.ASCII.GetBytes($"P6\n{n} {n}\n255\n");
        fs.Write(header);
        var px = new byte[n * n * 3];
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            int i = z * n + x;
            (double r, double g, double b) c = map.Terrain[i] switch
            {
                TerrainType.Ocean => (30, 70, 130),
                TerrainType.Lake => (50, 110, 190),
                TerrainType.River => (60, 130, 210),
                TerrainType.Beach => (220, 205, 150),
                _ => map.Slope[i] > 33 ? (120, 115, 105)
                    : map.Forest[i] ? (30, 95, 40)
                    : map.Height[i] > 220 ? (130, 135, 100)
                    : map.PlateauTop[i] ? (175, 170, 90)
                    : (110, 165, 80),
            };
            // Hill shading (light from the north-west) to show relief.
            if (map.Terrain[i] != TerrainType.Ocean)
            {
                int xl = Math.Max(x - 1, 0), xr = Math.Min(x + 1, n - 1), zd = Math.Max(z - 1, 0), zu = Math.Min(z + 1, n - 1);
                double gx = (map.Height[z * n + xr] - map.Height[z * n + xl]) / (2 * map.CellSize);
                double gz = (map.Height[zu * n + x] - map.Height[zd * n + x]) / (2 * map.CellSize);
                double shade = Math.Clamp(1.0 + (gx + gz) * -1.2, 0.45, 1.35);
                c = (c.r * shade, c.g * shade, c.b * shade);
            }
            px[i * 3] = (byte)Math.Clamp(c.r, 0, 255); px[i * 3 + 1] = (byte)Math.Clamp(c.g, 0, 255); px[i * 3 + 2] = (byte)Math.Clamp(c.b, 0, 255);
        }
        foreach (var cl in map.Resources)
        {
            (byte, byte, byte) c = cl.Type switch
            {
                ResourceType.Stone => (150, 150, 150),
                ResourceType.Berries => (200, 40, 120),
                _ => (230, 60, 40),
            };
            foreach (var (x, z) in cl.Nodes) Dot(x, z, 1, c);
        }
        foreach (var v in villages)
            Dot(v.CellX * map.CellSize, v.CellZ * map.CellSize, 6, v.OwnerId == 0 ? ((byte)40, (byte)80, (byte)255) : ((byte)255, (byte)30, (byte)30));
        fs.Write(px);
        Console.WriteLine($"Wrote {output}");
        return 0;

        void Dot(float wx, float wz, int r, (byte, byte, byte) c)
        {
            int cx = (int)(wx / map.CellSize), cz = (int)(wz / map.CellSize);
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if (x < 0 || z < 0 || x >= n || z >= n) continue;
                int i = z * n + x;
                px[i * 3] = c.Item1; px[i * 3 + 1] = c.Item2; px[i * 3 + 2] = c.Item3;
            }
        }
    }

    private static (byte, byte, byte) Shade(float h)
    {
        double t = Math.Clamp(h / 45.0, 0, 1);
        return ((byte)(110 + 60 * t), (byte)(160 - 20 * t), (byte)(80 + 10 * t));
    }
}
