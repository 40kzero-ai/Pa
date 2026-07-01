using UnityEngine;
using UnityEngine.UI;

public sealed class MainMenuController : MonoBehaviour
{
    [SerializeField] Button continueButton;
    [SerializeField] string defaultMapId = "default";
    [SerializeField] string defaultSlotId = SavePaths.DefaultSlotId;

    void Start()
    {
        RefreshContinueButton();
    }

    public void RefreshContinueButton()
    {
        if (continueButton != null)
            continueButton.interactable = SaveManager.GetOrCreate().HasSave(defaultSlotId);
    }

    public void OnNewGame() => GameFlowController.GetOrCreate().StartNewGame(defaultMapId);
    public void OnContinue() => GameFlowController.GetOrCreate().ContinueGame(defaultSlotId);
    public void OnQuit() => Application.Quit();
}
