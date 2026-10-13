using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TWR.EditorTools
{
    /// <summary>
    /// Makes the held gun draw on top of the world without an overlay camera (D-123, D-100).
    /// For every URP renderer used by the project's pipeline assets (default + each quality level):
    /// 1. removes the Weapon layer from the normal opaque/transparent drawing, and
    /// 2. adds two Render Objects features that draw ONLY the Weapon layer after everything else:
    ///    "TWR Weapon (stamp)" with depth test Always (the gun wins over walls it pokes into), then
    ///    "TWR Weapon" with normal depth testing (so the gun's own parts sort correctly).
    /// Safe to run again: features are found by name and updated, never added twice.
    /// </summary>
    public static class WeaponRenderSetup
    {
        const string StampFeatureName = "TWR Weapon (stamp)";
        const string DrawFeatureName = "TWR Weapon";

        /// Returns how many renderers are set up. 0 means it could not find a URP renderer (see warning).
        public static int Ensure(int weaponLayer)
        {
            if (weaponLayer < 0) return 0;

            int count = 0;
            foreach (UniversalRendererData data in FindUsedRendererData())
            {
                EnsureOn(data, weaponLayer);
                count++;
            }

            if (count == 0)
            {
                Debug.LogWarning("[TWR] Could not find a URP Universal Renderer to set up the gun-on-top drawing. Do it by hand: " +
                                 "select your PC_Renderer asset > Filtering: untick 'Weapon' in Opaque and Transparent Layer Mask > " +
                                 "Add Renderer Feature > Render Objects (Event AfterRenderingTransparents, Layer Mask Weapon, " +
                                 "Depth override: Write on, Test Always), then a second one the same but Test LessEqual.");
            }
            return count;
        }

        static IEnumerable<UniversalRendererData> FindUsedRendererData()
        {
            var pipelines = new List<RenderPipelineAsset> { GraphicsSettings.defaultRenderPipeline };
            for (int i = 0; i < QualitySettings.names.Length; i++)
                pipelines.Add(QualitySettings.GetRenderPipelineAssetAt(i));

            var seen = new HashSet<UniversalRendererData>();
            foreach (RenderPipelineAsset pipeline in pipelines)
            {
                if (!(pipeline is UniversalRenderPipelineAsset)) continue;
                // m_RendererDataList is internal; read it through serialization (works across URP versions).
                SerializedProperty list = new SerializedObject(pipeline).FindProperty("m_RendererDataList");
                if (list == null || !list.isArray) continue;
                for (int i = 0; i < list.arraySize; i++)
                {
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue is UniversalRendererData data && seen.Add(data))
                        yield return data;
                }
            }
        }

        static void EnsureOn(UniversalRendererData data, int weaponLayer)
        {
            int bit = 1 << weaponLayer;
            if ((data.opaqueLayerMask.value & bit) != 0) data.opaqueLayerMask = data.opaqueLayerMask.value & ~bit;
            if ((data.transparentLayerMask.value & bit) != 0) data.transparentLayerMask = data.transparentLayerMask.value & ~bit;

            RenderObjects stamp = FindOrAddFeature(data, StampFeatureName);
            Configure(stamp, StampFeatureName, bit, CompareFunction.Always);
            RenderObjects draw = FindOrAddFeature(data, DrawFeatureName);
            Configure(draw, DrawFeatureName, bit, CompareFunction.LessEqual);

            EnsureOrder(data, stamp, draw);

            stamp.Create();
            draw.Create();
            data.SetDirty(); // rebuild render passes
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            Debug.Log($"[TWR] Gun-on-top rendering set up on '{data.name}' (Weapon layer excluded + 2 Render Objects features).", data);
        }

        static RenderObjects FindOrAddFeature(UniversalRendererData data, string featureName)
        {
            foreach (ScriptableRendererFeature existing in data.rendererFeatures)
                if (existing is RenderObjects renderObjects && existing.name == featureName)
                    return renderObjects;

            // Same steps as URP's own "Add Renderer Feature" button: sub-asset + list entry + ID map entry.
            var feature = ScriptableObject.CreateInstance<RenderObjects>();
            feature.name = featureName;
            AssetDatabase.AddObjectToAsset(feature, data);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);

            var so = new SerializedObject(data);
            SerializedProperty features = so.FindProperty("m_RendererFeatures");
            SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            if (map != null)
            {
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return feature;
        }

        static void Configure(RenderObjects feature, string passName, int layerBits, CompareFunction depthTest)
        {
            RenderObjects.RenderObjectsSettings s = feature.settings;
            s.passTag = passName;
            s.Event = RenderPassEvent.AfterRenderingTransparents;
            s.filterSettings.RenderQueueType = RenderQueueType.Opaque;
            s.filterSettings.LayerMask = layerBits;
            s.filterSettings.PassNames = new string[0]; // default URP passes (UniversalForward etc.)
            s.overrideMode = RenderObjects.RenderObjectsSettings.OverrideMaterialMode.None;
            s.overrideMaterial = null;
            s.overrideDepthState = true;
            s.enableWrite = true;
            s.depthCompareFunction = depthTest;
            s.cameraSettings.overrideCamera = false;
            feature.SetActive(true);
            EditorUtility.SetDirty(feature);
        }

        /// The stamp pass must run before the normal pass (same event = list order).
        static void EnsureOrder(UniversalRendererData data, RenderObjects stamp, RenderObjects draw)
        {
            List<ScriptableRendererFeature> list = data.rendererFeatures;
            int stampIndex = list.IndexOf(stamp);
            int drawIndex = list.IndexOf(draw);
            if (stampIndex < 0 || drawIndex < 0 || stampIndex < drawIndex) return;

            var so = new SerializedObject(data);
            so.FindProperty("m_RendererFeatures").MoveArrayElement(stampIndex, drawIndex);
            SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
            if (map != null && map.arraySize == list.Count) map.MoveArrayElement(stampIndex, drawIndex);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
