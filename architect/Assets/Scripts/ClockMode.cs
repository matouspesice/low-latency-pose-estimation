using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays a live ROI image stream (base64 JPEG) received from pose.py in the
/// same UDP JSON packet as pose keypoints (roiImageBase64), after inference.
/// </summary>
public class ClockMode : MonoBehaviour
{
    [Header("Dependencies")]
    public PoseReceiver poseReceiver;

    [Header("UI")]
    public RawImage roiPreviewImage;
    public TMP_Text statusLabel;
    public TMP_Text infoLabel;

    [Header("Genies avatar")]
    public GeniesPoseAvatarDriver geniesAvatarDriver;
    public bool cycleGeniesAvatars = false;
    public float geniesAvatarCycleSeconds = 30f;

    [Header("Split view")]
    [Tooltip("Fraction of the screen width the transferred ROI image occupies on the right.")]
    [Range(0.25f, 0.75f)]
    public float rightPanelWidthFraction = 0.5f;
    [Tooltip("Horizontal world offset of the avatar/skeleton (negative = left half of the screen).")]
    public float avatarOffsetX = -4.5f;
    [Tooltip("World-space size of the avatar in Clock ROI. Raise for a bigger avatar on the left half.")]
    public float clockAvatarScale = 8.5f;

    [Tooltip("Max ROI texture updates per second (decoding JPEG on the main thread is expensive).")]
    public float maxRoiDecodeFps = 12f;

    Texture2D _roiTexture;
    PoseAvatarDriver _avatarDriver;
    string _lastPayload;
    string _pendingPayload;
    string _infoCore = "Resolution: --";
    float _lastFrameTime;
    float _lastDecodeTime;
    int _lastTextureWidth;
    int _lastTextureHeight;

    public bool IsActive { get; private set; }

    void Awake()
    {
        if (poseReceiver == null)
            poseReceiver = FindFirstObjectByType<PoseReceiver>();
        if (_avatarDriver == null)
            _avatarDriver = FindFirstObjectByType<PoseAvatarDriver>();
        if (geniesAvatarDriver == null)
            geniesAvatarDriver = FindFirstObjectByType<GeniesPoseAvatarDriver>();
    }

    void Update()
    {
        if (!IsActive) return;

        // Keep the avatar status (loading / loaded / failed-fallback) live on screen every frame,
        // independent of the ROI decode throttle.
        if (infoLabel != null) infoLabel.text = _infoCore + BuildAvatarStatus();

        if (poseReceiver == null)
        {
            if (statusLabel != null) statusLabel.text = "Clock ROI stream: PoseReceiver missing";
            return;
        }

        string payload = poseReceiver.LatestClockRoiBase64;
        if (string.IsNullOrWhiteSpace(payload))
        {
            if (statusLabel != null) statusLabel.text = "Clock ROI stream: waiting for UDP payload...";
            return;
        }

        if (payload != _lastPayload)
            _pendingPayload = payload;

        if (string.IsNullOrEmpty(_pendingPayload))
            return;

        float minInterval = 1f / Mathf.Max(1f, maxRoiDecodeFps);
        if (Time.time - _lastDecodeTime < minInterval)
            return;

        string decodePayload = _pendingPayload;
        _pendingPayload = null;
        _lastDecodeTime = Time.time;

        try
        {
            byte[] data = Convert.FromBase64String(decodePayload);
            if (_roiTexture == null)
                _roiTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            if (_roiTexture.LoadImage(data, false))
            {
                _lastPayload = decodePayload;
                if (PipelineTrace.Enabled && poseReceiver != null && poseReceiver.latestPose != null)
                    PipelineTrace.Log("unity_roi_displayed", poseReceiver.latestPose.frameSeq,
                        $"size={_roiTexture.width}x{_roiTexture.height}");
                if (roiPreviewImage != null)
                {
                    roiPreviewImage.texture = _roiTexture;
                    if (_roiTexture.width != _lastTextureWidth || _roiTexture.height != _lastTextureHeight)
                    {
                        _lastTextureWidth = _roiTexture.width;
                        _lastTextureHeight = _roiTexture.height;
                        FitRoiImageToRightEdge();
                    }
                }

                float now = Time.time;
                float fps = _lastFrameTime > 0f ? 1f / Mathf.Max(0.0001f, now - _lastFrameTime) : 0f;
                _lastFrameTime = now;
                if (statusLabel != null) statusLabel.text = "Clock ROI stream: LIVE";
                _infoCore = $"Resolution: {_roiTexture.width}x{_roiTexture.height} | Decode FPS: {fps:0.0}";
            }
        }
        catch (Exception)
        {
            if (statusLabel != null) statusLabel.text = "Clock ROI stream: invalid base64 payload";
        }
    }

