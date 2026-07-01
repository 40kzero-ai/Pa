using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Runtime shell for the grand-strategy prototype: boot loading, main menu,
/// settings, game HUD, and editor-mode entry.
/// </summary>
[DisallowMultipleComponent]
public class HexGameFlow : MonoBehaviour
{
    public HexMapEditor Editor;
    public HexGrid Grid;
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

    GameObject menuRoot;
    GameObject hudRoot;
    GameObject loadingRoot;
    Text loadingTitle;
    Text loadingDetail;
    Slider loadingSlider;
    Text fullscreenButtonText;
    Text keyPanButtonText;
    Coroutine transitionRoutine;

    void Awake()
    {
        if (Editor == null) Editor = GetComponent<HexMapEditor>();
        if (Grid == null) Grid = GetComponent<HexGrid>();
        if (Grid == null && Editor != null) Grid = Editor.Grid;
        EnsureFont();
        EnsureEventSystem();
    }

    void Start()
    {
        if (Editor == null) Editor = GetComponent<HexMapEditor>();
        if (Grid == null) Grid = GetComponent<HexGrid>();
        if (Grid == null && Editor != null) Grid = Editor.Grid;

        if (Editor != null) Editor.ExitEditorMode();
        ShowBootLoading();
    }

    void OnDestroy()
    {
        DestroySafe(menuRoot);
        DestroySafe(hudRoot);
        DestroySafe(loadingRoot);
    }

    void ShowBootLoading()
    {
        BeginTransition("로딩 중", "지도 데이터와 사용자 인터페이스를 준비하고 있습니다.", ShowMainMenu);
    }

    void BeginTransition(string title, string detail, System.Action onComplete)
    {
        if (transitionRoutine != null) StopCoroutine(transitionRoutine);
        transitionRoutine = StartCoroutine(TransitionRoutine(title, detail, onComplete));
    }

    IEnumerator TransitionRoutine(string title, string detail, System.Action onComplete)
    {
        DestroySafe(menuRoot);
        DestroySafe(hudRoot);
        CreateLoading(title, detail);

        float elapsed = 0f;
        while (elapsed < LoadingSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, LoadingSeconds));
            if (loadingSlider != null) loadingSlider.SetValueWithoutNotify(t);
            yield return null;
        }

        DestroySafe(loadingRoot);
        loadingRoot = null;
        transitionRoutine = null;
        onComplete?.Invoke();
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
        panelRect.sizeDelta = new Vector2(520f, 620f);
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
        Text subtitle = CreateLabel(panel.transform, "프로빈스 기반 지도 제작과 시나리오 테스트", 15, FontStyle.Normal, TextAnchor.MiddleLeft);
        subtitle.color = MutedTextColor;
        subtitle.GetComponent<LayoutElement>().preferredHeight = 34f;

        CreateSpacer(panel.transform, 18f);
        CreateButton(panel.transform, "새로운 게임", AccentColor, () => BeginTransition("새로운 게임", "기본 시나리오를 준비하고 있습니다.", EnterGameMode), 48f);

        Button continueButton = CreateButton(panel.transform, "이어하기", SurfaceColor, () => BeginTransition("이어하기", "저장된 지도를 불러오고 있습니다.", ContinueGame), 48f);
        continueButton.interactable = Editor != null && File.Exists(Editor.SavePath);

        CreateButton(panel.transform, "에디터 모드로 진입", SurfaceColor, () => BeginTransition("에디터 모드", "지도 제작 도구를 준비하고 있습니다.", EnterEditorMode), 48f);
        CreateButton(panel.transform, "설정", SurfaceColor, ShowSettings, 48f);
        CreateButton(panel.transform, "종료", new Color(0.30f, 0.16f, 0.15f, 1f), QuitGame, 44f);

        CreateSpacer(panel.transform, 18f);
        Text footer = CreateLabel(panel.transform, "현재 단계: 런타임 메뉴와 에디터 모드 분리", 13, FontStyle.Normal, TextAnchor.LowerLeft);
        footer.color = MutedTextColor;
        footer.GetComponent<LayoutElement>().flexibleHeight = 1f;
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
        CreateGameHud("게임 모드", "시나리오 시작됨");
    }

    void ContinueGame()
    {
        if (Editor != null && Grid == null) Grid = Editor.Grid;
        if (Editor != null && Grid != null && File.Exists(Editor.SavePath))
            Grid.LoadFromFile(Editor.SavePath);
        EnterGameMode();
    }

    void EnterEditorMode()
    {
        DestroySafe(menuRoot);
        DestroySafe(hudRoot);
        if (Editor != null) Editor.EnterEditorMode();
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
        CreateLabel(top.transform, "1836년 1월 1일", 14, FontStyle.Normal, TextAnchor.MiddleCenter).GetComponent<LayoutElement>().preferredWidth = 150f;
        CreateLabel(top.transform, "국고 1,000", 14, FontStyle.Normal, TextAnchor.MiddleCenter).GetComponent<LayoutElement>().preferredWidth = 120f;
        CreateButton(top.transform, "에디터", SurfaceColor, () => BeginTransition("에디터 모드", "지도 제작 도구를 준비하고 있습니다.", EnterEditorMode), 92f, 38f);
        CreateButton(top.transform, "메뉴", AccentColor, ShowMainMenu, 86f, 38f);

        GameObject bottom = CreateObject("하단 상태", hudRoot.transform, typeof(Image));
        RectTransform bottomRect = bottom.GetComponent<RectTransform>();
        bottomRect.anchorMin = new Vector2(0f, 0f);
        bottomRect.anchorMax = new Vector2(1f, 0f);
        bottomRect.pivot = new Vector2(0.5f, 0f);
        bottomRect.offsetMin = Vector2.zero;
        bottomRect.offsetMax = new Vector2(0f, 34f);
        bottom.GetComponent<Image>().color = new Color(0.045f, 0.052f, 0.06f, 0.82f);

        Text statusText = CreateLabel(bottom.transform, status + " · 우클릭 드래그/휠로 지도 탐색", 13, FontStyle.Normal, TextAnchor.MiddleLeft);
        statusText.color = MutedTextColor;
        Stretch(statusText.rectTransform, new Vector2(18f, 0f), new Vector2(-18f, 0f));
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
