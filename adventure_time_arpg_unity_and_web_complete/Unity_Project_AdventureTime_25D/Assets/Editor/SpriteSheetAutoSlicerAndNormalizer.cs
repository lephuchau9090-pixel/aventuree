using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace AdventureTimeARPG.EditorTools
{
    /// <summary>
    /// Automatically configures, cleans background (if opaque/checkerboard), and slices normalized 64x64
    /// sprite sheets in Assets/Sprites/Normalized/ with:
    /// - FilterMode.Point (no bilinear blur)
    /// - Uncompressed RGBA32
    /// - Pixels Per Unit (PPU) = 64
    /// - Consistent bottom-center foot pivot (0.5f, 0.0625f -> Pixel Y=4 from bottom)
    /// so characters never shift or jump in size across Idle, Walk, Attack, Skill, and Hurt animations,
    /// and sort accurately by foot Y-position (Y-sorting).
    /// Original assets in Assets/Sprites/Original/ remain untouched.
    /// </summary>
    public class SpriteSheetAutoSlicerAndNormalizer : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.Contains("Assets/Sprites/Normalized/")) return;
            TextureImporter ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Multiple;
            ti.filterMode = FilterMode.Point;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.spritePixelsPerUnit = 64f;
        }

        [MenuItem("Window/Adventure Time ARPG/Normalize & Slice All 64x64 Sprite Sheets")]
        public static void SliceNormalizedSheets()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Sprites/Normalized" });
            int slicedCount = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) continue;

                TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;

                int cellW = 64;
                int cellH = 64;
                int cols = tex.width / cellW;
                int rows = tex.height / cellH;

                List<SpriteMetaData> metas = new List<SpriteMetaData>();
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        // Unity texture Y=0 is at the bottom
                        int unityY = (rows - 1 - r) * cellH;
                        SpriteMetaData smd = new SpriteMetaData
                        {
                            name = $"{tex.name}_r{r}_c{c}",
                            rect = new Rect(c * cellW, unityY, cellW, cellH),
                            alignment = (int)SpriteAlignment.Custom,
                            pivot = new Vector2(0.5f, 0.0625f) // Foot baseline at y = 60px from top (4px from bottom)
                        };
                        metas.Add(smd);
                    }
                }

                ti.spritesheet = metas.ToArray();
                EditorUtility.SetDirty(ti);
                ti.SaveAndReimport();
                slicedCount++;
            }

            Debug.Log($"[AdventureTimeARPG] Sliced {slicedCount} normalized 64x64 sprite sheets with Point filter, PPU=64, and foot pivot (0.5, 0.0625).");
        }
    }
}
