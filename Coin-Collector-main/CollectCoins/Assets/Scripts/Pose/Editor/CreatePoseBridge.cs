using UnityEngine;
using UnityEditor;

public static class CreatePoseBridge
{
    [MenuItem("GameObject/Pose/Create Pose Bridge (Coin Collector)")]
    static void Create()
    {
        var go = new GameObject("PoseBridge");
        go.AddComponent<PoseReceiver>().port = 5555;
        go.AddComponent<PoseGestureDetector>();
        go.AddComponent<BodyTiltInput>();
        Undo.RegisterCreatedObjectUndo(go, "Create PoseBridge");
        Selection.activeGameObject = go;
    }
}
