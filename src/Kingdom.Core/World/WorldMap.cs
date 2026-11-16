using System;
using System.Collections.Generic;

namespace Kingdom.Core.World;

/// <summary>What covers a cell.</summary>
public enum TerrainType : byte
{
    Ocean,
    Beach,
    Land,
    Lake,
    River,
}

/// <summary>How hard a cell is to cross (GDD slope classes).</summary>
public enum SlopeClass : byte
{
    /// <summary>0-10 degrees: normal walking, carts fine.</summary>
    Normal,
    /// <summary>10-20 degrees: slightly slower on foot, slow for carts.</summary>
    Slow,
    /// <summary>20-35 degrees: climbing on foot, no carts.</summary>
    Climb,
    /// <summary>Above 35 degrees: cliff, impassable.</summary>
    Impassable,
}

/// <summary>A volcanic mountain: cone with a crater at the top, sometimes holding a crater lake.</summary>
/// <param name="BreachAngle">Direction (radians) of the notch that drains the crater; NaN when it holds a lake.</param>
public sealed record Volcano(float CenterX, float CenterZ, float Radius, float CraterRadius, bool HasCraterLake, float BreachAngle);

/// <summary>A raised flat area with steep edges and a few ramps up.</summary>
/// <param name="Accesses">Number of ways up (broad flanks, ravines, or the mountainside), 1-3.</param>
/// <param name="AgainstMountain">Placed against a mountain or volcano rather than standalone.</param>
public sealed record Plateau(float CenterX, float CenterZ, float Radius, float TopHeight, int Accesses, bool AgainstMountain);

public enum ResourceType : byte
{
    Stone,
    Berries,
    Apples,
}

/// <summary>A cluster of resource nodes (rocks, bushes, apple trees) around a centre.</summary>
public sealed class ResourceCluster
{
    public ResourceType Type { get; }
    public float CenterX { get; }
    public float CenterZ { get; }
    /// <summary>World positions (x, z) of the individual nodes.</summary>
    public IReadOnlyList<(float X, float Z)> Nodes { get; }

    public ResourceCluster(ResourceType type, float centerX, float centerZ, IReadOnlyList<(float X, float Z)> nodes)
    {
        Type = type;
        CenterX = centerX;
        CenterZ = centerZ;
        Nodes = nodes;
    }
}

/// <summary>
/// A generated world. Grid of Size x Size cells; cell (x, z) sits at world position (x * CellSize, z * CellSize).
/// Heights are metres; sea level is 0.
/// </summary>
public sealed class WorldMap
{
    public Seed Seed { get; }
    public WorldSettings Settings { get; }
    public int Size { get; }
    public float CellSize { get; }

    /// <summary>Ground height (river and lake beds included).</summary>
    public float[] Height { get; }
    /// <summary>Water surface for Ocean (0), Lake and River cells; NaN elsewhere.</summary>
    public float[] WaterLevel { get; }
    public TerrainType[] Terrain { get; }
    public bool[] Forest { get; }
    /// <summary>Straight-line distance to the nearest river or lake cell, in metres.</summary>
    public float[] FreshWaterDistance { get; }
    /// <summary>Cell passes the village rule except the distance-to-other-villages part.</summary>
    public bool[] SiteSuitable { get; }
    public IReadOnlyList<ResourceCluster> Resources { get; }

    /// <summary>Steepness of the ground in degrees.</summary>
    public float[] Slope { get; }
    public SlopeClass[] SlopeClasses { get; }
    /// <summary>Raised by mountains or volcanoes by more than <see cref="WorldSettings.MountainThreshold"/>.</summary>
    public bool[] Mountain { get; }
    /// <summary>Inside a plateau's flat top.</summary>
    public bool[] PlateauTop { get; }
    /// <summary>Coast stretch that is cliff instead of beach.</summary>
    public bool[] CliffCoast { get; }
    public IReadOnlyList<Volcano> Volcanoes { get; }
    /// <summary>How volcanic the ground is: 1 at a volcano's top, 0 at its foot and beyond.</summary>
    public float[] Volcanic { get; }
    public IReadOnlyList<Plateau> Plateaus { get; }

    /// <summary>Land area in km^2 (Beach + Land + River cells).</summary>
    public double LandAreaKm2 { get; }

