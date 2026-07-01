using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Runtime shell for the grand-strategy prototype: boot loading, main menu,
/// scenario selection, settings, game HUD, and editor-mode entry.
/// </summary>
[DisallowMultipleComponent]
public class HexGameFlow : MonoBehaviour
{
    public enum FlowSceneRole { Auto, Boot, Loading, MainMenu, ScenarioSelect, Game, Editor }

    public HexMapEditor Editor;
    public HexGrid Grid;
    public FlowSceneRole SceneRole = FlowSceneRole.Auto;
    public string BootSceneName = "BootScene";
    public string LoadingSceneName = "LoadingScene";
    public string MainMenuSceneName = "MainMenuScene";
    public string ScenarioSelectSceneName = "ScenarioSelectScene";
    public string GameSceneName = "GameScene";
    public string EditorSceneName = "EditorScene";
    public float LoadingSeconds = 0.85f;
    public Vector2 ReferenceResolution = new Vector2(1920, 1080);

    [Header("Style")]
    public Font Font;
    public Color BackgroundColor = new Color(0.035f, 0.045f, 0.052f, 0.98f);
    public Color PanelColor = new Color(0.08f, 0.095f, 0.11f, 0.94f);
    public Color SurfaceColor = new Color(0.12f, 0.14f, 0.16f, 0.96f);
    public Color AccentColor = new Color(0.20f, 0.58f, 0.88f, 1f);
    public Color TextColor = new Color(0.92f, 0.95f, 0.96f, 1f);
    public Color MutedTextColor = new Color(0.58f, 0.65f, 0.69f, 1f);

    struct ScenarioInfo
    {
        public string Id;
        public string Title;
        public string Period;
        public string Description;
        public string MapNote;

        public ScenarioInfo(string id, string title, string period, string description, string mapNote)
        {
            Id = id;
            Title = title;
            Period = period;
            Description = description;
            MapNote = mapNote;
        }
    }

    static readonly ScenarioInfo[] ScenarioCatalog =
    {
        new ScenarioInfo(
            "balance_1836",
            "1836년의 균형",
            "1836년",
            "초기 테스트용 기본 시나리오입니다. 안정적인 지도와 기본 자원 상태에서 시작합니다.",
            "기본 월드맵 프리팹을 사용합니다."),
        new ScenarioInfo(
            "frontier_1850",
            "개척 시대 샌드박스",
            "1850년",
            "확장과 개척을 실험하기 위한 샌드박스 시나리오입니다. 맵 제작 기능 검증에 적합합니다.",
            "지형 편집과 세력 배치 테스트에 적합합니다."),
        new ScenarioInfo(
            "fragmented_realms",
            "분열된 영지들",
            "가상 시대",
            "여러 소규모 세력이 분산된 가상 시나리오입니다. 모드 제작의 기본 구조를 점검합니다.",
            "프리팹 기반 오브젝트 배치 규칙을 검증합니다.")
    };

    GameObject menuRoot;
    GameObject hudRoot;
    GameObject loadingRoot;
    Text loadingTitle;
    Text loadingDetail;
    Slider loadingSlider;
    Text fullscreenButtonText;
    Text keyPanButtonText;

    const string TargetSceneKey = "HexFlow.TargetScene";
    const string LoadingTitleKey = "HexFlow.LoadingTitle";
    const string LoadingDetailKey = "HexFlow.LoadingDetail";
    const string NextActionKey = "HexFlow.NextAction";
    const string SelectedScenarioKey = "HexFlow.SelectedScenario";
    const string SelectedScenarioTitleKey = "HexFlow.SelectedScenarioTitle";
    const string ContinueAction = "continue";

    void Awake()
    {
        if (Editor == null) Editor = GetComponent<HexMapEditor>();
        if (Grid == null) Grid = GetComponent<HexGrid>();
        if (Grid == null && Editor != null) Grid = Editor.Grid;
        EnsureFont();
        EnsureRenderCamera();
        EnsureEventSystem();
    }

