using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Single-Leg Balance game: hold single-leg stance and keep stability (low wobble).
/// External focus: keep the "stability bar" full.
///
/// How it works:
/// 1. Press Start -> instruction says "Lift one leg to begin"
/// 2. When single-leg is detected and held for singleLegRequiredAfterSeconds, timer starts
/// 3. Timer counts up; score accumulates based on how stable you are (less sway = more points/sec)
/// 4. Putting your foot down (after the hold threshold) OR wobbling too much for too long -> round ends
/// 5. Reaching targetHoldTime also ends the round (success)
/// </summary>
public class SingleLegBalanceManager : MonoBehaviour
{
    [Header("Dependencies")]
    public PoseGestureDetector gestureDetector;

    [Header("Rules")]
    [Tooltip("Seconds to hold single-leg with stability to complete a round.")]
    public float targetHoldTime = 30f;
    [Tooltip("Sway above this counts as unstable (0-1).")]
    public float unstableSwayThreshold = 0.08f;
    [Tooltip("Seconds of instability before round fails (0 = fail immediately).")]
    public float instabilityGraceTime = 1.5f;
    [Tooltip("Seconds of single-leg hold required before scoring starts.")]
    public float singleLegRequiredAfterSeconds = 2f;

    [Header("Scoring")]
    [Tooltip("Points per second at perfect stability.")]
    public float scoreScale = 10f;

    [Header("UI (optional)")]
    public Slider stabilityBar;
    public TMP_Text timerText;
    public TMP_Text scoreText;
    public TMP_Text instructionText;
    public GameObject gameOverPanel;
    public TMP_Text gameOverScoreText;
    public TMP_Text gameOverTimeText;
    public GameObject startPromptPanel;

    public float ElapsedTime => _elapsed;
    public float StabilityScore { get; private set; }
    public bool IsPlaying { get; private set; }

    float _elapsed;
    float _unstableTime;
    float _singleLegHoldTime;
    bool _singleLegRequired;
    PoseGestureDetector _gestureDetector;
    float _integratedStability;

    void Start()
    {
        _gestureDetector = gestureDetector != null ? gestureDetector : FindFirstObjectByType<PoseGestureDetector>();
        if (_gestureDetector == null)
            Debug.LogWarning("[SingleLegBalanceManager] No PoseGestureDetector found.");
        StopGame();
    }

    void Update()
    {
        if (!IsPlaying || _gestureDetector == null) return;

        bool singleLeg = _gestureDetector.IsSingleLeg;
        bool stable = _gestureDetector.SwayMagnitude < unstableSwayThreshold;

        if (singleLeg)
            _singleLegHoldTime += Time.deltaTime;
        else
            _singleLegHoldTime = 0f;

        if (!_singleLegRequired && _singleLegHoldTime >= singleLegRequiredAfterSeconds)
            _singleLegRequired = true;

        if (_singleLegRequired && !singleLeg)
        {
            EndGame(false);
            return;
        }

        if (_singleLegRequired)
        {
            if (!stable)
            {
                _unstableTime += Time.deltaTime;
                if (instabilityGraceTime > 0f && _unstableTime >= instabilityGraceTime)
                {
                    EndGame(false);
                    return;
                }
            }
            else
                _unstableTime = 0f;

            _elapsed += Time.deltaTime;
            float stabilityThisFrame = stable ? (1f - _gestureDetector.SwayMagnitude / unstableSwayThreshold) : 0f;
            _integratedStability += stabilityThisFrame * Time.deltaTime;
            StabilityScore = _integratedStability * scoreScale;

            if (_elapsed >= targetHoldTime)
            {
                EndGame(true);
                return;
            }
        }

        RefreshUI();
    }

    public void StartGame()
    {
        _elapsed = 0f;
        _unstableTime = 0f;
        _singleLegHoldTime = 0f;
        _singleLegRequired = false;
        _integratedStability = 0f;
        StabilityScore = 0f;
        IsPlaying = true;
        if (startPromptPanel != null) startPromptPanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        RefreshUI();
    }

    public void StopGame()
    {
        IsPlaying = false;
        if (startPromptPanel != null) startPromptPanel.SetActive(true);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    void EndGame(bool success)
    {
        IsPlaying = false;
        if (gameOverPanel != null) gameOverPanel.SetActive(true);
        if (gameOverScoreText != null)
            gameOverScoreText.text = "Score: " + StabilityScore.ToString("F0");
        if (gameOverTimeText != null)
            gameOverTimeText.text = "Time: " + _elapsed.ToString("F1") + "s" +
                (success ? " (completed!)" : "");
        if (startPromptPanel != null) startPromptPanel.SetActive(false);
    }

    void RefreshUI()
    {
        if (stabilityBar != null)
        {
            float sway = _gestureDetector != null ? _gestureDetector.SwayMagnitude : 0f;
            stabilityBar.value = Mathf.Clamp01(1f - sway / unstableSwayThreshold);
        }
        if (timerText != null)
            timerText.text = "Time: " + _elapsed.ToString("F1") + "s / " + targetHoldTime.ToString("F0") + "s";
        if (scoreText != null)
            scoreText.text = "Score: " + StabilityScore.ToString("F0");
        if (instructionText != null)
        {
            if (!_singleLegRequired)
                instructionText.text = "Lift one leg to begin...";
            else if (_gestureDetector != null && !_gestureDetector.IsStable)
                instructionText.text = "Too much wobble! Stabilize!";
            else
                instructionText.text = "Hold steady!";
        }
    }
}
