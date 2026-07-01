using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Runtime uGUI bridge for the current single-scene project stage.
/// It creates a real Canvas-based main menu, loading overlay, and in-game HUD at runtime,
/// so the scene has visible UI without requiring prebuilt Canvas assets.
/// </summary>
public sealed class GameRuntimeController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] HexGrid grid;
    [SerializeField] HexMapEditor editor;
    [SerializeField] HexCameraController cameraController;
    [SerializeField] TextAsset defaultGeometryJson;

    [Header("Save")]
    [SerializeField] string slotId = SavePaths.DefaultSlotId;

    [Header("Startup")]
    [SerializeField] bool showMainMenuOnStart = true;
    [SerializeField] bool enableEditorAfterLoad = false;

    Canvas canvas;
    GameObject menuPanel;
    GameObject hudPanel;
    GameObject loadingPanel;
    Text menuStatusText;
    Text hudStatusText;
    Text editorToggleText;
    Button continueButton;
    bool loading;
    string status = "";
    Font uiFont;

    void Awake()
    {
        ResolveReferences();
        EnsureEventSystem();
        BuildRuntimeCanvas();

        if (editor != null && showMainMenuOnStart)
            editor.enabled = false;

        SetMenuVisible(showMainMenuOnStart);
        SetHudVisible(!showMainMenuOnStart);
        SetLoadingVisible(false);
        RefreshStatus();
    }

    void ResolveReferences()
    {
        if (grid == null) grid = FindFirstObjectByType<HexGrid>();
        if (editor == null) editor = FindFirstObjectByType<HexMapEditor>();
        if (cameraController == null) cameraController = FindFirstObjectByType<HexCameraController>();
        if (grid != null && cameraController != null) grid.CameraController = cameraController;
        if (defaultGeometryJson == null && grid != null) defaultGeometryJson = grid.GeometryJson;
    }

    void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;

        var eventSystemGo = new GameObject("EventSystem");
        eventSystemGo.AddComponent<EventSystem>();
        eventSystemGo.AddComponent<InputSystemUIInputModule>();
    }

    void BuildRuntimeCanvas()
    {
        uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (uiFont == null) uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

        var canvasGo = new GameObject("RuntimeUICanvas");
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGo.AddComponent<GraphicRaycaster>();

        menuPanel = CreateMainMenu(canvasGo.transform);
        hudPanel = CreateHud(canvasGo.transform);
        loadingPanel = CreateLoadingOverlay(canvasGo.transform);
    }

    GameObject CreateMainMenu(Transform parent)
    {
        GameObject overlay = CreateFullScreenPanel("MainMenuCanvas", parent, new Color(0f, 0f, 0f, 0.72f));

        GameObject panel = CreatePanel("MainMenuPanel", overlay.transform, new Vector2(420f, 360f), new Color(0.08f, 0.09f, 0.11f, 0.94f));
        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(28, 28, 26, 26);
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        CreateText("TitleText", panel.transform, "Province Architect", 34, FontStyle.Bold, TextAnchor.MiddleCenter, 52f);
        CreateText("SubtitleText", panel.transform, "Map editor prototype", 15, FontStyle.Normal, TextAnchor.MiddleCenter, 26f);
        CreateButton("NewGameButton", panel.transform, "새 게임", StartNewGame, 44f);
        continueButton = CreateButton("ContinueButton", panel.transform, "이어하기", ContinueGame, 44f);
        CreateButton("QuitButton", panel.transform, "종료", Application.Quit, 36f);
        menuStatusText = CreateText("StatusText", panel.transform, "", 14, FontStyle.Normal, TextAnchor.MiddleCenter, 34f);

        return overlay;
    }

    GameObject CreateHud(Transform parent)
    {
        GameObject panel = CreatePanel("InGameHudCanvas", parent, new Vector2(240f, 280f), new Color(0.06f, 0.07f, 0.08f, 0.88f));
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-12f, -12f);

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        CreateText("HudTitleText", panel.transform, "게임 HUD", 17, FontStyle.Bold, TextAnchor.MiddleCenter, 28f);
        CreateButton("SaveButton", panel.transform, "저장", SaveGame, 30f);
        CreateButton("LoadButton", panel.transform, "불러오기", ContinueGame, 30f);
        CreateButton("ExportProvincePngButton", panel.transform, "프로빈스 PNG 저장", ExportProvincePng, 30f);
        CreateButton("ImportProvincePngButton", panel.transform, "프로빈스 PNG 불러오기", ImportProvincePng, 30f);
        Button editorButton = CreateButton("ToggleEditorButton", panel.transform, "에디터 숨기기", ToggleEditor, 30f);
        editorToggleText = editorButton.GetComponentInChildren<Text>();
        CreateButton("ReturnToMenuButton", panel.transform, "메인 메뉴", ReturnToMenu, 30f);
        hudStatusText = CreateText("HudStatusText", panel.transform, "", 13, FontStyle.Normal, TextAnchor.MiddleCenter, 32f);

        return panel;
    }

    GameObject CreateLoadingOverlay(Transform parent)
    {
        GameObject overlay = CreateFullScreenPanel("LoadingOverlayCanvas", parent, new Color(0f, 0f, 0f, 0.62f));
        CreateText("LoadingText", overlay.transform, "로딩 중...", 32, FontStyle.Bold, TextAnchor.MiddleCenter, 80f);
        return overlay;
    }

    GameObject CreateFullScreenPanel(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        Image image = go.AddComponent<Image>();
        image.color = color;
        return go;
    }

    GameObject CreatePanel(string name, Transform parent, Vector2 size, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        Image image = go.AddComponent<Image>();
        image.color = color;
        return go;
    }

    Text CreateText(string name, Transform parent, string text, int fontSize, FontStyle fontStyle, TextAnchor alignment, float height)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(0f, height);
        var layout = go.AddComponent<LayoutElement>();
        layout.preferredHeight = height;

        Text label = go.AddComponent<Text>();
        label.text = text;
        label.font = uiFont;
        label.fontSize = fontSize;
        label.fontStyle = fontStyle;
        label.alignment = alignment;
        label.color = Color.white;
        return label;
    }

    Button CreateButton(string name, Transform parent, string label, UnityEngine.Events.UnityAction action, float height)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(0f, height);
        var layout = go.AddComponent<LayoutElement>();
        layout.preferredHeight = height;

        Image image = go.AddComponent<Image>();
        image.color = new Color(0.22f, 0.25f, 0.29f, 1f);
        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);

        Text text = CreateText("Text", go.transform, label, 15, FontStyle.Bold, TextAnchor.MiddleCenter, height);
        RectTransform textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return button;
    }

    void SetMenuVisible(bool visible)
    {
        if (menuPanel != null) menuPanel.SetActive(visible);
        if (visible) RefreshContinueButton();
    }

    void SetHudVisible(bool visible)
    {
        if (hudPanel != null) hudPanel.SetActive(visible);
        RefreshEditorToggleText();
    }

    void SetLoadingVisible(bool visible)
    {
        loading = visible;
        if (loadingPanel != null) loadingPanel.SetActive(visible);
    }

    void RefreshContinueButton()
    {
        if (continueButton != null)
            continueButton.interactable = !loading && SaveManager.GetOrCreate().HasSave(slotId);
    }

    void RefreshEditorToggleText()
    {
        if (editorToggleText != null)
            editorToggleText.text = editor != null && editor.enabled ? "에디터 숨기기" : "에디터 보이기";
    }

    void RefreshStatus()
    {
        if (menuStatusText != null) menuStatusText.text = status;
        if (hudStatusText != null) hudStatusText.text = status;
        RefreshContinueButton();
        RefreshEditorToggleText();
    }

    void StartNewGame()
    {
        ResolveReferences();
        if (defaultGeometryJson == null)
        {
            status = "기본 geometry.json이 연결되지 않았습니다.";
            RefreshStatus();
            return;
        }

        StartCoroutine(LoadMap(HexGeometryLoader.Load(defaultGeometryJson.text), "새 게임 시작"));
    }

    void ContinueGame()
    {
        ResolveReferences();
        SaveManager saveManager = SaveManager.GetOrCreate();
        if (!saveManager.HasSave(slotId))
        {
            status = "저장 파일이 없습니다.";
            RefreshStatus();
            return;
        }

        StartCoroutine(LoadMap(saveManager.LoadGeometry(slotId), "저장 파일 불러옴"));
    }

    IEnumerator LoadMap(GeometryData data, string doneStatus)
    {
        if (grid == null || data == null) yield break;

        SetLoadingVisible(true);
        SetMenuVisible(false);
        SetHudVisible(false);
        if (editor != null) editor.enabled = false;

        bool completed = false;
        void OnBuildCompleted() => completed = true;
        grid.BuildCompleted += OnBuildCompleted;
        grid.Build(data);
        while (grid.IsBuilding && !completed) yield return null;
        grid.BuildCompleted -= OnBuildCompleted;

        status = doneStatus;
        SetLoadingVisible(false);
        SetHudVisible(true);
        if (editor != null) editor.enabled = enableEditorAfterLoad;
        RefreshStatus();
    }

    void SaveGame()
    {
        if (grid == null) return;
        SaveManager.GetOrCreate().SaveGeometry(slotId, grid.ExportData());
        status = "저장 완료";
        RefreshStatus();
    }

    void ExportProvincePng()
    {
        if (grid == null) return;
        grid.SaveProvincePNG(SaveManager.GetOrCreate().GetProvincePngPath(slotId));
        status = "프로빈스 PNG 저장 완료";
        RefreshStatus();
    }

    void ImportProvincePng()
    {
        if (grid == null) return;
        status = grid.LoadProvincePNG(SaveManager.GetOrCreate().GetProvincePngPath(slotId))
            ? "프로빈스 PNG 불러옴"
            : "프로빈스 PNG 없음/실패";
        RefreshStatus();
    }

    void ToggleEditor()
    {
        if (editor == null) return;
        editor.enabled = !editor.enabled;
        RefreshStatus();
    }

    void ReturnToMenu()
    {
        SetLoadingVisible(false);
        SetHudVisible(false);
        SetMenuVisible(true);
        if (editor != null) editor.enabled = false;
        RefreshStatus();
    }
}
