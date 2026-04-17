using System;
using UnityEngine;

[Serializable]
public class PoseKeypoint
{
    public float x;
    public float y;
    public float s;
}

[Serializable]
public class PoseMessage
{
    public PoseKeypoint[] keypoints;
    public int width;
    public int height;
}

public static class CocoKeypointIndex
{
    public const int LeftShoulder = 5;
    public const int RightShoulder = 6;
    public const int LeftHip = 11;
    public const int RightHip = 12;
}