    public void Activate()
    {
        IsActive = true;
        _pendingPayload = null;
        ApplySplitViewLayout();
        if (geniesAvatarDriver != null)
        {
            geniesAvatarDriver.BeginPoseDisplay(avatarOffsetX, clockAvatarScale);
            if (cycleGeniesAvatars)
                geniesAvatarDriver.StartCycling(geniesAvatarCycleSeconds);
        }
        else if (_avatarDriver != null)
        {
            _avatarDriver.skeletonOffsetX = avatarOffsetX;
            _avatarDriver.avatarScale = clockAvatarScale;
            _avatarDriver.RefreshJointSizes();
        }
    }

    public void Deactivate()
    {
        IsActive = false;
        _pendingPayload = null;
        _infoCore = "Resolution: --";
        if (statusLabel != null) statusLabel.text = "Clock ROI stream: waiting for UDP payload...";
        if (infoLabel != null) infoLabel.text = "Resolution: --";
        if (geniesAvatarDriver != null)
            geniesAvatarDriver.EndPoseDisplay();
    }

    /// <summary>
    /// Human-readable avatar state appended to the info line so a failed SDK load is never silent:
    /// it makes clear when the debug skeleton is being shown as a fallback.
    /// </summary>
    string BuildAvatarStatus()
    {
        if (geniesAvatarDriver == null)
            return string.Empty;
        if (geniesAvatarDriver.IsDebugSkeletonSelected)
            return " | Avatar: Debug Skeleton";
        if (geniesAvatarDriver.IsLoading)
            return $" | Avatar: loading {geniesAvatarDriver.CurrentAvatarName}...";
        if (geniesAvatarDriver.IsLoaded)
            return $" | Avatar: {geniesAvatarDriver.CurrentAvatarName}";
        if (geniesAvatarDriver.IsCurrentSelectionFailed)
            return $" | <color=#FF6B6B>Avatar \"{geniesAvatarDriver.CurrentAvatarName}\" failed to load \u2014 showing Debug Skeleton</color>";
        return string.Empty;
    }

    void ApplySplitViewLayout()
    {
        if (roiPreviewImage == null) return;
        var previewRoot = roiPreviewImage.transform.parent as RectTransform;
        if (previewRoot != null)
            StretchRectToRightHalf(previewRoot, rightPanelWidthFraction);
        FitRoiImageToRightEdge();
    }

    static void StretchRectToRightHalf(RectTransform rt, float widthFraction)
    {
        float leftEdge = 1f - Mathf.Clamp(widthFraction, 0.25f, 0.75f);
        rt.anchorMin = new Vector2(leftEdge, 0f);
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    void FitRoiImageToRightEdge()
    {
        if (roiPreviewImage == null || _roiTexture == null) return;
        var parentRt = roiPreviewImage.transform.parent as RectTransform;
        if (parentRt == null) return;

        float panelW = parentRt.rect.width;
        float panelH = parentRt.rect.height;
        if (panelW <= 1f || panelH <= 1f) return;

        float texAspect = (float)_roiTexture.width / Mathf.Max(1, _roiTexture.height);
        float h = panelH;
        float w = h * texAspect;
        if (w > panelW)
        {
            w = panelW;
            h = w / texAspect;
        }

        var rt = roiPreviewImage.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(w, h);
    }
}