    void Start()
    {
        if (Editor == null) Editor = GetComponent<HexMapEditor>();
        if (Grid == null) Grid = GetComponent<HexGrid>();
        if (Grid == null && Editor != null) Grid = Editor.Grid;

        switch (ResolveSceneRole())
        {
            case FlowSceneRole.Boot:
                LoadThroughLoading(MainMenuSceneName, "시작 중", "시스템과 메인 메뉴 씬을 준비하고 있습니다.");
                break;
            case FlowSceneRole.Loading:
                StartCoroutine(LoadingSceneRoutine());
                break;
            case FlowSceneRole.MainMenu:
                ShowMainMenu();
                break;
            case FlowSceneRole.ScenarioSelect:
                ShowScenarioSelect();
                break;
            case FlowSceneRole.Editor:
                EnterEditorMode();
                break;
            default:
                EnterGameMode();
                break;
        }
    }

    void OnDestroy()
    {
        DestroySafe(menuRoot);
        DestroySafe(hudRoot);
        DestroySafe(loadingRoot);
    }

    FlowSceneRole ResolveSceneRole()
    {
        if (SceneRole != FlowSceneRole.Auto) return SceneRole;

        string scene = SceneManager.GetActiveScene().name;
        if (scene == BootSceneName) return FlowSceneRole.Boot;
        if (scene == LoadingSceneName) return FlowSceneRole.Loading;
        if (scene == MainMenuSceneName) return FlowSceneRole.MainMenu;
        if (scene == ScenarioSelectSceneName) return FlowSceneRole.ScenarioSelect;
        if (scene == EditorSceneName) return FlowSceneRole.Editor;
        return FlowSceneRole.Game;
    }

    void LoadThroughLoading(string targetScene, string title, string detail, string nextAction = "")
    {
        PlayerPrefs.SetString(TargetSceneKey, targetScene);
        PlayerPrefs.SetString(LoadingTitleKey, title);
        PlayerPrefs.SetString(LoadingDetailKey, detail);
        PlayerPrefs.SetString(NextActionKey, nextAction);
        PlayerPrefs.Save();

        if (Application.CanStreamedLevelBeLoaded(LoadingSceneName))
        {
            SceneManager.LoadScene(LoadingSceneName);
            return;
        }

        // Development fallback if the new scenes have not been added to Build Settings yet.
        if (targetScene == MainMenuSceneName) ShowMainMenu();
        else if (targetScene == ScenarioSelectSceneName) ShowScenarioSelect();
        else if (targetScene == EditorSceneName) EnterEditorMode();
        else EnterGameMode();
    }

    IEnumerator LoadingSceneRoutine()
    {
        string targetScene = PlayerPrefs.GetString(TargetSceneKey, MainMenuSceneName);
        string title = PlayerPrefs.GetString(LoadingTitleKey, "로딩 중");
        string detail = PlayerPrefs.GetString(LoadingDetailKey, "다음 화면을 준비하고 있습니다.");
        CreateLoading(title, detail);

        float warmup = 0f;
        while (warmup < LoadingSeconds)
        {
            warmup += Time.unscaledDeltaTime;
            if (loadingSlider != null)
                loadingSlider.SetValueWithoutNotify(Mathf.Clamp01(warmup / Mathf.Max(0.01f, LoadingSeconds)) * 0.35f);
            yield return null;
        }

        if (!Application.CanStreamedLevelBeLoaded(targetScene))
        {
            if (loadingDetail != null) loadingDetail.text = $"씬을 찾을 수 없습니다: {targetScene}";
            yield break;
        }

        AsyncOperation op = SceneManager.LoadSceneAsync(targetScene);
        while (op != null && !op.isDone)
        {
            float progress = Mathf.Clamp01(op.progress / 0.9f);
            if (loadingSlider != null)
                loadingSlider.SetValueWithoutNotify(Mathf.Lerp(0.35f, 1f, progress));
            yield return null;
        }
    }

