using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TWR.EditorTools
{
    /// <summary>
    /// Creates the dedicated "Portal" Universal Renderer for portal cameras (D-139; D-097, D-100) and adds
    /// it to the renderer list of every URP asset the project uses (default + each quality level, found the same way
    /// as WeaponRenderSetup). It is a plain renderer: no Render Objects features (the gun is never in a portal view),
    /// no SSAO, no depth priming. Depth/opaque textures and MSAA are switched off per portal camera.
    /// Rendering path, layer filters and post-process data are copied from the project's main renderer, so the hall
    /// is lit exactly like it looks when you stand in it (no pop when crossing). Safe to run again.
    /// </summary>
    public static class PortalRenderSetup
    {
        public const string RendererPath = "Assets/_Project/Settings/Rendering/Portal_Renderer.asset";

        /// <summary>Returns the Portal renderer asset (created if missing), registered in every used URP asset.</summary>
        public static UniversalRendererData Ensure()
        {
            UniversalRendererData source = FindMainRenderer();
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<UniversalRendererData>();
                if (source != null)
                {
                    data.postProcessData = source.postProcessData;
                    data.renderingMode = source.renderingMode;
                    data.opaqueLayerMask = source.opaqueLayerMask;
                    data.transparentLayerMask = source.transparentLayerMask;
                }
                AssetDatabase.CreateAsset(data, RendererPath);
                ResourceReloader.TryReloadAllNullIn(data, UniversalRenderPipelineAsset.packagePath); // shaders etc., like URP's own "Create" menu
                Debug.Log("[TWR] Created " + RendererPath + " (D-139).");
            }

            data.depthPrimingMode = DepthPrimingMode.Disabled;
            data.intermediateTextureMode = IntermediateTextureMode.Auto;
            if (data.rendererFeatures.Count > 0)
                Debug.LogWarning($"[TWR] {RendererPath} has {data.rendererFeatures.Count} renderer feature(s). Portal cameras should have none " +
                                 "(no SSAO, no Render Objects): remove them in the Inspector.", data);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);

            foreach (UniversalRenderPipelineAsset pipeline in UsedPipelines())
                AddToRendererList(pipeline, data);
            return data;
        }

        /// <summary>Index of the renderer in the currently active URP asset, or -1.</summary>
        public static int IndexInActivePipeline(ScriptableRendererData data)
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp) || data == null) return -1;
            System.ReadOnlySpan<ScriptableRendererData> list = urp.rendererDataList;
            for (int i = 0; i < list.Length; i++)
                if (list[i] == data) return i;
            return -1;
        }

        /// <summary>One line per used URP asset: where the Portal renderer sits in its list. False if missing anywhere.</summary>
        public static bool DescribeRegistration(ScriptableRendererData data, StringBuilder report)
        {
            bool ok = true;
            int found = 0;
            foreach (UniversalRenderPipelineAsset pipeline in UsedPipelines())
            {
                found++;
                int index = -1;
                System.ReadOnlySpan<ScriptableRendererData> list = pipeline.rendererDataList;
                for (int i = 0; i < list.Length; i++)
                    if (list[i] == data) { index = i; break; }
                report.AppendLine($"  {pipeline.name}: {(index >= 0 ? "renderer list index " + index : "NOT in the renderer list")}");
                if (index < 0) ok = false;
            }
            if (found == 0) { report.AppendLine("  no URP asset found"); ok = false; }
            return ok;
        }

        /// True for the Portal renderer, so WeaponRenderSetup leaves it alone (no Render Objects on it, D-123).
        public static bool IsPortalRenderer(ScriptableRendererData data) =>
            data != null && AssetDatabase.GetAssetPath(data) == RendererPath;

        static void AddToRendererList(UniversalRenderPipelineAsset pipeline, UniversalRendererData data)
        {
            var so = new SerializedObject(pipeline);
            SerializedProperty list = so.FindProperty("m_RendererDataList");
            if (list == null || !list.isArray)
            {
                Debug.LogWarning($"[TWR] Could not read the renderer list of '{pipeline.name}'. Add {RendererPath} to it by hand " +
                                 "(select the URP asset > Rendering > Renderer List > +).", pipeline);
                return;
            }
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == data) return; // already there

            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = data;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssetIfDirty(pipeline);
            Debug.Log($"[TWR] Added the Portal renderer to '{pipeline.name}' (renderer list index {list.arraySize - 1}).", pipeline);
        }

        /// The renderer the main camera uses: the default renderer of the default URP asset.
        static UniversalRendererData FindMainRenderer()
        {
            foreach (UniversalRenderPipelineAsset pipeline in UsedPipelines())
            {
                SerializedProperty list = new SerializedObject(pipeline).FindProperty("m_RendererDataList");
                SerializedProperty defaultIndex = new SerializedObject(pipeline).FindProperty("m_DefaultRendererIndex");
                int index = defaultIndex != null ? defaultIndex.intValue : 0;
                if (list != null && index >= 0 && index < list.arraySize &&
                    list.GetArrayElementAtIndex(index).objectReferenceValue is UniversalRendererData data && !IsPortalRenderer(data))
                    return data;
            }
            return null;
        }

        static IEnumerable<UniversalRenderPipelineAsset> UsedPipelines()
        {
            var seen = new HashSet<UniversalRenderPipelineAsset>();
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset main && seen.Add(main))
                yield return main;
            for (int i = 0; i < QualitySettings.names.Length; i++)
                if (QualitySettings.GetRenderPipelineAssetAt(i) is UniversalRenderPipelineAsset level && seen.Add(level))
                    yield return level;
        }
    }
}
