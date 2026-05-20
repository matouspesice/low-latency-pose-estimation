using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Select between Pose Dodge, Single-Leg Balance, and Pose Test.
/// Shows mode menu and enables the chosen game/mode.
/// </summary>
public class ArchitectGameSelector : MonoBehaviour
{
    public enum Mode
    {
        None,
        PoseDodge,
        SingleLegBalance,
        LeanBalance,
        CoinMine,
        PoseTest,
        Ocr,
        Clock
    }

    [Header("Game managers")]
    public DodgeGameManager dodgeGame;
    public SingleLegBalanceManager balanceGame;
    public LeanBalanceGameManager leanBalanceGame;
    public CoinMineGameManager coinMineGame;
    public PoseTestMode poseTest;
    public OcrMode ocrMode;
    public ClockMode clockMode;

    [Header("Mode selection UI")]
    public GameObject modeSelectPanel;
    public Button poseDodgeButton;
    public Button singleLegBalanceButton;
    public Button leanBalanceButton;
    public Button coinMineButton;
    public Button poseTestButton;
    public Button ocrButton;
    public Button clockButton;
    public Button backToMenuButton;

    [Header("Game UI panels")]
    public GameObject dodgeUIPanel;
    public GameObject balanceUIPanel;
    public GameObject leanBalanceUIPanel;
    public GameObject coinMineUIPanel;
    public GameObject poseTestUIPanel;
    public GameObject ocrUIPanel;
    public GameObject clockUIPanel;

    public Mode CurrentMode { get; private set; }

    void Start()
    {
        if (dodgeGame == null) dodgeGame = FindFirstObjectByType<DodgeGameManager>();
        if (balanceGame == null) balanceGame = FindFirstObjectByType<SingleLegBalanceManager>();
        if (leanBalanceGame == null) leanBalanceGame = FindFirstObjectByType<LeanBalanceGameManager>();
        if (coinMineGame == null) coinMineGame = FindFirstObjectByType<CoinMineGameManager>();
        if (poseTest == null) poseTest = FindFirstObjectByType<PoseTestMode>();
        if (ocrMode == null) ocrMode = FindFirstObjectByType<OcrMode>();
        if (clockMode == null) clockMode = FindFirstObjectByType<ClockMode>();

        // Find UI panels by name if inspector references are missing (e.g. scene was saved
        // with stale references). This guarantees a clean start screen on Play.
        if (modeSelectPanel == null) modeSelectPanel = FindInactiveGameObjectByName("ModeSelectPanel");
        if (dodgeUIPanel == null) dodgeUIPanel = FindInactiveGameObjectByName("DodgeUIPanel");
        if (balanceUIPanel == null) balanceUIPanel = FindInactiveGameObjectByName("BalanceUIPanel");
        if (leanBalanceUIPanel == null) leanBalanceUIPanel = FindInactiveGameObjectByName("LeanBalanceUIPanel");
        if (coinMineUIPanel == null) coinMineUIPanel = FindInactiveGameObjectByName("CoinMineUIPanel");
        if (poseTestUIPanel == null) poseTestUIPanel = FindInactiveGameObjectByName("PoseTestUIPanel");
        if (ocrUIPanel == null) ocrUIPanel = FindInactiveGameObjectByName("OcrUIPanel");
        if (clockUIPanel == null) clockUIPanel = FindInactiveGameObjectByName("ClockUIPanel");

        if (poseDodgeButton != null) poseDodgeButton.onClick.AddListener(SelectPoseDodge);
        if (singleLegBalanceButton != null) singleLegBalanceButton.onClick.AddListener(SelectSingleLegBalance);
        if (leanBalanceButton != null) leanBalanceButton.onClick.AddListener(SelectLeanBalance);
        if (coinMineButton != null) coinMineButton.onClick.AddListener(SelectCoinMine);
        if (poseTestButton != null) poseTestButton.onClick.AddListener(SelectPoseTest);
        if (ocrButton != null) ocrButton.onClick.AddListener(SelectOcr);
        if (clockButton != null) clockButton.onClick.AddListener(SelectClock);
        if (backToMenuButton != null) backToMenuButton.onClick.AddListener(BackToMenu);

        HideDisabledModesUi();
        ShowModeSelect();
        if (modeSelectPanel != null) modeSelectPanel.transform.SetAsLastSibling();
    }

