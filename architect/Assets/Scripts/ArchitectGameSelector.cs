using UnityEngine;
using UnityEngine.UI;

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
        Ocr
    }

    [Header("Game managers")]
    public DodgeGameManager dodgeGame;
    public SingleLegBalanceManager balanceGame;
    public LeanBalanceGameManager leanBalanceGame;
    public CoinMineGameManager coinMineGame;
    public PoseTestMode poseTest;
    public OcrMode ocrMode;

    [Header("Mode selection UI")]
    public GameObject modeSelectPanel;
    public Button poseDodgeButton;
    public Button singleLegBalanceButton;
    public Button leanBalanceButton;
    public Button coinMineButton;
    public Button poseTestButton;
    public Button ocrButton;
    public Button backToMenuButton;

    [Header("Game UI panels")]
    public GameObject dodgeUIPanel;
    public GameObject balanceUIPanel;
    public GameObject leanBalanceUIPanel;
    public GameObject coinMineUIPanel;
    public GameObject poseTestUIPanel;
    public GameObject ocrUIPanel;

    public Mode CurrentMode { get; private set; }

    void Start()
    {
        if (dodgeGame == null) dodgeGame = FindFirstObjectByType<DodgeGameManager>();
        if (balanceGame == null) balanceGame = FindFirstObjectByType<SingleLegBalanceManager>();
        if (leanBalanceGame == null) leanBalanceGame = FindFirstObjectByType<LeanBalanceGameManager>();
        if (coinMineGame == null) coinMineGame = FindFirstObjectByType<CoinMineGameManager>();
        if (poseTest == null) poseTest = FindFirstObjectByType<PoseTestMode>();
        if (ocrMode == null) ocrMode = FindFirstObjectByType<OcrMode>();

        if (poseDodgeButton != null) poseDodgeButton.onClick.AddListener(SelectPoseDodge);
        if (singleLegBalanceButton != null) singleLegBalanceButton.onClick.AddListener(SelectSingleLegBalance);
        if (leanBalanceButton != null) leanBalanceButton.onClick.AddListener(SelectLeanBalance);
        if (coinMineButton != null) coinMineButton.onClick.AddListener(SelectCoinMine);
        if (poseTestButton != null) poseTestButton.onClick.AddListener(SelectPoseTest);
        if (ocrButton != null) ocrButton.onClick.AddListener(SelectOcr);
        if (backToMenuButton != null) backToMenuButton.onClick.AddListener(BackToMenu);

        ShowModeSelect();
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

    public void BackToMenu()
    {
        CurrentMode = Mode.None;
        HideAllGameUI();
        DisableAllGames();
        if (modeSelectPanel != null) modeSelectPanel.SetActive(true);
    }

    void HideAllGameUI()
    {
        if (dodgeUIPanel != null) dodgeUIPanel.SetActive(false);
        if (balanceUIPanel != null) balanceUIPanel.SetActive(false);
        if (leanBalanceUIPanel != null) leanBalanceUIPanel.SetActive(false);
        if (coinMineUIPanel != null) coinMineUIPanel.SetActive(false);
        if (poseTestUIPanel != null) poseTestUIPanel.SetActive(false);
        if (ocrUIPanel != null) ocrUIPanel.SetActive(false);
    }

    void DisableAllGames()
    {
        if (dodgeGame != null) { dodgeGame.StopGame(); dodgeGame.gameObject.SetActive(false); }
        if (balanceGame != null) { balanceGame.StopGame(); balanceGame.gameObject.SetActive(false); }
        if (leanBalanceGame != null) { leanBalanceGame.StopGame(); leanBalanceGame.gameObject.SetActive(false); }
        if (coinMineGame != null) { coinMineGame.StopGame(); coinMineGame.gameObject.SetActive(false); }
        if (poseTest != null) { poseTest.Deactivate(); poseTest.gameObject.SetActive(false); }
        if (ocrMode != null) { ocrMode.Deactivate(); ocrMode.gameObject.SetActive(false); }
    }
}
