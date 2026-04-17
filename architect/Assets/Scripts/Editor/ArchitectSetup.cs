using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Editor menu to create the Architect pose bridge and game setup in the scene.
/// </summary>
public static class ArchitectSetup
{
    /// <summary>One-click: Pose Bridge + all games + full UI. Use on an empty or cleared scene.</summary>
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
        Debug.Log("[Architect] Complete setup created. Save the scene (Ctrl+S or File -> Save As) when ready.");
    }
    public static void CreatePoseBridge()
    {
        var go = new GameObject("PoseBridge");
        go.AddComponent<PoseReceiver>();
        var driver = go.AddComponent<PoseAvatarDriver>();
        driver.createDebugSkeleton = true;
        driver.createLimbSticks = true;
        driver.mirrorFlipX = true;
        driver.avatarScale = 3.5f;
        Undo.RegisterCreatedObjectUndo(go, "Create Pose Bridge");
        Selection.activeGameObject = go;
        Debug.Log("[Architect] PoseBridge created. Start pose.py with --udp-port 5555 and enter Play mode.");
    }

    public static void CreateFullGameSetup()
    {
        var bridge = Object.FindFirstObjectByType<PoseReceiver>();
        if (bridge == null)
        {
            CreatePoseBridge();
            bridge = Object.FindFirstObjectByType<PoseReceiver>();
        }
        var bridgeGo = bridge.gameObject;
        if (bridgeGo.GetComponent<PoseGestureDetector>() == null)
            bridgeGo.AddComponent<PoseGestureDetector>();

        var dodgeGo = new GameObject("DodgeGame");
        dodgeGo.AddComponent<DodgeGameManager>();

        var balanceGo = new GameObject("SingleLegBalanceGame");
        balanceGo.AddComponent<SingleLegBalanceManager>();

        var leanBalanceGo = new GameObject("LeanBalanceGame");
        leanBalanceGo.AddComponent<LeanBalanceGameManager>();

        var coinMineGo = new GameObject("CoinMineGame");
        coinMineGo.AddComponent<CoinMineGameManager>();

        var testGo = new GameObject("PoseTestMode");
        testGo.AddComponent<PoseTestMode>();
        var ocrGo = new GameObject("OcrMode");
        ocrGo.AddComponent<OcrMode>();

        var selectorGo = new GameObject("GameSelector");
        var selector = selectorGo.AddComponent<ArchitectGameSelector>();
        selector.dodgeGame = dodgeGo.GetComponent<DodgeGameManager>();
        selector.balanceGame = balanceGo.GetComponent<SingleLegBalanceManager>();
        selector.leanBalanceGame = leanBalanceGo.GetComponent<LeanBalanceGameManager>();
        selector.coinMineGame = coinMineGo.GetComponent<CoinMineGameManager>();
        selector.poseTest = testGo.GetComponent<PoseTestMode>();
        selector.ocrMode = ocrGo.GetComponent<OcrMode>();

        dodgeGo.SetActive(false);
        balanceGo.SetActive(false);
        leanBalanceGo.SetActive(false);
        coinMineGo.SetActive(false);
        testGo.SetActive(false);
        ocrGo.SetActive(false);

        Undo.RegisterCreatedObjectUndo(dodgeGo, "Create Dodge Game");
        Undo.RegisterCreatedObjectUndo(balanceGo, "Create Balance Game");
        Undo.RegisterCreatedObjectUndo(leanBalanceGo, "Create Lean Balance Game");
        Undo.RegisterCreatedObjectUndo(coinMineGo, "Create Coin Mine Game");
        Undo.RegisterCreatedObjectUndo(testGo, "Create Pose Test");
        Undo.RegisterCreatedObjectUndo(ocrGo, "Create OCR Mode");
        Undo.RegisterCreatedObjectUndo(selectorGo, "Create Game Selector");
        Selection.activeGameObject = selectorGo;
        Debug.Log("[Architect] Full game setup created. Add UI (Canvas with buttons for GameSelector, score/lives for Dodge, stability bar for Balance). See ARCHITECT_GAME.md.");
    }
}