    /// <summary>Hides modes that are not offered in the menu (OCR, legacy balance/dodge games).</summary>
    void HideDisabledModesUi()
    {
        if (ocrButton != null) ocrButton.gameObject.SetActive(false);
        if (ocrUIPanel != null) ocrUIPanel.SetActive(false);
        if (ocrMode != null) ocrMode.gameObject.SetActive(false);

        if (poseDodgeButton != null) poseDodgeButton.gameObject.SetActive(false);
        if (dodgeUIPanel != null) dodgeUIPanel.SetActive(false);
        if (dodgeGame != null) dodgeGame.gameObject.SetActive(false);

        if (singleLegBalanceButton != null) singleLegBalanceButton.gameObject.SetActive(false);
        if (balanceUIPanel != null) balanceUIPanel.SetActive(false);
        if (balanceGame != null) balanceGame.gameObject.SetActive(false);

        if (leanBalanceButton != null) leanBalanceButton.gameObject.SetActive(false);
        if (leanBalanceUIPanel != null) leanBalanceUIPanel.SetActive(false);
        if (leanBalanceGame != null) leanBalanceGame.gameObject.SetActive(false);

        HideMenuEntryByName("OcrButton", "OcrDescription", "OcrUIPanel");
        HideMenuEntryByName("PoseDodgeButton", "PoseDodgeDescription", "DodgeUIPanel");
        HideMenuEntryByName("SingleLegBalanceButton", "SingleLegBalanceDescription", "BalanceUIPanel");
        HideMenuEntryByName("LeanBalanceButton", "LeanBalanceDescription", "LeanBalanceUIPanel");
    }

    static void HideMenuEntryByName(string buttonName, string descriptionName, string panelName)
    {
        var btn = FindInactiveGameObjectByName(buttonName);
        if (btn != null) btn.SetActive(false);
        var desc = FindInactiveGameObjectByName(descriptionName);
        if (desc != null) desc.SetActive(false);
        var panel = FindInactiveGameObjectByName(panelName);
        if (panel != null) panel.SetActive(false);
    }

