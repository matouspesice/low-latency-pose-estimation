using TMPro;
using UnityEngine;

/// <summary>
/// OCR mode: displays the latest OCR text received from pose.py while pose avatar runs normally.
/// </summary>
public class OcrMode : MonoBehaviour
{
    [Header("Dependencies")]
    public PoseReceiver poseReceiver;

    [Header("UI")]
    public TMP_Text ocrValueLabel;
    public TMP_Text statusLabel;

    [Tooltip("Fallback shown when no OCR text is available.")]
    public string emptyText = "--";

    public bool IsActive { get; private set; }

    void Start()
    {
        if (poseReceiver == null)
            poseReceiver = FindFirstObjectByType<PoseReceiver>();
    }

    void Update()
    {
        if (!IsActive) return;

        string txt = emptyText;
        bool hasSignal = false;
        if (poseReceiver != null)
        {
            if (!string.IsNullOrWhiteSpace(poseReceiver.LatestOcrText))
            {
                txt = poseReceiver.LatestOcrText;
                hasSignal = true;
            }
            else if (poseReceiver.latestPose != null && !string.IsNullOrWhiteSpace(poseReceiver.latestPose.ocrText))
            {
                txt = poseReceiver.latestPose.ocrText;
                hasSignal = true;
            }
        }

        if (ocrValueLabel != null)
            ocrValueLabel.text = txt;

        if (statusLabel != null)
            statusLabel.text = hasSignal ? "OCR stream: LIVE" : "Waiting for OCR UDP...";
    }

    public void Activate()
    {
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }
}
