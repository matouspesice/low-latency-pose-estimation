using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

/// <summary>
/// Ball moves forward automatically; left/right only from body tilt (UDP pose).
/// Game lasts 30 seconds; collect as many coins as you can. Play button starts the round.
/// </summary>
public class BodyTiltCoinCollectorPlayer : MonoBehaviour
{
    [Header("Movement — direct position (tilt = lane position for low-latency feel)")]
    [Tooltip("Forward speed along the lane.")]
    public float forwardSpeed = 16f;
    [Tooltip("Lane width: ball X is clamped to [-sideBound, +sideBound] (walls).")]
    public float sideBound = 5f;
    [Range(0.5f, 1f)] [Tooltip("How much of the lane tilt uses. 1 = full lane; lower = larger center dead zone.")]
    public float tiltLaneRange = 1f;
    [Range(0f, 0.85f)] [Tooltip("Smooth ball position; 0 = direct (lowest latency), higher = smoother but more lag.")]
    public float positionSmoothing = 0.15f;

    [Header("Body tilt (pose)")]
    public BodyTiltInput bodyTiltInput;
    [Range(0f, 0.3f)] [Tooltip("Dead zone: tilt below this maps to center. 0 = no dead zone.")]
    public float minTiltToMove = 0.05f;

    [Header("Game duration")]
    public float gameDurationSeconds = 30f;
    public TextMeshProUGUI countText;
    public TextMeshProUGUI timerText;
    public TextMeshProUGUI winText;

    [Header("UI panels and buttons")]
    public GameObject playButtonPanel;
    public Button playButton;
    public GameObject gameOverPanel;
    public Button restartButton;
    public Button homeButton;

    [Header("Optional: disable scene coins at start")]
    public bool disableSceneCoinsAtStart = true;

    Rigidbody rb;
    int count;
    float _gameStartTime;
    bool _gameOver;
    bool _waitingToStart = true;
    float _smoothedX;

    public bool IsGameOver { get { return _gameOver; } }
    public bool IsWaitingToStart { get { return _waitingToStart; } }

    void Awake()
    {
        if (disableSceneCoinsAtStart)
        {
            GameObject[] sceneCoins = GameObject.FindGameObjectsWithTag("Coin");
            for (int i = 0; i < sceneCoins.Length; i++)
                sceneCoins[i].SetActive(false);
        }
    }

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = GetComponentInChildren<Rigidbody>();
        count = 0;
        _gameOver = false;
        _waitingToStart = true;
        if (bodyTiltInput == null) bodyTiltInput = FindObjectOfType<BodyTiltInput>();

        // Ensure UI references (in case Setup wasn't run or references were lost)
        if (playButtonPanel == null) playButtonPanel = GameObject.Find("BodyTiltPlayPanel");
        if (playButton == null && playButtonPanel != null) playButton = playButtonPanel.GetComponentInChildren<Button>();
        if (gameOverPanel == null) gameOverPanel = GameObject.Find("BodyTiltGameOverPanel");
        if (restartButton == null && gameOverPanel != null) foreach (var b in gameOverPanel.GetComponentsInChildren<Button>()) { if (b.name.Contains("Restart")) { restartButton = b; break; } }
        if (homeButton == null && gameOverPanel != null) foreach (var b in gameOverPanel.GetComponentsInChildren<Button>()) { if (b.name.Contains("Home")) { homeButton = b; break; } }

