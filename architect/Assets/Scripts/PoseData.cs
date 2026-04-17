using System;
using UnityEngine;

/// <summary>
/// COCO 17 keypoint data received from the pose estimation pipeline (pose.py).
/// Used by PoseReceiver and PoseAvatarDriver.
/// </summary>
[Serializable]
public class PoseKeypoint
{
    public float x; // normalized [0,1] or pixel
    public float y;
    public float s;  // confidence score
}

[Serializable]
public class PoseMessage
{
    public PoseKeypoint[] keypoints;
    public int width;
    public int height;
    public string ocrText;
}

/// <summary>
/// COCO 17 keypoint indices for mapping to avatar.
/// </summary>
public static class CocoKeypointIndex
{
    public const int Nose = 0;
    public const int LeftEye = 1;
    public const int RightEye = 2;
    public const int LeftEar = 3;
    public const int RightEar = 4;
    public const int LeftShoulder = 5;
    public const int RightShoulder = 6;
    public const int LeftElbow = 7;
    public const int RightElbow = 8;
    public const int LeftWrist = 9;
    public const int RightWrist = 10;
    public const int LeftHip = 11;
    public const int RightHip = 12;
    public const int LeftKnee = 13;
    public const int RightKnee = 14;
    public const int LeftAnkle = 15;
    public const int RightAnkle = 16;

    /// <summary>Limb segments (from, to) for drawing sticks. Order tuned for visibility.</summary>
    public static readonly (int from, int to)[] LimbEdges = new[]
    {
        (LeftShoulder, RightShoulder),
        (LeftShoulder, LeftElbow),
        (LeftElbow, LeftWrist),
        (RightShoulder, RightElbow),
        (RightElbow, RightWrist),
        (LeftShoulder, LeftHip),
        (RightShoulder, RightHip),
        (LeftHip, RightHip),
        (LeftHip, LeftKnee),
        (LeftKnee, LeftAnkle),
        (RightHip, RightKnee),
        (RightKnee, RightAnkle),
        (Nose, LeftEye),
        (Nose, RightEye),
        (LeftEye, LeftEar),
        (RightEye, RightEar),
    };
}
