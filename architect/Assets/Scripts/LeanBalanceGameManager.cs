using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Lean Balance game: use only torso lean (shoulders vs hips). Keep a bar in the green (neutral) zone.
/// Robust with noisy pose — one continuous value (TorsoLeanX), four keypoints, frame-independent.
/// Goal: stay in zone as long as possible; score = time in zone. Optional: fail after too long out of zone.
/// </summary>
public class LeanBalanceGameManager : MonoBehaviour
{
    [Header("Dependencies")]
    public PoseGestureDetector gestureDetector;

    [Header("Rules")]
    [Tooltip("|TorsoLeanX| below this = in zone. Match PoseGestureDetector.torsoLeanNeutralZone or slightly larger.")]
    [Range(0.02f, 0.08f)]
    public float neutralZoneHalfWidth = 0.04f;
    [Tooltip("Seconds out of zone before round ends (0 = never fail, only score time in zone).")]
    public float outOfZoneFailSeconds = 0f;
    [Tooltip("Optional: round ends successfully after this many seconds in zone.")]
    public float targetTimeInZone = 0f;

    [Header("UI (optional)")]
    public Slider leanBarSlider;
    [Tooltip("Slider range for TorsoLeanX (symmetric).")]
    public float sliderRange = 0.12f;
    public TMP_Text timerText;
    public TMP_Text scoreText;
    public TMP_Text instructionText;
    public GameObject gameOverPanel;
    public TMP_Text gameOverScoreText;
    public GameObject startPromptPanel;

    public float TimeInZone => _timeInZone;
    public float ElapsedTime => _elapsed;
    public bool IsPlaying { get; private set; }
    public bool IsInZone => _inZone;

    float _elapsed;
    float _timeInZone;
    float _timeOutOfZone;
    bool _inZone;

    void Start()
    {
        if (gestureDetector == null)
            gestureDetector = FindFirstObjectByType<PoseGestureDetector>();
        if (instructionText != null)
            instructionText.text = "Lean your body left or right. Keep the bar in the green (center).";
        StopGame();
    }

    void Update()
    {
        if (!IsPlaying || gestureDetector == null) return;

        _elapsed += Time.deltaTime;
        float lean = gestureDetector.TorsoLeanX;
        _inZone = Mathf.Abs(lean) <= neutralZoneHalfWidth;

        if (_inZone)
        {
            _timeInZone += Time.deltaTime;
            _timeOutOfZone = 0f;
        }
        else
        {
            _timeOutOfZone += Time.deltaTime;
        }

        if (leanBarSlider != null)
        {
            leanBarSlider.minValue = -sliderRange;
            leanBarSlider.maxValue = sliderRange;
            leanBarSlider.value = lean;
        }

        if (outOfZoneFailSeconds > 0f && _timeOutOfZone >= outOfZoneFailSeconds)
        {
            EndGame(false);
            return;
        }
        if (targetTimeInZone > 0f && _timeInZone >= targetTimeInZone)
        {
            EndGame(true);
            return;
        }

        RefreshUI();
    }

    void RefreshUI()
    {
        if (timerText != null)
            timerText.text = "Time: " + _elapsed.ToString("F1") + " s";
        if (scoreText != null)
            scoreText.text = "In zone: " + _timeInZone.ToString("F1") + " s";
    }

    public void StartGame()
    {
        _elapsed = 0f;
        _timeInZone = 0f;
        _timeOutOfZone = 0f;
        IsPlaying = true;
        if (startPromptPanel != null) startPromptPanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
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
            gameOverScoreText.text = success
                ? "Success! Time in zone: " + _timeInZone.ToString("F1") + " s"
                : "Out of zone too long. Time in zone: " + _timeInZone.ToString("F1") + " s";
        if (startPromptPanel != null) startPromptPanel.SetActive(false);
    }
}
