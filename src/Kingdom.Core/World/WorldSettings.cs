namespace Kingdom.Core.World;

/// <summary>
/// Tunable numbers for world generation and village placement.
/// All distances are in metres unless the name says "Cells".
/// </summary>
public sealed class WorldSettings
{
    /// <summary>Cells per side. 1000 cells x 10 m = 10 x 10 km (GDD decision).</summary>
    public int Size { get; init; } = 1000;

    /// <summary>Metres per cell.</summary>
    public float CellSize { get; init; } = 10f;

    // --- Island shape and hills ---
    public double BorderOceanMeters { get; init; } = 300;
    public double SeaFloorDepth { get; init; } = -35;
    public double LandBaseHeight { get; init; } = 4;
    /// <summary>Maximum extra height from hills. "Light hills" per GDD.</summary>
    public double HillHeight { get; init; } = 38;

    // --- Coast ---
    /// <summary>Beach ring width in cells (4 x 10 m = 40 m).</summary>
    public int BeachWidthCells { get; init; } = 4;

    // --- Lakes and rivers ---
    public double LakeMinDepth { get; init; } = 0.6;
    public int LakeMinCells { get; init; } = 80;
    /// <summary>Upstream area (in cells) needed before a stream counts as a river. 6000 cells = 0.6 km^2.</summary>
    public int RiverMinCells { get; init; } = 6000;
    /// <summary>Upstream area after which a river is drawn 3 cells wide.</summary>
    public int RiverWideCells { get; init; } = 40000;

    // --- Forests ---
    /// <summary>Forest where forest noise exceeds this. Higher = less forest.</summary>
    public double ForestThreshold { get; init; } = 0.10;

    // --- Resources (clusters per km^2 of land) ---
    public double StoneClustersPerKm2 { get; init; } = 0.6;
    public double BerryClustersPerKm2 { get; init; } = 1.0;
    public double AppleClustersPerKm2 { get; init; } = 0.6;

    // --- Village placement (temporary rule, GDD 2026-11-16) ---
    /// <summary>Max height difference to any neighbour cell, in metres (1.2 m over 10 m ~ 7 degrees).</summary>
    public double MaxSlope { get; init; } = 1.2;
    public double MaxFreshWaterDistance { get; init; } = 300;
    /// <summary>Radius (cells) of the village footprint that must be plain, fairly level land. 3 cells = 30 m.</summary>
    public int SiteFootprintCells { get; init; } = 3;
    /// <summary>Max height difference inside the footprint, in metres.</summary>
    public double SiteMaxHeightSpread { get; init; } = 2.0;
    public double MinVillageDistance { get; init; } = 1500;

    public static WorldSettings Default { get; } = new();
}
