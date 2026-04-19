using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays a live ROI image stream (base64 JPEG/PNG) received from pose.py UDP payload.
/// This mode is intended for clock experiments where OCR is replaced by direct ROI transfer.
/// </summary>
public class ClockMode : MonoBehaviour
{
    [Header("Dependencies")]
    public PoseReceiver poseReceiver;

    [Header("UI")]
    public RawImage roiPreviewImage;
    public TMP_Text statusLabel;
    public TMP_Text infoLabel;

    Texture2D _roiTexture;
    string _lastPayload;
    float _lastFrameTime;

    public bool IsActive { get; private set; }

    void Awake()
    {
        if (poseReceiver == null)
            poseReceiver = FindFirstObjectByType<PoseReceiver>();
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
                if (roiPreviewImage != null) roiPreviewImage.texture = _roiTexture;
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
    }

    public void Deactivate()
    {
        IsActive = false;
        if (statusLabel != null) statusLabel.text = "Clock ROI stream: waiting for UDP payload...";
        if (infoLabel != null) infoLabel.text = "Resolution: --";
    }
}
