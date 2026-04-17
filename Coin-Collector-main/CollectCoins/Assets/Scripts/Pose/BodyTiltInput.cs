using UnityEngine;

public class BodyTiltInput : MonoBehaviour
{
    public PoseGestureDetector poseGestureDetector;
    [Range(0.05f, 0.25f)] public float maxLean = 0.12f;
    [Range(0f, 0.5f)] [Tooltip("0 = minimum latency; higher = smoother but more delay.")]
    public float outputSmoothing = 0f;
    float _smoothedAxis;

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
        if (poseGestureDetector == null) poseGestureDetector = FindObjectOfType<PoseGestureDetector>();
    }
}
