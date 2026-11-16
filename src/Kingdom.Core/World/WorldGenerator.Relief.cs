using System;
using System.Collections.Generic;
using System.Linq;

namespace Kingdom.Core.World;

/// <summary>
/// Relief (0.0.4): mountain ranges, volcanoes, plateaus, coastal cliffs, river valleys and slope classes.
/// </summary>
public static partial class WorldGenerator
{
    private const ulong StreamMountainMask = 5, StreamRidges = 6, StreamVolcano = 7, StreamPlateau = 8, StreamCliffs = 9;

    // ---------------------------------------------------------------- volcanoes

    /// <summary>
    /// Every world has at least one volcano (volcanic island continent); sometimes two or three.
    /// Adds the cone to <paramref name="mountainAdd"/>; the crater either holds a lake or is breached so it drains.
    /// </summary>
    private static List<Volcano> AddVolcanoes(Seed seed, WorldSettings s, double[] coastDist, bool[] isOcean, double[] mountainAdd, double[] volcanoRn)
    {
        int n = s.Size;
        var rng = Rng.For(seed.Value, StreamVolcano);
        var shapeNoise = new Noise(Rng.For(seed.Value, StreamVolcano + 100));
        var result = new List<Volcano>();

        int wanted = 1 + (rng.NextDouble() < 0.35 ? 1 : 0) + (rng.NextDouble() < 0.10 ? 1 : 0);

        // Interior candidates: at least ~1.1 km from the sea.
        var candidates = new List<int>();
        for (int i = 0; i < coastDist.Length; i++)
            if (!isOcean[i] && coastDist[i] >= 110) candidates.Add(i);
        if (candidates.Count == 0)
        {
            // Small island: fall back to the most inland cell.
            int best = 0;
            for (int i = 1; i < coastDist.Length; i++) if (coastDist[i] > coastDist[best] && !isOcean[i]) best = i;
            candidates.Add(best);
        }

        int landCells = 0;
        for (int i = 0; i < isOcean.Length; i++) if (!isOcean[i]) landCells++;
        double[]? addBackup = null;
        double[]? rnBackup = null;

        for (int attempt = 0; attempt < 400 && result.Count < wanted; attempt++)
        {
            int c = candidates[rng.NextInt(0, candidates.Count)];
            float cx = (c % n) * s.CellSize, cz = (c / n) * s.CellSize;
            bool tooClose = false;
            foreach (var v in result)
            {
                float dx = v.CenterX - cx, dz = v.CenterZ - cz;
                if (dx * dx + dz * dz < 2600f * 2600f) { tooClose = true; break; }
            }
            if (tooClose) continue;

            double radius = rng.Range(750, 1100);
            double height = rng.Range(s.VolcanoMinHeight, s.VolcanoMaxHeight);
            double craterR = radius * rng.Range(0.15, 0.21);
            double craterDepth = rng.Range(25, 55);
            bool lake = rng.NextDouble() < 0.5;
            double breachAngle = rng.Range(0, Math.PI * 2);

            // Real volcanoes are rarely round: stretched along a fissure, lumpy footprint, eroded ravines,
            // sometimes a collapsed flank (horseshoe scar) and a small parasitic cone on the side.
            double stretch = rng.NextDouble() < 0.4 ? rng.Range(1.15, 1.5) : 1.0;
            double stretchAngle = rng.Range(0, Math.PI * 2);
            // Keep the whole cone on land: shrink it to fit, or skip spots too close to the sea.
            radius = Math.Min(radius, coastDist[c] * s.CellSize / (stretch * 1.3));
            if (radius < 550) continue;
            // Not steeper than ~29 degrees at the base (H * 1.25 / R), so the cone itself isn't a wall.
            height = Math.Min(height, 0.45 * radius);
            double footprintOffset = rng.Range(0, 100);
            double ravineDepth = rng.Range(8, 18);
            int ravineCount = rng.NextInt(9, 16);
            bool collapse = rng.NextDouble() < 0.3;
            double collapseAngle = rng.Range(0, Math.PI * 2);
            double collapseWidth = rng.Range(0.18, 0.3) * radius; // half width at the crater, metres
            bool parasite = rng.NextDouble() < 0.5;
            double parasiteAngle = rng.Range(0, Math.PI * 2);
            double parasiteDist = radius * rng.Range(0.5, 0.8);
            double parasiteRadius = rng.Range(150, 260);
            double parasiteHeight = rng.Range(40, 90);

            if (result.Count > 0)
            {
                // Extra volcanoes may not push the mountain share past the cap: keep a backup to undo.
                addBackup = (double[])mountainAdd.Clone();
                rnBackup = (double[])volcanoRn.Clone();
            }
            var volcano = new Volcano(cx, cz, (float)radius, (float)craterR, lake, lake ? float.NaN : (float)breachAngle);
            result.Add(volcano);

            double rimH = Cone(craterR / radius);
            double floorH = rimH - craterDepth;
            // A bit beyond the foot too: ranges fade out over rn 0.45-1.3, so rn is needed out to 1.3.
            int rc = (int)Math.Ceiling(radius * 1.25 * 1.3 * stretch / s.CellSize) + 1;
            int gx = c % n, gz = c / n;
            double sa = Math.Cos(stretchAngle), sb = Math.Sin(stretchAngle);
            for (int z = Math.Max(0, gz - rc); z <= Math.Min(n - 1, gz + rc); z++)
            for (int x = Math.Max(0, gx - rc); x <= Math.Min(n - 1, gx + rc); x++)
            {
                int i = z * n + x;
                if (isOcean[i]) continue;
                double dx = (x - gx) * s.CellSize, dz = (z - gz) * s.CellSize;

                // Local frame: squeeze the stretch axis so the cone becomes an oval.
                double u = (dx * sa + dz * sb) / stretch, v = -dx * sb + dz * sa;
                double theta = Math.Atan2(v, u);
                double ct = Math.Cos(theta), st = Math.Sin(theta);
                double footprint = 1 + 0.25 * shapeNoise.Fbm(ct * 1.3 + footprintOffset, st * 1.3, 3);
                double rn = Math.Sqrt(u * u + v * v) / (radius * footprint); // 0 at the top, 1 at the foot
                if (rn < volcanoRn[i]) volcanoRn[i] = rn;
                if (rn >= 1) continue;
                double craterN = craterR / radius * (1 + 0.15 * shapeNoise.Sample(ct * 2 + footprintOffset + 40, st * 2));

                double h;
                if (rn < craterN)
                {
                    double t = rn / craterN;
                    h = floorH + (rimH - floorH) * t * t;
                }
                else
                {
                    double rough = 1 + 0.08 * shapeNoise.Fbm((cx + dx) / 300.0, (cz + dz) / 300.0, 3);
                    h = Cone(rn) * rough;
                    // Radial ravines on the flanks, deepest halfway down.
                    // Mostly radial (small wobble), broad enough not to form walls.
                    double g = Math.Abs(Math.Sin(ravineCount * 0.5 * theta + 0.7 * shapeNoise.Fbm(ct * 1.5 + footprintOffset + 80, st * 1.5 + rn * 2.0, 2)));
                    double band = Math.Sin(Math.PI * Math.Clamp((rn - 0.2) / 0.75, 0, 1));
                    double strength = 0.5 + 0.5 * shapeNoise.Sample(ct * 3 + footprintOffset + 160, st * 3);
                    h -= ravineDepth * Math.Max(0, strength) * Math.Pow(1 - g, 3) * band;
                }

                if (collapse)
                {
                    // Horseshoe scar: a steep headwall near the crater, walls that open outward, a floor that
                    // runs out of the cone. Measured across the collapse axis (metres), with ragged edges.
                    double ax = Math.Cos(collapseAngle), az = Math.Sin(collapseAngle);
                    double along = dx * ax + dz * az;
                    double lateral = Math.Abs(-dx * az + dz * ax);
                    if (along > -craterR * 0.3)
                    {
                        double half = collapseWidth + Math.Max(0, along) * 0.45
                                      + 40 * shapeNoise.Sample(along / 120.0 + footprintOffset + 220, 3.3);
                        double w = 1 - SmoothStep(half * 0.7, half, lateral);
                        double head = SmoothStep(-craterR * 0.3, craterR * 0.6, along);
                        if (w > 0)
                            h -= w * head * 0.5 * Cone(rn) * SmoothStep(craterN, craterN + 0.12, rn);
                    }
                }

                h = Math.Max(h, 0);
                if (h > mountainAdd[i]) mountainAdd[i] = h;
            }

            if (parasite)
            {
                double px = cx + Math.Cos(parasiteAngle) * parasiteDist, pz = cz + Math.Sin(parasiteAngle) * parasiteDist;
                int pgx = (int)(px / s.CellSize), pgz = (int)(pz / s.CellSize);
                int pr = (int)(parasiteRadius / s.CellSize) + 1;
                for (int z = Math.Max(0, pgz - pr); z <= Math.Min(n - 1, pgz + pr); z++)
                for (int x = Math.Max(0, pgx - pr); x <= Math.Min(n - 1, pgx + pr); x++)
                {
                    int i = z * n + x;
                    if (isOcean[i]) continue;
                    double dx = x * s.CellSize - px, dz = z * s.CellSize - pz;
                    double t = Math.Sqrt(dx * dx + dz * dz) / parasiteRadius;
                    if (t >= 1) continue;
                    // Small cone with a shallow dimple at the top.
                    double add = parasiteHeight * Math.Pow(1 - t, 1.3) - (t < 0.15 ? 6 * (1 - t / 0.15) : 0);
                    mountainAdd[i] += Math.Max(0, add);
                }
            }

            if (addBackup != null && rnBackup != null)
            {
                int m = 0;
                for (int i = 0; i < mountainAdd.Length; i++) if (!isOcean[i] && mountainAdd[i] > s.MountainThreshold) m++;
                if ((double)m / landCells > s.MountainMaxShare - 0.02)
                {
                    Array.Copy(addBackup, mountainAdd, mountainAdd.Length);
                    Array.Copy(rnBackup, volcanoRn, volcanoRn.Length);
                    result.RemoveAt(result.Count - 1);
                    break;
                }
                addBackup = null;
                rnBackup = null;
            }

            double Cone(double rnorm) => height * Math.Pow(Math.Max(0, 1 - rnorm), 1.25);
        }
        return result;
    }

