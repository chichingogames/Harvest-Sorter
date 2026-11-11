using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Applies the tileable surface textures from Art Data/HS_Surface_Materials.json to every
/// Materials/&lt;Model&gt;/&lt;Model&gt;_&lt;Swatch&gt;.mat. Re-runnable. The models' UV0 is already in units of each
/// surface's tileMeters (Tools/Blender/split_palette_materials.py), so material tiling stays (1, 1).
/// </summary>
public static class SurfaceMaterialBuilder
{
    private const string SpecPath = "Assets/Game Assets/Art Data/HS_Surface_Materials.json";
    private const string TexturesPath = "Assets/Game Assets/Textures/Surfaces/";
    private const string MaterialsPath = "Assets/Game Assets/Materials";

    [Serializable] private class Spec { public Surface[] materials; }
    [Serializable] private class Surface { public string sourceName; public string albedo; public string normal; public float tileMeters; public float metallic; public float roughness; }

    [MenuItem("Harvest Sorter/Apply Surface Textures")]
    public static void Apply()
    {
        var spec = JsonUtility.FromJson<Spec>(File.ReadAllText(SpecPath));
        var bySuffix = spec.materials.ToDictionary(s => Pascal(s.sourceName));

        ConfigureTextures(spec.materials);

        int applied = 0;
        var unmatched = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialsPath }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var model = Path.GetFileName(Path.GetDirectoryName(path));
            var name = Path.GetFileNameWithoutExtension(path);
            if (!name.StartsWith(model + "_") || !bySuffix.TryGetValue(name.Substring(model.Length + 1), out var surface))
            {
                unmatched.Add(path);
                continue;
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            mat.SetTexture("_BaseMap", Load(surface.albedo));
            mat.SetTextureScale("_BaseMap", Vector2.one);
            mat.SetColor("_BaseColor", Color.white); // the albedo already carries the swatch colour
            mat.SetTexture("_BumpMap", Load(surface.normal));
            mat.SetFloat("_BumpScale", 1f);
            mat.EnableKeyword("_NORMALMAP");
            mat.SetFloat("_Metallic", surface.metallic);
            mat.SetFloat("_Smoothness", 1f - surface.roughness);
            EditorUtility.SetDirty(mat);
            applied++;
        }

        AssetDatabase.SaveAssets();
        foreach (var path in unmatched) Debug.LogWarning($"[SurfaceMaterialBuilder] No surface for {path}");
        Debug.Log($"[SurfaceMaterialBuilder] Applied surface textures to {applied} materials.");
    }

    private static void ConfigureTextures(IEnumerable<Surface> surfaces)
    {
        var normals = new HashSet<string>(surfaces.Select(s => s.normal));
        foreach (var file in surfaces.Select(s => s.albedo).Concat(normals).Distinct())
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturesPath + file);
            if (importer == null) throw new FileNotFoundException(TexturesPath + file);

            var isNormal = normals.Contains(file);
            var type = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType == type && importer.wrapMode == TextureWrapMode.Repeat && importer.mipmapEnabled) continue;

            importer.textureType = type;
            importer.sRGBTexture = !isNormal;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }
    }

    private static Texture2D Load(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>(TexturesPath + file);

    // Same rule as pascal() in split_palette_materials.py: "Warm sand variation" -> "WarmSandVariation"
    private static string Pascal(string name)
        => string.Concat(name.Replace("-", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
}
