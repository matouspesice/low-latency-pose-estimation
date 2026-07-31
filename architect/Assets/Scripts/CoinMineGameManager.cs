using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Self-contained 3D Coin Collector aligned with the standalone body-tilt project
/// (CollectCoins). On StartGame() this manager builds its own floor, walls, ball,
/// coins and follow-camera as children of its GameObject, reads body tilt from the
/// shared PoseGestureDetector/BodyTiltInput pipeline, and tears everything down on
/// StopGame()/EndGame(). The ball rolls forward at a constant speed and the player
/// steers it left/right by leaning their torso, picking up coins for 30 seconds.
/// </summary>
public class CoinMineGameManager : MonoBehaviour
{
    [Header("Dependencies")]
    public BodyTiltInput bodyTiltInput;
    public PoseGestureDetector gestureDetector;

    [Header("Movement")]
    [Tooltip("Forward speed of the ball along +Z.")]
    public float forwardSpeed = 16f;
    [Tooltip("Lane half-width. Ball X is clamped to [-sideBound, +sideBound].")]
    public float sideBound = 5f;
    [Tooltip("World Y of the ball centre while rolling on the lane.")]
    public float ballSurfaceY = 0.5f;
    [Tooltip("Ball radius used for invisible side boundaries (default sphere radius).")]
    public float ballRadius = 0.5f;
    [Range(0.5f, 1f)]
    [Tooltip("How much of the lane tilt uses. 1 = full lane.")]
    public float tiltLaneRange = 1f;
    [Range(0f, 0.85f)]
    [Tooltip("0 = direct (lowest latency), higher = smoother but more lag.")]
    public float positionSmoothing = 0.15f;
    [Range(0f, 0.3f)]
    [Tooltip("Dead zone: tilt below this maps to center.")]
    public float minTiltToMove = 0.05f;

    [Header("Round")]
    public float gameDurationSeconds = 30f;

    [Header("Coin path")]
    [Tooltip("Z of first coin row ahead of the ball. ~2 s warm-up at forwardSpeed≈16 → offset ≈ 32–36.")]
    public float firstCoinOffsetZ = 35f;
    public float gapBetweenCoins = 4f;
    public int totalCoinsToSpawn = 120;
    public float leftLaneX = -3f;
    public float middleLaneX = 0f;
    public float rightLaneX = 3f;
    public float coinHeight = 1f;
    public float laneLength = 600f;

    [Header("Performance (optional)")]
    [Tooltip("If > 0, sets Application.targetFrameRate for the active coin round (snappier input on high-Hz displays). Restored when the round ends or this object tears down.")]
    public int targetFrameRateWhilePlaying = 0;

    [Header("UI (wired by ArchitectUIBuilder)")]
    public TMP_Text scoreText;
    public TMP_Text timerText;
    public TMP_Text laneHintText;
    public TMP_Text youAreHereText;
    public TMP_Text gameOverScoreText;
    public GameObject gameOverPanel;
    public GameObject startPromptPanel;

    // Runtime-created scene objects (destroyed on StopGame/EndGame)
    Transform _world;
    Rigidbody _ballRb;
    Camera _gameCamera;
    Camera _previousMainCamera;
    bool _previousMainCameraWasEnabled;

    float _elapsed;
    int _score;
    bool _playing;
    float _smoothedX;
    int _savedApplicationTargetFrameRate = -1;
    bool _overrodeApplicationTargetFrameRate;

    public int Score { get { return _score; } }
    public bool IsPlaying { get { return _playing; } }

    void Awake()
    {
        ResolveInputChain();
    }

    void Start()
    {
        EnsureGameOverButtons();
    }

