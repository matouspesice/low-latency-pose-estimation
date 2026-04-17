using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;
using UnityEditor.Events;
using UnityEngine.Events;

/// <summary>
/// Editor menu that creates the full game UI and wires everything automatically.
/// Run: Architect -> Create Game UI (auto-wired)
/// </summary>
public static class ArchitectUIBuilder
{
    public static void BuildGameUI()
    {
        var selector = Object.FindFirstObjectByType<ArchitectGameSelector>();
        if (selector == null)
        {
            EditorUtility.DisplayDialog("Architect",
                "Run 'Architect -> Create Full Game Setup' first, then run this.", "OK");
            return;
        }
        var dodgeMgr = Object.FindFirstObjectByType<DodgeGameManager>(FindObjectsInactive.Include);
        var balanceMgr = Object.FindFirstObjectByType<SingleLegBalanceManager>(FindObjectsInactive.Include);
        var leanBalanceMgr = Object.FindFirstObjectByType<LeanBalanceGameManager>(FindObjectsInactive.Include);
        var coinMineMgr = Object.FindFirstObjectByType<CoinMineGameManager>(FindObjectsInactive.Include);
        var testMode = Object.FindFirstObjectByType<PoseTestMode>(FindObjectsInactive.Include);
        var ocrMode = Object.FindFirstObjectByType<OcrMode>(FindObjectsInactive.Include);
        if (dodgeMgr == null || balanceMgr == null)
        {
            EditorUtility.DisplayDialog("Architect",
                "DodgeGame or SingleLegBalanceGame not found. Run 'Create Full Game Setup' first.", "OK");
            return;
        }

        // ── Canvas + EventSystem ──
        var canvasGo = new GameObject("GameCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            Undo.RegisterCreatedObjectUndo(es, "Create EventSystem");
        }

        // ── Mode Select Panel ──
        var modePanel = CreatePanel(canvasGo.transform, "ModeSelectPanel");
        StretchFill(modePanel);
        modePanel.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f, 0.95f);

        var title = CreateTMPText(modePanel.transform, "TitleText", "ARCHITECT", 52, TextAlignmentOptions.Center);
        SetAnchored(title, new Vector2(0.5f, 0.78f), new Vector2(600, 70));

        var subtitle = CreateTMPText(modePanel.transform, "Subtitle", "Choose a mode", 24, TextAlignmentOptions.Center);
        SetAnchored(subtitle, new Vector2(0.5f, 0.68f), new Vector2(400, 35));
        subtitle.GetComponent<TMP_Text>().color = new Color(0.7f, 0.7f, 0.7f);

        var dodgeBtn = CreateTMPButton(modePanel.transform, "PoseDodgeButton", "Pose Dodge");
        SetAnchored(dodgeBtn, new Vector2(0.5f, 0.55f), new Vector2(340, 60));

        var balanceBtn = CreateTMPButton(modePanel.transform, "SingleLegBalanceButton", "Single-Leg Balance");
        SetAnchored(balanceBtn, new Vector2(0.5f, 0.46f), new Vector2(340, 60));

        GameObject leanBalanceBtnGo = null;
        if (leanBalanceMgr != null)
        {
            leanBalanceBtnGo = CreateTMPButton(modePanel.transform, "LeanBalanceButton", "Lean Balance");
            SetAnchored(leanBalanceBtnGo, new Vector2(0.5f, 0.40f), new Vector2(340, 55));
            leanBalanceBtnGo.GetComponent<Image>().color = new Color(0.15f, 0.6f, 0.35f, 1f);
        }

        GameObject coinMineBtnGo = null;
        if (coinMineMgr != null)
        {
            coinMineBtnGo = CreateTMPButton(modePanel.transform, "CoinMineButton", "Coin Mine");
            SetAnchored(coinMineBtnGo, new Vector2(0.5f, 0.31f), new Vector2(340, 55));
            coinMineBtnGo.GetComponent<Image>().color = new Color(0.9f, 0.7f, 0.15f, 1f);
        }

        var testBtn = CreateTMPButton(modePanel.transform, "PoseTestButton", "Pose Test");
        SetAnchored(testBtn, new Vector2(0.5f, 0.22f), new Vector2(340, 55));
        testBtn.GetComponent<Image>().color = new Color(0.4f, 0.4f, 0.5f, 1f);

        var ocrBtn = CreateTMPButton(modePanel.transform, "OcrButton", "OCR");
        SetAnchored(ocrBtn, new Vector2(0.5f, 0.14f), new Vector2(340, 55));
        ocrBtn.GetComponent<Image>().color = new Color(0.32f, 0.48f, 0.7f, 1f);

