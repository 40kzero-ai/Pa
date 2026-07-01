using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 게임다운 진입 흐름을 담당하는 런처 UI.
/// Unity UI(Canvas/Button/Text/Image) 오브젝트를 런타임에 구성해서
/// 접속 화면 → 로딩 → 메인 메뉴 → 설정/종료 → 인게임 HUD를 제공한다.
/// </summary>
public class GameShell : MonoBehaviour
{
    enum ShellState { Connect, Loading, MainMenu, Settings, InGame, ExitConfirm }

    [Header("연결 대상")]
    public HexGrid Grid;
    public HexMapEditor Editor;
    public HexCameraController CameraController;

    [Header("게임 표시")]
    public string GameTitle = "Province Architect";
    public string VersionText = "Prototype Build";
    public bool HideMapUntilGameStarts = true;

    [Header("로딩 연출")]
    [Min(0.1f)] public float MinimumLoadingSeconds = 1.2f;
    public string[] LoadingMessages =
    {
        "지형 팔레트 초기화 중...",
        "프로빈스 경계 계산 중...",
        "카메라 위치 보정 중...",
        "게임 세계 접속 완료"
    };

    ShellState state = ShellState.Connect;
    ShellState stateBeforeExit = ShellState.Connect;
    ShellState loadingTarget = ShellState.MainMenu;

    Canvas canvas;
    CanvasScaler scaler;
    RectTransform root;
    GameObject backdrop;
    GameObject connectPanel;
    GameObject loadingPanel;
    GameObject mainMenuPanel;
    GameObject settingsPanel;
    GameObject exitPanel;
    GameObject inGameHud;

    InputField profileInput;
    Text loadingText;
    Image loadingFill;
    Text continueButtonText;
    Button continueButton;
    Toggle fullscreenToggle;
    Toggle editorToggle;
    Slider uiScaleSlider;
    Text uiScaleText;

    string profileName = "Player";
    bool hasSave;
    bool fullscreen = true;
    bool showEditorOnStart = true;
    float loadingProgress;
    Font defaultFont;

    string SavePath => Path.Combine(Application.persistentDataPath, "edited_geometry.json");

    void Awake()
    {
        ResolveReferences();
        SetGameplayActive(!HideMapUntilGameStarts);
        EnsureEventSystem();
        BuildUi();
        RefreshSaveState();
        ShowState(ShellState.Connect);
    }

    void ResolveReferences()
    {
        if (Grid == null) Grid = FindFirstObjectByType<HexGrid>();
        if (Editor == null) Editor = FindFirstObjectByType<HexMapEditor>();
        if (CameraController == null) CameraController = FindFirstObjectByType<HexCameraController>();
    }

    void SetGameplayActive(bool active)
    {
        if (Grid != null) Grid.enabled = active;
        if (Editor != null) Editor.enabled = active && showEditorOnStart;
        if (CameraController != null) CameraController.enabled = active;
    }

