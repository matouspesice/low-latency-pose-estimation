using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

/// <summary>
/// Replaces legacy StandaloneInputModule with InputSystemUIInputModule so UI works when
/// Player Settings use Input System Package (not "Both" or legacy Input Manager).
/// </summary>
public static class ArchitectEventSystemInputFix
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void FixEventSystemAfterSceneLoad()
    {
        Apply();
    }

    public static void Apply()
    {
        var eventSystems = UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
        for (int i = 0; i < eventSystems.Length; i++)
        {
            var es = eventSystems[i];
            if (es == null) continue;
            UpgradeEventSystem(es.gameObject);
        }
    }

    public static void UpgradeEventSystem(GameObject eventSystemGo)
    {
        if (eventSystemGo == null)
            return;

        var standalone = eventSystemGo.GetComponent<StandaloneInputModule>();
        if (standalone != null)
        {
            standalone.enabled = false;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(standalone);
            else
                UnityEngine.Object.DestroyImmediate(standalone);
        }

        var inputModule = eventSystemGo.GetComponent<InputSystemUIInputModule>();
        if (inputModule == null)
            inputModule = eventSystemGo.AddComponent<InputSystemUIInputModule>();

        inputModule.enabled = true;
        if (inputModule.actionsAsset == null)
            inputModule.AssignDefaultActions();
    }
}
