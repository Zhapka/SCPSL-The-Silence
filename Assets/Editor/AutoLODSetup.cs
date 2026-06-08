using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class AutoLODSetup : EditorWindow
{
    [MenuItem("Tools/Auto Setup LOD Groups")]
    public static void ShowWindow()
    {
        GetWindow<AutoLODSetup>("LOD Setup");
    }

    private void OnGUI()
    {
        if (GUILayout.Button("Setup LODs for Selected Object", GUILayout.Height(40)))
        {
            SetupLODs();
        }
    }

    private void SetupLODs()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
        {
            Debug.LogError("Сначала выберите общий родительский объект (например, Props_) в Hierarchy!");
            return;
        }

        // --- ШАГ 1: Полная очистка ошибочных LOD Group на дочерних моделях ---
        LODGroup[] childLodGroups = selected.GetComponentsInChildren<LODGroup>(true);
        int removedCount = 0;

        foreach (var oldGroup in childLodGroups)
        {
            // Если компонент висит НЕ на выделенном корне и НЕ на прямых детях (Alarm_Light, Barrel), а глубже (на моделях)
            if (oldGroup.gameObject != selected && oldGroup.transform.parent.gameObject != selected)
            {
                DestroyImmediate(oldGroup);
                removedCount++;
            }
        }
        if (removedCount > 0)
        {
            Debug.Log($"[AutoLOD] Удалено {removedCount} старых ошибочных LOD Group с дочерних моделей.");
        }

        // --- ШАГ 2: Сбор данных и настройка правильных LOD Group ---
        Renderer[] allRenderers = selected.GetComponentsInChildren<Renderer>(true);
        Dictionary<GameObject, List<Renderer>> parentGroups = new Dictionary<GameObject, List<Renderer>>();

        foreach (var r in allRenderers)
        {
            if (r.name.Contains("_LOD"))
            {
                GameObject parentGo = r.transform.parent.gameObject;
                if (parentGo == selected) continue;

                if (!parentGroups.ContainsKey(parentGo))
                {
                    parentGroups[parentGo] = new List<Renderer>();
                }
                parentGroups[parentGo].Add(r);
            }
        }

        int successCount = 0;

        foreach (var pair in parentGroups)
        {
            GameObject parentGo = pair.Key;
            List<Renderer> renderers = pair.Value;

            if (!renderers.Exists(r => r.name.EndsWith("_LOD0"))) continue;

            LODGroup lodGroup = parentGo.GetComponent<LODGroup>();
            if (lodGroup == null)
            {
                lodGroup = parentGo.AddComponent<LODGroup>();
            }

            List<LOD> lods = new List<LOD>();

            Renderer r0 = renderers.Find(r => r.name.EndsWith("_LOD0"));
            if (r0 != null) lods.Add(new LOD(0.7f, new Renderer[] { r0 }));

            Renderer r1 = renderers.Find(r => r.name.EndsWith("_LOD1"));
            if (r1 != null) lods.Add(new LOD(0.4f, new Renderer[] { r1 }));

            Renderer r2 = renderers.Find(r => r.name.EndsWith("_LOD2"));
            if (r2 != null) lods.Add(new LOD(0.15f, new Renderer[] { r2 }));

            Renderer r3 = renderers.Find(r => r.name.EndsWith("_LOD3"));
            if (r3 != null) lods.Add(new LOD(0.05f, new Renderer[] { r3 }));

            lodGroup.SetLODs(lods.ToArray());
            lodGroup.RecalculateBounds();

            EditorUtility.SetDirty(parentGo);
            successCount++;
        }

        Debug.Log($"[AutoLOD] Успешно настроено LOD-групп на родителях: {successCount}");
    }
}
