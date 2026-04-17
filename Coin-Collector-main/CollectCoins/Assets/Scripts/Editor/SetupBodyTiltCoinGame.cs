using UnityEngine;
using UnityEditor;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// One-click setup: switch to body-tilt coin collector mode (PoseBridge, player, spawner, walls, UI).
/// Run once per scene. Creates CountText, TimerText, WinText if missing and centers the ball.
/// </summary>
public static class SetupBodyTiltCoinGame
{
    [MenuItem("Tools/Body Tilt Coin Collector/Setup Scene")]
    static void Setup()
    {
        // 1. PoseBridge
        if (Object.FindFirstObjectByType<PoseReceiver>() == null)
        {
            var bridge = new GameObject("PoseBridge");
            bridge.AddComponent<PoseReceiver>().port = 5555;
            bridge.AddComponent<PoseGestureDetector>();
            bridge.AddComponent<BodyTiltInput>();
            Undo.RegisterCreatedObjectUndo(bridge, "PoseBridge");
            Debug.Log("Created PoseBridge.");
        }

        // 2. Player (ball): create if missing, then add BodyTiltCoinCollectorPlayer, remove old controllers, center on lane (X=0)
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) player = Object.FindFirstObjectByType<Rigidbody>()?.gameObject;
        if (player == null)
        {
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Player";
            ball.tag = "Player";
            ball.transform.position = new Vector3(0f, 0.5f, 0f);
            var rb = ball.GetComponent<Rigidbody>();
            if (rb == null) rb = ball.AddComponent<Rigidbody>();
            rb.useGravity = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            Undo.RegisterCreatedObjectUndo(ball, "Player");
            player = ball;
            Debug.Log("Created Player (ball) - no Player or Rigidbody found in scene.");
        }
        if (player != null)
        {
            var bodyTilt = player.GetComponent<BodyTiltCoinCollectorPlayer>();
            if (bodyTilt == null)
            {
                bodyTilt = Undo.AddComponent<BodyTiltCoinCollectorPlayer>(player);
                bodyTilt.sideBound = 5f;
                bodyTilt.forwardSpeed = 16f;
                bodyTilt.tiltLaneRange = 1f;
                bodyTilt.gameDurationSeconds = 30f;
            }
            // Center ball on the lane (middle)
            var t = player.transform;
            if (t.position.x != 0f)
            {
                Undo.RecordObject(t, "Center Player");
                t.position = new Vector3(0f, t.position.y, t.position.z);
                Debug.Log("Player centered on lane (X=0).");
            }
            // Find or create UI texts (use BodyTilt* names to avoid duplicate coin count from old scene)
            var countText = GameObject.Find("BodyTiltCountText")?.GetComponent<TextMeshProUGUI>()
                ?? GameObject.Find("CountText")?.GetComponent<TextMeshProUGUI>();
            var timerText = GameObject.Find("BodyTiltTimerText")?.GetComponent<TextMeshProUGUI>()
                ?? GameObject.Find("TimerText")?.GetComponent<TextMeshProUGUI>();
            var winText = GameObject.Find("BodyTiltWinText")?.GetComponent<TextMeshProUGUI>()
                ?? GameObject.Find("WinText")?.GetComponent<TextMeshProUGUI>();
            if (countText == null || timerText == null || winText == null)
                CreateBodyTiltUI(out countText, out timerText, out winText);
            if (countText != null) bodyTilt.countText = countText;
            if (timerText != null) bodyTilt.timerText = timerText;
            if (winText != null) bodyTilt.winText = winText;
            var playPanel = GameObject.Find("BodyTiltPlayPanel");
            var gameOverPanel = GameObject.Find("BodyTiltGameOverPanel");
            if (playPanel != null) bodyTilt.playButtonPanel = playPanel;
            if (gameOverPanel != null) bodyTilt.gameOverPanel = gameOverPanel;
            var playBtn = playPanel != null ? playPanel.GetComponentInChildren<Button>() : null;
            if (playBtn != null) bodyTilt.playButton = playBtn;
            if (gameOverPanel != null)
            {
                var buttons = gameOverPanel.GetComponentsInChildren<Button>();
                foreach (var b in buttons)
                {
                    if (b.name.Contains("Restart")) bodyTilt.restartButton = b;
                    else if (b.name.Contains("Home")) bodyTilt.homeButton = b;
                }
            }
            // Ensure Play/GameOver panels exist even when UI was not created this run
            var canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas != null && (playPanel == null || gameOverPanel == null))
            {
                var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset") ?? Resources.Load<TMPro.TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                CreatePlayAndGameOverUI(canvas, canvas.transform, font, bodyTilt.winText);
                if (playPanel == null) bodyTilt.playButtonPanel = GameObject.Find("BodyTiltPlayPanel");
                if (gameOverPanel == null) bodyTilt.gameOverPanel = GameObject.Find("BodyTiltGameOverPanel");
                var newGo = bodyTilt.gameOverPanel;
                if (newGo != null) foreach (var b in newGo.GetComponentsInChildren<Button>())
                {
                    if (b.name.Contains("Restart")) bodyTilt.restartButton = b;
                    else if (b.name.Contains("Home")) bodyTilt.homeButton = b;
                }
            }
            DisableDuplicateCoinTexts(countText);
            Debug.Log("Updated Player with BodyTiltCoinCollectorPlayer and UI.");
        }
        else
            Debug.LogWarning("No Player found. Add BodyTiltCoinCollectorPlayer to the ball and assign Count Text / Win Text.");

        // 3. Coin spawner
        var spawnerObj = GameObject.Find("CoinSpawner");
        if (spawnerObj == null)
        {
            spawnerObj = new GameObject("CoinSpawner");
            Undo.RegisterCreatedObjectUndo(spawnerObj, "CoinSpawner");
        }
        var spawner = spawnerObj.GetComponent<CoinSpawner>();
        if (spawner == null) spawner = Undo.AddComponent<CoinSpawner>(spawnerObj);
        if (player != null) spawner.player = player.transform;
        var coinPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Coin.prefab");
        if (coinPrefab != null) spawner.coinPrefab = coinPrefab;
        if (player != null) spawner.gamePlayer = player.GetComponent<BodyTiltCoinCollectorPlayer>();
        spawner.firstCoinOffsetZ = 25f;
        spawner.gapBetweenCoins = 4f;
        spawner.totalCoinsToSpawn = 120;
        spawner.laneLength = 600f;
        spawner.leftLaneX = -3f;
        spawner.middleLaneX = 0f;
        spawner.rightLaneX = 3f;
        Debug.Log("CoinSpawner ready. Assign Coin prefab if missing.");

        // 4. Lane walls
        var wallsObj = GameObject.Find("LaneWalls");
        if (wallsObj == null)
        {
            wallsObj = new GameObject("LaneWalls");
            Undo.RegisterCreatedObjectUndo(wallsObj, "LaneWalls");
        }
        var walls = wallsObj.GetComponent<LaneWalls>();
        if (walls == null) walls = Undo.AddComponent<LaneWalls>(wallsObj);
        walls.sideBound = 5f;
        walls.wallDepth = 600f;
        walls.createAtStart = true;
        Debug.Log("LaneWalls added. Walls are created at Play.");

        // 5. Floor (so ball doesn't fall when Park FBX/assets fail to load)
        var floorObj = GameObject.Find("BodyTiltFloor");
        if (floorObj == null)
        {
            floorObj = new GameObject("BodyTiltFloor");
            Undo.RegisterCreatedObjectUndo(floorObj, "BodyTiltFloor");
        }
        var ensureFloor = floorObj.GetComponent<EnsureFloor>();
        if (ensureFloor == null) ensureFloor = Undo.AddComponent<EnsureFloor>(floorObj);
        ensureFloor.createAtStart = true;
        ensureFloor.sideBound = 5f;
        ensureFloor.length = 600f;
        ensureFloor.backExtension = 15f;
        ensureFloor.surfaceY = 0f;
        Debug.Log("BodyTiltFloor added. Narrow lane floor is created at Play (with back extension).");

        // 6. Camera: follow the ball
        var cam = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
        if (cam != null && player != null)
        {
            var camCtrl = cam.GetComponent<CameraController>();
            if (camCtrl == null) camCtrl = Undo.AddComponent<CameraController>(cam.gameObject);
            camCtrl.player = player;
            Debug.Log("Camera set to follow Player.");
        }
    }

    static void CreateBodyTiltUI(out TextMeshProUGUI countText, out TextMeshProUGUI timerText, out TextMeshProUGUI winText)
    {
        countText = null;
        timerText = null;
        winText = null;
        var canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            var canvasGo = new GameObject("Canvas");
            Undo.RegisterCreatedObjectUndo(canvasGo, "Canvas");
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGo.AddComponent<GraphicRaycaster>();
            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                Undo.RegisterCreatedObjectUndo(es, "EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }
        }
        var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        if (font == null) font = Resources.Load<TMPro.TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        var parent = canvas.transform;
        const float cornerMargin = 12f;
        float cornerW = 180f;
        float cornerH = 48f;
        var black = Color.black;
        if (GameObject.Find("BodyTiltCountText") == null)
        {
            var go = CreateTMPText(parent, "BodyTiltCountText", "Coins: 0", font, cornerW, cornerH);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(-cornerW - cornerMargin, -cornerH - cornerMargin); rt.offsetMax = new Vector2(-cornerMargin, -cornerMargin);
            var tmp = go.GetComponent<TextMeshProUGUI>(); tmp.color = black;
            countText = tmp;
        }
        else
        {
            countText = GameObject.Find("BodyTiltCountText").GetComponent<TextMeshProUGUI>();
            countText.color = black;
            var rt = countText.GetComponent<RectTransform>();
            if (rt != null) { rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(1f, 1f); rt.offsetMin = new Vector2(-cornerW - cornerMargin, -cornerH - cornerMargin); rt.offsetMax = new Vector2(-cornerMargin, -cornerMargin); }
        }
        if (GameObject.Find("BodyTiltTimerText") == null)
        {
            var go = CreateTMPText(parent, "BodyTiltTimerText", "0:30", font, cornerW, cornerH);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(cornerMargin, -cornerH - cornerMargin); rt.offsetMax = new Vector2(cornerW + cornerMargin, -cornerMargin);
            var tmp = go.GetComponent<TextMeshProUGUI>(); tmp.color = black;
            timerText = tmp;
        }
        else
        {
            timerText = GameObject.Find("BodyTiltTimerText").GetComponent<TextMeshProUGUI>();
            timerText.color = black;
            var rt = timerText.GetComponent<RectTransform>();
            if (rt != null) { rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f); rt.offsetMin = new Vector2(cornerMargin, -cornerH - cornerMargin); rt.offsetMax = new Vector2(cornerW + cornerMargin, -cornerMargin); }
        }
        if (GameObject.Find("BodyTiltWinText") == null)
        {
            var go = CreateTMPText(parent, "BodyTiltWinText", "Time's up! Coins: 0", font, 500f, 80f);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = new Vector2(0f, 40f);
            winText = go.GetComponent<TextMeshProUGUI>();
            winText.fontSize = 42f;
            winText.alignment = TextAlignmentOptions.Center;
            winText.color = black;
        }
        else winText = GameObject.Find("BodyTiltWinText").GetComponent<TextMeshProUGUI>();
        CreatePlayAndGameOverUI(canvas, parent, font, winText);
        if (countText != null || timerText != null || winText != null)
            Debug.Log("Body Tilt UI (BodyTiltCountText, BodyTiltTimerText, BodyTiltWinText) created or found.");
    }

    static void CreatePlayAndGameOverUI(Canvas canvas, Transform parent, TMPro.TMP_FontAsset font, TextMeshProUGUI winText)
    {
        if (GameObject.Find("BodyTiltPlayPanel") != null && GameObject.Find("BodyTiltGameOverPanel") != null) return;

        var canvasTransform = canvas.transform;

        if (GameObject.Find("BodyTiltPlayPanel") == null)
        {
            var playPanel = new GameObject("BodyTiltPlayPanel");
            Undo.RegisterCreatedObjectUndo(playPanel, "BodyTiltPlayPanel");
            playPanel.transform.SetParent(canvasTransform, false);
            var panelRt = playPanel.AddComponent<RectTransform>();
            panelRt.anchorMin = Vector2.zero; panelRt.anchorMax = Vector2.one; panelRt.offsetMin = Vector2.zero; panelRt.offsetMax = Vector2.zero;
            var img = playPanel.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.4f);
            var playBtn = CreateButton(playPanel.transform, "Play", font, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(200f, 70f));
            playBtn.name = "PlayButton";
        }

        if (GameObject.Find("BodyTiltGameOverPanel") == null)
        {
            var goPanel = new GameObject("BodyTiltGameOverPanel");
            Undo.RegisterCreatedObjectUndo(goPanel, "BodyTiltGameOverPanel");
            goPanel.transform.SetParent(canvasTransform, false);
            var panelRt = goPanel.AddComponent<RectTransform>();
            panelRt.anchorMin = Vector2.zero; panelRt.anchorMax = Vector2.one; panelRt.offsetMin = Vector2.zero; panelRt.offsetMax = Vector2.zero;
            var img = goPanel.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.5f);
            if (winText != null) { winText.transform.SetParent(goPanel.transform, false); var rt = winText.GetComponent<RectTransform>(); rt.anchoredPosition = new Vector2(0f, 90f); }
            var restartBtn = CreateButton(goPanel.transform, "Restart", font, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(180f, 50f));
            restartBtn.name = "RestartButton";
            var homeBtn = CreateButton(goPanel.transform, "Home", font, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -130f), new Vector2(180f, 50f));
            homeBtn.name = "HomeButton";
            goPanel.SetActive(false);
        }
    }

    static Button CreateButton(Transform parent, string label, TMPro.TMP_FontAsset font, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
    {
        var go = new GameObject();
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.color = new Color(0.2f, 0.6f, 0.2f);
        var btn = go.AddComponent<Button>();
        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        var textRt = textGo.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero; textRt.anchorMax = Vector2.one; textRt.offsetMin = Vector2.zero; textRt.offsetMax = Vector2.zero;
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 28f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        if (font != null) tmp.font = font;
        return btn;
    }

    /// <summary>Disable any other TMP text that shows "Coins:" so only one coin count is visible.</summary>
    static void DisableDuplicateCoinTexts(TextMeshProUGUI keepThisOne)
    {
        var all = Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None);
        foreach (var tmp in all)
        {
            if (tmp == keepThisOne) continue;
            if (tmp.text != null && tmp.text.StartsWith("Coins:"))
            {
                tmp.gameObject.SetActive(false);
                Debug.Log("Disabled duplicate coin text: " + tmp.gameObject.name);
            }
        }
    }

    static GameObject CreateTMPText(Transform parent, string name, string text, TMPro.TMP_FontAsset font, float width, float height)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, height);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 36f;
        if (font != null) tmp.font = font;
        return go;
    }
}