    void ShowMainMenu()
    {
        if (Editor != null) Editor.ExitEditorMode();
        DestroySafe(hudRoot);
        DestroySafe(menuRoot);

        menuRoot = CreateCanvasRoot("메인 메뉴", 80);
        Image background = menuRoot.AddComponent<Image>();
        background.color = BackgroundColor;

        GameObject panel = CreateObject("메뉴 패널", menuRoot.transform, typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 0.5f);
        panelRect.anchorMax = new Vector2(0f, 0.5f);
        panelRect.pivot = new Vector2(0f, 0.5f);
        panelRect.anchoredPosition = new Vector2(96f, 0f);
        panelRect.sizeDelta = new Vector2(540f, 640f);
        panel.GetComponent<Image>().color = PanelColor;

        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(28, 28, 30, 30);
        layout.spacing = 14f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        Text title = CreateLabel(panel.transform, "대전략 샌드박스", 34, FontStyle.Bold, TextAnchor.MiddleLeft);
        title.GetComponent<LayoutElement>().preferredHeight = 46f;
        Text subtitle = CreateLabel(panel.transform, "프리팹 기반 지도와 시나리오 제작을 위한 기초 플레이 환경", 15, FontStyle.Normal, TextAnchor.MiddleLeft);
        subtitle.color = MutedTextColor;
        subtitle.GetComponent<LayoutElement>().preferredHeight = 40f;

        CreateSpacer(panel.transform, 18f);
        CreateButton(panel.transform, "새로운 게임", AccentColor,
            () => LoadThroughLoading(ScenarioSelectSceneName, "시나리오 선택", "플레이할 시작 조건을 준비하고 있습니다."), 48f);

        Button continueButton = CreateButton(panel.transform, "이어하기", SurfaceColor,
            () => LoadThroughLoading(GameSceneName, "이어하기", "저장된 지도를 불러오고 있습니다.", ContinueAction), 48f);
        continueButton.interactable = HasSaveFile();

        CreateButton(panel.transform, "에디터 모드", SurfaceColor,
            () => LoadThroughLoading(EditorSceneName, "에디터 모드", "지도 제작 도구를 준비하고 있습니다."), 48f);
        CreateButton(panel.transform, "설정", SurfaceColor, ShowSettings, 48f);
        CreateButton(panel.transform, "종료", new Color(0.30f, 0.16f, 0.15f, 1f), QuitGame, 44f);

        CreateSpacer(panel.transform, 18f);
        Text footer = CreateLabel(panel.transform, "현재 단계: 씬 전환, 메인 메뉴, 시나리오 선택, 에디터 모드", 13, FontStyle.Normal, TextAnchor.LowerLeft);
        footer.color = MutedTextColor;
        footer.GetComponent<LayoutElement>().flexibleHeight = 1f;
    }