    /// <summary>
    /// Resolve (and if needed, auto-create) the full BodyTiltInput → PoseGestureDetector
    /// → PoseReceiver chain. This exists because older scene builds may have a PoseBridge
    /// that is missing BodyTiltInput or PoseGestureDetector (earlier versions of
    /// ArchitectSetup.CreatePoseBridge didn't add them). Instead of forcing the user to
    /// rebuild the scene, we attach the missing components to the same GameObject that
    /// already hosts PoseReceiver so the tilt pipeline comes alive at runtime.
    /// </summary>
    void ResolveInputChain()
    {
        var receiver = FindFirstObjectByType<PoseReceiver>();

        if (gestureDetector == null)
            gestureDetector = FindFirstObjectByType<PoseGestureDetector>();
        if (gestureDetector == null && receiver != null)
            gestureDetector = receiver.gameObject.AddComponent<PoseGestureDetector>();
        if (gestureDetector != null && gestureDetector.poseReceiver == null)
            gestureDetector.poseReceiver = receiver;

        if (bodyTiltInput == null)
            bodyTiltInput = FindFirstObjectByType<BodyTiltInput>();
        if (bodyTiltInput == null && gestureDetector != null)
            bodyTiltInput = gestureDetector.gameObject.AddComponent<BodyTiltInput>();
        if (bodyTiltInput != null && bodyTiltInput.poseGestureDetector == null)
            bodyTiltInput.poseGestureDetector = gestureDetector;
    }