    static GameObject FindInactiveGameObjectByName(string name)
    {
        var all = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int i = 0; i < all.Length; i++)
        {
            var go = all[i];
            if (go == null) continue;
            if (go.name != name) continue;
            if (go.hideFlags != HideFlags.None) continue;
            if (!go.scene.IsValid()) continue;
            return go;
        }
        return null;
    }

    void ShowModeSelect()
    {
        CurrentMode = Mode.None;
        if (modeSelectPanel != null) modeSelectPanel.SetActive(true);
        HideAllGameUI();
        DisableAllGames();
    }

    public void SelectPoseDodge()
    {
        CurrentMode = Mode.PoseDodge;
        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);
        HideAllGameUI();
        DisableAllGames();
        if (dodgeUIPanel != null) dodgeUIPanel.SetActive(true);
        if (dodgeGame != null)
        {
            dodgeGame.gameObject.SetActive(true);
            dodgeGame.StopGame();
        }
    }

    public void SelectSingleLegBalance()
    {
        CurrentMode = Mode.SingleLegBalance;
        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);
        HideAllGameUI();
        DisableAllGames();
        if (balanceUIPanel != null) balanceUIPanel.SetActive(true);
        if (balanceGame != null)
        {
            balanceGame.gameObject.SetActive(true);
            balanceGame.StopGame();
        }
    }

    public void SelectLeanBalance()
    {
        CurrentMode = Mode.LeanBalance;
        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);
        HideAllGameUI();
        DisableAllGames();
        if (leanBalanceUIPanel != null) leanBalanceUIPanel.SetActive(true);
        if (leanBalanceGame != null)
        {
            leanBalanceGame.gameObject.SetActive(true);
            leanBalanceGame.StopGame();
        }
    }

    public void SelectCoinMine()
    {
        CurrentMode = Mode.CoinMine;
        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);
        HideAllGameUI();
        DisableAllGames();
        if (coinMineUIPanel != null) coinMineUIPanel.SetActive(true);
        if (coinMineGame != null)
        {
            coinMineGame.gameObject.SetActive(true);
            coinMineGame.StopGame();
        }
    }

    public void SelectPoseTest()
    {
        CurrentMode = Mode.PoseTest;
        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);
        HideAllGameUI();
        DisableAllGames();
        if (poseTestUIPanel != null) poseTestUIPanel.SetActive(true);
        if (poseTest != null)
        {
            poseTest.gameObject.SetActive(true);
            poseTest.Activate();
        }
    }

    public void SelectOcr()
    {
        CurrentMode = Mode.Ocr;
        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);
        HideAllGameUI();
        DisableAllGames();
        if (ocrUIPanel != null) ocrUIPanel.SetActive(true);
        if (ocrMode != null)
        {
            ocrMode.gameObject.SetActive(true);
            ocrMode.Activate();
        }
    }

    public void SelectClock()
    {
        CurrentMode = Mode.Clock;
        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);
        HideAllGameUI();
        DisableAllGames();
        if (clockUIPanel != null) clockUIPanel.SetActive(true);
        if (clockMode != null)
        {
            clockMode.gameObject.SetActive(true);
            clockMode.Activate();
        }
    }

    public void BackToMenu()
    {
        CurrentMode = Mode.None;
        HideAllGameUI();
        DisableAllGames();
        if (modeSelectPanel != null) modeSelectPanel.SetActive(true);
    }

    static readonly string[] AllGamePanelNames =
    {
        "DodgeUIPanel",
        "BalanceUIPanel",
        "LeanBalanceUIPanel",
        "CoinMineUIPanel",
        "PoseTestUIPanel",
        "OcrUIPanel",
        "ClockUIPanel",
    };

    void HideAllGameUI()
    {
        // Hide via inspector references first
        if (dodgeUIPanel != null) dodgeUIPanel.SetActive(false);
        if (balanceUIPanel != null) balanceUIPanel.SetActive(false);
        if (leanBalanceUIPanel != null) leanBalanceUIPanel.SetActive(false);
        if (coinMineUIPanel != null) coinMineUIPanel.SetActive(false);
        if (poseTestUIPanel != null) poseTestUIPanel.SetActive(false);
        if (ocrUIPanel != null) ocrUIPanel.SetActive(false);
        if (clockUIPanel != null) clockUIPanel.SetActive(false);

        // Also scan the scene for any panel GameObject by name and hide it, in case
        // a stale copy from an earlier build still exists without an inspector reference.
        var all = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int i = 0; i < all.Length; i++)
        {
            var go = all[i];
            if (go == null) continue;
            if (go.hideFlags != HideFlags.None) continue;
            if (!go.scene.IsValid()) continue;
            for (int n = 0; n < AllGamePanelNames.Length; n++)
            {
                if (go.name == AllGamePanelNames[n]) { go.SetActive(false); break; }
            }
        }
    }

    void DisableAllGames()
    {
        if (dodgeGame != null) { dodgeGame.StopGame(); dodgeGame.gameObject.SetActive(false); }
        if (balanceGame != null) { balanceGame.StopGame(); balanceGame.gameObject.SetActive(false); }
        if (leanBalanceGame != null) { leanBalanceGame.StopGame(); leanBalanceGame.gameObject.SetActive(false); }
        if (coinMineGame != null) { coinMineGame.StopGame(); coinMineGame.gameObject.SetActive(false); }
        if (poseTest != null) { poseTest.Deactivate(); poseTest.gameObject.SetActive(false); }
        if (ocrMode != null) { ocrMode.Deactivate(); ocrMode.gameObject.SetActive(false); }
        if (clockMode != null) { clockMode.Deactivate(); clockMode.gameObject.SetActive(false); }
    }

    public void ReturnToStartMenuScene()
    {
        SceneManager.LoadScene(0);
    }
}
