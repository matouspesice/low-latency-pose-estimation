using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Editor menu to create the Architect pose bridge and game setup in the scene.
/// </summary>
public static class ArchitectSetup
{
    static GameObject GetOrCreate(string name)
    {
        // GameObject.Find only searches ACTIVE objects in the scene, so it would
        // return null for any managed GameObject that was SetActive(false) on the
        // previous run and would cause CreateFullGameSetup to duplicate it on rebuild.
        // Search inactive-inclusive via Resources.FindObjectsOfTypeAll to stay idempotent.
        var all = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int i = 0; i < all.Length; i++)
        {
            var existing = all[i];
            if (existing == null) continue;
            if (existing.name != name) continue;
            if (existing.hideFlags != HideFlags.None) continue;
            if (!existing.scene.IsValid()) continue;
            return existing;
        }
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        return go;
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c != null) return c;
        return go.AddComponent<T>();
    }

    /// <summary>One-click idempotent setup: reuses existing objects, rebuilds UI, saves scene.</summary>
    [MenuItem("Architect/Create Complete Setup (Bridge + Games + UI)")]
    public static void CreateCompleteSetup()
    {
        var bridge = Object.FindFirstObjectByType<PoseReceiver>();
        if (bridge == null)
        {
            CreatePoseBridge();
            bridge = Object.FindFirstObjectByType<PoseReceiver>();
        }
        CreateFullGameSetup();
        ArchitectUIBuilder.BuildGameUI();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[Architect] Complete setup created/updated. Rerunning is safe — existing objects are reused.");
    }

    public static void CreatePoseBridge()
    {
        var go = GetOrCreate("PoseBridge");
        GetOrAdd<PoseReceiver>(go);
        var driver = GetOrAdd<PoseAvatarDriver>(go);
        driver.createDebugSkeleton = true;
        driver.createLimbSticks = true;
        driver.mirrorFlipX = true;
        driver.avatarScale = 3.5f;
        var geniesDriver = GetOrAdd<GeniesPoseAvatarDriver>(go);
        geniesDriver.poseReceiver = go.GetComponent<PoseReceiver>();
        geniesDriver.poseAvatarDriver = driver;
        geniesDriver.loadOnStart = true;
        geniesDriver.replaceDebugSkeleton = true;
        GetOrAdd<PoseGestureDetector>(go);
        var bodyTilt = GetOrAdd<BodyTiltInput>(go);
        if (bodyTilt.poseGestureDetector == null)
            bodyTilt.poseGestureDetector = go.GetComponent<PoseGestureDetector>();
        Selection.activeGameObject = go;
        Debug.Log("[Architect] PoseBridge created. Start pose.py with --udp-port 5555 and enter Play mode.");
    }

    /// <summary>
    /// Removes duplicate GameObjects that share a managed Architect name. The first
    /// instance found (in Resources enumeration order) is kept, any others are destroyed.
    /// This lets re-running the setup silently recover from an older build that created
    /// duplicates due to the GameObject.Find (active-only) bug.
    /// </summary>
    static void DedupeManagedObjects()
    {
        var managed = new System.Collections.Generic.HashSet<string>
        {
            "PoseBridge", "GameSelector",
            "DodgeGame", "SingleLegBalanceGame", "LeanBalanceGame", "CoinMineGame",
            "PoseTestMode", "OcrMode", "ClockMode",
        };
        var seen = new System.Collections.Generic.HashSet<string>();
        var all = Resources.FindObjectsOfTypeAll<GameObject>();
        int destroyed = 0;
        for (int i = 0; i < all.Length; i++)
        {
            var go = all[i];
            if (go == null) continue;
            if (go.hideFlags != HideFlags.None) continue;
            if (!go.scene.IsValid()) continue;
            if (!managed.Contains(go.name)) continue;
            if (seen.Add(go.name)) continue;
            Undo.DestroyObjectImmediate(go);
            destroyed++;
        }
        if (destroyed > 0)
            Debug.Log($"[Architect] Dedup removed {destroyed} duplicate managed GameObject(s).");
    }

    public static void CreateFullGameSetup()
    {
        DedupeManagedObjects();

        var bridge = Object.FindFirstObjectByType<PoseReceiver>();
        if (bridge == null)
        {
            CreatePoseBridge();
            bridge = Object.FindFirstObjectByType<PoseReceiver>();
        }
        var bridgeGo = bridge.gameObject;
        if (bridgeGo.GetComponent<PoseGestureDetector>() == null)
            bridgeGo.AddComponent<PoseGestureDetector>();

        var geniesDriver = GetOrAdd<GeniesPoseAvatarDriver>(bridgeGo);
        geniesDriver.poseReceiver = bridgeGo.GetComponent<PoseReceiver>();
        geniesDriver.poseAvatarDriver = bridgeGo.GetComponent<PoseAvatarDriver>();
        geniesDriver.loadOnStart = true;
        geniesDriver.replaceDebugSkeleton = true;
        if (bridgeGo.GetComponent<BodyTiltInput>() == null)
        {
            var bodyTilt = bridgeGo.AddComponent<BodyTiltInput>();
            bodyTilt.poseGestureDetector = bridgeGo.GetComponent<PoseGestureDetector>();
        }

        var dodgeGo = GetOrCreate("DodgeGame");
        GetOrAdd<DodgeGameManager>(dodgeGo);

        var balanceGo = GetOrCreate("SingleLegBalanceGame");
        GetOrAdd<SingleLegBalanceManager>(balanceGo);

        var leanBalanceGo = GetOrCreate("LeanBalanceGame");
        GetOrAdd<LeanBalanceGameManager>(leanBalanceGo);

        var coinMineGo = GetOrCreate("CoinMineGame");
        var coinMineMgr = GetOrAdd<CoinMineGameManager>(coinMineGo);
        coinMineMgr.bodyTiltInput = bridgeGo.GetComponent<BodyTiltInput>();
        coinMineMgr.gestureDetector = bridgeGo.GetComponent<PoseGestureDetector>();

        var testGo = GetOrCreate("PoseTestMode");
        var poseTest = GetOrAdd<PoseTestMode>(testGo);
        poseTest.geniesAvatarDriver = geniesDriver;
        var ocrGo = GetOrCreate("OcrMode");
        GetOrAdd<OcrMode>(ocrGo);
        var clockGo = GetOrCreate("ClockMode");
        var clockMode = GetOrAdd<ClockMode>(clockGo);
        clockMode.geniesAvatarDriver = geniesDriver;

        var selectorGo = GetOrCreate("GameSelector");
        var selector = GetOrAdd<ArchitectGameSelector>(selectorGo);
        selector.dodgeGame = dodgeGo.GetComponent<DodgeGameManager>();
        selector.balanceGame = balanceGo.GetComponent<SingleLegBalanceManager>();
        selector.leanBalanceGame = leanBalanceGo.GetComponent<LeanBalanceGameManager>();
        selector.coinMineGame = coinMineGo.GetComponent<CoinMineGameManager>();
        selector.poseTest = testGo.GetComponent<PoseTestMode>();
        selector.ocrMode = ocrGo.GetComponent<OcrMode>();
        selector.clockMode = clockGo.GetComponent<ClockMode>();

        dodgeGo.SetActive(false);
        balanceGo.SetActive(false);
        leanBalanceGo.SetActive(false);
        coinMineGo.SetActive(false);
        testGo.SetActive(false);
        ocrGo.SetActive(false);
        clockGo.SetActive(false);

        Selection.activeGameObject = selectorGo;
        Debug.Log("[Architect] Full game setup created. Add UI (Canvas with buttons for GameSelector, score/lives for Dodge, stability bar for Balance). See ARCHITECT_GAME.md.");
    }
}
