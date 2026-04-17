using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Pose Dodge game: obstacles approach a visible hit-line; player matches the gesture to pass.
/// Features: varied obstacle sizes, color-coded by type, action label, hit-line marker,
/// current gesture display, score/lives UI.
/// </summary>
public class DodgeGameManager : MonoBehaviour
{
    [Header("Dependencies")]
    public PoseGestureDetector gestureDetector;
    public GameObject obstaclePrefab;

    [Header("Spawn")]
    [Tooltip("Seconds between obstacles.")]
    public float spawnInterval = 2.5f;
    [Tooltip("Z position where obstacles spawn (far away).")]
    public float spawnZ = 18f;
    [Tooltip("Z position of the hit-line (where avatar stands).")]
    public float hitZ = 0f;
    public float obstacleSpeed = 4f;

    [Header("Obstacle size range")]
    public float minWidth = 0.6f;
    public float maxWidth = 1.6f;
    public float minHeight = 0.4f;
    public float maxHeight = 1.2f;

    [Header("Rules")]
    public int lives = 3;
    public bool useLeanObstacles = true;

    [Header("UI (optional)")]
    public TMP_Text scoreText;
    public TMP_Text livesText;
    public TMP_Text gestureText;
    public TMP_Text nextActionText;
    public GameObject gameOverPanel;
    public TMP_Text gameOverScoreText;
    public GameObject startPromptPanel;

    public int Score { get; private set; }
    public int LivesLeft => _livesLeft;
    public bool IsPlaying { get; private set; }

    int _livesLeft;
    float _nextSpawnTime;
    readonly List<DodgeObstacle> _obstacles = new List<DodgeObstacle>();
    GameObject _hitLine;

    void Start()
    {
        if (gestureDetector == null)
            gestureDetector = FindFirstObjectByType<PoseGestureDetector>();
        StopGame();
    }

    void Update()
    {
        if (!IsPlaying) return;

        for (int i = _obstacles.Count - 1; i >= 0; i--)
        {
            var ob = _obstacles[i];
            if (ob == null) { _obstacles.RemoveAt(i); continue; }
            if (ob.PastHitZone)
            {
                bool correct = DodgeObstacle.GestureMatches(gestureDetector.CurrentGesture, ob.obstacleType);
                if (correct)
                    Score++;
                else
                {
                    _livesLeft--;
                    ShowHitFeedback(false);
                    if (_livesLeft <= 0) { EndGame(); return; }
                }
                Destroy(ob.gameObject);
                _obstacles.RemoveAt(i);
                continue;
            }
        }

        if (Time.time >= _nextSpawnTime)
        {
            SpawnObstacle();
            _nextSpawnTime = Time.time + spawnInterval;
        }

        RefreshUI();
    }

