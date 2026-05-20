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

    [Header("Split view")]
    [Tooltip("Fraction of screen width used for the clock ROI panel (right side).")]
    [Range(0.25f, 0.75f)]
    public float rightPanelWidthFraction = 0.5f;

    [Tooltip("World-space X offset for the pose avatar while Clock mode is active (negative = left).")]
    public float avatarOffsetX = -4.5f;

    [Tooltip("Avatar scale while Clock mode is active (larger = easier to see on the left panel).")]
    public float clockAvatarScale = 7f;

    Texture2D _roiTexture;
    PoseAvatarDriver _avatarDriver;
    float _savedAvatarOffsetX;
    float _savedAvatarScale;
    string _lastPayload;
    float _lastFrameTime;

    public bool IsActive { get; private set; }

    void Awake()
    {
        if (poseReceiver == null)
            poseReceiver = FindFirstObjectByType<PoseReceiver>();
        if (_avatarDriver == null)
            _avatarDriver = FindFirstObjectByType<PoseAvatarDriver>();
    }
    // NOTE: do NOT reset IsActive in Start(). Start() is deferred by Unity to
    // after OnEnable, so `SelectClock()` in ArchitectGameSelector runs
    // SetActive(true) + Activate() (IsActive=true) first, and Start() would
    // then overwrite it back to false before the first Update — leaving the
    // panel stuck on "waiting for UDP" even when packets are arriving.
    // Activation/deactivation is fully controlled by Activate()/Deactivate().

    void Update()
    {
        if (!IsActive) return;

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

        if (payload == _lastPayload)
        {
            return;
        }

        _lastPayload = payload;
        try
        {
            byte[] data = Convert.FromBase64String(payload);
            if (_roiTexture == null) _roiTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (_roiTexture.LoadImage(data, false))
            {
                if (roiPreviewImage != null)
                {
                    roiPreviewImage.texture = _roiTexture;
                    FitRoiImageToRightEdge();
                }
                float now = Time.time;
                float fps = _lastFrameTime > 0f ? 1f / Mathf.Max(0.0001f, now - _lastFrameTime) : 0f;
                _lastFrameTime = now;
                if (statusLabel != null) statusLabel.text = "Clock ROI stream: LIVE";
                if (infoLabel != null) infoLabel.text = $"Resolution: {_roiTexture.width}x{_roiTexture.height} | Decode FPS: {fps:0.0}";
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
        ApplySplitViewLayout();
        if (_avatarDriver != null)
        {
            _savedAvatarOffsetX = _avatarDriver.skeletonOffsetX;
            _savedAvatarScale = _avatarDriver.avatarScale;
            _avatarDriver.skeletonOffsetX = avatarOffsetX;
            _avatarDriver.avatarScale = clockAvatarScale;
            _avatarDriver.RefreshJointSizes();
        }
    }

    public void Deactivate()
    {
        IsActive = false;
        if (statusLabel != null) statusLabel.text = "Clock ROI stream: waiting for UDP payload...";
        if (infoLabel != null) infoLabel.text = "Resolution: --";
        if (_avatarDriver != null)
        {
            _avatarDriver.skeletonOffsetX = _savedAvatarOffsetX;
            _avatarDriver.avatarScale = _savedAvatarScale;
            _avatarDriver.RefreshJointSizes();
        }
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