    void ShowScenarioSelect()
    {
        if (Editor != null) Editor.ExitEditorMode();
        DestroySafe(hudRoot);
        DestroySafe(menuRoot);

        menuRoot = CreateCanvasRoot("시나리오 선택", 85);
        menuRoot.AddComponent<Image>().color = BackgroundColor;

        GameObject panel = CreateCenteredPanel(menuRoot.transform, "시나리오 선택 패널", new Vector2(860f, 700f));
        CreateLabel(panel.transform, "시나리오 선택", 32, FontStyle.Bold, TextAnchor.MiddleLeft).GetComponent<LayoutElement>().preferredHeight = 44f;
        Text note = CreateLabel(panel.transform, "새 게임에서 사용할 시작 조건을 선택하세요.", 15, FontStyle.Normal, TextAnchor.MiddleLeft);
        note.color = MutedTextColor;
        note.GetComponent<LayoutElement>().preferredHeight = 32f;

        GameObject list = CreateObject("시나리오 목록", panel.transform, typeof(VerticalLayoutGroup), typeof(LayoutElement));
        LayoutElement listLayout = list.GetComponent<LayoutElement>();
        listLayout.flexibleWidth = 1f;
        listLayout.flexibleHeight = 1f;

        VerticalLayoutGroup listGroup = list.GetComponent<VerticalLayoutGroup>();
        listGroup.spacing = 12f;
        listGroup.childControlWidth = true;
        listGroup.childControlHeight = true;
        listGroup.childForceExpandWidth = true;
        listGroup.childForceExpandHeight = false;

        foreach (ScenarioInfo scenario in ScenarioCatalog)
        {
            ScenarioInfo current = scenario;
            CreateScenarioButton(list.transform, current);
        }

        GameObject bottomRow = CreateObject("하단 버튼", panel.transform, typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        bottomRow.GetComponent<LayoutElement>().preferredHeight = 48f;
        HorizontalLayoutGroup rowLayout = bottomRow.GetComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 10f;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = true;

        CreateButton(bottomRow.transform, "뒤로", SurfaceColor,
            () => LoadThroughLoading(MainMenuSceneName, "메인 메뉴", "메뉴 화면을 준비하고 있습니다."), 160f, 46f);
        CreateButton(bottomRow.transform, "에디터 모드", SurfaceColor,
            () => LoadThroughLoading(EditorSceneName, "에디터 모드", "지도 제작 도구를 준비하고 있습니다."), 180f, 46f);
    }

    Button CreateScenarioButton(Transform parent, ScenarioInfo scenario)
    {
        GameObject go = CreateObject(scenario.Title + " 버튼", parent, typeof(Image), typeof(Button), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        Image image = go.GetComponent<Image>();
        image.color = SurfaceColor;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => SelectScenario(scenario));

        ColorBlock colors = button.colors;
        colors.normalColor = SurfaceColor;
        colors.highlightedColor = Color.Lerp(SurfaceColor, AccentColor, 0.35f);
        colors.pressedColor = Color.Lerp(SurfaceColor, Color.black, 0.12f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;

        LayoutElement layout = go.GetComponent<LayoutElement>();
        layout.preferredHeight = 126f;
        layout.flexibleWidth = 1f;

        VerticalLayoutGroup group = go.GetComponent<VerticalLayoutGroup>();
        group.padding = new RectOffset(18, 18, 12, 12);
        group.spacing = 4f;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;

        GameObject header = CreateObject("시나리오 머리글", go.transform, typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        header.GetComponent<LayoutElement>().preferredHeight = 28f;
        HorizontalLayoutGroup headerGroup = header.GetComponent<HorizontalLayoutGroup>();
        headerGroup.spacing = 10f;
        headerGroup.childControlWidth = true;
        headerGroup.childControlHeight = true;
        headerGroup.childForceExpandWidth = false;

        Text title = CreateLabel(header.transform, scenario.Title, 18, FontStyle.Bold, TextAnchor.MiddleLeft);
        title.GetComponent<LayoutElement>().flexibleWidth = 1f;
        Text period = CreateLabel(header.transform, scenario.Period, 13, FontStyle.Bold, TextAnchor.MiddleRight);
        period.color = AccentColor;
        period.GetComponent<LayoutElement>().preferredWidth = 140f;

        Text description = CreateLabel(go.transform, scenario.Description, 14, FontStyle.Normal, TextAnchor.UpperLeft);
        description.color = MutedTextColor;
        description.GetComponent<LayoutElement>().preferredHeight = 44f;

        Text mapNote = CreateLabel(go.transform, scenario.MapNote, 13, FontStyle.Normal, TextAnchor.UpperLeft);
        mapNote.color = Color.Lerp(MutedTextColor, TextColor, 0.25f);
        mapNote.GetComponent<LayoutElement>().preferredHeight = 24f;

        return button;
    }

    void SelectScenario(ScenarioInfo scenario)
    {
        PlayerPrefs.SetString(SelectedScenarioKey, scenario.Id);
        PlayerPrefs.SetString(SelectedScenarioTitleKey, scenario.Title);
        PlayerPrefs.Save();
        LoadThroughLoading(GameSceneName, scenario.Title, "선택한 시나리오의 지도를 불러오고 있습니다.");
    }

    void ShowSettings()
    {
        DestroySafe(menuRoot);
        menuRoot = CreateCanvasRoot("설정", 80);
        menuRoot.AddComponent<Image>().color = BackgroundColor;

        GameObject panel = CreateCenteredPanel(menuRoot.transform, "설정 패널", new Vector2(520f, 430f));
        CreateLabel(panel.transform, "설정", 30, FontStyle.Bold, TextAnchor.MiddleLeft).GetComponent<LayoutElement>().preferredHeight = 44f;
        Text note = CreateLabel(panel.transform, "프로토타입용 기본 설정입니다.", 14, FontStyle.Normal, TextAnchor.MiddleLeft);
        note.color = MutedTextColor;

        Button fullscreen = CreateButton(panel.transform, "", SurfaceColor, ToggleFullscreen, 46f);
        fullscreenButtonText = fullscreen.GetComponentInChildren<Text>();
        Button keyPan = CreateButton(panel.transform, "", SurfaceColor, ToggleKeyPan, 46f);
        keyPanButtonText = keyPan.GetComponentInChildren<Text>();
        RefreshSettingsLabels();

        CreateSpacer(panel.transform, 18f);
        CreateButton(panel.transform, "뒤로", AccentColor, ShowMainMenu, 46f);
    }

    void EnterGameMode()
    {
        if (Editor != null) Editor.ExitEditorMode();
        DestroySafe(menuRoot);

        string nextAction = PlayerPrefs.GetString(NextActionKey, "");
        string status = PlayerPrefs.GetString(SelectedScenarioTitleKey, ScenarioCatalog[0].Title) + " 시작됨";

        if (nextAction == ContinueAction && Editor != null && Grid != null && File.Exists(Editor.SavePath))
        {
            Grid.LoadFromFile(Editor.SavePath);
            status = "저장된 지도 불러옴";
        }

        PlayerPrefs.DeleteKey(NextActionKey);
        CreateGameHud("게임 모드", status);
    }

    void EnterEditorMode()
    {
        DestroySafe(menuRoot);
        DestroySafe(hudRoot);
        if (Editor != null) Editor.EnterEditorMode();
        CreateEditorHud();
    }

    void CreateGameHud(string mode, string status)
    {
        DestroySafe(hudRoot);
        hudRoot = CreateCanvasRoot("게임 HUD", 70);

        GameObject top = CreateObject("상단 바", hudRoot.transform, typeof(Image), typeof(HorizontalLayoutGroup));
        RectTransform topRect = top.GetComponent<RectTransform>();
        topRect.anchorMin = new Vector2(0f, 1f);
        topRect.anchorMax = new Vector2(1f, 1f);
        topRect.pivot = new Vector2(0.5f, 1f);
        topRect.offsetMin = new Vector2(0f, -62f);
        topRect.offsetMax = Vector2.zero;
        top.GetComponent<Image>().color = new Color(0.045f, 0.052f, 0.06f, 0.96f);

        HorizontalLayoutGroup layout = top.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 10, 10);
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;

        Text title = CreateLabel(top.transform, $"대전략 샌드박스 · {mode}", 16, FontStyle.Bold, TextAnchor.MiddleLeft);
        title.GetComponent<LayoutElement>().flexibleWidth = 1f;
        CreateLabel(top.transform, GetScenarioDateLabel(), 14, FontStyle.Normal, TextAnchor.MiddleCenter).GetComponent<LayoutElement>().preferredWidth = 150f;
        CreateLabel(top.transform, "국고 1,000", 14, FontStyle.Normal, TextAnchor.MiddleCenter).GetComponent<LayoutElement>().preferredWidth = 120f;
        CreateButton(top.transform, "에디터", SurfaceColor,
            () => LoadThroughLoading(EditorSceneName, "에디터 모드", "지도 제작 도구를 준비하고 있습니다."), 92f, 38f);
        CreateButton(top.transform, "메뉴", AccentColor,
            () => LoadThroughLoading(MainMenuSceneName, "메인 메뉴", "메뉴 화면을 준비하고 있습니다."), 86f, 38f);

        GameObject bottom = CreateObject("하단 상태", hudRoot.transform, typeof(Image));
        RectTransform bottomRect = bottom.GetComponent<RectTransform>();
        bottomRect.anchorMin = new Vector2(0f, 0f);
        bottomRect.anchorMax = new Vector2(1f, 0f);
        bottomRect.pivot = new Vector2(0.5f, 0f);
        bottomRect.offsetMin = Vector2.zero;
        bottomRect.offsetMax = new Vector2(0f, 34f);
        bottom.GetComponent<Image>().color = new Color(0.045f, 0.052f, 0.06f, 0.82f);

        Text statusText = CreateLabel(bottom.transform, status + " · 휠 드래그로 지도 탐색", 13, FontStyle.Normal, TextAnchor.MiddleLeft);
        statusText.color = MutedTextColor;
        Stretch(statusText.rectTransform, new Vector2(18f, 0f), new Vector2(-18f, 0f));
    }

    void CreateEditorHud()
    {
        DestroySafe(hudRoot);
        hudRoot = CreateCanvasRoot("에디터 상단 메뉴", 130);

        GameObject bar = CreateObject("에디터 상단 바", hudRoot.transform, typeof(HorizontalLayoutGroup));
        RectTransform rect = bar.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-18f, -16f);
        rect.sizeDelta = new Vector2(220f, 40f);

        HorizontalLayoutGroup layout = bar.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;

        CreateButton(bar.transform, "게임", SurfaceColor,
            () => LoadThroughLoading(GameSceneName, "게임 모드", "시나리오 화면을 준비하고 있습니다."), 92f, 38f);
        CreateButton(bar.transform, "메뉴", AccentColor,
            () => LoadThroughLoading(MainMenuSceneName, "메인 메뉴", "메뉴 화면을 준비하고 있습니다."), 92f, 38f);
    }

    void CreateLoading(string title, string detail)
    {
        DestroySafe(loadingRoot);
        loadingRoot = CreateCanvasRoot("로딩", 200);
        loadingRoot.AddComponent<Image>().color = BackgroundColor;

        GameObject panel = CreateCenteredPanel(loadingRoot.transform, "로딩 패널", new Vector2(560f, 240f));
        loadingTitle = CreateLabel(panel.transform, title, 30, FontStyle.Bold, TextAnchor.MiddleLeft);
        loadingTitle.GetComponent<LayoutElement>().preferredHeight = 46f;
        loadingDetail = CreateLabel(panel.transform, detail, 15, FontStyle.Normal, TextAnchor.MiddleLeft);
        loadingDetail.color = MutedTextColor;
        loadingSlider = CreateSlider(panel.transform, 0f, 1f, 0f);
    }

    void ToggleFullscreen()
    {
        Screen.fullScreen = !Screen.fullScreen;
        RefreshSettingsLabels();
    }

    void ToggleKeyPan()
    {
        HexCameraController cameraController = Grid != null ? Grid.CameraController : null;
        if (cameraController == null) cameraController = FindFirstObjectByType<HexCameraController>();
        if (cameraController != null) cameraController.EnableKeyPan = !cameraController.EnableKeyPan;
        RefreshSettingsLabels();
    }

    void RefreshSettingsLabels()
    {
        if (fullscreenButtonText != null)
            fullscreenButtonText.text = Screen.fullScreen ? "전체 화면: 켜짐" : "전체 화면: 꺼짐";

        HexCameraController cameraController = Grid != null ? Grid.CameraController : null;
        if (cameraController == null) cameraController = FindFirstObjectByType<HexCameraController>();
        if (keyPanButtonText != null)
            keyPanButtonText.text = cameraController == null || cameraController.EnableKeyPan
                ? "키보드 카메라 이동: 켜짐"
                : "키보드 카메라 이동: 꺼짐";
    }

    void QuitGame()
    {
        Application.Quit();
#if UNITY_EDITOR
        Debug.Log("종료 요청");
#endif
    }

    bool HasSaveFile()
    {
        if (Editor != null) return File.Exists(Editor.SavePath);
        string path = Path.Combine(Application.persistentDataPath, "edited_geometry.json");
        return File.Exists(path);
    }

    string GetScenarioDateLabel()
    {
        string scenarioId = PlayerPrefs.GetString(SelectedScenarioKey, ScenarioCatalog[0].Id);
        ScenarioInfo scenario = FindScenario(scenarioId);
        if (scenario.Period.Contains("년")) return scenario.Period + " 1월 1일";
        return scenario.Period;
    }

    ScenarioInfo FindScenario(string scenarioId)
    {
        for (int i = 0; i < ScenarioCatalog.Length; i++)
        {
            if (ScenarioCatalog[i].Id == scenarioId) return ScenarioCatalog[i];
        }

        return ScenarioCatalog[0];
    }

    GameObject CreateCanvasRoot(string name, int sortingOrder)
    {
        GameObject root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform rect = root.GetComponent<RectTransform>();
        Stretch(rect, Vector2.zero, Vector2.zero);
        return root;
    }

    GameObject CreateCenteredPanel(Transform parent, string name, Vector2 size)
    {
        GameObject panel = CreateObject(name, parent, typeof(Image), typeof(VerticalLayoutGroup));
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
        panel.GetComponent<Image>().color = PanelColor;

        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(26, 26, 24, 24);
        layout.spacing = 14f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return panel;
    }

    Button CreateButton(Transform parent, string label, Color color, UnityEngine.Events.UnityAction onClick, float height)
    {
        return CreateButton(parent, label, color, onClick, -1f, height);
    }

    Button CreateButton(Transform parent, string label, Color color, UnityEngine.Events.UnityAction onClick, float width, float height)
    {
        GameObject go = CreateObject(label + " 버튼", parent, typeof(Image), typeof(Button), typeof(LayoutElement));
        Image image = go.GetComponent<Image>();
        image.color = color;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        ColorBlock colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.14f);
        colors.pressedColor = Color.Lerp(color, Color.black, 0.12f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(color.r, color.g, color.b, 0.35f);
        button.colors = colors;

        LayoutElement layout = go.GetComponent<LayoutElement>();
        if (width > 0f) layout.preferredWidth = width;
        else layout.flexibleWidth = 1f;
        layout.preferredHeight = height;

        Text text = CreateLabel(go.transform, label, 15, FontStyle.Bold, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform, Vector2.zero, Vector2.zero);
        return button;
    }

    Text CreateLabel(Transform parent, string text, int size, FontStyle style, TextAnchor anchor)
    {
        GameObject go = CreateObject("텍스트", parent, typeof(Text), typeof(LayoutElement));
        Text label = go.GetComponent<Text>();
        label.font = Font;
        label.text = text;
        label.fontSize = size;
        label.fontStyle = style;
        label.color = TextColor;
        label.alignment = anchor;
        label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        go.GetComponent<LayoutElement>().preferredHeight = Mathf.Max(24f, size + 10f);
        return label;
    }

    Slider CreateSlider(Transform parent, float min, float max, float value)
    {
        GameObject go = CreateObject("진행 막대", parent, typeof(Image), typeof(Slider), typeof(LayoutElement));
        go.GetComponent<Image>().color = new Color(0.055f, 0.065f, 0.075f, 1f);
        LayoutElement layout = go.GetComponent<LayoutElement>();
        layout.preferredHeight = 20f;
        layout.flexibleWidth = 1f;

        GameObject fillArea = CreateObject("채움 영역", go.transform, typeof(RectTransform));
        RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0f, 0.2f);
        fillAreaRect.anchorMax = new Vector2(1f, 0.8f);
        fillAreaRect.offsetMin = new Vector2(8f, 0f);
        fillAreaRect.offsetMax = new Vector2(-8f, 0f);

        GameObject fill = CreateObject("채움", fillArea.transform, typeof(Image));
        fill.GetComponent<Image>().color = AccentColor;
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        Stretch(fillRect, Vector2.zero, Vector2.zero);

        Slider slider = go.GetComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = value;
        slider.fillRect = fillRect;
        slider.interactable = false;
        return slider;
    }

    void CreateSpacer(Transform parent, float height)
    {
        GameObject spacer = CreateObject("여백", parent, typeof(LayoutElement));
        spacer.GetComponent<LayoutElement>().preferredHeight = height;
    }

    GameObject CreateObject(string name, Transform parent, params System.Type[] components)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        foreach (System.Type component in components)
        {
            if (component == typeof(RectTransform)) continue;
            go.AddComponent(component);
        }
        go.transform.SetParent(parent, false);
        return go;
    }

    void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem));
        InputSystemUIInputModule module = eventSystem.AddComponent<InputSystemUIInputModule>();
        module.AssignDefaultActions();
        eventSystem.transform.SetParent(transform, false);
    }

    void EnsureRenderCamera()
    {
        if (Camera.main != null) return;

        GameObject cameraObject = new GameObject("Menu Camera", typeof(Camera));
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(transform, false);
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);

        Camera camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = BackgroundColor;
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 100f;
    }

    void EnsureFont()
    {
        if (Font != null) return;
        Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (Font == null) Font = Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    void Stretch(RectTransform rect, Vector2 minOffset, Vector2 maxOffset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = minOffset;
        rect.offsetMax = maxOffset;
    }

    void DestroySafe(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }
}