    /// <summary>
    /// Cuts the drainage notch for volcanoes without a crater lake. Runs on the final heights (lowland, ranges and
    /// side cones included) and walks outward from the crater centre, always descending, until the channel
    /// meets lower ground outside.
    /// </summary>
    private static void CutCraterBreaches(WorldSettings s, double[] height, bool[] isOcean, IReadOnlyList<Volcano> volcanoes)
    {
        int n = s.Size;
        foreach (var v in volcanoes)
        {
            if (float.IsNaN(v.BreachAngle)) continue;
            double ax = Math.Cos(v.BreachAngle), az = Math.Sin(v.BreachAngle);
            int gx = (int)(v.CenterX / s.CellSize), gz = (int)(v.CenterZ / s.CellSize);
            double level = height[gz * n + gx];
            int below = 0;
            for (double t = 0; t < v.Radius * 1.6 && below < 6; t += s.CellSize * 0.5)
            {
                level -= 0.15; // ~1.7 degrees
                double px = v.CenterX + ax * t, pz = v.CenterZ + az * t;
                bool cut = false;
                for (double off = -20; off <= 20; off += s.CellSize * 0.5)
                {
                    int x = (int)Math.Round((px - az * off) / s.CellSize), z = (int)Math.Round((pz + ax * off) / s.CellSize);
                    if (x < 0 || z < 0 || x >= n || z >= n) continue;
                    int i = z * n + x;
                    if (isOcean[i]) continue;
                    if (height[i] > level) { height[i] = level; cut = true; }
                }
                below = cut ? 0 : below + 1;
            }
        }
    }

