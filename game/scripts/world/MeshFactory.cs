using Godot;

namespace Kingdom.Game.World;

/// <summary>
/// Builds the low-poly placeholder props (trees, rocks, bushes, apple trees, houses) from Godot primitives.
/// Every prop is one ArrayMesh with one surface per coloured part, so it can be drawn with a MultiMesh.
/// </summary>
public static class MeshFactory
{
    private static Shader? _flatShader;

    public static ShaderMaterial FlatMaterial(Color color, float roughness = 0.9f)
    {
        _flatShader ??= GD.Load<Shader>("res://shaders/flat_color.gdshader");
        var mat = new ShaderMaterial { Shader = _flatShader };
        mat.SetShaderParameter("albedo", color);
        mat.SetShaderParameter("roughness", roughness);
        return mat;
    }

    /// <summary>Pine-like tree: short trunk and a 6-sided cone. About 8 m tall at scale 1.</summary>
    public static ArrayMesh Tree()
    {
        var mesh = new ArrayMesh();
        AddPart(mesh, new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0.3f, Height = 2.0f, RadialSegments = 5, Rings = 1 },
            new Vector3(0, 1.0f, 0), Vector3.One, FlatMaterial(new Color(0.36f, 0.25f, 0.16f)));
        AddPart(mesh, new CylinderMesh { TopRadius = 0f, BottomRadius = 2.0f, Height = 6.5f, RadialSegments = 6, Rings = 0, CapTop = false },
            new Vector3(0, 1.6f + 3.25f, 0), Vector3.One, FlatMaterial(new Color(0.16f, 0.36f, 0.18f)));
        return mesh;
    }

    /// <summary>Grey boulder.</summary>
    public static ArrayMesh Rock()
    {
        var mesh = new ArrayMesh();
        AddPart(mesh, new SphereMesh { Radius = 1.2f, Height = 1.8f, RadialSegments = 6, Rings = 3 },
            new Vector3(0, 0.5f, 0), new Vector3(1.0f, 0.8f, 0.85f), FlatMaterial(new Color(0.55f, 0.55f, 0.53f)));
        return mesh;
    }

    /// <summary>Berry bush: green ball with a few dark-red berry clumps.</summary>
    public static ArrayMesh BerryBush()
    {
        var mesh = new ArrayMesh();
        AddPart(mesh, new SphereMesh { Radius = 0.9f, Height = 1.4f, RadialSegments = 6, Rings = 3 },
            new Vector3(0, 0.6f, 0), Vector3.One, FlatMaterial(new Color(0.22f, 0.42f, 0.20f)));
        var berry = FlatMaterial(new Color(0.55f, 0.08f, 0.22f), 0.5f);
        foreach (var offset in new[] { new Vector3(0.55f, 0.95f, 0.2f), new Vector3(-0.4f, 1.0f, 0.45f), new Vector3(0.1f, 1.1f, -0.55f) })
            AddPart(mesh, new SphereMesh { Radius = 0.18f, Height = 0.36f, RadialSegments = 5, Rings = 2 }, offset, Vector3.One, berry);
        return mesh;
    }

    /// <summary>Apple tree: trunk, round light-green crown, a few red apples.</summary>
    public static ArrayMesh AppleTree()
    {
        var mesh = new ArrayMesh();
        AddPart(mesh, new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.28f, Height = 2.2f, RadialSegments = 5, Rings = 1 },
            new Vector3(0, 1.1f, 0), Vector3.One, FlatMaterial(new Color(0.40f, 0.28f, 0.18f)));
        AddPart(mesh, new SphereMesh { Radius = 1.8f, Height = 3.0f, RadialSegments = 7, Rings = 4 },
            new Vector3(0, 3.3f, 0), Vector3.One, FlatMaterial(new Color(0.36f, 0.55f, 0.22f)));
        var apple = FlatMaterial(new Color(0.80f, 0.12f, 0.10f), 0.5f);
        foreach (var offset in new[] { new Vector3(1.3f, 3.0f, 0.6f), new Vector3(-1.1f, 3.4f, 0.9f), new Vector3(0.3f, 2.6f, -1.4f), new Vector3(-0.7f, 4.2f, -0.9f) })
            AddPart(mesh, new SphereMesh { Radius = 0.22f, Height = 0.44f, RadialSegments = 5, Rings = 2 }, offset, Vector3.One, apple);
        return mesh;
    }

    /// <summary>Small house: walls and a pitched roof in the given roof colour. About 5 m tall.</summary>
    public static ArrayMesh House(Color roofColor)
    {
        var mesh = new ArrayMesh();
        AddPart(mesh, new BoxMesh { Size = new Vector3(4f, 3f, 5f) },
            new Vector3(0, 1.5f, 0), Vector3.One, FlatMaterial(new Color(0.78f, 0.70f, 0.56f)));
        AddPart(mesh, new PrismMesh { Size = new Vector3(4.6f, 2.2f, 5.6f) },
            new Vector3(0, 3f + 1.1f, 0), Vector3.One, FlatMaterial(roofColor));
        return mesh;
    }

    /// <summary>Flag pole with a coloured banner, marks the village centre.</summary>
    public static ArrayMesh Banner(Color color)
    {
        var mesh = new ArrayMesh();
        AddPart(mesh, new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.15f, Height = 12f, RadialSegments = 5, Rings = 1 },
            new Vector3(0, 6f, 0), Vector3.One, FlatMaterial(new Color(0.35f, 0.26f, 0.18f)));
        AddPart(mesh, new BoxMesh { Size = new Vector3(3.2f, 2.0f, 0.1f) },
            new Vector3(1.7f, 10.5f, 0), Vector3.One, FlatMaterial(color, 0.6f));
        return mesh;
    }

    /// <summary>Copies a primitive into <paramref name="target"/> as a new surface, scaled then moved by offset.</summary>
    private static void AddPart(ArrayMesh target, PrimitiveMesh part, Vector3 offset, Vector3 scale, Material material)
    {
        var arrays = part.GetMeshArrays();
        var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        for (int i = 0; i < verts.Length; i++)
        {
            verts[i] = verts[i] * scale + offset;
            if (i < normals.Length)
                normals[i] = (normals[i] / scale).Normalized();
        }
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        target.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        target.SurfaceSetMaterial(target.GetSurfaceCount() - 1, material);
    }
}
