using UnityEngine;

/// <summary>
/// Discrete gestures and balance metrics from pose data for Pose Dodge and Single-Leg Balance.
/// Consumes PoseReceiver; exposes current gesture (with optional hold) and balance/wobble metrics.
/// </summary>
public class PoseGestureDetector : MonoBehaviour
{
    public enum Gesture
    {
        None,
        ArmsUp,
        TPose,
        Crouch,
        LeanLeft,
        LeanRight
    }

    public enum StandingLeg
    {
        None,
        Left,
        Right
    }

    /// <summary>Discrete torso lean from shoulders vs hips (robust, frame-independent).</summary>
    public enum TorsoLeanState
    {
        Neutral,
        Left,
        Right
    }

    [Header("Source")]
    public PoseReceiver poseReceiver;

    [Header("Gesture thresholds (normalized 0-1, Y down)")]
    [Tooltip("Arms up: wrist Y must be above shoulder Y (smaller value).")]
    [Range(0.02f, 0.15f)]
    public float armsUpWristAboveShoulder = 0.05f;

    [Tooltip("Crouch: knee Y must be this much below hip Y. When standing, knee is already slightly below hip; use ~0.10 so only real squat triggers.")]
    [Range(0.04f, 0.2f)]
    public float crouchKneeBelowHip = 0.10f;

    [Tooltip("Lean: shoulder center X must be this far from 0.5 to register. Larger = only clear left/right shift (avoids false lean when not perfectly centered in frame).")]
    [Range(0.04f, 0.25f)]
    public float leanThreshold = 0.10f;

    [Tooltip("Single-leg: ankle Y difference (raised foot has smaller Y).")]
    [Range(0.02f, 0.12f)]
    public float singleLegAnkleDiff = 0.05f;

    [Header("Torso lean (shoulders vs hips — robust, frame-independent)")]
    [Tooltip("If true, lean right moves the bar/avatar right. Turn off if your camera mirror flips it.")]
    public bool invertTorsoLean = true;
    [Tooltip("Shoulder center X minus hip center X. Positive = lean right. Smoothed for stability.")]
    [Range(0.05f, 0.5f)]
    public float torsoLeanSmoothing = 0.25f;
    [Tooltip("|TorsoLeanX| below this = Neutral. Used for CurrentTorsoLeanState.")]
    [Range(0.01f, 0.08f)]
    public float torsoLeanNeutralZone = 0.03f;

    [Header("Stability")]
    [Tooltip("Frames to average for sway (balance).")]
    [Range(5, 60)]
    public int swayHistoryFrames = 30;

    [Tooltip("Sway magnitude above this = unstable (0-1 scale).")]
    [Range(0.01f, 0.2f)]
    public float unstableSwayThreshold = 0.08f;

    [Header("Debounce")]
    [Tooltip("Frames gesture must be held to register (reduces jitter).")]
    [Range(1, 15)]
    public int gestureHoldFrames = 5;

    // Current outputs
    public Gesture CurrentGesture { get; private set; }
    public StandingLeg CurrentStandingLeg { get; private set; }
    public float SwayMagnitude { get; private set; }
    public bool IsStable => SwayMagnitude < unstableSwayThreshold;
    public bool IsSingleLeg => CurrentStandingLeg != StandingLeg.None;
    /// <summary>Lean for steering: positive = right (when invertTorsoLean is true). Use for bar position / lanes.</summary>
    public float TorsoLeanX => invertTorsoLean ? -_torsoLeanSmoothed : _torsoLeanSmoothed;
    public TorsoLeanState CurrentTorsoLeanState =>
        Mathf.Abs(TorsoLeanX) < torsoLeanNeutralZone ? TorsoLeanState.Neutral :
        TorsoLeanX < 0 ? TorsoLeanState.Left : TorsoLeanState.Right;

    int _gestureHoldCount;
    Gesture _pendingGesture;
    float[] _hipCenterX;
    float[] _hipCenterY;
    int _swayIndex;
    int _swayCount;
    float _torsoLeanSmoothed;

    void Start()
    {
        if (poseReceiver == null)
            poseReceiver = FindFirstObjectByType<PoseReceiver>();
        if (poseReceiver == null)
            Debug.LogWarning("[PoseGestureDetector] No PoseReceiver found.");
        _hipCenterX = new float[swayHistoryFrames];
        _hipCenterY = new float[swayHistoryFrames];
    }

    void Update()
    {
        if (poseReceiver == null || poseReceiver.latestPose == null ||
            poseReceiver.latestPose.keypoints == null || poseReceiver.latestPose.keypoints.Length < 17)
        {
            CurrentGesture = Gesture.None;
            CurrentStandingLeg = StandingLeg.None;
            return;
        }

        var k = poseReceiver.latestPose.keypoints;
        float minC = poseReceiver.minConfidence;

        Gesture detected = DetectGesture(k, minC);
        if (detected == _pendingGesture)
        {
            _gestureHoldCount++;
            if (_gestureHoldCount >= gestureHoldFrames)
                CurrentGesture = detected;
        }
        else
        {
            _pendingGesture = detected;
            _gestureHoldCount = 1;
            if (gestureHoldFrames <= 1)
                CurrentGesture = detected;
        }

        CurrentStandingLeg = DetectStandingLeg(k, minC);
        UpdateSway(k, minC);
        UpdateTorsoLean(k, minC);
    }

