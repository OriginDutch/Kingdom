using System;
using System.Collections.Generic;
using Godot;
using Kingdom.Core.World;

namespace Kingdom.Game.World;

/// <summary>
/// Turns a <see cref="WorldMap"/> into 3D: terrain chunks, sea, lakes and rivers, forests,
/// resource props and village markers. Pure presentation; all rules live in Kingdom.Core.
/// </summary>
public partial class WorldView : Node3D
{
    private const int ChunkCells = 100;          // 100 cells = 1 km per terrain chunk
    private const float TreeChance = 0.55f;      // share of forest cells that get a tree
    private const float TreeViewDistance = 3500f;
    private const float VillageClearRadius = 70f; // trees removed around a village centre

    private WorldMap? _map;
    private uint _seedSalt; // mixes the world seed into prop placement
    private Node3D? _terrainRoot, _waterRoot, _treeRoot, _propRoot, _villageRoot;
    private ArrayMesh? _treeMesh;

    // Trees per terrain chunk, so a chunk can be rebuilt when a village clears its spot.
    private readonly Dictionary<(int, int), List<Transform3D>> _treesByChunk = new();
    private readonly Dictionary<(int, int), MultiMeshInstance3D> _treeNodes = new();
    private IReadOnlyList<VillageSite> _shownVillages = Array.Empty<VillageSite>();

    public WorldMap? Map => _map;

    /// <summary>Removes the previous world and builds this one.</summary>
    public void Build(WorldMap map)
    {
        Clear();
        _map = map;
        _seedSalt = (uint)(map.Seed.Value ^ (map.Seed.Value >> 32));

        _terrainRoot = AddRoot("Terrain");
        _waterRoot = AddRoot("Water");
        _treeRoot = AddRoot("Trees");
        _propRoot = AddRoot("Resources");
        _villageRoot = AddRoot("Villages");

        BuildTerrain(map);
        BuildWater(map);
        BuildTrees(map);
        BuildResources(map);
    }

