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
        for (int i = 0; i < n * n; i++)
        {
            (byte r, byte g, byte b) c = map.Terrain[i] switch
            {
                TerrainType.Ocean => (30, 70, 130),
                TerrainType.Lake => (50, 110, 190),
                TerrainType.River => (60, 130, 210),
                TerrainType.Beach => (220, 205, 150),
                _ => map.Forest[i] ? ((byte)30, (byte)95, (byte)40) : Shade(map.Height[i]),
            };
            px[i * 3] = c.r; px[i * 3 + 1] = c.g; px[i * 3 + 2] = c.b;
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
