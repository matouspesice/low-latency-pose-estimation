using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Mode-menu controls for choosing which Genies avatar preset to load before entering a game mode.
/// </summary>
public class GeniesAvatarMenuSelector : MonoBehaviour
{
    public GeniesPoseAvatarDriver avatarDriver;
    public TMP_Text statusLabel;
    public Button prevButton;
    public Button nextButton;

    void Start()
    {
        if (avatarDriver == null)
            avatarDriver = FindFirstObjectByType<GeniesPoseAvatarDriver>();

        if (prevButton != null)
            prevButton.onClick.AddListener(SelectPrevious);
        if (nextButton != null)
            nextButton.onClick.AddListener(SelectNext);

        if (avatarDriver != null)
            avatarDriver.SelectionChanged += RefreshLabel;

        RefreshLabel();
    }

    void OnDestroy()
    {
        if (avatarDriver != null)
            avatarDriver.SelectionChanged -= RefreshLabel;
    }

    public void SelectPrevious()
    {
        if (avatarDriver == null) return;
        avatarDriver.SelectPreset(avatarDriver.SelectedPresetIndex - 1);
    }

    public void SelectNext()
    {
        if (avatarDriver == null) return;
        avatarDriver.SelectPreset(avatarDriver.SelectedPresetIndex + 1);
    }

    void RefreshLabel()
    {
        if (statusLabel == null || avatarDriver == null)
            return;

        bool loadingCurrent = avatarDriver.IsLoadingPreset(avatarDriver.SelectedPresetIndex);
        if (prevButton != null) prevButton.interactable = true;
        if (nextButton != null) nextButton.interactable = true;

        if (loadingCurrent)
        {
            statusLabel.text = $"Loading {avatarDriver.CurrentAvatarName}...";
            return;
        }

        statusLabel.text = avatarDriver.IsSelectionReady
            ? avatarDriver.CurrentAvatarName
            : $"{avatarDriver.CurrentAvatarName} (unavailable)";
    }
}