    // ---------------------------------------------------------------- mountain ranges

    /// <summary>
    /// Ridged mountain ranges. The mask cut-off is searched so the mountain share (ranges + volcanoes)
    /// lands near <see cref="WorldSettings.MountainTargetShare"/> and never above the cap.
    /// </summary>
    private static void AddMountainRanges(Seed seed, WorldSettings s, double[] coastDist, bool[] isOcean, double[] mountainAdd, double[] volcanoRn)
    {
        int n = s.Size, count = n * n;
        var maskNoise = new Noise(Rng.For(seed.Value, StreamMountainMask));
        var ridgeNoise = new Noise(Rng.For(seed.Value, StreamRidges));

        var raw = new double[count];
        var ridge = new double[count];
        var volcanoScale = new double[count];
        int land = 0;
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            int i = z * n + x;
            if (isOcean[i]) continue;
            land++;
            double wx = x * s.CellSize, wz = z * s.CellSize;
            // Keep a coastal plain: ranges fade out near the sea; and softly towards a volcano's top,
            // so a range never fills the crater or cuts a wall into the cone.
            double fade = SmoothStep(10, 120, coastDist[i]);
            raw[i] = maskNoise.Fbm(wx / 3500.0, wz / 3500.0, 3) - (1 - fade) * 0.7;
            // Near a volcano the range's height fades out over a wide band, so it slopes into the cone instead
            // of ending in a wall.
            volcanoScale[i] = SmoothStep(0.45, 1.3, volcanoRn[i]);
            double r = 1 - Math.Abs(ridgeNoise.Fbm(wx / 1100.0, wz / 1100.0, 5));
            ridge[i] = r * r;
        }
        if (land == 0) return;

        double Range(int i, double cut) => SmoothStep(cut, cut + 0.45, raw[i]) * (40 + s.MountainRangeHeight * ridge[i]) * volcanoScale[i];

