using UnityEditor;
using UnityEngine;

public class AlignMapCenter
{
    [MenuItem("Tools/BettingRoyale/Align Map Center To Marker")]
    private static void Align()
    {
        GameObject[] selected = Selection.gameObjects;

        if (selected.Length != 2)
        {
            Debug.LogWarning(
                "MapRoot와 CombatCenterMark 두 개를 선택해주세요."
            );
            return;
        }

        GameObject mapRoot = null;
        GameObject marker = null;

        foreach (GameObject obj in selected)
        {
            if (obj.name.Contains("CenterMark"))
                marker = obj;
            else
                mapRoot = obj;
        }

        if (mapRoot == null || marker == null)
        {
            Debug.LogWarning(
                "MapRoot와 CombatCenterMark를 선택해주세요."
            );
            return;
        }

        Renderer[] renderers =
            mapRoot.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
        {
            Debug.LogWarning("MapRoot 아래에 Renderer가 없습니다.");
            return;
        }

        Bounds bounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        Vector3 mapCenter = bounds.center;
        Vector3 target = marker.transform.position;

        // 높이(Y)는 건드리지 않고 X/Z만 정렬
        Vector3 offset = new Vector3(
            target.x - mapCenter.x,
            0f,
            target.z - mapCenter.z
        );

        Undo.RecordObject(mapRoot.transform, "Align Map Center");

        mapRoot.transform.position += offset;

        Debug.Log(
            $"Map 중심 정렬 완료. 이동량: {offset}"
        );
    }
}