    internal WorldMap(Seed seed, WorldSettings settings, float[] height, float[] waterLevel, TerrainType[] terrain,
        bool[] forest, float[] freshWaterDistance, bool[] siteSuitable, IReadOnlyList<ResourceCluster> resources,
        double landAreaKm2, float[] slope, SlopeClass[] slopeClasses, bool[] mountain, bool[] plateauTop, bool[] cliffCoast,
        IReadOnlyList<Volcano> volcanoes, IReadOnlyList<Plateau> plateaus, float[] volcanic)
    {
        Volcanic = volcanic;
        Slope = slope;
        SlopeClasses = slopeClasses;
        Mountain = mountain;
        PlateauTop = plateauTop;
        CliffCoast = cliffCoast;
        Volcanoes = volcanoes;
        Plateaus = plateaus;
        Seed = seed;
        Settings = settings;
        Size = settings.Size;
        CellSize = settings.CellSize;
        Height = height;
        WaterLevel = waterLevel;
        Terrain = terrain;
        Forest = forest;
        FreshWaterDistance = freshWaterDistance;
        SiteSuitable = siteSuitable;
        Resources = resources;
        LandAreaKm2 = landAreaKm2;
    }

    /// <summary>Length of one side in metres, from the first to the last cell.</summary>
    public float Extent => (Size - 1) * CellSize;

    public int Index(int x, int z) => z * Size + x;
    public bool InBounds(int x, int z) => x >= 0 && z >= 0 && x < Size && z < Size;

    public bool IsWater(int index) => Terrain[index] is TerrainType.Ocean or TerrainType.Lake or TerrainType.River;

    /// <summary>The cell nearest to a world position, or false if outside the map.</summary>
    public bool TryGetCell(float worldX, float worldZ, out int x, out int z)
    {
        x = (int)MathF.Round(worldX / CellSize);
        z = (int)MathF.Round(worldZ / CellSize);
        return InBounds(x, z);
    }

    /// <summary>Visible top surface of a cell: water surface where there is water, otherwise ground.</summary>
    public float SurfaceHeight(int index)
    {
        float w = WaterLevel[index];
        return float.IsNaN(w) ? Height[index] : MathF.Max(w, Height[index]);
    }

    /// <summary>Bilinear ground height at a world position (clamped to the map).</summary>
    public float GroundHeightAt(float worldX, float worldZ) => Bilinear(worldX, worldZ, surface: false);

    /// <summary>Bilinear visible surface (ground or water) at a world position (clamped to the map).</summary>
    public float SurfaceHeightAt(float worldX, float worldZ) => Bilinear(worldX, worldZ, surface: true);

    private float Bilinear(float worldX, float worldZ, bool surface)
    {
        float fx = Math.Clamp(worldX / CellSize, 0, Size - 1.0001f);
        float fz = Math.Clamp(worldZ / CellSize, 0, Size - 1.0001f);
        int x0 = (int)fx, z0 = (int)fz;
        float tx = fx - x0, tz = fz - z0;
        float h00 = H(Index(x0, z0)), h10 = H(Index(x0 + 1, z0));
        float h01 = H(Index(x0, z0 + 1)), h11 = H(Index(x0 + 1, z0 + 1));
        float a = h00 + (h10 - h00) * tx;
        float b = h01 + (h11 - h01) * tx;
        return a + (b - a) * tz;

        float H(int i) => surface ? SurfaceHeight(i) : Height[i];
    }

    /// <summary>
    /// Casts a ray against the visible surface (terrain or water). Used for mouse picking.
    /// Returns false when the ray leaves the map without hitting anything within maxDistance.
    /// </summary>
    public bool Raycast(float ox, float oy, float oz, float dx, float dy, float dz, float maxDistance,
        out float hitX, out float hitY, out float hitZ)
    {
        hitX = hitY = hitZ = 0;
        float len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (len < 1e-6f) return false;
        dx /= len; dy /= len; dz /= len;

        const float step = 4f;
        float prevT = 0;
        bool prevAbove = oy > SurfaceHeightAt(ox, oz);
        for (float t = step; t <= maxDistance; t += step)
        {
            float x = ox + dx * t, y = oy + dy * t, z = oz + dz * t;
            if (x < 0 || z < 0 || x > Extent || z > Extent)
            {
                prevT = t;
                prevAbove = true; // outside the map counts as open air
                continue;
            }
            bool above = y > SurfaceHeightAt(x, z);
            if (prevAbove && !above)
            {
                // Refine between prevT and t.
                float lo = prevT, hi = t;
                for (int i = 0; i < 16; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    float mx = ox + dx * mid, my = oy + dy * mid, mz = oz + dz * mid;
                    if (my > SurfaceHeightAt(mx, mz)) lo = mid; else hi = mid;
                }
                hitX = ox + dx * hi;
                hitZ = oz + dz * hi;
                hitY = SurfaceHeightAt(hitX, hitZ);
                return true;
            }
            prevAbove = above;
            prevT = t;
        }
        return false;
    }
}