        // For each cell, the highest cut-off at which the range still makes it a mountain. Taking a quantile of
        // these hits the target share in one pass instead of a bisection over the whole map.
        int already = 0;
        var critical = new List<double>(land);
        for (int i = 0; i < count; i++)
        {
            if (isOcean[i]) continue;
            if (mountainAdd[i] > s.MountainThreshold) { already++; continue; }
            double peak = (40 + s.MountainRangeHeight * ridge[i]) * volcanoScale[i];
            if (peak <= 0) continue;
            double need = s.MountainThreshold / peak;
            if (need >= 1) continue;
            // Invert the smoothstep: t with t^2(3-2t) = need.
            double lo2 = 0, hi2 = 1;
            for (int k = 0; k < 30; k++) { double m = (lo2 + hi2) / 2; if (m * m * (3 - 2 * m) < need) lo2 = m; else hi2 = m; }
            critical.Add(raw[i] - 0.45 * hi2);
        }
        int wantedCells = (int)(s.MountainTargetShare * land) - already;
        // Volcanoes alone may already use the budget.
        if (wantedCells <= 0 || critical.Count == 0) return;
        critical.Sort();
        critical.Reverse();
        double hi = critical[Math.Min(wantedCells, critical.Count - 1)];
        for (int i = 0; i < count; i++)
            if (!isOcean[i]) mountainAdd[i] = Math.Max(mountainAdd[i], Range(i, hi));
    }

    // ---------------------------------------------------------------- plateaus

    private enum PlateauAccess { Flank, Gully }

    /// <summary>
    /// 2-5 raised, gently undulating areas (300-800 m across) with steep, lobed edges. About 2 in 3 lean against a
    /// mountain or volcano. Access is natural: broad sloping flanks, ravines cut into the edge, or (next to a mountain)
    /// the mountainside itself. Strong defensive spots.
    /// </summary>
    private static List<Plateau> AddPlateaus(Seed seed, WorldSettings s, double[] height, double[] coastDist, bool[] isOcean,
        double[] mountainAdd, bool[] plateauTop, bool[] plateauStamp)
    {
        int n = s.Size, count = n * n;
        var rng = Rng.For(seed.Value, StreamPlateau);
        var noise = new Noise(Rng.For(seed.Value, StreamPlateau + 100));
        var result = new List<Plateau>();
        int wanted = rng.NextInt(s.PlateauMin, s.PlateauMax + 1);

        const double cliffSlope = 2.2; // ~65 degrees: impassable

        // Where would water pool? Plateaus in a basin end up as islands in a lake, so avoid basins.
        var filled = new double[count];
        PriorityFlood(n, height, isOcean, filled, new int[count]);

        // Distance to mountains, to put most plateaus against one.
        var mountain = new bool[count];
        for (int i = 0; i < count; i++) mountain[i] = mountainAdd[i] > s.MountainThreshold;
        var mountainDist = DistanceTransform(n, mountain);

        var nearCandidates = new List<int>();
        var freeCandidates = new List<int>();
        for (int i = 0; i < count; i++)
        {
            if (isOcean[i] || coastDist[i] < 60 || mountainAdd[i] > 5 || filled[i] - height[i] >= 0.3) continue;
            double md = mountainDist[i] * s.CellSize;
            // Close enough that the plateau's edge reaches the mountain's foot.
            if (md >= s.PlateauMinRadius && md <= s.PlateauMaxRadius + 80) nearCandidates.Add(i);
            else if (md > 1000) freeCandidates.Add(i);
        }

        // About 2 in 3 against a mountain: decide per plateau, keep trying that kind for a while, then fall back.
        var wantNearList = new List<bool>();
        for (int k = 0; k < wanted; k++) wantNearList.Add(rng.NextDouble() < 2.0 / 3.0);
        int slotAttempts = 0;
        for (int attempt = 0; attempt < 3500 && result.Count < wanted; attempt++)
        {
            bool wantNear = wantNearList[result.Count];
            if (++slotAttempts > 700) wantNear = !wantNear;
            var pool = wantNear && nearCandidates.Count > 0 ? nearCandidates : freeCandidates.Count > 0 ? freeCandidates : nearCandidates;
            if (pool.Count == 0) break;
            bool near = ReferenceEquals(pool, nearCandidates);

            int c = pool[rng.NextInt(0, pool.Count)];
            int gx = c % n, gz = c / n;
            float cx = gx * s.CellSize, cz = gz * s.CellSize;
            double r = rng.Range(s.PlateauMinRadius, s.PlateauMaxRadius);
            if (near)
            {
                // Edge 0-80 m short of the nearest mountain cell, so the plateau leans against it.
                r = Math.Clamp(mountainDist[c] * s.CellSize - rng.Range(0, 80), s.PlateauMinRadius, s.PlateauMaxRadius);
            }
            double edgeOffset = rng.Range(0, 100);

            bool ok = true;
            foreach (var p in result)
            {
                float dx = p.CenterX - cx, dz = p.CenterZ - cz;
                if (dx * dx + dz * dz < Math.Pow(p.Radius + r + 500, 2)) { ok = false; break; }
            }
            // The top itself: no sea, basins or real mountain; around it (free plateaus) no mountain either.
            int rr = (int)(r * 1.5 / s.CellSize) + 3;
            for (int z = gz - rr; z <= gz + rr && ok; z += 3)
            for (int x = gx - rr; x <= gx + rr && ok; x += 3)
            {
                if (x < 0 || z < 0 || x >= n || z >= n) { ok = false; break; }
                int i = z * n + x;
                double dd = Math.Sqrt((x - gx) * (x - gx) + (z - gz) * (z - gz)) * s.CellSize;
                bool inside = dd <= r * (near ? 0.8 : 1.25);
                if (isOcean[i] || coastDist[i] < 25 || filled[i] - height[i] > 0.3) ok = false;
                else if (inside ? mountainAdd[i] > 40 : (!near && mountainAdd[i] > 5)) ok = false;
            }
            if (!ok) continue;

            double baseTop = height[c] + rng.Range(25, 45);
            // Gently undulating top: mostly a tilt (so few closed hollows), a little wave; scaled below so the
            // whole top spans 3-6 m.
            double topRange = rng.Range(3.3, 5.3);
            double tiltAngle = rng.Range(0, Math.PI * 2);
            double topScale = 1, topMid = 0;
            {
                double lo = double.MaxValue, hi = double.MinValue;
                for (double sx = -r * 1.3; sx <= r * 1.3; sx += 15)
                for (double sz = -r * 1.3; sz <= r * 1.3; sz += 15)
                {
                    // Only the real top (inside the lobed edge, away from the rim).
                    double sd = Math.Sqrt(sx * sx + sz * sz);
                    if (sd > EdgeRadius(sx, sz) - 30) continue;
                    double v = RawTop(sx, sz);
                    lo = Math.Min(lo, v);
                    hi = Math.Max(hi, v);
                }
                topScale = hi > lo ? topRange / (hi - lo) : 0;
                topMid = (hi + lo) / 2;
            }

            // Accesses (1-3): against a mountain the first is the mountainside itself; otherwise the first is a
            // broad flank. The others are flanks or ravines.
            int accessCount = rng.NextInt(1, 4);
            var accesses = new List<(PlateauAccess Kind, double Angle, double HalfWidth, double Slope)>();
            double start = rng.Range(0, Math.PI * 2);
            if (near)
            {
                // Towards the mountain: the edge eases into a gentle saddle that meets the rising mountainside.
                double best = double.MinValue;
                double dist = mountainDist[c] * s.CellSize + 40;
                for (int k = 0; k < 32; k++)
                {
                    double a = k * Math.PI / 16;
                    int sx = Math.Clamp((int)((cx + Math.Cos(a) * dist) / s.CellSize), 0, n - 1);
                    int sz = Math.Clamp((int)((cz + Math.Sin(a) * dist) / s.CellSize), 0, n - 1);
                    int si = sz * n + sx;
                    // Prefer mountain ground that can actually be walked onto (not a cliff face).
                    int sx2 = Math.Clamp(sx + (int)Math.Round(Math.Cos(a) * 2), 0, n - 1), sz2 = Math.Clamp(sz + (int)Math.Round(Math.Sin(a) * 2), 0, n - 1);
                    double rise = Math.Abs(height[sz2 * n + sx2] - height[si]) / (2 * s.CellSize);
                    if (rise > Math.Tan(28 * Math.PI / 180)) continue;
                    if (mountainAdd[si] > best) { best = mountainAdd[si]; start = a; }
                }
                accesses.Add((PlateauAccess.Flank, start, 0.55, Math.Tan(rng.Range(5, 8) * Math.PI / 180)));
            }
            for (int k = accesses.Count; k < accessCount; k++)
            {
                double a = start + k * (Math.PI * 2 / accessCount) + rng.Range(-0.4, 0.4);
                var kind = k == 0 || rng.NextDouble() < 0.5 ? PlateauAccess.Flank : PlateauAccess.Gully;
                if (kind == PlateauAccess.Flank)
                    accesses.Add((kind, a, rng.Range(0.35, 0.7), Math.Tan(rng.Range(8, 12) * Math.PI / 180)));
                else
                    accesses.Add((kind, a, rng.Range(16, 24), 0)); // half width in metres
            }

            double lowest = height[c];
            int look = (int)(r * 2.5 / s.CellSize);
            for (int z = Math.Max(0, gz - look); z <= Math.Min(n - 1, gz + look); z += 3)
            for (int x = Math.Max(0, gx - look); x <= Math.Min(n - 1, gx + look); x += 3)
                if (!isOcean[z * n + x]) lowest = Math.Min(lowest, height[z * n + x]);
            double maxFlank = accesses.Where(a => a.Kind == PlateauAccess.Flank).Min(a => a.Slope);
            double reach = r * 1.3 + (baseTop + 3 - lowest) / maxFlank + 30;
            // Gentle flanks can run far; keep the stamp bounded.
            reach = Math.Min(reach, r * 1.3 + 900);
            int rc = (int)(reach / s.CellSize) + 2;
            int x0 = Math.Max(0, gx - rc), x1 = Math.Min(n - 1, gx + rc);
            int z0 = Math.Max(0, gz - rc), z1 = Math.Min(n - 1, gz + rc);

            int bw = x1 - x0 + 1;
            var backup = new double[bw * (z1 - z0 + 1)];
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
                backup[(z - z0) * bw + (x - x0)] = height[z * n + x];
            var touched = new List<int>();
            var topCells = new List<int>();

            // Ground level where each ravine leaves the plateau (before stamping).
            var gullyGround = new double[accesses.Count];
            for (int k = 0; k < accesses.Count; k++)
            {
                double ax = Math.Cos(accesses[k].Angle), az = Math.Sin(accesses[k].Angle);
                double e = EdgeRadius(ax, az) + 20;
                int ex = Math.Clamp((int)((cx + ax * e) / s.CellSize), 0, n - 1), ez = Math.Clamp((int)((cz + az * e) / s.CellSize), 0, n - 1);
                gullyGround[k] = height[ez * n + ex];
            }

            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                int i = z * n + x;
                if (isOcean[i]) continue;
                double dx = (x - gx) * s.CellSize, dz = (z - gz) * s.CellSize;
                double d = Math.Sqrt(dx * dx + dz * dz);
                double ang = Math.Atan2(dz, dx);
                double edge = EdgeRadius(dx, dz);
                double top = TopAt(dx, dz);

                // Edge: a cliff, except where a flank eases into a broad slope.
                double cliffTarget = top - Math.Max(0, d - edge) * cliffSlope;
                double flankTarget = double.MinValue;
                bool inGully = false;
                double gullyFloor = double.MaxValue;
                for (int k = 0; k < accesses.Count; k++)
                {
                    var acc = accesses[k];
                    if (acc.Kind == PlateauAccess.Flank)
                    {
                        // Measured in metres across the flank's axis: the flank widens as it runs out, and its
                        // sides ease down into the surroundings (~27 degrees) instead of forming walls.
                        double ax = Math.Cos(acc.Angle), az = Math.Sin(acc.Angle);
                        double along = dx * ax + dz * az;
                        if (along <= 0) continue;
                        double lateral = Math.Abs(-dx * az + dz * ax);
                        double edgeAxis = EdgeRadius(ax, az);
                        double halfWidth = r * Math.Sin(acc.HalfWidth) + Math.Max(0, along - edgeAxis) * 0.6;
                        // Debris makes the flank a little uneven.
                        double slope = acc.Slope * (1 + 0.1 * noise.Sample(dx / 60.0 + edgeOffset, dz / 60.0));
                        double ft = top - Math.Max(0, d - edge) * slope - Math.Max(0, lateral - halfWidth) * 0.45;
                        flankTarget = Math.Max(flankTarget, ft);
                    }
                    else
                    {
                        // Ravine cut into the edge: its floor descends to the ground right at the edge.
                        double ax = Math.Cos(acc.Angle), az = Math.Sin(acc.Angle);
                        double along = dx * ax + dz * az;
                        double across = Math.Abs(-dx * az + dz * ax);
                        if (along <= 0) continue;
                        double edgeHere = EdgeRadius(ax, az);
                        double drop = top - gullyGround[k];
                        double length = Math.Clamp(drop / Math.Tan(10 * Math.PI / 180), 60, edgeHere * 0.8);
                        double floor = along >= edgeHere ? gullyGround[k]
                            : top - drop * Math.Clamp((along - (edgeHere - length)) / length, 0, 1);
                        // U-shaped: flat floor, steep walls.
                        double wall = Math.Max(0, across - acc.HalfWidth) * 1.6;
                        if (across < acc.HalfWidth + 40)
                        {
                            inGully = true;
                            gullyFloor = Math.Min(gullyFloor, floor + wall);
                        }
                    }
                }

                double target;
                if (d <= edge)
                {
                    target = top;
                    // Part of the flat top (not of a ravine cutting into it).
                    if (!inGully || gullyFloor >= top - 0.5) topCells.Add(i);
                }
                else
                {
                    target = Math.Max(cliffTarget, flankTarget);
                    if (target <= height[i] && !inGully) continue;
                }
                if (inGully) target = Math.Min(target, Math.Max(gullyFloor, height[i] - 0.0));

                if (d <= edge || target > height[i])
                {
                    height[i] = d <= edge ? target : Math.Max(height[i], target);
                    touched.Add(i);
                }
            }

            // There must be a walkable (<= 35 degrees) way from the top down past the edge.
            if (!HasWalkableWayDown(s, height, isOcean, gx, gz, r * 1.25 + 60))
            {
                for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    height[z * n + x] = backup[(z - z0) * bw + (x - x0)];
                continue;
            }
            // Against a mountain, the mountainside must really be a way up: walk from the top onto mountain ground
            // well above it without dropping more than a few metres. If not, it's just a plateau near a mountain.
            bool againstMountain = near && HasWayOntoMountain(s, height, isOcean, mountainAdd, gx, gz, baseTop, r * 2.5);
            if (near && !againstMountain)
            {
                // Meant to lean against a mountain but the mountainside isn't climbable here: try another spot.
                for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    height[z * n + x] = backup[(z - z0) * bw + (x - x0)];
                continue;
            }
            foreach (int i in touched) plateauStamp[i] = true;
            foreach (int i in topCells) plateauTop[i] = true;
            result.Add(new Plateau(cx, cz, (float)r, (float)baseTop, accessCount, againstMountain));
            slotAttempts = 0;

            double EdgeRadius(double dx, double dz)
            {
                double a = Math.Atan2(dz, dx);
                // Lobed, eroded outline.
                return r * (1 + 0.3 * noise.Fbm(Math.Cos(a) * 2.0 + edgeOffset, Math.Sin(a) * 2.0, 3));
            }

            double RawTop(double dx, double dz)
            {
                double w = 0.35 * noise.Fbm((cx + dx) / 260.0 + edgeOffset, (cz + dz) / 260.0, 2);
                double t = (dx * Math.Cos(tiltAngle) + dz * Math.Sin(tiltAngle)) / r;
                return w + t;
            }

            double TopAt(double dx, double dz) => baseTop + (RawTop(dx, dz) - topMid) * topScale;
        }
        return result;
    }

    /// <summary>Walk from the plateau top over climbable cells that never drop more than 5 m below the top, until a
    /// mountain cell at least 20 m above the top is reached.</summary>
    private static bool HasWayOntoMountain(WorldSettings s, double[] height, bool[] isOcean, double[] mountainAdd,
        int gx, int gz, double top, double maxDistance)
    {
        int n = s.Size;
        var seen = new HashSet<int> { gz * n + gx };
        var q = new Queue<int>();
        q.Enqueue(gz * n + gx);
        while (q.Count > 0)
        {
            int c = q.Dequeue();
            int cx = c % n, cz = c / n;
            for (int k = 0; k < 4; k++)
            {
                int x = cx + Dx8[k], z = cz + Dz8[k];
                if (x < 1 || z < 1 || x >= n - 1 || z >= n - 1) continue;
                int j = z * n + x;
                if (isOcean[j] || !seen.Add(j)) continue;
                if (height[j] < top - 5) continue;
                if (CellSlopeDegrees(height, n, s.CellSize, x, z) > s.SlopeClimbMax) continue;
                double dx = (x - gx) * s.CellSize, dz = (z - gz) * s.CellSize;
                if (dx * dx + dz * dz > maxDistance * maxDistance) continue;
                if (mountainAdd[j] > s.MountainThreshold && height[j] >= top + 20) return true;
                q.Enqueue(j);
            }
        }
        return false;
    }

    /// <summary>Breadth-first walk over cells no steeper than the climb limit, from (gx, gz) until <paramref name="escape"/> metres away.</summary>
    private static bool HasWalkableWayDown(WorldSettings s, double[] height, bool[] isOcean, int gx, int gz, double escape)
    {
        int n = s.Size;
        var seen = new HashSet<int> { gz * n + gx };
        var q = new Queue<int>();
        q.Enqueue(gz * n + gx);
        while (q.Count > 0)
        {
            int c = q.Dequeue();
            int cx = c % n, cz = c / n;
            for (int k = 0; k < 4; k++)
            {
                int x = cx + Dx8[k], z = cz + Dz8[k];
                if (x < 1 || z < 1 || x >= n - 1 || z >= n - 1) continue;
                int j = z * n + x;
                if (isOcean[j] || !seen.Add(j)) continue;
                if (CellSlopeDegrees(height, n, s.CellSize, x, z) > s.SlopeClimbMax) continue;
                double dx = (x - gx) * s.CellSize, dz = (z - gz) * s.CellSize;
                if (dx * dx + dz * dz > escape * escape) return true;
                q.Enqueue(j);
            }
        }
        return false;
    }

    // ---------------------------------------------------------------- coastal cliffs

    /// <summary>
    /// Turns stretches of the coast (target ~28%, cap 35% of coastline) into cliffs: the land rises steeply
    /// straight out of the sea instead of a beach. Returns the cliff zone (no beach there).
    /// </summary>
    private static bool[] AddCoastCliffs(Seed seed, WorldSettings s, double[] height, double[] coastDist, bool[] isOcean, bool[] valley)
    {
        int n = s.Size, count = n * n;
        var zoneNoise = new Noise(Rng.For(seed.Value, StreamCliffs));
        var heightNoise = new Noise(Rng.For(seed.Value, StreamCliffs + 100));
        double centre = (n - 1) / 2.0;

        var value = new double[count];
        var coastValues = new List<double>();
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            int i = z * n + x;
            if (isOcean[i]) continue;
            double a = Math.Atan2(z - centre, x - centre);
            // Sampled on a circle, so there's no seam at +-180 degrees.
            value[i] = zoneNoise.Fbm(Math.Cos(a) * 2.2 + 11, Math.Sin(a) * 2.2 + 11, 3);
            if (coastDist[i] < 1.5) coastValues.Add(value[i]);
        }
        var zone = new bool[count];
        if (coastValues.Count == 0) return zone;
        coastValues.Sort();
        int cutIndex = Math.Clamp((int)(coastValues.Count * (1 - s.CoastCliffTargetShare)), 0, coastValues.Count - 1);
        double cut = coastValues[cutIndex];

        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            int i = z * n + x;
            // River mouths stay open (the valley reaches the sea there), so rivers don't end in a waterfall.
            if (isOcean[i] || value[i] <= cut || valley[i]) continue;
            zone[i] = true;
            double d = coastDist[i];
            if (d > 30) continue;
            double a = Math.Atan2(z - centre, x - centre);
            double taper = SmoothStep(cut, cut + 0.015, value[i]);
            double cliffTop = (16 + 24 * (heightNoise.Fbm(Math.Cos(a) * 3 + 3, Math.Sin(a) * 3 + 3, 2) * 0.5 + 0.5)) * taper;
            double target = d <= 10 ? cliffTop : cliffTop - (d - 10) * 1.0;
            if (target > height[i]) height[i] = target;
        }
        return zone;
    }

    // ---------------------------------------------------------------- river valleys

    /// <summary>
    /// Runs the drainage once to find the rivers, then lowers the land around them into valleys whose width
    /// grows with the river's upstream area. The real drainage is computed afterwards on the carved terrain.
    /// </summary>
    private static bool[] CarveRiverValleys(int n, WorldSettings s, double[] height, bool[] isOcean, bool[] protect)
    {
        int count = n * n;
        var h = (double[])height.Clone();
        var filled = new double[count];
        var parent = new int[count];
        var order = PriorityFlood(n, h, isOcean, filled, parent);
        var acc = Accumulate(order, parent, isOcean, count);

        var valley = new bool[count];
        var source = new int[count];
        Array.Fill(source, -1);
        var queue = new Queue<int>();
        for (int i = 0; i < count; i++)
        {
            if (isOcean[i] || acc[i] < s.RiverMinCells) continue;
            source[i] = i;
            queue.Enqueue(i);
        }

        double Width(int i)
        {
            double t = Math.Clamp(Math.Log((double)acc[i] / s.RiverMinCells) / Math.Log(40), 0, 1);
            return 40 + 110 * t; // metres
        }

        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            int src = source[c];
            int sx = src % n, sz = src / n;
            double w = Width(src);
            double riverH = filled[src];
            int cx = c % n, cz = c / n;
            valley[c] = true;

            if (c != src && !protect[c] && height[c] > riverH)
            {
                double dx = (cx - sx) * s.CellSize, dz = (cz - sz) * s.CellSize;
                double t = Math.Sqrt(dx * dx + dz * dz) / w;
                double target = riverH + (height[c] - riverH) * Math.Pow(t, 1.5);
                if (target < height[c]) height[c] = target;
            }

            for (int k = 0; k < 8; k++)
            {
                int x = cx + Dx8[k], z = cz + Dz8[k];
                if (x < 0 || z < 0 || x >= n || z >= n) continue;
                int j = z * n + x;
                if (source[j] >= 0 || isOcean[j]) continue;
                double ddx = (x - sx) * s.CellSize, ddz = (z - sz) * s.CellSize;
                if (ddx * ddx + ddz * ddz >= w * w) continue;
                source[j] = src;
                queue.Enqueue(j);
            }
        }
        return valley;
    }

    private static int[] Accumulate(int[] order, int[] parent, bool[] isOcean, int count)
    {
        var acc = new int[count];
        for (int i = 0; i < count; i++) acc[i] = isOcean[i] ? 0 : 1;
        // Upstream cells were processed later in the flood, so walk the order backwards.
        for (int k = order.Length - 1; k >= 0; k--)
        {
            int c = order[k];
            int p = parent[c];
            if (p >= 0 && !isOcean[p]) acc[p] += acc[c];
        }
        return acc;
    }

    // ---------------------------------------------------------------- slopes

    private static float[] ComputeSlope(int n, float cellSize, float[] height, out SlopeClass[] classes, WorldSettings s)
    {
        var h = new double[height.Length];
        for (int i = 0; i < h.Length; i++) h[i] = height[i];
        var slope = new float[n * n];
        classes = new SlopeClass[n * n];
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            int i = z * n + x;
            double deg = CellSlopeDegrees(h, n, cellSize, x, z);
            slope[i] = (float)deg;
            classes[i] = Classify(deg, s);
        }
        return slope;
    }

    /// <summary>
    /// Slope of a cell in degrees, using the steepest one-sided step per axis, so a single 10 m cliff step
    /// counts as a cliff on both sides instead of being averaged away.
    /// </summary>
    private static double CellSlopeDegrees(double[] height, int n, float cellSize, int x, int z)
    {
        double hc = height[z * n + x];
        double gx = 0, gz = 0;
        if (x > 0) gx = Math.Max(gx, Math.Abs(hc - height[z * n + x - 1]));
        if (x < n - 1) gx = Math.Max(gx, Math.Abs(height[z * n + x + 1] - hc));
        if (z > 0) gz = Math.Max(gz, Math.Abs(hc - height[(z - 1) * n + x]));
        if (z < n - 1) gz = Math.Max(gz, Math.Abs(height[(z + 1) * n + x] - hc));
        gx /= cellSize;
        gz /= cellSize;
        return Math.Atan(Math.Sqrt(gx * gx + gz * gz)) * 180 / Math.PI;
    }

    /// <summary>GDD slope classes: 0-10 normal, 10-20 slow, 20-35 climb, above 35 impassable.</summary>
    public static SlopeClass Classify(double degrees, WorldSettings? settings = null)
    {
        var s = settings ?? WorldSettings.Default;
        if (degrees <= s.SlopeNormalMax) return SlopeClass.Normal;
        if (degrees <= s.SlopeSlowMax) return SlopeClass.Slow;
        if (degrees <= s.SlopeClimbMax) return SlopeClass.Climb;
        return SlopeClass.Impassable;
    }
}