    public void StartGame()
    {
        Score = 0;
        _livesLeft = lives;
        IsPlaying = true;
        _nextSpawnTime = Time.time + 1.5f;
        ClearObstacles();
        EnsureHitLine();
        if (startPromptPanel != null) startPromptPanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    public void StopGame()
    {
        IsPlaying = false;
        ClearObstacles();
        if (_hitLine != null) { Destroy(_hitLine); _hitLine = null; }
        if (startPromptPanel != null) startPromptPanel.SetActive(true);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    void EndGame()
    {
        IsPlaying = false;
        ClearObstacles();
        if (_hitLine != null) { Destroy(_hitLine); _hitLine = null; }
        if (gameOverPanel != null) gameOverPanel.SetActive(true);
        if (gameOverScoreText != null) gameOverScoreText.text = "Final Score: " + Score;
        if (startPromptPanel != null) startPromptPanel.SetActive(false);
    }

    void ClearObstacles()
    {
        foreach (var ob in _obstacles)
            if (ob != null) Destroy(ob.gameObject);
        _obstacles.Clear();
    }

    void EnsureHitLine()
    {
        if (_hitLine != null) return;
        _hitLine = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _hitLine.name = "HitLine";
        _hitLine.transform.position = new Vector3(0f, -0.05f, hitZ);
        _hitLine.transform.localScale = new Vector3(6f, 0.05f, 0.15f);
        var col = _hitLine.GetComponent<Collider>();
        if (col != null) col.enabled = false;
        var r = _hitLine.GetComponent<Renderer>();
        if (r != null) r.material.color = new Color(1f, 1f, 1f, 0.7f);
    }

    void SpawnObstacle()
    {
        GameObject go;
        if (obstaclePrefab != null)
            go = Instantiate(obstaclePrefab, new Vector3(0f, 0.5f, spawnZ), Quaternion.identity);
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.position = new Vector3(0f, 0.5f, spawnZ);
            var col = go.GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }
        var ob = go.GetComponent<DodgeObstacle>();
        if (ob == null) ob = go.AddComponent<DodgeObstacle>();

        int typeCount = useLeanObstacles ? 5 : 3;
        var type = (DodgeObstacle.ObstacleType)Random.Range(0, typeCount);

        float w = Random.Range(minWidth, maxWidth);
        float h = Random.Range(minHeight, maxHeight);
        ob.Setup(type, obstacleSpeed, hitZ, w, h);

        _obstacles.Add(ob);
    }

    void ShowHitFeedback(bool success)
    {
        if (_hitLine == null) return;
        var r = _hitLine.GetComponent<Renderer>();
        if (r != null) r.material.color = success ? Color.green : Color.red;
        CancelInvoke(nameof(ResetHitLineColor));
        Invoke(nameof(ResetHitLineColor), 0.3f);
    }

    void ResetHitLineColor()
    {
        if (_hitLine == null) return;
        var r = _hitLine.GetComponent<Renderer>();
        if (r != null) r.material.color = new Color(1f, 1f, 1f, 0.7f);
    }

    void RefreshUI()
    {
        if (scoreText != null) scoreText.text = "Score: " + Score;
        if (livesText != null) livesText.text = "Lives: " + _livesLeft;
        if (gestureText != null && gestureDetector != null)
            gestureText.text = "You: " + FormatGesture(gestureDetector.CurrentGesture);
        if (nextActionText != null && _obstacles.Count > 0)
        {
            var nearest = GetNearestObstacle();
            if (nearest != null)
                nextActionText.text = "Next: " + FormatAction(nearest.obstacleType);
        }
        else if (nextActionText != null)
            nextActionText.text = "";
    }

    DodgeObstacle GetNearestObstacle()
    {
        DodgeObstacle nearest = null;
        float minDist = float.MaxValue;
        foreach (var ob in _obstacles)
        {
            if (ob == null) continue;
            float d = ob.transform.position.z - hitZ;
            if (d < minDist) { minDist = d; nearest = ob; }
        }
        return nearest;
    }

    static string FormatGesture(PoseGestureDetector.Gesture g)
    {
        switch (g)
        {
            case PoseGestureDetector.Gesture.ArmsUp:    return "ARMS UP";
            case PoseGestureDetector.Gesture.Crouch:    return "DUCK";
            case PoseGestureDetector.Gesture.TPose:     return "T-POSE";
            case PoseGestureDetector.Gesture.LeanLeft:  return "LEAN LEFT";
            case PoseGestureDetector.Gesture.LeanRight: return "LEAN RIGHT";
            default:                                    return "STANDING";
        }
    }

    static string FormatAction(DodgeObstacle.ObstacleType t)
    {
        switch (t)
        {
            case DodgeObstacle.ObstacleType.Duck:      return "DUCK";
            case DodgeObstacle.ObstacleType.Jump:      return "ARMS UP";
            case DodgeObstacle.ObstacleType.Stand:     return "STAND";
            case DodgeObstacle.ObstacleType.LeanLeft:  return "LEAN LEFT";
            case DodgeObstacle.ObstacleType.LeanRight: return "LEAN RIGHT";
            default:                                   return "?";
        }
    }
}
