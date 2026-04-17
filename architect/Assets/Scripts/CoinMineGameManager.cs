using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Coin Mine: Temple Run–style. You run in the middle; lean left/center/right to move into lanes.
/// Coins spawn in left, center, or right lane and move toward you. Lean into the coin's lane to collect.
/// Clear hint shows which way to lean for the next coin.
/// </summary>
public class CoinMineGameManager : MonoBehaviour
{
    public const int LaneLeft = 0;
    public const int LaneCenter = 1;
    public const int LaneRight = 2;

    [Header("Dependencies")]
    public PoseGestureDetector gestureDetector;

    [Header("Lanes (world X positions)")]
    public float laneLeftX = -2f;
    public float laneCenterX = 0f;
    public float laneRightX = 2f;

    [Header("Spawn & movement")]
    public float spawnZ = 20f;
    public float collectZ = 2.5f;
    public float coinSpeed = 8f;
    public float spawnInterval = 1.8f;

    [Header("Lean thresholds (TorsoLeanX)")]
    [Tooltip("|lean| below this = center lane.")]
    [Range(0.02f, 0.08f)]
    public float centerZone = 0.04f;

    [Header("UI (optional)")]
    public TMP_Text scoreText;
    public TMP_Text laneHintText;
    public TMP_Text youAreHereText;
    public GameObject gameOverPanel;
    public TMP_Text gameOverScoreText;
    public GameObject startPromptPanel;

    public int Score { get; private set; }
    public bool IsPlaying { get; private set; }

    float _nextSpawnTime;
    readonly List<CoinMineCoin> _coins = new List<CoinMineCoin>();
    static readonly string[] LaneNames = { "LEFT", "CENTER", "RIGHT" };
    static readonly string[] LeanHint = { "← LEAN LEFT", "○ STAY CENTER", "LEAN RIGHT →" };

    float LaneX(int lane)
    {
        if (lane == LaneLeft) return laneLeftX;
        if (lane == LaneRight) return laneRightX;
        return laneCenterX;
    }

    int GetPlayerLane()
    {
        if (gestureDetector == null) return LaneCenter;
        float lean = gestureDetector.TorsoLeanX;
        if (lean < -centerZone) return LaneLeft;
        if (lean > centerZone) return LaneRight;
        return LaneCenter;
    }

    void Start()
    {
        if (gestureDetector == null)
            gestureDetector = FindFirstObjectByType<PoseGestureDetector>();
        StopGame();
    }

    void Update()
    {
        if (!IsPlaying) return;

        int playerLane = GetPlayerLane();

        for (int i = _coins.Count - 1; i >= 0; i--)
        {
            var coin = _coins[i];
            if (coin == null) { _coins.RemoveAt(i); continue; }
            if (coin.ReachedCollectZone)
            {
                if (coin.Lane == playerLane)
                {
                    Score++;
                    if (laneHintText != null) laneHintText.text = "✓ Collected!";
                }
                Destroy(coin.gameObject);
                _coins.RemoveAt(i);
            }
        }

        if (Time.time >= _nextSpawnTime)
        {
            SpawnCoin();
            _nextSpawnTime = Time.time + spawnInterval;
        }

        UpdateNextCoinHint(playerLane);
        if (youAreHereText != null)
            youAreHereText.text = "You: " + LaneNames[playerLane];
        if (scoreText != null)
            scoreText.text = "Coins: " + Score;
    }

    void UpdateNextCoinHint(int playerLane)
    {
        if (laneHintText == null) return;
        CoinMineCoin next = null;
        float nearestZ = float.MaxValue;
        foreach (var c in _coins)
        {
            if (c == null) continue;
            if (c.transform.position.z < nearestZ && c.transform.position.z > collectZ + 2f)
            {
                nearestZ = c.transform.position.z;
                next = c;
            }
        }
        if (next != null)
            laneHintText.text = LeanHint[next.Lane] + " for coin!";
    }

    void SpawnCoin()
    {
        int lane = Random.Range(0, 3);
        float x = LaneX(lane);
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Coin";
        go.transform.position = new Vector3(x, 1.2f, spawnZ);
        go.transform.localScale = Vector3.one * 0.8f;
        var col = go.GetComponent<Collider>();
        if (col != null) col.enabled = false;
        var renderer = go.GetComponent<Renderer>();
        if (renderer != null && renderer.material != null)
            renderer.material.color = new Color(1f, 0.85f, 0.2f);

        var coin = go.AddComponent<CoinMineCoin>();
        coin.Lane = lane;
        coin.Speed = coinSpeed;
        coin.CollectZ = collectZ;
        _coins.Add(coin);
    }

    public void StartGame()
    {
        Score = 0;
        IsPlaying = true;
        _nextSpawnTime = Time.time + 1f;
        ClearCoins();
        if (laneHintText != null) laneHintText.text = "Lean to match the coin's lane!";
        if (startPromptPanel != null) startPromptPanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    public void StopGame()
    {
        IsPlaying = false;
        ClearCoins();
        if (startPromptPanel != null) startPromptPanel.SetActive(true);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    public void EndGame()
    {
        IsPlaying = false;
        ClearCoins();
        if (gameOverPanel != null) gameOverPanel.SetActive(true);
        if (gameOverScoreText != null) gameOverScoreText.text = "Coins: " + Score;
        if (startPromptPanel != null) startPromptPanel.SetActive(false);
    }

    void ClearCoins()
    {
        foreach (var c in _coins)
            if (c != null) Destroy(c.gameObject);
        _coins.Clear();
    }
}