        // ────────────────────────────────────────────
        // ── Dodge UI Panel ──
        // ────────────────────────────────────────────
        var dodgeUI = CreatePanel(canvasGo.transform, "DodgeUIPanel");
        StretchFill(dodgeUI);
        dodgeUI.GetComponent<Image>().color = new Color(0, 0, 0, 0);
        dodgeUI.GetComponent<Image>().raycastTarget = false;

        var dodgeScore = CreateTMPText(dodgeUI.transform, "DodgeScoreText", "Score: 0", 36, TextAlignmentOptions.TopLeft);
        SetAnchoredCorner(dodgeScore, Corner.TopLeft, new Vector2(20, -15), new Vector2(280, 50));

        var dodgeLives = CreateTMPText(dodgeUI.transform, "DodgeLivesText", "Lives: 3", 36, TextAlignmentOptions.TopRight);
        SetAnchoredCorner(dodgeLives, Corner.TopRight, new Vector2(-20, -15), new Vector2(280, 50));

        var dodgeGesture = CreateTMPText(dodgeUI.transform, "DodgeGestureText", "You: STANDING", 28, TextAlignmentOptions.BottomLeft);
        SetAnchoredCorner(dodgeGesture, Corner.BottomLeft, new Vector2(20, 15), new Vector2(350, 45));

        var dodgeNext = CreateTMPText(dodgeUI.transform, "DodgeNextText", "", 28, TextAlignmentOptions.BottomRight);
        SetAnchoredCorner(dodgeNext, Corner.BottomRight, new Vector2(-20, 15), new Vector2(350, 45));

