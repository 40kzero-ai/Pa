using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class GameFlowController : MonoBehaviour
{
    public static GameFlowController Instance { get; private set; }

    GameStartRequest pendingRequest;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public static GameFlowController GetOrCreate()
    {
        if (Instance != null) return Instance;
        var go = new GameObject(nameof(GameFlowController));
        return go.AddComponent<GameFlowController>();
    }

    public void StartNewGame(string mapId)
    {
        pendingRequest = new GameStartRequest
        {
            Mode = GameStartMode.NewGame,
            MapId = mapId,
            SlotId = SavePaths.DefaultSlotId
        };
        SceneManager.LoadScene(SceneNames.Game);
    }

    public void ContinueGame(string slotId)
    {
        pendingRequest = new GameStartRequest
        {
            Mode = GameStartMode.Continue,
            SlotId = slotId
        };
        SceneManager.LoadScene(SceneNames.Game);
    }

    public GameStartRequest ConsumeStartRequest()
    {
        var request = pendingRequest;
        pendingRequest = null;
        return request;
    }

    public void ReturnToMainMenu() => SceneManager.LoadScene(SceneNames.MainMenu);
}
