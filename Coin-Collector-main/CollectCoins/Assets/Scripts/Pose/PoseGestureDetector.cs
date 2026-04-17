using UnityEngine;

public class PoseGestureDetector : MonoBehaviour
{
    public PoseReceiver poseReceiver;
    public bool invertTorsoLean = true;
    [Range(0.05f, 0.5f)] public float torsoLeanSmoothing = 0.25f;

    public float TorsoLeanX { get { return invertTorsoLean ? -_torsoLeanSmoothed : _torsoLeanSmoothed; } }
    float _torsoLeanSmoothed;

    void Start()
    {
        if (poseReceiver == null) poseReceiver = FindObjectOfType<PoseReceiver>();
    }

    void Update()
    {
        if (poseReceiver == null || poseReceiver.latestPose == null || poseReceiver.latestPose.keypoints == null || poseReceiver.latestPose.keypoints.Length < 17)
            return;
        var k = poseReceiver.latestPose.keypoints;
        float minC = poseReceiver.minConfidence;
        if (!TryGet(k, CocoKeypointIndex.LeftShoulder, minC, out float lsX, out float lsY) ||
            !TryGet(k, CocoKeypointIndex.RightShoulder, minC, out float rsX, out float rsY) ||
            !TryGet(k, CocoKeypointIndex.LeftHip, minC, out float lhX, out float lhY) ||
            !TryGet(k, CocoKeypointIndex.RightHip, minC, out float rhX, out float rhY))
            return;
        float shoulderCenterX = (lsX + rsX) * 0.5f;
        float hipCenterX = (lhX + rhX) * 0.5f;
        float raw = shoulderCenterX - hipCenterX;
        _torsoLeanSmoothed = Mathf.Clamp01(torsoLeanSmoothing) * raw + (1f - Mathf.Clamp01(torsoLeanSmoothing)) * _torsoLeanSmoothed;
    }

    static bool TryGet(PoseKeypoint[] k, int i, float minC, out float x, out float y)
    {
        x = y = 0f;
        if (i < 0 || i >= k.Length || k[i].s < minC) return false;
        x = k[i].x; y = k[i].y;
        return true;
    }
}
