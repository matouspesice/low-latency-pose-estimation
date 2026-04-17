using UnityEngine;

/// <summary>
/// Exposes body tilt (torso lean from pose) as a single axis in [-1, 1] for use by
/// external tilt-based games. Use with PoseReceiver + PoseGestureDetector (or equivalent).
/// Copy this and PoseData.cs, PoseReceiver.cs, PoseGestureDetector.cs into the external
/// game project; then have the game read TiltAxis instead of Input.acceleration or
/// Input.GetAxis("Horizontal").
/// </summary>
public class BodyTiltInput : MonoBehaviour
{
    [Header("Source")]
    public PoseGestureDetector poseGestureDetector;

    [Header("Mapping")]
    [Tooltip("|TorsoLeanX| at or above this maps to ±1. Tune so full lean feels right.")]
    [Range(0.05f, 0.25f)]
    public float maxLean = 0.12f;

    [Tooltip("Optional smoothing (0 = use PoseGestureDetector's value as-is).")]
    [Range(0f, 0.5f)]
    public float outputSmoothing = 0f;

    float _smoothedAxis;

    /// <summary>Body tilt as -1 (left) to +1 (right). Use this instead of Input.acceleration.x or Horizontal axis.</summary>
    public float TiltAxis
    {
        get
        {
            if (poseGestureDetector == null) return 0f;
            float raw = Mathf.Clamp(poseGestureDetector.TorsoLeanX / maxLean, -1f, 1f);
            if (outputSmoothing <= 0f) return raw;
            _smoothedAxis = Mathf.Lerp(_smoothedAxis, raw, 1f - outputSmoothing);
            return _smoothedAxis;
        }
    }

    void Start()
    {
        if (poseGestureDetector == null)
            poseGestureDetector = FindFirstObjectByType<PoseGestureDetector>();
        if (poseGestureDetector == null)
            Debug.LogWarning("[BodyTiltInput] No PoseGestureDetector assigned. TiltAxis will be 0.");
    }
}
