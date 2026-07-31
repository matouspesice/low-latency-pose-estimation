using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sharpens generated UI text at runtime (and when UI is rebuilt in the editor).
/// </summary>
public static class ArchitectUITextQualityFix
{
    const string MenuFontResourcePath = "Fonts & Materials/LiberationSans SDF";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void ApplyOnLoad()
    {
        ApplyToScene();
    }

    // SDF "sharpness": positive values render crisper edges (range -1..1). ~0.4 noticeably
    // sharpens the generated UI text without introducing aliasing.
    const float FontSharpness = 0.4f;
    static readonly int SharpnessId = Shader.PropertyToID("_Sharpness");

    public static void ApplyToScene()
    {
        ApplyFontSharpness();

        var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
            UpgradeCanvas(canvases[i]);

        UpgradeAllText();
    }

    static void ApplyFontSharpness()
    {
        var font = LoadMenuFont();
        if (font == null || font.material == null)
            return;
        if (font.material.HasProperty(SharpnessId))
            font.material.SetFloat(SharpnessId, FontSharpness);
    }

    public static void UpgradeCanvas(Canvas canvas)
    {
        if (canvas == null)
            return;

        canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1
            | AdditionalCanvasShaderChannels.Normal
            | AdditionalCanvasShaderChannels.Tangent;

        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100f;
        }
    }

    public static void UpgradeAllText()
    {
        var labels = Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < labels.Length; i++)
            UpgradeText(labels[i]);
    }

    public static void UpgradeText(TextMeshProUGUI tmp)
    {
        if (tmp == null)
            return;

        var font = LoadMenuFont();
        if (font != null)
            tmp.font = font;

        tmp.enableAutoSizing = false;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.richText = true;
        tmp.raycastTarget = false;
        tmp.fontStyle = FontStyles.Normal;
        tmp.enableKerning = true;

        string name = tmp.gameObject.name;
        if (name == "TitleText")
            tmp.fontSize = Mathf.Max(tmp.fontSize, 64f);
        else if (name == "Subtitle" || name == "AvatarSelectLabel")
            tmp.fontSize = Mathf.Max(tmp.fontSize, 30f);
        else if (name.Contains("Description"))
            tmp.fontSize = Mathf.Max(tmp.fontSize, 24f);
        else if (name == "AvatarStatusText")
            tmp.fontSize = Mathf.Max(tmp.fontSize, 30f);
        else if (name == "Text" && tmp.transform.parent != null && tmp.transform.parent.GetComponent<Button>() != null)
            tmp.fontSize = Mathf.Max(tmp.fontSize, 28f);
        else
            tmp.fontSize = Mathf.Max(tmp.fontSize, 22f);

        var rt = tmp.rectTransform;
        if (name.Contains("Description"))
        {
            // Give descriptions room for two lines and align to the top so they read as a
            // caption sitting under the button (never overlapping it).
            if (rt.sizeDelta.y < 60f)
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, 60f);
            tmp.alignment = TextAlignmentOptions.Top;
        }

        if (tmp.fontSharedMaterial != null && tmp.fontSharedMaterial.HasProperty(SharpnessId))
            tmp.fontSharedMaterial.SetFloat(SharpnessId, FontSharpness);

        tmp.SetAllDirty();
    }

    static TMP_FontAsset LoadMenuFont()
    {
        if (TMP_Settings.defaultFontAsset != null)
            return TMP_Settings.defaultFontAsset;

        return Resources.Load<TMP_FontAsset>(MenuFontResourcePath);
    }
}
