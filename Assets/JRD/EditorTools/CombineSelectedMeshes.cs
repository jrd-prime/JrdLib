using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace JRD.EditorTools
{
    /// <summary>
    /// Combines multiple selected GameObjects (with MeshRenderers and MeshFilters)
    /// into a single mesh while preserving all materials and generating proper UV2 for lightmapping.
    /// </summary>
    public class CombineSelectedMeshes
    {
        private const float HardAngle = 30f; // split UV islands when angle between faces > 30°
        private const float PackMargin = 0.01f; // margin between UV islands
        private const float AngleError = 0.5f; // UV angle distortion tolerance
        private const float AreaError = 0.05f; // UV area distortion tolerance

        private const bool GenerateSecondaryUVSetForLightmapping = true;

        [MenuItem("Tools/Combine Selected Meshes")]
        private static void CombineSelected()
        {
            // Get all currently selected GameObjects in the Unity Editor
            var selectedObjects = Selection.gameObjects;
            if (selectedObjects.Length == 0)
            {
                Debug.LogWarning("Nothing selected! Please select GameObjects with MeshFilters.");
                return;
            }

            // ------------------------------------------------------------------------
            // COLLECT ALL UNIQUE MATERIALS
            // ------------------------------------------------------------------------
            // We'll need a full list of all materials used by all selected objects.
            // This ensures the final combined mesh supports any combination (2, 3, or more materials).
            var allMaterials = new List<Material>();
            foreach (var selected in selectedObjects)
            {
                var renderer = selected.GetComponent<MeshRenderer>();

                if (!renderer)
                    continue;

                foreach (var material in renderer.sharedMaterials)
                {
                    // Add each material once (avoid duplicates)
                    if (material && !allMaterials.Contains(material))
                        allMaterials.Add(material);
                }
            }

            if (allMaterials.Count == 0)
            {
                Debug.LogWarning("No materials found among selected objects.");
                return;
            }

            // ------------------------------------------------------------------------
            // GROUP SUBMESHES BY MATERIAL
            // ------------------------------------------------------------------------
            // For each material, we’ll collect all CombineInstances that use it.
            // This allows us to merge all meshes that share the same material into one submesh.
            var matToCombines = new Dictionary<Material, List<CombineInstance>>();
            foreach (var material in allMaterials)
                matToCombines[material] = new List<CombineInstance>();

            foreach (var selected in selectedObjects)
            {
                var filter = selected.GetComponent<MeshFilter>();
                var renderer = selected.GetComponent<MeshRenderer>();

                if (!filter || !renderer || !filter.sharedMesh)
                    continue;

                var mesh = filter.sharedMesh;
                var mats = renderer.sharedMaterials;

                // Iterate through all submeshes and assign them to the correct material group
                for (var sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (sub >= mats.Length) continue; // skip if mesh has more submeshes than materials
                    var mat = mats[sub];

                    if (!mat)
                        continue;

                    var ci = new CombineInstance
                    {
                        mesh = mesh,
                        subMeshIndex = sub,
                        transform = filter.transform.localToWorldMatrix
                    };

                    matToCombines[mat].Add(ci);
                }
            }

            // ------------------------------------------------------------------------
            // COMBINE ALL MESHES PER MATERIAL
            // ------------------------------------------------------------------------
            // For every material, we combine all its corresponding submeshes into a single mesh.
            var finalCombine = new List<CombineInstance>();
            foreach (var material in allMaterials)
            {
                var combines = matToCombines[material];
                if (combines.Count == 0)
                    continue;

                var subMesh = new Mesh();
                // mergeSubMeshes = true → combines all pieces into one submesh per material
                // useMatrices = true → keeps world-space transformations
                subMesh.CombineMeshes(combines.ToArray(), true, true);

                finalCombine.Add(new CombineInstance
                {
                    mesh = subMesh,
                    subMeshIndex = 0
                });
            }

            // ------------------------------------------------------------------------
            // COMBINE ALL MATERIAL-SPECIFIC MESHES INTO ONE FINAL MESH
            // ------------------------------------------------------------------------
            var newMesh = new Mesh();
            // mergeSubMeshes = false → keep submeshes separate (one per material)
            // useMatrices = false → we already baked transforms above
            newMesh.CombineMeshes(finalCombine.ToArray(), false, false);

            if (GenerateSecondaryUVSetForLightmapping)
            {
                // ------------------------------------------------------------------------
                // GENERATE SECONDARY UV SET (UV2) FOR LIGHTMAPPING
                // ------------------------------------------------------------------------
                // Unity requires a non-overlapping UV2 for baked lighting.
                // We use custom unwrap parameters to reduce lightmap artifacts

                UnwrapParam.SetDefaults(out UnwrapParam settings);
                settings.hardAngle = HardAngle;
                settings.packMargin = PackMargin;
                settings.angleError = AngleError;
                settings.areaError = AreaError;

                Unwrapping.GenerateSecondaryUVSet(newMesh, settings);
            }

            // ------------------------------------------------------------------------
            // CREATE THE FINAL GAMEOBJECT IN THE SCENE
            // ------------------------------------------------------------------------
            var combined = new GameObject("Combined_" + selectedObjects[0].name);

            var filterCombined = combined.AddComponent<MeshFilter>();
            filterCombined.sharedMesh = newMesh;

            var rendererCombined = combined.AddComponent<MeshRenderer>();
            rendererCombined.sharedMaterials = allMaterials.ToArray();

            // Mark object as static to allow static batching and GI contribution
            GameObjectUtility.SetStaticEditorFlags(combined,
                StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.ContributeGI);

            Debug.Log(
                $"Combined {selectedObjects.Length} objects into 1 mesh with {allMaterials.Count} materials. Created: {combined.name}");
        }
    }
}