        if (countText != null) { countText.text = "Coins: 0"; countText.color = Color.black; ApplyCornerLayout(countText, true); }
        if (timerText != null) { timerText.text = "0:30"; timerText.color = Color.black; ApplyCornerLayout(timerText, false); }
        if (winText != null) winText.gameObject.SetActive(false);
        if (playButtonPanel != null) playButtonPanel.SetActive(true);
        if (playButton != null) { playButton.onClick.RemoveAllListeners(); playButton.onClick.AddListener(StartGame); }
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (restartButton != null) { restartButton.onClick.RemoveAllListeners(); restartButton.onClick.AddListener(RestartGame); }
        if (homeButton != null) { homeButton.onClick.RemoveAllListeners(); homeButton.onClick.AddListener(GoToHome); }
    }

    /// <summary>Pin timer (top-left) or count (top-right) to screen corners; keep fully in frame.</summary>
    static void ApplyCornerLayout(TextMeshProUGUI tmp, bool topRight)
    {
        if (tmp == null) return;
        var rt = tmp.GetComponent<RectTransform>();
        if (rt == null) return;
        const float margin = 12f;
        float w = 180f;
        float h = 48f;
        if (topRight)
        {
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(-w - margin, -h - margin);
            rt.offsetMax = new Vector2(-margin, -margin);
        }
        else
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(margin, -h - margin);
            rt.offsetMax = new Vector2(w + margin, -margin);
        }
        tmp.color = Color.black;
    }

    public void StartGame()
    {
        _waitingToStart = false;
        _gameStartTime = Time.time;
        _smoothedX = transform.position.x;
        if (playButtonPanel != null) playButtonPanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    public void RestartGame()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void GoToHome()
    {
        SceneManager.LoadScene(0);
    }

    void ShowGameOverScreen()
    {
        if (gameOverPanel == null) gameOverPanel = GameObject.Find("BodyTiltGameOverPanel");
        if (gameOverPanel == null) CreateGameOverPanelRuntime();
        if (winText == null) winText = GameObject.Find("BodyTiltWinText")?.GetComponent<TextMeshProUGUI>();
        if (winText == null && gameOverPanel != null)
        {
            var t = gameOverPanel.transform.Find("BodyTiltWinText");
            if (t != null) winText = t.GetComponent<TextMeshProUGUI>();
        }

        if (timerText != null) timerText.text = "0:00";

        if (winText != null)
        {
            winText.text = "Time's up!\nFinal score: " + count + " coins";
            winText.color = Color.white;
            winText.gameObject.SetActive(true);
        }
        if (gameOverPanel != null)
        {
            gameOverPanel.transform.SetAsLastSibling();
            gameOverPanel.SetActive(true);
            var buttons = gameOverPanel.GetComponentsInChildren<Button>(true);
            foreach (var b in buttons)
            {
                if (b.name.Contains("Restart")) { restartButton = b; b.onClick.RemoveAllListeners(); b.onClick.AddListener(RestartGame); }
                if (b.name.Contains("Home")) { homeButton = b; b.onClick.RemoveAllListeners(); b.onClick.AddListener(GoToHome); }
            }
        }
    }

    void CreateGameOverPanelRuntime()
    {
        var canvas = Object.FindObjectOfType<Canvas>();
        if (canvas == null) return;
        var font = countText != null ? countText.font : Resources.Load<TMPro.TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font == null) font = Resources.Load<TMPro.TMP_FontAsset>("LiberationSans SDF");

        var goPanel = new GameObject("BodyTiltGameOverPanel");
        goPanel.transform.SetParent(canvas.transform, false);
        var panelRt = goPanel.AddComponent<RectTransform>();
        panelRt.anchorMin = Vector2.zero; panelRt.anchorMax = Vector2.one; panelRt.offsetMin = Vector2.zero; panelRt.offsetMax = Vector2.zero;
        var img = goPanel.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.7f);
        img.raycastTarget = true;

        var winGo = new GameObject("BodyTiltWinText");
        winGo.transform.SetParent(goPanel.transform, false);
        var winRt = winGo.AddComponent<RectTransform>();
        winRt.anchorMin = new Vector2(0.5f, 0.5f); winRt.anchorMax = new Vector2(0.5f, 0.5f); winRt.pivot = new Vector2(0.5f, 0.5f);
        winRt.anchoredPosition = new Vector2(0f, 90f); winRt.sizeDelta = new Vector2(500f, 100f);
        winText = winGo.AddComponent<TextMeshProUGUI>();
        winText.text = "Time's up!\nFinal score: " + count + " coins";
        winText.fontSize = 42f;
        winText.alignment = TextAlignmentOptions.Center;
        winText.color = Color.white;
        if (font != null) winText.font = font;

        CreateButtonRuntime(goPanel.transform, "RestartButton", "Restart", font, new Vector2(0f, -60f));
        CreateButtonRuntime(goPanel.transform, "HomeButton", "Home", font, new Vector2(0f, -130f));

        gameOverPanel = goPanel;
    }

    static void CreateButtonRuntime(Transform panelParent, string buttonName, string label, TMPro.TMP_FontAsset font, Vector2 pos)
    {
        var go = new GameObject(buttonName);
        go.transform.SetParent(panelParent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(180f, 50f);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.2f, 0.6f, 0.2f);
        go.AddComponent<Button>();
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
    }

    void FixedUpdate()
    {
        if (_waitingToStart)
        {
            if (rb != null) rb.linearVelocity = Vector3.zero;
            return;
        }
        if (_gameOver)
        {
            if (rb != null) rb.linearVelocity = Vector3.zero;
            return;
        }

        float elapsed = Time.time - _gameStartTime;
        if (elapsed >= gameDurationSeconds)
        {
            _gameOver = true;
            if (timerText != null) timerText.text = "0:00";
            ShowGameOverScreen();
            return;
        }

        if (timerText != null)
        {
            float left = gameDurationSeconds - elapsed;
            int sec = Mathf.Max(0, Mathf.CeilToInt(left));
            timerText.text = "0:" + sec.ToString("00");
        }

        // Forward: keep moving along Z
        Vector3 vel = rb.linearVelocity;
        vel.z = forwardSpeed;
        vel.x = 0f;
        rb.linearVelocity = vel;

        // Tilt → target X; optional smoothing to reduce jitter
        float axis = 0f;
        if (bodyTiltInput != null && bodyTiltInput.poseGestureDetector != null
            && bodyTiltInput.poseGestureDetector.poseReceiver != null
            && bodyTiltInput.poseGestureDetector.poseReceiver.HasReceivedPose)
        {
            axis = bodyTiltInput.TiltAxis;
            if (Mathf.Abs(axis) < minTiltToMove) axis = 0f;
        }
        float targetX = Mathf.Clamp(axis * sideBound * tiltLaneRange, -sideBound, sideBound);
        _smoothedX = Mathf.Lerp(_smoothedX, targetX, 1f - positionSmoothing);
        Vector3 pos = transform.position;
        pos.x = _smoothedX;
        rb.MovePosition(pos);
    }

    void OnTriggerEnter(Collider other)
    {
        if (_gameOver || _waitingToStart) return;
        if (other.gameObject.CompareTag("Coin"))
        {
            other.gameObject.SetActive(false);
            count++;
            if (countText != null) countText.text = "Coins: " + count;
        }
    }
}