        // Dodge start prompt
        var dodgeStartPanel = CreatePanel(dodgeUI.transform, "DodgeStartPrompt");
        StretchFill(dodgeStartPanel);
        dodgeStartPanel.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.15f, 0.85f);
        CreateTMPText(dodgeStartPanel.transform, "Label", "Pose Dodge\nDodge with your body!", 36, TextAlignmentOptions.Center);
        SetAnchored(dodgeStartPanel.transform.Find("Label").gameObject, new Vector2(0.5f, 0.6f), new Vector2(500, 120));
        var dodgeStartBtn = CreateTMPButton(dodgeStartPanel.transform, "DodgeStartButton", "Start");
        SetAnchored(dodgeStartBtn, new Vector2(0.5f, 0.4f), new Vector2(220, 55));

        // Dodge game over
        var dodgeGameOver = CreatePanel(dodgeUI.transform, "DodgeGameOverPanel");
        StretchFill(dodgeGameOver);
        dodgeGameOver.GetComponent<Image>().color = new Color(0.15f, 0.05f, 0.05f, 0.85f);
        var goLabel = CreateTMPText(dodgeGameOver.transform, "GameOverLabel", "Game Over", 48, TextAlignmentOptions.Center);
        SetAnchored(goLabel, new Vector2(0.5f, 0.68f), new Vector2(400, 60));
        var goScore = CreateTMPText(dodgeGameOver.transform, "GameOverScore", "Final Score: 0", 32, TextAlignmentOptions.Center);
        SetAnchored(goScore, new Vector2(0.5f, 0.56f), new Vector2(400, 50));
        var dodgeRestartBtn = CreateTMPButton(dodgeGameOver.transform, "DodgeRestartButton", "Restart");
        SetAnchored(dodgeRestartBtn, new Vector2(0.5f, 0.42f), new Vector2(220, 55));
        var dodgeBackBtn = CreateTMPButton(dodgeGameOver.transform, "DodgeBackButton", "Back to Menu");
        SetAnchored(dodgeBackBtn, new Vector2(0.5f, 0.30f), new Vector2(260, 55));
        dodgeBackBtn.GetComponent<Image>().color = new Color(0.4f, 0.4f, 0.5f, 1f);
        dodgeGameOver.SetActive(false);

        dodgeUI.SetActive(false);

        // ────────────────────────────────────────────
        // ── Balance UI Panel ──
        // ────────────────────────────────────────────
        var balanceUI = CreatePanel(canvasGo.transform, "BalanceUIPanel");
        StretchFill(balanceUI);
        balanceUI.GetComponent<Image>().color = new Color(0, 0, 0, 0);
        balanceUI.GetComponent<Image>().raycastTarget = false;

        var balTimer = CreateTMPText(balanceUI.transform, "BalanceTimerText", "Time: 0s / 30s", 32, TextAlignmentOptions.TopLeft);
        SetAnchoredCorner(balTimer, Corner.TopLeft, new Vector2(20, -15), new Vector2(350, 50));

        var balScore = CreateTMPText(balanceUI.transform, "BalanceScoreText", "Score: 0", 32, TextAlignmentOptions.TopRight);
        SetAnchoredCorner(balScore, Corner.TopRight, new Vector2(-20, -15), new Vector2(280, 50));

        var balInstruction = CreateTMPText(balanceUI.transform, "BalanceInstructionText",
            "Lift one leg to begin...", 30, TextAlignmentOptions.Center);
        SetAnchored(balInstruction, new Vector2(0.5f, 0.82f), new Vector2(550, 50));

        var sliderGo = CreateSlider(balanceUI.transform, "StabilityBar");
        SetAnchored(sliderGo, new Vector2(0.5f, 0.08f), new Vector2(500, 30));

        var sliderLabel = CreateTMPText(balanceUI.transform, "StabilityLabel", "Stability", 20, TextAlignmentOptions.Center);
        SetAnchored(sliderLabel, new Vector2(0.5f, 0.04f), new Vector2(200, 30));
        sliderLabel.GetComponent<TMP_Text>().color = new Color(0.7f, 0.7f, 0.7f);

        // Balance start prompt
        var balStartPanel = CreatePanel(balanceUI.transform, "BalanceStartPrompt");
        StretchFill(balStartPanel);
        balStartPanel.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.15f, 0.85f);
        CreateTMPText(balStartPanel.transform, "Label",
            "Single-Leg Balance\nStand on one leg and stay stable!", 36, TextAlignmentOptions.Center);
        SetAnchored(balStartPanel.transform.Find("Label").gameObject, new Vector2(0.5f, 0.6f), new Vector2(600, 120));
        var balStartBtn = CreateTMPButton(balStartPanel.transform, "BalanceStartButton", "Start");
        SetAnchored(balStartBtn, new Vector2(0.5f, 0.4f), new Vector2(220, 55));

        // Balance game over
        var balGameOver = CreatePanel(balanceUI.transform, "BalanceGameOverPanel");
        StretchFill(balGameOver);
        balGameOver.GetComponent<Image>().color = new Color(0.15f, 0.05f, 0.05f, 0.85f);
        var balGoLabel = CreateTMPText(balGameOver.transform, "GameOverLabel", "Round Over", 48, TextAlignmentOptions.Center);
        SetAnchored(balGoLabel, new Vector2(0.5f, 0.72f), new Vector2(400, 60));
        var balGoScore = CreateTMPText(balGameOver.transform, "BalGoScore", "Score: 0", 32, TextAlignmentOptions.Center);
        SetAnchored(balGoScore, new Vector2(0.5f, 0.60f), new Vector2(400, 45));
        var balGoTime = CreateTMPText(balGameOver.transform, "BalGoTime", "Time: 0s", 28, TextAlignmentOptions.Center);
        SetAnchored(balGoTime, new Vector2(0.5f, 0.51f), new Vector2(400, 40));
        var balRestartBtn = CreateTMPButton(balGameOver.transform, "BalRestartButton", "Restart");
        SetAnchored(balRestartBtn, new Vector2(0.5f, 0.38f), new Vector2(220, 55));
        var balBackBtn = CreateTMPButton(balGameOver.transform, "BalBackButton", "Back to Menu");
        SetAnchored(balBackBtn, new Vector2(0.5f, 0.26f), new Vector2(260, 55));
        balBackBtn.GetComponent<Image>().color = new Color(0.4f, 0.4f, 0.5f, 1f);
        balGameOver.SetActive(false);

        balanceUI.SetActive(false);

        // ────────────────────────────────────────────
        // ── Lean Balance UI Panel ──
        // ────────────────────────────────────────────
        GameObject leanBalanceUI = null;
        Slider leanBarSlider = null;
        if (leanBalanceMgr != null)
        {
            leanBalanceUI = CreatePanel(canvasGo.transform, "LeanBalanceUIPanel");
            StretchFill(leanBalanceUI);
            leanBalanceUI.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            leanBalanceUI.GetComponent<Image>().raycastTarget = false;

            var leanTimer = CreateTMPText(leanBalanceUI.transform, "LeanTimerText", "Time: 0s", 32, TextAlignmentOptions.TopLeft);
            SetAnchoredCorner(leanTimer, Corner.TopLeft, new Vector2(20, -15), new Vector2(280, 50));

            var leanScore = CreateTMPText(leanBalanceUI.transform, "LeanScoreText", "In zone: 0s", 32, TextAlignmentOptions.TopRight);
            SetAnchoredCorner(leanScore, Corner.TopRight, new Vector2(-20, -15), new Vector2(280, 50));

            var leanInstruction = CreateTMPText(leanBalanceUI.transform, "LeanInstructionText",
                "Lean your body left or right. Keep the bar in the green (center).", 28, TextAlignmentOptions.Center);
            SetAnchored(leanInstruction, new Vector2(0.5f, 0.88f), new Vector2(700, 60));

            var leanSliderGo = CreateLeanBarSlider(leanBalanceUI.transform, "LeanBarSlider", 0.12f);
            SetAnchored(leanSliderGo, new Vector2(0.5f, 0.15f), new Vector2(600, 40));
            leanBarSlider = leanSliderGo.GetComponent<Slider>();

            var leanStartPanel = CreatePanel(leanBalanceUI.transform, "LeanBalanceStartPrompt");
            StretchFill(leanStartPanel);
            leanStartPanel.GetComponent<Image>().color = new Color(0.1f, 0.15f, 0.1f, 0.85f);
            CreateTMPText(leanStartPanel.transform, "Label",
                "Lean Balance\nKeep the bar in the green by leaning your body.", 36, TextAlignmentOptions.Center);
            SetAnchored(leanStartPanel.transform.Find("Label").gameObject, new Vector2(0.5f, 0.6f), new Vector2(600, 120));
            var leanStartBtn = CreateTMPButton(leanStartPanel.transform, "LeanStartButton", "Start");
            SetAnchored(leanStartBtn, new Vector2(0.5f, 0.4f), new Vector2(220, 55));

            var leanGameOver = CreatePanel(leanBalanceUI.transform, "LeanBalanceGameOverPanel");
            StretchFill(leanGameOver);
            leanGameOver.GetComponent<Image>().color = new Color(0.15f, 0.05f, 0.05f, 0.85f);
            var leanGoLabel = CreateTMPText(leanGameOver.transform, "LeanGoLabel", "Round Over", 48, TextAlignmentOptions.Center);
            SetAnchored(leanGoLabel, new Vector2(0.5f, 0.68f), new Vector2(400, 60));
            var leanGoScore = CreateTMPText(leanGameOver.transform, "LeanGoScore", "Time in zone: 0s", 32, TextAlignmentOptions.Center);
            SetAnchored(leanGoScore, new Vector2(0.5f, 0.56f), new Vector2(400, 50));
            var leanRestartBtn = CreateTMPButton(leanGameOver.transform, "LeanRestartButton", "Restart");
            SetAnchored(leanRestartBtn, new Vector2(0.5f, 0.42f), new Vector2(220, 55));
            var leanBackBtn = CreateTMPButton(leanGameOver.transform, "LeanBackButton", "Back to Menu");
            SetAnchored(leanBackBtn, new Vector2(0.5f, 0.30f), new Vector2(260, 55));
            leanBackBtn.GetComponent<Image>().color = new Color(0.4f, 0.4f, 0.5f, 1f);
            leanGameOver.SetActive(false);

            leanBalanceUI.SetActive(false);

            leanBalanceMgr.leanBarSlider = leanBarSlider;
            leanBalanceMgr.timerText = leanTimer.GetComponent<TMP_Text>();
            leanBalanceMgr.scoreText = leanScore.GetComponent<TMP_Text>();
            leanBalanceMgr.instructionText = leanInstruction.GetComponent<TMP_Text>();
            leanBalanceMgr.startPromptPanel = leanStartPanel;
            leanBalanceMgr.gameOverPanel = leanGameOver;
            leanBalanceMgr.gameOverScoreText = leanGoScore.GetComponent<TMP_Text>();
            WireButton(leanStartBtn, leanBalanceMgr, nameof(LeanBalanceGameManager.StartGame));
            WireButton(leanRestartBtn, leanBalanceMgr, nameof(LeanBalanceGameManager.StartGame));
            WireButton(leanBackBtn, selector, nameof(ArchitectGameSelector.BackToMenu));
            EditorUtility.SetDirty(leanBalanceMgr);
        }

        // ────────────────────────────────────────────
        // ── Coin Mine UI Panel ──
        // ────────────────────────────────────────────
        GameObject coinMineUI = null;
        if (coinMineMgr != null)
        {
            coinMineUI = CreatePanel(canvasGo.transform, "CoinMineUIPanel");
            StretchFill(coinMineUI);
            coinMineUI.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            coinMineUI.GetComponent<Image>().raycastTarget = false;

            var coinScore = CreateTMPText(coinMineUI.transform, "CoinMineScoreText", "Coins: 0", 36, TextAlignmentOptions.TopLeft);
            SetAnchoredCorner(coinScore, Corner.TopLeft, new Vector2(20, -15), new Vector2(220, 50));

            var coinLaneHint = CreateTMPText(coinMineUI.transform, "CoinMineLaneHintText", "← LEAN LEFT  CENTER  LEAN RIGHT →", 28, TextAlignmentOptions.Center);
            SetAnchored(coinLaneHint, new Vector2(0.5f, 0.92f), new Vector2(700, 45));
            coinLaneHint.GetComponent<TMP_Text>().color = new Color(1f, 0.9f, 0.5f, 1f);

            var coinYouAre = CreateTMPText(coinMineUI.transform, "CoinMineYouAreText", "You: CENTER", 26, TextAlignmentOptions.Center);
            SetAnchored(coinYouAre, new Vector2(0.5f, 0.85f), new Vector2(280, 40));

            var coinEndRunBtn = CreateTMPButton(coinMineUI.transform, "CoinMineEndRunButton", "End Run");
            SetAnchoredCorner(coinEndRunBtn, Corner.TopRight, new Vector2(-20, -15), new Vector2(160, 45));
            coinEndRunBtn.GetComponent<Image>().color = new Color(0.5f, 0.4f, 0.2f, 1f);

            var coinStartPanel = CreatePanel(coinMineUI.transform, "CoinMineStartPrompt");
            StretchFill(coinStartPanel);
            coinStartPanel.GetComponent<Image>().color = new Color(0.12f, 0.1f, 0.05f, 0.9f);
            CreateTMPText(coinStartPanel.transform, "Label",
                "Coin Mine\nLean left, center, or right to match the coin's lane and collect!", 34, TextAlignmentOptions.Center);
            SetAnchored(coinStartPanel.transform.Find("Label").gameObject, new Vector2(0.5f, 0.6f), new Vector2(650, 120));
            var coinStartBtn = CreateTMPButton(coinStartPanel.transform, "CoinMineStartButton", "Start");
            SetAnchored(coinStartBtn, new Vector2(0.5f, 0.4f), new Vector2(220, 55));
            coinStartBtn.GetComponent<Image>().color = new Color(0.9f, 0.7f, 0.15f, 1f);

            var coinGameOver = CreatePanel(coinMineUI.transform, "CoinMineGameOverPanel");
            StretchFill(coinGameOver);
            coinGameOver.GetComponent<Image>().color = new Color(0.15f, 0.1f, 0.02f, 0.9f);
            var coinGoLabel = CreateTMPText(coinGameOver.transform, "CoinGoLabel", "Round Over", 48, TextAlignmentOptions.Center);
            SetAnchored(coinGoLabel, new Vector2(0.5f, 0.68f), new Vector2(400, 60));
            var coinGoScore = CreateTMPText(coinGameOver.transform, "CoinGoScore", "Coins: 0", 32, TextAlignmentOptions.Center);
            SetAnchored(coinGoScore, new Vector2(0.5f, 0.56f), new Vector2(400, 50));
            var coinBackBtn = CreateTMPButton(coinGameOver.transform, "CoinMineBackButton", "Back to Menu");
            SetAnchored(coinBackBtn, new Vector2(0.5f, 0.40f), new Vector2(260, 55));
            coinBackBtn.GetComponent<Image>().color = new Color(0.4f, 0.4f, 0.5f, 1f);
            coinGameOver.SetActive(false);

            coinMineUI.SetActive(false);

            coinMineMgr.scoreText = coinScore.GetComponent<TMP_Text>();
            coinMineMgr.laneHintText = coinLaneHint.GetComponent<TMP_Text>();
            coinMineMgr.youAreHereText = coinYouAre.GetComponent<TMP_Text>();
            coinMineMgr.startPromptPanel = coinStartPanel;
            coinMineMgr.gameOverPanel = coinGameOver;
            coinMineMgr.gameOverScoreText = coinGoScore.GetComponent<TMP_Text>();
            WireButton(coinStartBtn, coinMineMgr, nameof(CoinMineGameManager.StartGame));
            WireButton(coinEndRunBtn, coinMineMgr, nameof(CoinMineGameManager.EndGame));
            WireButton(coinBackBtn, selector, nameof(ArchitectGameSelector.BackToMenu));
            EditorUtility.SetDirty(coinMineMgr);
        }

        // ────────────────────────────────────────────
        // ── Pose Test UI Panel ──
        // ────────────────────────────────────────────
        var testUI = CreatePanel(canvasGo.transform, "PoseTestUIPanel");
        StretchFill(testUI);
        testUI.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.1f, 0.6f);
        testUI.GetComponent<Image>().raycastTarget = false;

        var testTitle = CreateTMPText(testUI.transform, "TestTitle", "POSE TEST", 42, TextAlignmentOptions.TopLeft);
        SetAnchoredCorner(testTitle, Corner.TopLeft, new Vector2(30, -20), new Vector2(400, 55));

        var gestureL = CreateTMPText(testUI.transform, "GestureLabel", "Gesture: --", 36, TextAlignmentOptions.TopLeft);
        SetAnchoredCorner(gestureL, Corner.TopLeft, new Vector2(30, -85), new Vector2(500, 50));

        var legL = CreateTMPText(testUI.transform, "StandingLegLabel", "Standing Leg: None", 30, TextAlignmentOptions.TopLeft);
        SetAnchoredCorner(legL, Corner.TopLeft, new Vector2(30, -140), new Vector2(500, 45));

        var swayL = CreateTMPText(testUI.transform, "SwayLabel", "Sway: 0.0000", 30, TextAlignmentOptions.TopLeft);
        SetAnchoredCorner(swayL, Corner.TopLeft, new Vector2(30, -190), new Vector2(500, 45));

        var stableL = CreateTMPText(testUI.transform, "StabilityLabel2", "STABLE", 36, TextAlignmentOptions.TopLeft);
        SetAnchoredCorner(stableL, Corner.TopLeft, new Vector2(30, -245), new Vector2(300, 50));
        stableL.GetComponent<TMP_Text>().color = Color.green;

        var kpInfo = CreateTMPText(testUI.transform, "KeypointInfo", "", 22, TextAlignmentOptions.BottomLeft);
        SetAnchoredCorner(kpInfo, Corner.BottomLeft, new Vector2(30, 60), new Vector2(600, 160));
        kpInfo.GetComponent<TMP_Text>().color = new Color(0.8f, 0.8f, 0.8f);

        var testBackBtn = CreateTMPButton(testUI.transform, "TestBackButton", "Back to Menu");
        SetAnchoredCorner(testBackBtn, Corner.TopRight, new Vector2(-20, -20), new Vector2(220, 50));
        testBackBtn.GetComponent<Image>().color = new Color(0.4f, 0.4f, 0.5f, 1f);

        // ── Frame Sync Indicator (for MTP latency measurement) ──
        var syncGo = new GameObject("FrameSyncIndicator", typeof(RectTransform));
        syncGo.transform.SetParent(testUI.transform, false);
        var syncIndicator = new GameObject("SyncColour", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        syncIndicator.transform.SetParent(syncGo.transform, false);
        SetAnchoredCorner(syncIndicator, Corner.BottomRight, new Vector2(-20, 15), new Vector2(80, 80));
        syncIndicator.GetComponent<Image>().color = Color.red;
        var syncFrameText = CreateTMPText(syncGo.transform, "SyncFrameCount", "0", 36, TextAlignmentOptions.BottomRight);
        SetAnchoredCorner(syncFrameText, Corner.BottomRight, new Vector2(-110, 15), new Vector2(300, 80));
        var syncTmp = syncFrameText.GetComponent<TMP_Text>();
        syncTmp.color = new Color(0.9f, 0.9f, 0.9f, 0.8f);
        syncTmp.overflowMode = TextOverflowModes.Overflow;
        syncTmp.enableWordWrapping = false;
        var syncComp = syncGo.AddComponent<FrameSyncIndicator>();
        syncComp.indicator = syncIndicator.GetComponent<Image>();
        syncComp.frameCountText = syncTmp;

        testUI.SetActive(false);

        // ────────────────────────────────────────────
        // ── OCR UI Panel ──
        // ────────────────────────────────────────────
        var ocrUI = CreatePanel(canvasGo.transform, "OcrUIPanel");
        StretchFill(ocrUI);
        ocrUI.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.1f, 0.6f);
        ocrUI.GetComponent<Image>().raycastTarget = false;

        var ocrTitle = CreateTMPText(ocrUI.transform, "OcrTitle", "OCR", 42, TextAlignmentOptions.TopLeft);
        SetAnchoredCorner(ocrTitle, Corner.TopLeft, new Vector2(30, -20), new Vector2(400, 55));

        var ocrStatus = CreateTMPText(ocrUI.transform, "OcrStatus", "Waiting for pose UDP...", 26, TextAlignmentOptions.TopLeft);
        SetAnchoredCorner(ocrStatus, Corner.TopLeft, new Vector2(30, -78), new Vector2(650, 42));
        ocrStatus.GetComponent<TMP_Text>().color = new Color(0.75f, 0.9f, 0.75f, 1f);

        var ocrValue = CreateTMPText(ocrUI.transform, "OcrValue", "--", 260, TextAlignmentOptions.Center);
        SetAnchored(ocrValue, new Vector2(0.5f, 0.08f), new Vector2(1600, 400));
        var ocrValueTmp = ocrValue.GetComponent<TMP_Text>();
        ocrValueTmp.enableWordWrapping = false;
        ocrValueTmp.overflowMode = TextOverflowModes.Overflow;

        var ocrBackBtn = CreateTMPButton(ocrUI.transform, "OcrBackButton", "Back to Menu");
        SetAnchoredCorner(ocrBackBtn, Corner.TopRight, new Vector2(-20, -20), new Vector2(220, 50));
        ocrBackBtn.GetComponent<Image>().color = new Color(0.4f, 0.4f, 0.5f, 1f);

        ocrUI.SetActive(false);

        // ────────────────────────────────────────────
        // ── Wire everything ──
        // ────────────────────────────────────────────

        // GameSelector
        selector.modeSelectPanel = modePanel;
        selector.poseDodgeButton = dodgeBtn.GetComponent<Button>();
        selector.singleLegBalanceButton = balanceBtn.GetComponent<Button>();
        selector.leanBalanceButton = leanBalanceBtnGo != null ? leanBalanceBtnGo.GetComponent<Button>() : null;
        selector.coinMineButton = coinMineBtnGo != null ? coinMineBtnGo.GetComponent<Button>() : null;
        selector.poseTestButton = testBtn.GetComponent<Button>();
        selector.ocrButton = ocrBtn.GetComponent<Button>();
        selector.dodgeUIPanel = dodgeUI;
        selector.balanceUIPanel = balanceUI;
        selector.leanBalanceUIPanel = leanBalanceUI;
        selector.coinMineUIPanel = coinMineUI;
        selector.poseTestUIPanel = testUI;
        selector.ocrUIPanel = ocrUI;

        // Back-to-menu buttons
        WireButton(dodgeBackBtn, selector, nameof(ArchitectGameSelector.BackToMenu));
        WireButton(balBackBtn, selector, nameof(ArchitectGameSelector.BackToMenu));
        WireButton(testBackBtn, selector, nameof(ArchitectGameSelector.BackToMenu));
        WireButton(ocrBackBtn, selector, nameof(ArchitectGameSelector.BackToMenu));

        // Dodge
        dodgeMgr.scoreText = dodgeScore.GetComponent<TMP_Text>();
        dodgeMgr.livesText = dodgeLives.GetComponent<TMP_Text>();
        dodgeMgr.gestureText = dodgeGesture.GetComponent<TMP_Text>();
        dodgeMgr.nextActionText = dodgeNext.GetComponent<TMP_Text>();
        dodgeMgr.startPromptPanel = dodgeStartPanel;
        dodgeMgr.gameOverPanel = dodgeGameOver;
        dodgeMgr.gameOverScoreText = goScore.GetComponent<TMP_Text>();
        WireButton(dodgeStartBtn, dodgeMgr, nameof(DodgeGameManager.StartGame));
        WireButton(dodgeRestartBtn, dodgeMgr, nameof(DodgeGameManager.StartGame));
        EditorUtility.SetDirty(dodgeMgr);

        // Balance
        balanceMgr.stabilityBar = sliderGo.GetComponent<Slider>();
        balanceMgr.timerText = balTimer.GetComponent<TMP_Text>();
        balanceMgr.scoreText = balScore.GetComponent<TMP_Text>();
        balanceMgr.instructionText = balInstruction.GetComponent<TMP_Text>();
        balanceMgr.startPromptPanel = balStartPanel;
        balanceMgr.gameOverPanel = balGameOver;
        balanceMgr.gameOverScoreText = balGoScore.GetComponent<TMP_Text>();
        balanceMgr.gameOverTimeText = balGoTime.GetComponent<TMP_Text>();
        WireButton(balStartBtn, balanceMgr, nameof(SingleLegBalanceManager.StartGame));
        WireButton(balRestartBtn, balanceMgr, nameof(SingleLegBalanceManager.StartGame));
        EditorUtility.SetDirty(balanceMgr);

        // Pose Test
        if (testMode != null)
        {
            testMode.gestureLabel = gestureL.GetComponent<TMP_Text>();
            testMode.standingLegLabel = legL.GetComponent<TMP_Text>();
            testMode.swayLabel = swayL.GetComponent<TMP_Text>();
            testMode.stabilityLabel = stableL.GetComponent<TMP_Text>();
            testMode.keypointInfoLabel = kpInfo.GetComponent<TMP_Text>();
            EditorUtility.SetDirty(testMode);
        }

        // OCR mode
        if (ocrMode != null)
        {
            ocrMode.ocrValueLabel = ocrValueTmp;
            ocrMode.statusLabel = ocrStatus.GetComponent<TMP_Text>();
            EditorUtility.SetDirty(ocrMode);
        }

        EditorUtility.SetDirty(selector);
        Undo.RegisterCreatedObjectUndo(canvasGo, "Create Game UI");
        Selection.activeGameObject = canvasGo;
        Debug.Log("[Architect] Game UI created and wired. Press Play to test.");
    }

    // ── Helpers ──

    static GameObject CreatePanel(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        return go;
    }

    static GameObject CreateTMPText(Transform parent, string name, string text, float size, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.alignment = align;
        tmp.color = Color.white;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        return go;
    }

    static GameObject CreateTMPButton(Transform parent, string name, string label)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = new Color(0.22f, 0.55f, 0.85f, 1f);
        var textGo = CreateTMPText(go.transform, "Text", label, 24, TextAlignmentOptions.Center);
        StretchFill(textGo);
        return go;
    }

    static GameObject CreateSlider(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Slider));
        go.transform.SetParent(parent, false);

        var bg = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        bg.transform.SetParent(go.transform, false);
        StretchFill(bg);
        bg.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f, 1f);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(go.transform, false);
        StretchFill(fillArea);

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        StretchFill(fill);
        fill.GetComponent<Image>().color = new Color(0.2f, 0.8f, 0.3f, 1f);

        var slider = go.GetComponent<Slider>();
        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.interactable = false;
        slider.value = 1f;
        return go;
    }

    static GameObject CreateLeanBarSlider(Transform parent, string name, float range)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Slider));
        go.transform.SetParent(parent, false);

        var bg = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        bg.transform.SetParent(go.transform, false);
        StretchFill(bg);
        bg.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.25f, 1f);

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(go.transform, false);
        StretchFill(handleArea);

        var handle = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        handle.transform.SetParent(handleArea.transform, false);
        var handleRt = handle.GetComponent<RectTransform>();
        handleRt.anchorMin = new Vector2(0.5f, 0.5f);
        handleRt.anchorMax = new Vector2(0.5f, 0.5f);
        handleRt.pivot = new Vector2(0.5f, 0.5f);
        handleRt.anchoredPosition = Vector2.zero;
        handleRt.sizeDelta = new Vector2(24f, 36f);
        handle.GetComponent<Image>().color = new Color(0.2f, 0.85f, 0.4f, 1f);

        var slider = go.GetComponent<Slider>();
        slider.minValue = -range;
        slider.maxValue = range;
        slider.value = 0f;
        slider.handleRect = handleRt;
        slider.direction = Slider.Direction.LeftToRight;
        slider.interactable = false;
        return go;
    }

    static void StretchFill(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void SetAnchored(GameObject go, Vector2 anchorPos, Vector2 size)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorPos;
        rt.anchorMax = anchorPos;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
    }

    enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

    static void SetAnchoredCorner(GameObject go, Corner corner, Vector2 offset, Vector2 size)
    {
        var rt = go.GetComponent<RectTransform>();
        switch (corner)
        {
            case Corner.TopLeft:
                rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
                rt.pivot = new Vector2(0, 1);
                break;
            case Corner.TopRight:
                rt.anchorMin = rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(1, 1);
                break;
            case Corner.BottomLeft:
                rt.anchorMin = rt.anchorMax = new Vector2(0, 0);
                rt.pivot = new Vector2(0, 0);
                break;
            case Corner.BottomRight:
                rt.anchorMin = rt.anchorMax = new Vector2(1, 0);
                rt.pivot = new Vector2(1, 0);
                break;
        }
        rt.anchoredPosition = offset;
        rt.sizeDelta = size;
    }

    static void WireButton(GameObject buttonGo, Object target, string methodName)
    {
        var btn = buttonGo.GetComponent<Button>();
        if (btn == null) return;
        var method = target.GetType().GetMethod(methodName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (method == null) return;
        UnityEventTools.AddVoidPersistentListener(btn.onClick,
            (UnityAction)System.Delegate.CreateDelegate(typeof(UnityAction), target, method));
    }
}
