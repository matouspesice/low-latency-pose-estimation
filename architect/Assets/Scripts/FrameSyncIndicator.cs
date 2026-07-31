using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Cycles a UI Image through high-contrast colours every Unity frame.
/// Used for motion-to-photon latency measurement: a 240 fps phone camera
/// recording the screen can read the colour to co-register phone frames
/// with Unity frames and verify the monitor refresh rate.
///
/// Also enforces V-Sync and an optional frame-rate cap at runtime so the
/// editor/build actually renders at the monitor refresh rate, regardless
/// of driver or render-pipeline overrides.
/// </summary>
public class FrameSyncIndicator : MonoBehaviour
{
    [Tooltip("UI Image whose colour is cycled each frame.")]
    public Image indicator;

    [Tooltip("Also show the Unity frame count as text (optional).")]
    public TMPro.TMP_Text frameCountText;

    [Header("Frame-Rate Control")]
    [Tooltip("Force V-Sync on at start (1 = every V-Blank). Set 0 to leave unchanged.")]
    public int forceVSyncCount = 1;

    [Tooltip("Fallback: cap frame rate if V-Sync alone doesn't work (-1 = no cap).")]
    public int fallbackTargetFps = -1;

    static readonly Color[] Palette =
    {
        Color.red,
        Color.green,
        Color.blue,
        Color.yellow,
        Color.cyan,
        Color.magenta,
        Color.white,
        new Color(1f, 0.5f, 0f) // orange
    };

    int _frameIndex;

    void Start()
    {
        if (forceVSyncCount > 0)
            QualitySettings.vSyncCount = forceVSyncCount;

        if (fallbackTargetFps > 0)
            Application.targetFrameRate = fallbackTargetFps;

        if (frameCountText != null)
        {
            frameCountText.enableAutoSizing = false;
            frameCountText.overflowMode = TMPro.TextOverflowModes.Overflow;
            frameCountText.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        }

        Debug.Log($"[FrameSyncIndicator] vSyncCount={QualitySettings.vSyncCount}, " +
                  $"targetFrameRate={Application.targetFrameRate}, " +
                  $"screen={Screen.currentResolution.width}x{Screen.currentResolution.height}@{Screen.currentResolution.refreshRateRatio.value:F1}Hz");
    }

    void Update()
    {
        if (indicator != null)
            indicator.color = Palette[_frameIndex % Palette.Length];

        if (frameCountText != null)
            frameCountText.text = Time.frameCount.ToString();

        _frameIndex++;
    }
}