    void UpdateTorsoLean(PoseKeypoint[] k, float minC)
    {
        if (!TryGet(k, CocoKeypointIndex.LeftShoulder, minC, out float lsX, out _) ||
            !TryGet(k, CocoKeypointIndex.RightShoulder, minC, out float rsX, out _) ||
            !TryGet(k, CocoKeypointIndex.LeftHip, minC, out float lhX, out _) ||
            !TryGet(k, CocoKeypointIndex.RightHip, minC, out float rhX, out _))
        {
            return;
        }
        float shoulderCenterX = (lsX + rsX) * 0.5f;
        float hipCenterX = (lhX + rhX) * 0.5f;
        float raw = shoulderCenterX - hipCenterX;
        float alpha = Mathf.Clamp01(torsoLeanSmoothing);
        _torsoLeanSmoothed = alpha * raw + (1f - alpha) * _torsoLeanSmoothed;
    }

    /// <summary>Gesture is derived here from keypoints only; Python sends no gesture labels.</summary>
    Gesture DetectGesture(PoseKeypoint[] k, float minC)
    {
        if (!TryGet(k, CocoKeypointIndex.LeftShoulder, minC, out float lsX, out float lsY) ||
            !TryGet(k, CocoKeypointIndex.RightShoulder, minC, out float rsX, out float rsY))
            return Gesture.None;

        float shoulderCenterX = (lsX + rsX) * 0.5f;

        // Lean: shoulder center X vs frame center 0.5 (where you stand in frame, not body angle)
        if (shoulderCenterX < 0.5f - leanThreshold) return Gesture.LeanLeft;
        if (shoulderCenterX > 0.5f + leanThreshold) return Gesture.LeanRight;

        // Crouch: knees well below hips (larger Y = lower in image). Standing already has knee slightly below hip.
        bool hasCrouch = true;
        if (TryGet(k, CocoKeypointIndex.LeftHip, minC, out float lhX, out float lhY) &&
            TryGet(k, CocoKeypointIndex.LeftKnee, minC, out float lkX, out float lkY))
        {
            if (lkY <= lhY + crouchKneeBelowHip) hasCrouch = false;
        }
        else hasCrouch = false;
        if (hasCrouch && TryGet(k, CocoKeypointIndex.RightHip, minC, out float rhX, out float rhY) &&
            TryGet(k, CocoKeypointIndex.RightKnee, minC, out float rkX, out float rkY))
        {
            if (rkY <= rhY + crouchKneeBelowHip) hasCrouch = false;
        }
        else hasCrouch = false;
        if (hasCrouch) return Gesture.Crouch;

        // Arms up: both wrists above shoulders (smaller Y)
        bool armsUp = true;
        if (TryGet(k, CocoKeypointIndex.LeftWrist, minC, out float lwX, out float lwY))
        {
            if (lwY >= lsY - armsUpWristAboveShoulder) armsUp = false;
        }
        else armsUp = false;
        if (armsUp && TryGet(k, CocoKeypointIndex.RightWrist, minC, out float rwX, out float rwY))
        {
            if (rwY >= rsY - armsUpWristAboveShoulder) armsUp = false;
        }
        else armsUp = false;
        if (armsUp) return Gesture.ArmsUp;

        // T-pose: wrists roughly at shoulder height and spread out
        if (TryGet(k, CocoKeypointIndex.LeftWrist, minC, out float lwx, out float lwy) &&
            TryGet(k, CocoKeypointIndex.RightWrist, minC, out float rwx, out float rwy))
        {
            float shoulderHeight = (lsY + rsY) * 0.5f;
            float wristSpread = Mathf.Abs(rwx - lwx);
            if (Mathf.Abs(lwy - shoulderHeight) < 0.08f && Mathf.Abs(rwy - shoulderHeight) < 0.08f && wristSpread > 0.25f)
                return Gesture.TPose;
        }

        return Gesture.None;
    }

    StandingLeg DetectStandingLeg(PoseKeypoint[] k, float minC)
    {
        if (!TryGet(k, CocoKeypointIndex.LeftAnkle, minC, out float laX, out float laY) ||
            !TryGet(k, CocoKeypointIndex.RightAnkle, minC, out float raX, out float raY))
            return StandingLeg.None;
        if (laY < raY - singleLegAnkleDiff) return StandingLeg.Right;
        if (raY < laY - singleLegAnkleDiff) return StandingLeg.Left;
        return StandingLeg.None;
    }

    void UpdateSway(PoseKeypoint[] k, float minC)
    {
        if (!TryGet(k, CocoKeypointIndex.LeftHip, minC, out float lhX, out float lhY) ||
            !TryGet(k, CocoKeypointIndex.RightHip, minC, out float rhX, out float rhY))
            return;
        float cx = (lhX + rhX) * 0.5f;
        float cy = (lhY + rhY) * 0.5f;
        _hipCenterX[_swayIndex] = cx;
        _hipCenterY[_swayIndex] = cy;
        _swayIndex = (_swayIndex + 1) % swayHistoryFrames;
        _swayCount = Mathf.Min(_swayCount + 1, swayHistoryFrames);

        if (_swayCount < 5) { SwayMagnitude = 0f; return; }
        float sumX = 0f, sumY = 0f;
        for (int i = 0; i < _swayCount; i++)
        {
            sumX += _hipCenterX[i];
            sumY += _hipCenterY[i];
        }
        float meanX = sumX / _swayCount;
        float meanY = sumY / _swayCount;
        float var = 0f;
        for (int i = 0; i < _swayCount; i++)
        {
            float dx = _hipCenterX[i] - meanX;
            float dy = _hipCenterY[i] - meanY;
            var += dx * dx + dy * dy;
        }
        SwayMagnitude = Mathf.Sqrt(var / _swayCount);
    }

    static bool TryGet(PoseKeypoint[] k, int i, float minC, out float x, out float y)
    {
        x = y = 0f;
        if (i < 0 || i >= k.Length || k[i].s < minC) return false;
        x = k[i].x;
        y = k[i].y;
        return true;
    }
}