    void OnEnable()
    {
        UpdateTimerText(gameDurationSeconds);
        UpdateScoreText();
        if (startPromptPanel != null) startPromptPanel.SetActive(true);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    void OnDisable()
    {
        TearDownWorld();
        _playing = false;
    }

    public void StartGame()
    {
        TearDownWorld();
        BuildWorld();
        _elapsed = 0f;
        _score = 0;
        _smoothedX = 0f;
        _playing = true;
        ApplyTargetFrameRateForRound();
        UpdateScoreText();
        UpdateTimerText(gameDurationSeconds);
        if (laneHintText != null) laneHintText.text = "Lean to steer the ball!";
        if (youAreHereText != null) youAreHereText.text = "You: CENTER";
        if (startPromptPanel != null) startPromptPanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    public void StopGame()
    {
        _playing = false;
        _elapsed = 0f;
        _score = 0;
        TearDownWorld();
        UpdateScoreText();
        UpdateTimerText(gameDurationSeconds);
        if (startPromptPanel != null) startPromptPanel.SetActive(true);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    public void EndGame()
    {
        _playing = false;
        if (_ballRb != null) _ballRb.linearVelocity = Vector3.zero;
        TearDownWorld();
        EnsureGameOverButtons();
        if (gameOverPanel != null)
        {
            gameOverPanel.transform.SetAsLastSibling();
            gameOverPanel.SetActive(true);
        }
        if (gameOverScoreText != null) gameOverScoreText.text = "Coins: " + _score;
        if (startPromptPanel != null) startPromptPanel.SetActive(false);
    }

    /// <summary>Returns to the Architect mode menu (used by game-over and exit buttons).</summary>
    public void ExitToMenu()
    {
        StopGame();
        var selector = FindFirstObjectByType<ArchitectGameSelector>();
        if (selector != null)
            selector.BackToMenu();
    }

    /// <summary>
    /// Ensures Restart/Exit buttons exist on the game-over overlay and are wired.
    /// The full-screen panel was covering the old top-right Exit button after a round ended.
    /// </summary>
    void EnsureGameOverButtons()
    {
        if (gameOverPanel == null) return;

        EnsureGameOverButton("CoinRestartButton", "Restart", 0.40f, new Color(0.9f, 0.7f, 0.15f), StartGame);
        EnsureGameOverButton("CoinGameOverExitButton", "Exit", 0.30f, new Color(0.65f, 0.2f, 0.2f), ExitToMenu);

        var inGameExit = gameOverPanel.transform.parent != null
            ? gameOverPanel.transform.parent.Find("CoinExitButton")
            : null;
        if (inGameExit != null)
            WireButtonOnce(inGameExit.GetComponent<Button>(), ExitToMenu);
    }

    void EnsureGameOverButton(string name, string label, float anchorY, Color color, UnityAction action)
    {
        var existing = gameOverPanel.transform.Find(name);
        GameObject go;
        Button btn;
        if (existing != null)
        {
            go = existing.gameObject;
            btn = go.GetComponent<Button>();
        }
        else
        {
            go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(gameOverPanel.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, anchorY);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(220f, 55f);
            go.GetComponent<Image>().color = color;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            var tmp = textGo.GetComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 28;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            if (TMP_Settings.defaultFontAsset != null)
                tmp.font = TMP_Settings.defaultFontAsset;
            btn = go.GetComponent<Button>();
        }

        WireButtonOnce(btn, action);
    }

    static void WireButtonOnce(Button btn, UnityAction action)
    {
        if (btn == null || action == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(action);
    }

    void FixedUpdate()
    {
        if (!_playing || _ballRb == null) return;

        _elapsed += Time.fixedDeltaTime;
        if (_elapsed >= gameDurationSeconds)
        {
            UpdateTimerText(0f);
            EndGame();
            return;
        }
        UpdateTimerText(gameDurationSeconds - _elapsed);

        // Forward velocity
        var vel = _ballRb.linearVelocity;
        vel.z = forwardSpeed;
        vel.x = 0f;
        _ballRb.linearVelocity = vel;

        // Tilt -> target X. Re-resolve (and auto-attach) the full input chain every
        // physics step so a scene rebuilt mid-play or an old PoseBridge missing
        // BodyTiltInput/PoseGestureDetector can't leave us steering with null refs.
        if (bodyTiltInput == null || bodyTiltInput.poseGestureDetector == null
            || (bodyTiltInput.poseGestureDetector != null && bodyTiltInput.poseGestureDetector.poseReceiver == null))
        {
            ResolveInputChain();
        }

        float axis = 0f;
        string source = "no-input";
        if (bodyTiltInput != null)
        {
            var gd = bodyTiltInput.poseGestureDetector;
            if (gd == null) source = "no-gesture-detector";
            else if (gd.poseReceiver == null) source = "no-pose-receiver";
            else if (!gd.poseReceiver.HasReceivedPose) source = "waiting-for-pose";
            else
            {
                axis = bodyTiltInput.TiltAxis;
                source = $"axis={axis:+0.00;-0.00; 0.00}";
                if (Mathf.Abs(axis) < minTiltToMove) axis = 0f;
            }
        }
        float maxX = EffectiveSideBoundX();
        float targetX = Mathf.Clamp(axis * sideBound * tiltLaneRange, -maxX, maxX);
        _smoothedX = Mathf.Lerp(_smoothedX, targetX, 1f - positionSmoothing);

        // Apply X through both the Rigidbody and the Transform so a residual
        // physics state (frozen constraint, queued velocity) cannot cancel the
        // steering. Non-kinematic MovePosition alone is sometimes ignored when
        // the body was just built and has a mid-step cache.
        var pos = _ballRb.transform.position;
        pos.x = _smoothedX;
        pos = ClampBallToLane(pos);
        _ballRb.position = pos;
        _ballRb.MovePosition(pos);
        ClampBallVelocity();

        if (youAreHereText != null)
        {
            string lane = _smoothedX < -0.5f ? "LEFT" : (_smoothedX > 0.5f ? "RIGHT" : "CENTER");
            youAreHereText.text = $"You: {lane}  ({source})";
        }
    }

    void LateUpdate()
    {
        if (_ballRb == null || _gameCamera == null) return;
        // Camera stays behind and above the ball, looking ahead along the lane.
        // This keeps the ball roughly in the center of the view while the lane
        // and coins appear to move past underneath it.
        var ballPos = _ballRb.transform.position;
        _gameCamera.transform.position = new Vector3(ballPos.x * 0.25f, 5f, ballPos.z - 8f);
        _gameCamera.transform.LookAt(new Vector3(ballPos.x, 1f, ballPos.z + 6f));
    }

    /// <summary>Called by CoinMineCoin when the ball enters a coin trigger.</summary>
    public void OnCoinCollected(GameObject coin)
    {
        if (!_playing || coin == null) return;
        coin.SetActive(false);
        _score++;
        UpdateScoreText();
    }

    void UpdateScoreText()
    {
        if (scoreText != null) scoreText.text = "Coins: " + _score;
    }

    void UpdateTimerText(float secondsLeft)
    {
        if (timerText == null) return;
        int s = Mathf.Max(0, Mathf.CeilToInt(secondsLeft));
        timerText.text = "Time: " + s + "s";
    }

    // ── World construction ────────────────────────────────────────────────

    void BuildWorld()
    {
        var worldGo = new GameObject("CoinMineWorld");
        worldGo.transform.SetParent(transform, false);
        _world = worldGo.transform;

        BuildFloor(_world);
        BuildWalls(_world);
        BuildBall(_world);
        BuildCoins(_world);
        BuildCamera(_world);
    }

    void TearDownWorld()
    {
        RestoreTargetFrameRateAfterRound();
        RestoreMainCamera();
        if (_world != null)
        {
            if (Application.isPlaying) Destroy(_world.gameObject);
            else DestroyImmediate(_world.gameObject);
            _world = null;
        }
        _ballRb = null;
        _gameCamera = null;
    }

    void ApplyTargetFrameRateForRound()
    {
        if (targetFrameRateWhilePlaying <= 0) return;
        if (!_overrodeApplicationTargetFrameRate)
        {
            _savedApplicationTargetFrameRate = Application.targetFrameRate;
            _overrodeApplicationTargetFrameRate = true;
        }
        Application.targetFrameRate = targetFrameRateWhilePlaying;
    }

    void RestoreTargetFrameRateAfterRound()
    {
        if (!_overrodeApplicationTargetFrameRate) return;
        Application.targetFrameRate = _savedApplicationTargetFrameRate;
        _overrodeApplicationTargetFrameRate = false;
    }

    /// <summary>
    /// Creates a material that works in both URP and Built-in pipelines. URP uses
    /// `_BaseColor`, Built-in uses `_Color`. Without this the shader lookup fails
    /// and everything renders pink ("missing shader").
    /// </summary>
    float EffectiveSideBoundX()
    {
        float inset = Mathf.Max(0.01f, ballRadius);
        return Mathf.Max(inset, sideBound - inset);
    }

    Vector3 ClampBallToLane(Vector3 pos)
    {
        float maxX = EffectiveSideBoundX();
        pos.x = Mathf.Clamp(pos.x, -maxX, maxX);
        pos.y = ballSurfaceY;
        return pos;
    }

    void ClampBallVelocity()
    {
        if (_ballRb == null) return;
        var vel = _ballRb.linearVelocity;
        vel.x = 0f;
        vel.y = 0f;
        _ballRb.linearVelocity = vel;
    }

    static PhysicsMaterial MakeBouncelessPhysicsMaterial()
    {
        var mat = new PhysicsMaterial("CoinMineBallNoBounce")
        {
            bounciness = 0f,
            dynamicFriction = 0f,
            staticFriction = 0f,
            bounceCombine = PhysicsMaterialCombine.Minimum,
            frictionCombine = PhysicsMaterialCombine.Minimum,
        };
        return mat;
    }

    static Material MakePipelineMaterial(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.color = color;
        return mat;
    }

    void BuildFloor(Transform parent)
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.SetParent(parent, false);
        float totalLength = laneLength + 15f;
        float centerZ = (laneLength - 15f) * 0.5f;
        floor.transform.localPosition = new Vector3(0f, -0.25f, centerZ);
        floor.transform.localScale = new Vector3(2f * sideBound, 0.5f, totalLength);
        var renderer = floor.GetComponent<Renderer>();
        if (renderer != null) renderer.material = MakePipelineMaterial(new Color(0.25f, 0.45f, 0.28f));
    }

    void BuildWalls(Transform parent)
    {
        BuildWall(parent, "WallLeft", -sideBound);
        BuildWall(parent, "WallRight", sideBound);
    }

    void BuildWall(Transform parent, string name, float x)
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.SetParent(parent, false);
        const float wallHeight = 8f;
        wall.transform.localPosition = new Vector3(x, wallHeight * 0.5f, laneLength * 0.5f);
        wall.transform.localScale = new Vector3(1f, wallHeight, laneLength);
        var r = wall.GetComponent<Renderer>();
        if (r != null) r.enabled = false;
    }

    void BuildBall(Transform parent)
    {
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Player";
        ball.tag = "Player";
        ball.transform.SetParent(parent, false);
        ball.transform.localPosition = new Vector3(0f, ballSurfaceY, 0f);
        var rb = ball.GetComponent<Rigidbody>();
        if (rb == null) rb = ball.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
        var ballCollider = ball.GetComponent<SphereCollider>();
        if (ballCollider != null)
        {
            ballCollider.material = MakeBouncelessPhysicsMaterial();
            ballRadius = ballCollider.radius * Mathf.Max(ball.transform.localScale.x, ball.transform.localScale.y, ball.transform.localScale.z);
        }
        var trigger = ball.AddComponent<CoinMineBallTrigger>();
        trigger.manager = this;
        var r = ball.GetComponent<Renderer>();
        if (r != null) r.material = MakePipelineMaterial(new Color(0.95f, 0.25f, 0.25f));
        _ballRb = rb;
    }

    void BuildCoins(Transform parent)
    {
        float laneEndZ = laneLength - 5f;
        int maxCoins = Mathf.Max(0, Mathf.FloorToInt((laneEndZ - firstCoinOffsetZ) / gapBetweenCoins) + 1);
        int toSpawn = Mathf.Min(totalCoinsToSpawn, maxCoins);

        int prevLane = -1;
        for (int i = 0; i < toSpawn; i++)
        {
            int lane;
            if (prevLane < 0) lane = Random.Range(0, 3);
            else
            {
                int a = (prevLane + 1) % 3;
                int b = (prevLane + 2) % 3;
                lane = Random.Range(0, 2) == 0 ? a : b;
            }
            prevLane = lane;

            float x = lane == 0 ? leftLaneX : (lane == 1 ? middleLaneX : rightLaneX);
            float z = firstCoinOffsetZ + i * gapBetweenCoins;

            var coin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            coin.name = "Coin_" + i;
            coin.transform.SetParent(parent, false);
            coin.transform.localPosition = new Vector3(x, coinHeight, z);
            coin.transform.localScale = new Vector3(0.8f, 0.08f, 0.8f);
            coin.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Replace the default primitive collider with an explicit SphereCollider
            // slightly larger than the visual disc so the fast-moving ball always
            // overlaps the trigger (continuous detection plus a generous radius
            // prevents tunnelling at forwardSpeed ≈ 16 units/s).
            var oldCol = coin.GetComponent<Collider>();
            if (oldCol != null)
            {
                if (Application.isPlaying) Destroy(oldCol);
                else DestroyImmediate(oldCol);
            }
            var sphere = coin.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = 0.9f;

            var r = coin.GetComponent<Renderer>();
            if (r != null) r.material = MakePipelineMaterial(new Color(1f, 0.82f, 0.1f));

            coin.AddComponent<CoinMineCoinRotator>();
        }
    }

    void BuildCamera(Transform parent)
    {
        var camGo = new GameObject("CoinMineCamera");
        camGo.transform.SetParent(parent, false);
        _gameCamera = camGo.AddComponent<Camera>();
        _gameCamera.clearFlags = CameraClearFlags.SolidColor;
        _gameCamera.backgroundColor = new Color(0.55f, 0.75f, 0.95f);
        _gameCamera.fieldOfView = 60f;
        _gameCamera.nearClipPlane = 0.1f;
        _gameCamera.farClipPlane = 800f;
        _gameCamera.transform.position = new Vector3(0f, 6f, -10f);
        _gameCamera.transform.LookAt(new Vector3(0f, 0.5f, -2f));
        _gameCamera.depth = 10f;

        _previousMainCamera = Camera.main;
        if (_previousMainCamera != null && _previousMainCamera != _gameCamera)
        {
            _previousMainCameraWasEnabled = _previousMainCamera.enabled;
            _previousMainCamera.enabled = false;
        }
    }

    void RestoreMainCamera()
    {
        if (_previousMainCamera != null)
        {
            _previousMainCamera.enabled = _previousMainCameraWasEnabled;
        }
        _previousMainCamera = null;
        _previousMainCameraWasEnabled = false;
    }
}
