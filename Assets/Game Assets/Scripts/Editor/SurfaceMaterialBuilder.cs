using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds one shared URP Lit material per surface in Art Data/HS_Surface_Materials.json at
/// Materials/&lt;Family&gt;/&lt;name&gt;.mat, and remaps every model's material slots to them by name. Re-runnable:
/// existing materials are updated in place, so their GUIDs (and scene references) survive. The models' UV0 is
/// already in units of each surface's tileMeters (Tools/Blender/split_palette_materials.py), so tiling stays (1, 1).
/// </summary>
public static class SurfaceMaterialBuilder
{
    private const string SpecPath = "Assets/Game Assets/Art Data/HS_Surface_Materials.json";
    private const string TexturesPath = "Assets/Game Assets/Textures/Surfaces/";
    private const string MaterialsPath = "Assets/Game Assets/Materials/";
    private const string ModelsPath = "Assets/Game Assets/Models";
    private const string LitShader = "Universal Render Pipeline/Lit";
    private const int MaxTextureSize = 256;

    [Serializable] private class Spec { public Surface[] materials; }
    [Serializable] private class Surface { public string sourceName; public string name; public string family; public string albedo; public float tileMeters; }

    [MenuItem("Harvest Sorter/Apply Surface Materials")]
    public static void Apply()
    {
        var spec = JsonUtility.FromJson<Spec>(File.ReadAllText(SpecPath));
        ConfigureTextures(spec.materials);

        var materials = spec.materials.ToDictionary(s => s.name, BuildMaterial);
        AssetDatabase.SaveAssets();

        RemapModels(materials);
        Debug.Log($"[SurfaceMaterialBuilder] {materials.Count} shared surface materials applied.");
    }

    private static Material BuildMaterial(Surface surface)
    {
        var folder = MaterialsPath + surface.family;
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(MaterialsPath.TrimEnd('/'), surface.family);

        var path = $"{folder}/{surface.name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find(LitShader)) { name = surface.name };
            AssetDatabase.CreateAsset(mat, path);
        }

        mat.SetTexture("_BaseMap", Load(surface.albedo));
        mat.SetTextureScale("_BaseMap", Vector2.one);
        mat.SetColor("_BaseColor", Color.white); // the albedo already carries the swatch colour
        // Hyper-casual look: matte and flat. No normal map, specular highlights or environment reflections,
        // which also keeps the shader cheap on mobile.
        mat.SetTexture("_BumpMap", null);
        mat.DisableKeyword("_NORMALMAP");
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Smoothness", 0f);
        mat.SetFloat("_SpecularHighlights", 0f);
        mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        mat.SetFloat("_EnvironmentReflections", 0f);
        mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Each FBX material slot is named after its surface, so a slot remaps to the shared material of that name.
    private static void RemapModels(Dictionary<string, Material> materials)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ModelsPath }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) continue;

            // Clear the old remaps first so the renderers report the FBX's own slot names.
            var oldRemaps = importer.GetExternalObjectMap().Keys.Where(k => k.type == typeof(Material)).ToList();
            if (oldRemaps.Count > 0)
            {
                foreach (var old in oldRemaps) importer.RemoveRemap(old);
                importer.SaveAndReimport();
            }

            var slotNames = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<Renderer>(true)
                .SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name).Distinct();
            foreach (var slot in slotNames)
            {
                if (materials.TryGetValue(slot, out var mat))
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), slot), mat);
                else Debug.LogWarning($"[SurfaceMaterialBuilder] {path}: no surface material named {slot}");
            }
            importer.SaveAndReimport();
        }
    }

    private static void ConfigureTextures(IEnumerable<Surface> surfaces)
    {
        foreach (var file in surfaces.Select(s => s.albedo).Distinct())
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturesPath + file);
            if (importer == null) throw new FileNotFoundException(TexturesPath + file);
            if (importer.wrapMode == TextureWrapMode.Repeat && importer.mipmapEnabled && importer.maxTextureSize == MaxTextureSize) continue;

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = MaxTextureSize; // the albedos are soft colour variation; 256 is plenty
            importer.SaveAndReimport();
        }
    }

    private static Texture2D Load(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>(TexturesPath + file);
}