    void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;
        GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        DontDestroyOnLoad(eventSystem);
    }

    void BuildUi()
    {
        defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (defaultFont == null) defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

        GameObject canvasObject = new GameObject("Game Shell Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        root = canvasObject.GetComponent<RectTransform>();
        Stretch(root);

        backdrop = CreateBackdrop(root);
        connectPanel = CreateConnectPanel(root);
        loadingPanel = CreateLoadingPanel(root);
        mainMenuPanel = CreateMainMenuPanel(root);
        settingsPanel = CreateSettingsPanel(root);
        exitPanel = CreateExitPanel(root);
        inGameHud = CreateInGameHud(root);
    }

    GameObject CreateBackdrop(RectTransform parent)
    {
        GameObject go = CreateUiObject("Shell Backdrop", parent);
        Stretch(go.GetComponent<RectTransform>());
        Image image = go.AddComponent<Image>();
        image.color = new Color(0.03f, 0.04f, 0.07f, 0.94f);

        Text title = CreateText("Title", go.transform, GameTitle, 56, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.92f, 0.96f, 1f));
        SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -110), new Vector2(900, 82));

        Text version = CreateText("Version", go.transform, VersionText, 18, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(0.65f, 0.72f, 0.82f));
        SetRect(version.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -175), new Vector2(500, 32));
        return go;
    }

    GameObject CreateConnectPanel(RectTransform parent)
    {
        GameObject panel = CreatePanel("Connect Panel", parent, new Vector2(560, 360));
        AddPanelTitle(panel, "접속 화면");
        CreateLabel(panel.transform, "프로필 이름", new Vector2(0, 64), new Vector2(420, 30));
        profileInput = CreateInput(panel.transform, profileName, new Vector2(0, 18), new Vector2(420, 46));
        CreateText("Server Label", panel.transform, "서버: 로컬 샌드박스", 17, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(0.6f, 0.75f, 1f), new Vector2(0, -42), new Vector2(420, 32));
        CreateButton(panel.transform, "접속", new Vector2(0, -105), new Vector2(420, 54), () => BeginLoading(ShellState.MainMenu, "계정 인증 및 세션 생성 중..."));
        CreateButton(panel.transform, "종료", new Vector2(0, -168), new Vector2(420, 42), AskExit);
        return panel;
    }

    GameObject CreateLoadingPanel(RectTransform parent)
    {
        GameObject panel = CreatePanel("Loading Panel", parent, new Vector2(680, 260));
        AddPanelTitle(panel, "로딩");
        loadingText = CreateText("Loading Text", panel.transform, "로딩 중...", 21, FontStyle.Normal, TextAnchor.MiddleCenter, Color.white, new Vector2(0, 30), new Vector2(560, 36));

        GameObject bar = CreateUiObject("Loading Bar", panel.transform);
        SetRect(bar.GetComponent<RectTransform>(), Center(), Center(), new Vector2(0, -35), new Vector2(560, 28));
        Image bg = bar.AddComponent<Image>();
        bg.color = new Color(0.1f, 0.12f, 0.16f, 1f);

        GameObject fill = CreateUiObject("Loading Fill", bar.transform);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0, 0);
        fillRect.anchorMax = new Vector2(0, 1);
        fillRect.pivot = new Vector2(0, 0.5f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        loadingFill = fill.AddComponent<Image>();
        loadingFill.color = new Color(0.25f, 0.65f, 1f, 1f);
        return panel;
    }

    GameObject CreateMainMenuPanel(RectTransform parent)
    {
        GameObject panel = CreatePanel("Main Menu Panel", parent, new Vector2(560, 460));
        AddPanelTitle(panel, "메인 메뉴");
        CreateButton(panel.transform, "새로운 게임", new Vector2(0, 96), new Vector2(420, 58), () => BeginLoading(ShellState.InGame, "새 캠페인 맵 생성 중..."));
        continueButton = CreateButton(panel.transform, "이어하기", new Vector2(0, 25), new Vector2(420, 58), ContinueGame);
        continueButtonText = continueButton.GetComponentInChildren<Text>();
        CreateButton(panel.transform, "설정", new Vector2(0, -46), new Vector2(420, 50), () => ShowState(ShellState.Settings));
        CreateButton(panel.transform, "종료", new Vector2(0, -108), new Vector2(420, 50), AskExit);
        CreateText("Feature Hint", panel.transform, "현재 기능: 헥스 맵 생성 + 인게임 에디터", 16, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(0.62f, 0.68f, 0.76f), new Vector2(0, -184), new Vector2(460, 32));
        return panel;
    }

    GameObject CreateSettingsPanel(RectTransform parent)
    {
        GameObject panel = CreatePanel("Settings Panel", parent, new Vector2(620, 430));
        AddPanelTitle(panel, "설정");
        fullscreenToggle = CreateToggle(panel.transform, "전체 화면 모드", new Vector2(-140, 86), fullscreen);
        editorToggle = CreateToggle(panel.transform, "게임 시작 시 에디터 패널 표시", new Vector2(-140, 36), showEditorOnStart);
        uiScaleText = CreateText("Scale Text", panel.transform, "UI 배율: 1.0", 18, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white, new Vector2(0, -24), new Vector2(420, 30));
        uiScaleSlider = CreateSlider(panel.transform, new Vector2(0, -66), new Vector2(420, 24), 1f, 0.85f, 1.25f);
        CreateText("Save Path", panel.transform, "저장 파일: Application.persistentDataPath/edited_geometry.json", 14, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(0.62f, 0.68f, 0.76f), new Vector2(0, -122), new Vector2(520, 38));
        CreateButton(panel.transform, "적용", new Vector2(-110, -178), new Vector2(200, 46), ApplySettings);
        CreateButton(panel.transform, "메인 메뉴", new Vector2(110, -178), new Vector2(200, 46), () => ShowState(ShellState.MainMenu));
        uiScaleSlider.onValueChanged.AddListener(value =>
        {
            scaler.scaleFactor = value;
            uiScaleText.text = $"UI 배율: {value:0.00}";
        });
        return panel;
    }

    GameObject CreateExitPanel(RectTransform parent)
    {
        GameObject panel = CreatePanel("Exit Panel", parent, new Vector2(480, 240));
        AddPanelTitle(panel, "게임을 종료할까요?");
        CreateButton(panel.transform, "종료", new Vector2(-105, -58), new Vector2(190, 50), Application.Quit);
        CreateButton(panel.transform, "취소", new Vector2(105, -58), new Vector2(190, 50), () => ShowState(stateBeforeExit));
        return panel;
    }

    GameObject CreateInGameHud(RectTransform parent)
    {
        GameObject panel = CreateUiObject("In Game HUD", parent);
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(1, 1);
        rect.anchoredPosition = new Vector2(-24, -24);
        rect.sizeDelta = new Vector2(270, 210);
        Image image = panel.AddComponent<Image>();
        image.color = new Color(0.04f, 0.05f, 0.08f, 0.82f);
        AddPanelTitle(panel, "게임 메뉴", 22, new Vector2(0, 62));
        CreateButton(panel.transform, "에디터 표시/숨김", new Vector2(0, 10), new Vector2(220, 40), ToggleEditor);
        CreateButton(panel.transform, "메인 메뉴", new Vector2(0, -40), new Vector2(220, 40), ReturnToMenu);
        CreateButton(panel.transform, "종료", new Vector2(0, -90), new Vector2(220, 40), AskExit);
        return panel;
    }

    void ShowState(ShellState next)
    {
        state = next;
        bool menuVisible = next != ShellState.InGame;
        backdrop.SetActive(menuVisible);
        connectPanel.SetActive(next == ShellState.Connect);
        loadingPanel.SetActive(next == ShellState.Loading);
        mainMenuPanel.SetActive(next == ShellState.MainMenu);
        settingsPanel.SetActive(next == ShellState.Settings);
        exitPanel.SetActive(next == ShellState.ExitConfirm);
        inGameHud.SetActive(next == ShellState.InGame);

        if (next == ShellState.MainMenu) RefreshSaveState();
        if (next == ShellState.InGame) canvas.sortingOrder = 20;
        else canvas.sortingOrder = 100;
    }

    void RefreshSaveState()
    {
        hasSave = File.Exists(SavePath);
        if (continueButton != null) continueButton.interactable = hasSave;
        if (continueButtonText != null) continueButtonText.text = hasSave ? "이어하기" : "이어하기 (저장 없음)";
    }

    void BeginLoading(ShellState target, string firstMessage)
    {
        profileName = profileInput != null ? profileInput.text : profileName;
        loadingTarget = target;
        loadingProgress = 0f;
        SetLoadingVisual(firstMessage, 0f);
        ShowState(ShellState.Loading);
        StopAllCoroutines();
        StartCoroutine(LoadingRoutine());
    }

    IEnumerator LoadingRoutine()
    {
        float elapsed = 0f;
        while (elapsed < MinimumLoadingSeconds)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / MinimumLoadingSeconds);
            string message = LoadingMessages != null && LoadingMessages.Length > 0
                ? LoadingMessages[Mathf.Min(LoadingMessages.Length - 1, Mathf.FloorToInt(progress * LoadingMessages.Length))]
                : "로딩 중...";
            SetLoadingVisual(message, progress);
            yield return null;
        }

        if (loadingTarget == ShellState.InGame) SetGameplayActive(true);
        ShowState(loadingTarget);
    }

    void SetLoadingVisual(string message, float progress)
    {
        loadingProgress = Mathf.Clamp01(progress);
        if (loadingText != null) loadingText.text = message;
        if (loadingFill != null)
        {
            RectTransform rect = loadingFill.rectTransform;
            rect.anchorMax = new Vector2(loadingProgress, 1f);
        }
    }

    void ContinueGame()
    {
        if (Grid == null || !File.Exists(SavePath)) return;
        SetGameplayActive(true);
        if (Grid.LoadFromFile(SavePath)) BeginLoading(ShellState.InGame, "저장된 캠페인 복원 중...");
        else SetGameplayActive(false);
    }

    void ApplySettings()
    {
        fullscreen = fullscreenToggle == null || fullscreenToggle.isOn;
        showEditorOnStart = editorToggle == null || editorToggle.isOn;
        Screen.fullScreen = fullscreen;
        if (Editor != null && state == ShellState.InGame) Editor.enabled = showEditorOnStart;
    }

    void ToggleEditor()
    {
        if (Editor != null) Editor.enabled = !Editor.enabled;
    }

    void ReturnToMenu()
    {
        SetGameplayActive(false);
        RefreshSaveState();
        ShowState(ShellState.MainMenu);
    }

    void AskExit()
    {
        stateBeforeExit = state;
        ShowState(ShellState.ExitConfirm);
    }

    GameObject CreateUiObject(string objectName, Transform parent)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    GameObject CreatePanel(string objectName, RectTransform parent, Vector2 size)
    {
        GameObject panel = CreateUiObject(objectName, parent);
        SetRect(panel.GetComponent<RectTransform>(), Center(), Center(), Vector2.zero, size);
        Image image = panel.AddComponent<Image>();
        image.color = new Color(0.055f, 0.07f, 0.105f, 0.96f);
        return panel;
    }

    void AddPanelTitle(GameObject panel, string text, int size = 26, Vector2? offset = null)
    {
        CreateText("Panel Title", panel.transform, text, size, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.94f, 0.97f, 1f), offset ?? new Vector2(0, 132), new Vector2(460, 42));
    }

    Text CreateLabel(Transform parent, string text, Vector2 pos, Vector2 size) =>
        CreateText("Label", parent, text, 17, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.85f, 0.9f, 0.96f), pos, size);

    Text CreateText(string objectName, Transform parent, string text, int size, FontStyle style, TextAnchor alignment, Color color)
    {
        Text label = CreateUiObject(objectName, parent).AddComponent<Text>();
        label.font = defaultFont;
        label.text = text;
        label.fontSize = size;
        label.fontStyle = style;
        label.alignment = alignment;
        label.color = color;
        return label;
    }

    Text CreateText(string objectName, Transform parent, string text, int size, FontStyle style, TextAnchor alignment, Color color, Vector2 pos, Vector2 sizeDelta)
    {
        Text label = CreateText(objectName, parent, text, size, style, alignment, color);
        SetRect(label.rectTransform, Center(), Center(), pos, sizeDelta);
        return label;
    }

    InputField CreateInput(Transform parent, string text, Vector2 pos, Vector2 size)
    {
        GameObject go = CreateUiObject("Profile Input", parent);
        SetRect(go.GetComponent<RectTransform>(), Center(), Center(), pos, size);
        Image image = go.AddComponent<Image>();
        image.color = new Color(0.09f, 0.11f, 0.16f, 1f);
        InputField input = go.AddComponent<InputField>();
        Text value = CreateText("Text", go.transform, text, 18, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white);
        Stretch(value.rectTransform, new Vector2(16, 6), new Vector2(-16, -6));
        Text placeholder = CreateText("Placeholder", go.transform, "이름 입력", 18, FontStyle.Italic, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.35f));
        Stretch(placeholder.rectTransform, new Vector2(16, 6), new Vector2(-16, -6));
        input.textComponent = value;
        input.placeholder = placeholder;
        input.text = text;
        return input;
    }

    Button CreateButton(Transform parent, string text, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction action)
    {
        GameObject go = CreateUiObject(text + " Button", parent);
        SetRect(go.GetComponent<RectTransform>(), Center(), Center(), pos, size);
        Image image = go.AddComponent<Image>();
        image.color = new Color(0.14f, 0.26f, 0.42f, 1f);
        Button button = go.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.14f, 0.26f, 0.42f, 1f);
        colors.highlightedColor = new Color(0.2f, 0.38f, 0.6f, 1f);
        colors.pressedColor = new Color(0.08f, 0.16f, 0.28f, 1f);
        colors.disabledColor = new Color(0.12f, 0.12f, 0.14f, 0.5f);
        button.colors = colors;
        button.onClick.AddListener(action);
        CreateText("Text", go.transform, text, 20, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white, Vector2.zero, size);
        return button;
    }

    Toggle CreateToggle(Transform parent, string text, Vector2 pos, bool on)
    {
        GameObject go = CreateUiObject(text + " Toggle", parent);
        SetRect(go.GetComponent<RectTransform>(), Center(), Center(), pos, new Vector2(420, 34));
        Toggle toggle = go.AddComponent<Toggle>();
        Image box = CreateUiObject("Box", go.transform).AddComponent<Image>();
        SetRect(box.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(26, 26));
        box.color = new Color(0.11f, 0.14f, 0.2f, 1f);
        Image check = CreateUiObject("Checkmark", box.transform).AddComponent<Image>();
        Stretch(check.rectTransform, new Vector2(5, 5), new Vector2(-5, -5));
        check.color = new Color(0.25f, 0.7f, 1f, 1f);
        toggle.targetGraphic = box;
        toggle.graphic = check;
        toggle.isOn = on;
        CreateText("Label", go.transform, text, 18, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white, new Vector2(58, 0), new Vector2(340, 32));
        return toggle;
    }

    Slider CreateSlider(Transform parent, Vector2 pos, Vector2 size, float value, float min, float max)
    {
        GameObject go = CreateUiObject("UI Scale Slider", parent);
        SetRect(go.GetComponent<RectTransform>(), Center(), Center(), pos, size);
        Slider slider = go.AddComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = value;
        Image bg = CreateUiObject("Background", go.transform).AddComponent<Image>();
        Stretch(bg.rectTransform);
        bg.color = new Color(0.1f, 0.12f, 0.16f, 1f);
        RectTransform fillArea = CreateUiObject("Fill Area", go.transform).GetComponent<RectTransform>();
        Stretch(fillArea, new Vector2(8, 0), new Vector2(-8, 0));
        Image fill = CreateUiObject("Fill", fillArea).AddComponent<Image>();
        Stretch(fill.rectTransform);
        fill.color = new Color(0.25f, 0.65f, 1f, 1f);
        Image handle = CreateUiObject("Handle", go.transform).AddComponent<Image>();
        SetRect(handle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(28, 34));
        handle.color = Color.white;
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        return slider;
    }

    static Vector2 Center() => new Vector2(0.5f, 0.5f);

    static void Stretch(RectTransform rect) => Stretch(rect, Vector2.zero, Vector2.zero);

    static void Stretch(RectTransform rect, Vector2 minOffset, Vector2 maxOffset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = minOffset;
        rect.offsetMax = maxOffset;
    }

    static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = Center();
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
    }
}
