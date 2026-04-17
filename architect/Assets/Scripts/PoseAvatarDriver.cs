using UnityEngine;

/// <summary>
/// Drives an avatar from pose data: places joint transforms in 3D and optional limb sticks (capsules).
/// Low-latency options: mirror (flip X), light smoothing (EMA), sticks as capsules.
/// </summary>
public class PoseAvatarDriver : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("Pose source (UDP receiver).")]
    public PoseReceiver poseReceiver;

    [Header("Display")]
    [Tooltip("Scale of the avatar in world space (spread of shoulders ~ 1 unit). Use 3.5+ for latency measurement visibility.")]
    public float avatarScale = 3.5f;

    [Tooltip("Flip X so avatar matches mirror view (your right = avatar right). Keeps latency minimal.")]
    public bool mirrorFlipX = true;

    [Tooltip("Smoothing 0 = off (lowest latency), 0.3–0.5 = light. Higher = smoother but more lag.")]
    [Range(0f, 0.9f)]
    public float smoothing = 0.4f;

    [Tooltip("Forward axis: 0 = X, 1 = Y, 2 = Z. Avatar faces this axis.")]
    public int forwardAxis = 2;

    [Tooltip("Optional: assign 17 Transforms in COCO order. If empty, debug skeleton is created.")]
    public Transform[] jointTransforms = new Transform[17];

    [Tooltip("Create spheres at joints when not assigned.")]
    public bool createDebugSkeleton = true;

    [Tooltip("Create capsule sticks between joints (arms, legs, torso).")]
    public bool createLimbSticks = true;

    [Tooltip("Stick thickness (radius) relative to avatar scale.")]
    [Range(0.02f, 0.2f)]
    public float stickThickness = 0.04f;

    Transform _debugRoot;
    bool _createdDebug;
    Vector3[] _smoothedPositions = new Vector3[17];
    bool _hasSmoothed;
    Transform[] _limbTransforms;
    static readonly int LimbCount = CocoKeypointIndex.LimbEdges.Length;

    void Start()
    {
        if (poseReceiver == null)
            poseReceiver = FindFirstObjectByType<PoseReceiver>();
        if (poseReceiver == null)
            Debug.LogWarning("[PoseAvatarDriver] No PoseReceiver assigned or found in scene.");
    }

    void Update()
    {
        if (poseReceiver == null || poseReceiver.latestPose == null) return;

        var pose = poseReceiver.latestPose;
        if (pose.keypoints == null || pose.keypoints.Length < 17) return;

        if (jointTransforms == null || jointTransforms.Length < 17)
            jointTransforms = new Transform[17];

        if (createDebugSkeleton && !_createdDebug)
        {
            EnsureDebugSkeleton();
            _createdDebug = true;
        }
        else if (createLimbSticks && _limbTransforms == null && _debugRoot != null)
        {
            _limbTransforms = new Transform[LimbCount];
            for (int i = 0; i < LimbCount; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = $"Limb_{i}";
                go.transform.SetParent(_debugRoot);
                var col = go.GetComponent<Collider>();
                if (col != null) col.enabled = false;
                _limbTransforms[i] = go.transform;
            }
        }

        float w = pose.width > 0 ? pose.width : 1f;
        float h = pose.height > 0 ? pose.height : 1f;
        float aspect = w / h;

        float shoulderWidth = 0f;
        if (pose.keypoints.Length > CocoKeypointIndex.RightShoulder)
        {
            var ls = pose.keypoints[CocoKeypointIndex.LeftShoulder];
            var rs = pose.keypoints[CocoKeypointIndex.RightShoulder];
            if (ls.s >= poseReceiver.minConfidence && rs.s >= poseReceiver.minConfidence)
            {
                float dx = (rs.x - ls.x) * aspect;
                float dy = rs.y - ls.y;
                shoulderWidth = Mathf.Sqrt(dx * dx + dy * dy);
            }
        }
        if (shoulderWidth < 0.01f) shoulderWidth = 0.25f;

        float scale = avatarScale * (0.25f / shoulderWidth);

        float smooth = Mathf.Clamp01(smoothing);
        float sharpness = smooth > 0.001f ? (1f - smooth) : 1f;

        for (int i = 0; i < 17 && i < pose.keypoints.Length; i++)
        {
            var k = pose.keypoints[i];
            if (k.s < poseReceiver.minConfidence) continue;

            float nx = (k.x - 0.5f) * aspect;
            if (mirrorFlipX) nx = -nx;
            float ny = 0.5f - k.y;

            Vector3 localPos;
            if (forwardAxis == 0)      localPos = new Vector3(0, ny * scale, nx * scale);
            else if (forwardAxis == 1) localPos = new Vector3(nx * scale, 0, ny * scale);
            else                       localPos = new Vector3(nx * scale, ny * scale, 0);

            if (smooth > 0.001f)
            {
                if (!_hasSmoothed) _smoothedPositions[i] = localPos;
                else _smoothedPositions[i] = Vector3.Lerp(_smoothedPositions[i], localPos, sharpness);
            }
            else
            {
                _smoothedPositions[i] = localPos;
            }

            var t = jointTransforms[i];
            if (t != null)
                t.localPosition = _smoothedPositions[i];
        }
        _hasSmoothed = true;

        UpdateLimbSticks();
    }

    void EnsureDebugSkeleton()
    {
        _debugRoot = new GameObject("PoseSkeleton").transform;
        _debugRoot.SetParent(transform);
        _debugRoot.localPosition = Vector3.zero;
        _debugRoot.localRotation = Quaternion.identity;
        _debugRoot.localScale = Vector3.one;

        float r = 0.08f * avatarScale;
        for (int i = 0; i < 17; i++)
        {
            if (jointTransforms[i] != null) continue;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"Joint_{i}";
            go.transform.SetParent(_debugRoot);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one * r;
            var col = go.GetComponent<Collider>();
            if (col != null) col.enabled = false;
            jointTransforms[i] = go.transform;
        }

        if (createLimbSticks)
        {
            _limbTransforms = new Transform[LimbCount];
            for (int i = 0; i < LimbCount; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = $"Limb_{i}";
                go.transform.SetParent(_debugRoot);
                var col = go.GetComponent<Collider>();
                if (col != null) col.enabled = false;
                _limbTransforms[i] = go.transform;
            }
        }
    }

    void UpdateLimbSticks()
    {
        if (_limbTransforms == null || _limbTransforms.Length != LimbCount) return;

        float thick = stickThickness * avatarScale;
        var edges = CocoKeypointIndex.LimbEdges;

        for (int i = 0; i < LimbCount; i++)
        {
            int a = edges[i].from;
            int b = edges[i].to;
            var tr = _limbTransforms[i];
            if (tr == null) continue;

            Vector3 pa = _smoothedPositions[a];
            Vector3 pb = _smoothedPositions[b];
            float len = Vector3.Distance(pa, pb);
            if (len < 0.001f) { tr.localScale = Vector3.one * 0.001f; continue; }

            tr.localPosition = (pa + pb) * 0.5f;
            tr.localRotation = Quaternion.FromToRotation(Vector3.up, (pb - pa).normalized);
            tr.localScale = new Vector3(thick, len * 0.5f, thick);
        }
    }
}