    public void Clear()
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _treesByChunk.Clear();
        _treeNodes.Clear();
        _shownVillages = Array.Empty<VillageSite>();
        _map = null;
    }

    private Node3D AddRoot(string name)
    {
        var node = new Node3D { Name = name };
        AddChild(node);
        return node;
    }

    // ------------------------------------------------------------------ terrain

    private void BuildTerrain(WorldMap map)
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/terrain.gdshader") };
        int n = map.Size;
        for (int cz = 0; cz * ChunkCells < n - 1; cz++)
        for (int cx = 0; cx * ChunkCells < n - 1; cx++)
        {
            int x0 = cx * ChunkCells, z0 = cz * ChunkCells;
            int x1 = Math.Min(x0 + ChunkCells, n - 1), z1 = Math.Min(z0 + ChunkCells, n - 1);
            var mesh = BuildTerrainChunk(map, x0, z0, x1, z1);
            var inst = new MeshInstance3D
            {
                Name = $"Chunk_{cx}_{cz}",
                Mesh = mesh,
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            };
            _terrainRoot!.AddChild(inst);
        }
    }

    private ArrayMesh BuildTerrainChunk(WorldMap map, int x0, int z0, int x1, int z1)
    {
        int w = x1 - x0 + 1, h = z1 - z0 + 1;
        var verts = new Vector3[w * h];
        var normals = new Vector3[w * h];
        var colors = new Color[w * h];
        float cs = map.CellSize;

        for (int z = 0; z < h; z++)
        for (int x = 0; x < w; x++)
        {
            int gx = x0 + x, gz = z0 + z;
            int i = map.Index(gx, gz);
            int v = z * w + x;
            verts[v] = new Vector3(gx * cs, map.Height[i], gz * cs);

            // Smooth normal from neighbouring heights (the shader turns it into flat facets).
            float hl = map.Height[map.Index(Math.Max(gx - 1, 0), gz)];
            float hr = map.Height[map.Index(Math.Min(gx + 1, map.Size - 1), gz)];
            float hd = map.Height[map.Index(gx, Math.Max(gz - 1, 0))];
            float hu = map.Height[map.Index(gx, Math.Min(gz + 1, map.Size - 1))];
            normals[v] = new Vector3(hl - hr, 2 * cs, hd - hu).Normalized();

            colors[v] = TerrainColor(map, i, gx, gz);
        }

        var indices = new int[(w - 1) * (h - 1) * 6];
        int k = 0;
        for (int z = 0; z < h - 1; z++)
        for (int x = 0; x < w - 1; x++)
        {
            int a = z * w + x, b = a + 1, c = a + w, d = c + 1;
            // Clockwise seen from above (Godot's front-face winding).
            indices[k++] = a; indices[k++] = b; indices[k++] = d;
            indices[k++] = a; indices[k++] = d; indices[k++] = c;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private Color TerrainColor(WorldMap map, int i, int gx, int gz)
    {
        // Small per-cell variation so large areas don't look flat-painted.
        float jitter = (Hash(gx, gz) - 0.5f) * 0.05f;
        float height = map.Height[i];
        Color c;
        switch (map.Terrain[i])
        {
            case TerrainType.Ocean:
                float depth = Mathf.Clamp(-height / 30f, 0, 1);
                c = new Color(0.80f, 0.74f, 0.55f).Lerp(new Color(0.35f, 0.40f, 0.38f), depth);
                break;
            case TerrainType.Beach:
                c = new Color(0.88f, 0.82f, 0.60f);
                break;
            case TerrainType.Lake:
            case TerrainType.River:
                c = new Color(0.50f, 0.46f, 0.34f);
                break;
            default:
                if (map.Forest[i])
                {
                    c = new Color(0.25f, 0.40f, 0.19f);
                }
                else
                {
                    float t = Mathf.Clamp(height / 40f, 0, 1);
                    c = new Color(0.45f, 0.64f, 0.29f).Lerp(new Color(0.58f, 0.60f, 0.34f), t);
                }
                break;
        }
        return new Color(c.R + jitter, c.G + jitter, c.B + jitter * 0.5f);
    }

    // ------------------------------------------------------------------ water

    private void BuildWater(WorldMap map)
    {
        var shader = GD.Load<Shader>("res://shaders/water.gdshader");

        // Sea: one big plane at sea level, larger than the map so the horizon is water.
        var seaMaterial = new ShaderMaterial { Shader = shader };
        var sea = new MeshInstance3D
        {
            Name = "Sea",
            Mesh = new PlaneMesh { Size = new Vector2(map.Extent * 4, map.Extent * 4) },
            MaterialOverride = seaMaterial,
            Position = new Vector3(map.Extent / 2, 0, map.Extent / 2),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _waterRoot!.AddChild(sea);

        // Opaque sea floor outside the map, so the map edge doesn't show as a hard square through the water.
        _waterRoot.AddChild(new MeshInstance3D
        {
            Name = "SeaFloor",
            Mesh = new PlaneMesh { Size = new Vector2(map.Extent * 4, map.Extent * 4) },
            MaterialOverride = MeshFactory.FlatMaterial(new Color(0.35f, 0.40f, 0.38f), 1f),
            Position = new Vector3(map.Extent / 2, (float)map.Settings.SeaFloorDepth - 1f, map.Extent / 2),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        // Lakes and rivers: one quad per water cell at its water level.
        var freshMaterial = new ShaderMaterial { Shader = shader };
        freshMaterial.SetShaderParameter("water_color", new Color(0.16f, 0.42f, 0.55f));
        int n = map.Size;
        float half = map.CellSize * 0.5f;
        for (int cz = 0; cz * ChunkCells < n; cz++)
        for (int cx = 0; cx * ChunkCells < n; cx++)
        {
            var verts = new List<Vector3>();
            var indices = new List<int>();
            int x1 = Math.Min((cx + 1) * ChunkCells, n), z1 = Math.Min((cz + 1) * ChunkCells, n);
            for (int z = cz * ChunkCells; z < z1; z++)
            for (int x = cx * ChunkCells; x < x1; x++)
            {
                int i = map.Index(x, z);
                if (map.Terrain[i] is not (TerrainType.Lake or TerrainType.River)) continue;
                float y = map.WaterLevel[i];
                float wx = x * map.CellSize, wz = z * map.CellSize;
                int b = verts.Count;
                verts.Add(new Vector3(wx - half, y, wz - half));
                verts.Add(new Vector3(wx + half, y, wz - half));
                verts.Add(new Vector3(wx - half, y, wz + half));
                verts.Add(new Vector3(wx + half, y, wz + half));
                indices.AddRange(new[] { b, b + 1, b + 3, b, b + 3, b + 2 });
            }
            if (verts.Count == 0) continue;

            var normals = new Vector3[verts.Count];
            Array.Fill(normals, Vector3.Up);
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = normals;
            arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            _waterRoot.AddChild(new MeshInstance3D
            {
                Name = $"FreshWater_{cx}_{cz}",
                Mesh = mesh,
                MaterialOverride = freshMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
    }

    // ------------------------------------------------------------------ trees

    private void BuildTrees(WorldMap map)
    {
        _treeMesh ??= MeshFactory.Tree();
        int n = map.Size;
        float cs = map.CellSize;

        // Keep trees off resource nodes so bushes and rocks don't clip into pines.
        var blocked = new HashSet<int>();
        foreach (var cluster in map.Resources)
        foreach (var (nx, nz) in cluster.Nodes)
        {
            if (!map.TryGetCell(nx, nz, out int bx, out int bz)) continue;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                if (map.InBounds(bx + dx, bz + dz)) blocked.Add(map.Index(bx + dx, bz + dz));
        }
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            int i = map.Index(x, z);
            if (!map.Forest[i] || blocked.Contains(i)) continue;
            if (Hash(x, z) > TreeChance) continue;

            float jx = (Hash(x + 7919, z) - 0.5f) * cs * 0.8f;
            float jz = (Hash(x, z + 7919) - 0.5f) * cs * 0.8f;
            float px = x * cs + jx, pz = z * cs + jz;
            float scale = 0.75f + Hash(x + 31, z + 17) * 0.6f;
            float rot = Hash(x + 101, z + 53) * Mathf.Tau;
            var basis = new Basis(Vector3.Up, rot).Scaled(new Vector3(scale, scale, scale));
            var t = new Transform3D(basis, new Vector3(px, map.GroundHeightAt(px, pz) - 0.2f, pz));

            var key = (x / ChunkCells, z / ChunkCells);
            if (!_treesByChunk.TryGetValue(key, out var list))
            {
                list = new List<Transform3D>();
                _treesByChunk[key] = list;
            }
            list.Add(t);
        }
        foreach (var key in _treesByChunk.Keys)
            RebuildTreeChunk(key, Array.Empty<VillageSite>());
    }

    private void RebuildTreeChunk((int, int) key, IReadOnlyList<VillageSite> villages)
    {
        if (_map == null || _treeRoot == null) return;
        if (_treeNodes.TryGetValue(key, out var old))
        {
            _treeRoot.RemoveChild(old);
            old.QueueFree();
            _treeNodes.Remove(key);
        }

        var kept = new List<Transform3D>();
        foreach (var t in _treesByChunk[key])
        {
            bool cleared = false;
            foreach (var v in villages)
            {
                float dx = t.Origin.X - v.CellX * _map.CellSize, dz = t.Origin.Z - v.CellZ * _map.CellSize;
                if (dx * dx + dz * dz < VillageClearRadius * VillageClearRadius) { cleared = true; break; }
            }
            if (!cleared) kept.Add(t);
        }
        if (kept.Count == 0) return;

        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = _treeMesh,
        };
        mm.InstanceCount = kept.Count;
        for (int i = 0; i < kept.Count; i++) mm.SetInstanceTransform(i, kept[i]);

        var node = new MultiMeshInstance3D
        {
            Name = $"Trees_{key.Item1}_{key.Item2}",
            Multimesh = mm,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            VisibilityRangeEnd = TreeViewDistance,
            VisibilityRangeEndMargin = 300f,
            VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
        };
        _treeRoot.AddChild(node);
        _treeNodes[key] = node;
    }

    // ------------------------------------------------------------------ resources

    private void BuildResources(WorldMap map)
    {
        AddResourceProps(map, ResourceType.Stone, MeshFactory.Rock());
        AddResourceProps(map, ResourceType.Berries, MeshFactory.BerryBush());
        AddResourceProps(map, ResourceType.Apples, MeshFactory.AppleTree());
    }

    private void AddResourceProps(WorldMap map, ResourceType type, ArrayMesh mesh)
    {
        var transforms = new List<Transform3D>();
        int seq = 0;
        foreach (var cluster in map.Resources)
        {
            if (cluster.Type != type) continue;
            foreach (var (x, z) in cluster.Nodes)
            {
                seq++;
                float scale = 0.8f + Hash(seq, (int)type) * 0.6f;
                float rot = Hash((int)type, seq) * Mathf.Tau;
                var basis = new Basis(Vector3.Up, rot).Scaled(new Vector3(scale, scale, scale));
                transforms.Add(new Transform3D(basis, new Vector3(x, map.GroundHeightAt(x, z) - 0.1f, z)));
            }
        }
        if (transforms.Count == 0) return;

        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh };
        mm.InstanceCount = transforms.Count;
        for (int i = 0; i < transforms.Count; i++) mm.SetInstanceTransform(i, transforms[i]);
        _propRoot!.AddChild(new MultiMeshInstance3D
        {
            Name = type.ToString(),
            Multimesh = mm,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        });
    }

    // ------------------------------------------------------------------ villages

    /// <summary>Redraws all village markers and clears trees around them.</summary>
    public void ShowVillages(IReadOnlyList<VillageSite> villages)
    {
        if (_map == null || _villageRoot == null) return;
        foreach (Node child in _villageRoot.GetChildren())
        {
            _villageRoot.RemoveChild(child);
            child.QueueFree();
        }

        foreach (var v in villages)
            _villageRoot.AddChild(BuildVillage(_map, v));

        // Only rebuild tree chunks near old or new villages; rebuilding all of them would hitch.
        var affected = new HashSet<(int, int)>();
        foreach (var v in _shownVillages) AddAffectedChunks(v, affected);
        foreach (var v in villages) AddAffectedChunks(v, affected);
        _shownVillages = new List<VillageSite>(villages);
        foreach (var key in affected)
            if (_treesByChunk.ContainsKey(key)) RebuildTreeChunk(key, _shownVillages);
    }

    private void AddAffectedChunks(VillageSite v, HashSet<(int, int)> into)
    {
        if (_map == null) return;
        int r = (int)MathF.Ceiling(VillageClearRadius / _map.CellSize) + 1;
        for (int z = (v.CellZ - r) / ChunkCells; z <= (v.CellZ + r) / ChunkCells; z++)
        for (int x = (v.CellX - r) / ChunkCells; x <= (v.CellX + r) / ChunkCells; x++)
            into.Add((x, z));
    }

    private static Node3D BuildVillage(WorldMap map, VillageSite v)
    {
        bool isPlayer = v.OwnerId == VillagePlacement.PlayerId;
        var color = isPlayer ? new Color(0.20f, 0.40f, 0.85f) : new Color(0.80f, 0.18f, 0.15f);
        float cx = v.CellX * map.CellSize, cz = v.CellZ * map.CellSize;

        var root = new Node3D { Name = $"Village_{v.OwnerId}", Position = new Vector3(cx, 0, cz) };

        var banner = new MeshInstance3D { Mesh = MeshFactory.Banner(color) };
        banner.Position = new Vector3(0, map.GroundHeightAt(cx, cz), 0);
        root.AddChild(banner);

        var houseMesh = MeshFactory.House(color.Darkened(0.2f));
        const int houses = 6;
        for (int i = 0; i < houses; i++)
        {
            float ang = i * Mathf.Tau / houses + 0.3f;
            float r = 16f + (i % 2) * 6f;
            float hx = Mathf.Cos(ang) * r, hz = Mathf.Sin(ang) * r;
            var house = new MeshInstance3D { Mesh = houseMesh };
            house.Position = new Vector3(hx, map.GroundHeightAt(cx + hx, cz + hz), hz);
            // Face the centre.
            house.Rotation = new Vector3(0, -ang + Mathf.Pi / 2, 0);
            root.AddChild(house);
        }

        var label = new Label3D
        {
            Text = v.Name,
            FontSize = 48,
            OutlineSize = 12,
            Modulate = Colors.White,
            OutlineModulate = new Color(0, 0, 0, 0.8f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FixedSize = true,
            PixelSize = 0.0008f,
            NoDepthTest = true,
            Position = new Vector3(0, map.GroundHeightAt(cx, cz) + 18f, 0),
        };
        root.AddChild(label);
        return root;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Deterministic pseudo-random value in [0, 1) for a cell; keeps visuals stable per seed.</summary>
    private float Hash(int x, int z)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)z * 668265263u + _seedSalt * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
