using UnityEngine;
using TMPro;

/// <summary>
/// Pose Test mode: no game, no lives. Shows real-time gesture detection, standing leg,
/// sway magnitude, and stability status so you can test and calibrate pose recognition.
/// </summary>
public class PoseTestMode : MonoBehaviour
{
    [Header("Dependencies")]
    public PoseGestureDetector gestureDetector;
    public PoseReceiver poseReceiver;

    [Header("UI")]
    public TMP_Text gestureLabel;
    public TMP_Text standingLegLabel;
    public TMP_Text swayLabel;
    public TMP_Text stabilityLabel;
    public TMP_Text keypointInfoLabel;

    public bool IsActive { get; private set; }

    void Start()
    {
        if (gestureDetector == null)
            gestureDetector = FindFirstObjectByType<PoseGestureDetector>();
        if (poseReceiver == null)
            poseReceiver = FindFirstObjectByType<PoseReceiver>();
    }

    void Update()
    {
        if (!IsActive) return;
        if (gestureDetector == null) return;

        if (gestureLabel != null)
            gestureLabel.text = "Gesture: " + FormatGesture(gestureDetector.CurrentGesture);

        if (standingLegLabel != null)
            standingLegLabel.text = "Standing Leg: " + gestureDetector.CurrentStandingLeg;

        if (swayLabel != null)
            swayLabel.text = "Sway: " + gestureDetector.SwayMagnitude.ToString("F4");

        if (stabilityLabel != null)
        {
            bool stable = gestureDetector.IsStable;
            stabilityLabel.text = stable ? "STABLE" : "UNSTABLE";
            stabilityLabel.color = stable ? Color.green : Color.red;
        }

        if (keypointInfoLabel != null && poseReceiver != null && poseReceiver.latestPose != null)
        {
            var kp = poseReceiver.latestPose.keypoints;
            if (kp != null && kp.Length >= 17)
            {
                keypointInfoLabel.text =
                    $"Shoulders: L({kp[5].x:F2},{kp[5].y:F2}) R({kp[6].x:F2},{kp[6].y:F2})\n" +
                    $"Hips: L({kp[11].x:F2},{kp[11].y:F2}) R({kp[12].x:F2},{kp[12].y:F2})\n" +
                    $"Knees: L({kp[13].x:F2},{kp[13].y:F2}) R({kp[14].x:F2},{kp[14].y:F2})\n" +
                    $"Ankles: L({kp[15].x:F2},{kp[15].y:F2}) R({kp[16].x:F2},{kp[16].y:F2})\n" +
                    $"Wrists: L({kp[9].x:F2},{kp[9].y:F2}) R({kp[10].x:F2},{kp[10].y:F2})";
            }
        }
    }

    public void Activate()
    {
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    static string FormatGesture(PoseGestureDetector.Gesture g)
    {
        switch (g)
        {
            case PoseGestureDetector.Gesture.ArmsUp:    return "ARMS UP";
            case PoseGestureDetector.Gesture.Crouch:    return "DUCK / CROUCH";
            case PoseGestureDetector.Gesture.TPose:     return "T-POSE";
            case PoseGestureDetector.Gesture.LeanLeft:  return "LEAN LEFT";
            case PoseGestureDetector.Gesture.LeanRight: return "LEAN RIGHT";
            default:                                    return "STANDING (none)";
        }
    }
}
