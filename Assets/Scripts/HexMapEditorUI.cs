using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Object-based runtime UI for the map editor. It builds a clean uGUI hierarchy
/// so the editor no longer depends on IMGUI and can later be converted to a prefab.
/// </summary>
[DisallowMultipleComponent]
public class HexMapEditorUI : MonoBehaviour
{
    public HexMapEditor Editor;

    [Header("Layout")]
    public float PanelWidth = 380f;
    public float PanelMargin = 16f;
    public Vector2 ReferenceResolution = new Vector2(1920, 1080);

    [Header("Style")]
    public Font Font;
    public Color PanelColor = new Color(0.055f, 0.062f, 0.07f, 0.94f);
    public Color SurfaceColor = new Color(0.10f, 0.115f, 0.13f, 0.96f);
    public Color SurfaceHoverColor = new Color(0.15f, 0.17f, 0.19f, 1f);
    public Color AccentColor = new Color(0.22f, 0.62f, 0.94f, 1f);
    public Color WarningColor = new Color(0.92f, 0.42f, 0.28f, 1f);
    public Color TextColor = new Color(0.92f, 0.95f, 0.96f, 1f);
    public Color MutedTextColor = new Color(0.58f, 0.64f, 0.68f, 1f);

    GameObject root;
    RectTransform paletteContent;
    GameObject provinceActions;
    Text titleText;
    Text modeHintText;
    Text brushValueText;
    Text statusText;
    Text landOnlyText;
    Text protectText;
    Text terrainModeText;
    Text provinceModeText;
    Button undoButton;
    Button redoButton;
    Button removeProvinceButton;
    Slider brushSlider;
    InputField widthInput;
    InputField heightInput;

    int lastMode = -1;
    int lastTerrainCount = -1;
    int lastProvinceCount = -1;
    int lastActiveTerrain = int.MinValue;
    int lastActiveProvince = int.MinValue;
    int lastProvinceVersion = -1;
    int lastBrush = int.MinValue;
    bool dirty = true;
    bool subscribed;

    public void Bind(HexMapEditor editor)
    {
        if (Editor == editor && subscribed) return;
        Unsubscribe();
        Editor = editor;
        Subscribe();
        dirty = true;
        if (isActiveAndEnabled) Build();
    }

    void Awake()
    {
        if (Editor == null) Editor = GetComponent<HexMapEditor>();
        Subscribe();
    }

    void OnEnable()
    {
        Subscribe();
        Build();
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void OnDestroy()
    {
        Unsubscribe();
        DestroySafe(root);
    }

    void Subscribe()
    {
        if (subscribed || Editor == null) return;
        Editor.StateChanged += MarkDirty;
        subscribed = true;
    }

    void Unsubscribe()
    {
        if (!subscribed || Editor == null) return;
        Editor.StateChanged -= MarkDirty;
        subscribed = false;
    }

    void MarkDirty()
    {
        dirty = true;
    }

    void LateUpdate()
    {
        if (Editor == null) return;
        if (root == null) Build();
        if (!dirty && !HasExternalStateChanged()) return;
        Refresh();
    }

    bool HasExternalStateChanged()
    {
        return Editor.PaintMode != (HexGrid.EditChannel)lastMode
            || Editor.BrushSize != lastBrush
            || Editor.ActiveTerrain != lastActiveTerrain
            || Editor.ActiveProvince != lastActiveProvince
            || (Editor.TerrainTypes?.Length ?? 0) != lastTerrainCount
            || Editor.ProvinceCount != lastProvinceCount
            || (Editor.Grid != null && Editor.Grid.ProvinceEditVersion != lastProvinceVersion);
    }

    void Build()
    {
        if (Editor == null) return;

        DestroySafe(root);
        EnsureEventSystem();
        EnsureFont();

        root = new GameObject("Hex Map Editor UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        GameObject panel = CreateObject("Panel", root.transform, typeof(Image), typeof(VerticalLayoutGroup));
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 0f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 0.5f);
        panelRect.anchoredPosition = new Vector2(PanelMargin, 0f);
        panelRect.sizeDelta = new Vector2(PanelWidth, -PanelMargin * 2f);
        panel.GetComponent<Image>().color = PanelColor;

        VerticalLayoutGroup panelLayout = panel.GetComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(16, 16, 16, 16);
        panelLayout.spacing = 10f;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        titleText = CreateLabel(panel.transform, "Map Builder", 24, FontStyle.Bold, TextAnchor.MiddleLeft);
        titleText.GetComponent<LayoutElement>().preferredHeight = 30f;
        modeHintText = CreateLabel(panel.transform, "", 13, FontStyle.Normal, TextAnchor.MiddleLeft);
        modeHintText.color = MutedTextColor;

        CreateModeRow(panel.transform);
        CreateBrushSection(panel.transform);
        CreatePaletteSection(panel.transform);
        CreateProvinceActions(panel.transform);
        CreateFileActions(panel.transform);

        statusText = CreateLabel(panel.transform, "", 13, FontStyle.Normal, TextAnchor.MiddleLeft);
        statusText.color = MutedTextColor;
        statusText.GetComponent<LayoutElement>().preferredHeight = 42f;

        dirty = true;
        Refresh();
    }

    void CreateModeRow(Transform parent)
    {
        GameObject row = CreateRow(parent, "Mode Row", 8f);
        Button terrain = CreateButton(row.transform, "Terrain", AccentColor, () => Editor.SetMode(HexGrid.EditChannel.Terrain), 36f);
        Button province = CreateButton(row.transform, "Province", SurfaceColor, () => Editor.SetMode(HexGrid.EditChannel.Province), 36f);
        terrainModeText = terrain.GetComponentInChildren<Text>();
        provinceModeText = province.GetComponentInChildren<Text>();
    }

    void CreateBrushSection(Transform parent)
    {
        GameObject box = CreateSection(parent, "Brush");
        Text label = CreateLabel(box.transform, "Brush", 14, FontStyle.Bold, TextAnchor.MiddleLeft);
        label.GetComponent<LayoutElement>().preferredHeight = 20f;

        GameObject row = CreateRow(box.transform, "Brush Row", 10f);
        brushSlider = CreateSlider(row.transform, 0f, Editor.MaxBrushSize, Editor.BrushSize, value => Editor.SetBrushSize(value));
        brushValueText = CreateLabel(row.transform, "", 14, FontStyle.Bold, TextAnchor.MiddleCenter);
        brushValueText.GetComponent<LayoutElement>().preferredWidth = 58f;
    }

    void CreatePaletteSection(Transform parent)
    {
        GameObject scrollRoot = CreateObject("Palette Scroll", parent, typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
        scrollRoot.GetComponent<Image>().color = SurfaceColor;
        LayoutElement le = scrollRoot.GetComponent<LayoutElement>();
        le.minHeight = 260f;
        le.flexibleHeight = 1f;

        ScrollRect scroll = scrollRoot.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 18f;

        GameObject viewport = CreateObject("Viewport", scrollRoot.transform, typeof(Image), typeof(Mask));
        viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.08f);
        viewport.GetComponent<Mask>().showMaskGraphic = false;
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect, Vector2.zero, Vector2.zero);

        GameObject content = CreateObject("Content", viewport.transform, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        paletteContent = content.GetComponent<RectTransform>();
        paletteContent.anchorMin = new Vector2(0f, 1f);
        paletteContent.anchorMax = new Vector2(1f, 1f);
        paletteContent.pivot = new Vector2(0.5f, 1f);
        paletteContent.offsetMin = new Vector2(8f, 0f);
        paletteContent.offsetMax = new Vector2(-8f, -8f);

        VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 8, 8);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewportRect;
        scroll.content = paletteContent;
    }

    void CreateProvinceActions(Transform parent)
    {
        provinceActions = CreateSection(parent, "Province Actions");

        GameObject row = CreateRow(provinceActions.transform, "Province Buttons", 8f);
        CreateButton(row.transform, "+ Province", AccentColor, () => Editor.AddProvince(), 32f);
        removeProvinceButton = CreateButton(row.transform, "Remove", WarningColor, () => Editor.RemoveActiveProvince(), 32f);

        CreateButton(provinceActions.transform, "Paint Unassigned", SurfaceHoverColor, () => Editor.SelectProvince(-1), 32f);

        Button land = CreateButton(provinceActions.transform, "", SurfaceColor, () => Editor.TogglePaintLandOnly(), 30f);
        landOnlyText = land.GetComponentInChildren<Text>();
        Button protect = CreateButton(provinceActions.transform, "", SurfaceColor, () => Editor.ToggleProtectOtherProvinces(), 30f);
        protectText = protect.GetComponentInChildren<Text>();

        GameObject pngRow = CreateRow(provinceActions.transform, "PNG Row", 8f);
        CreateButton(pngRow.transform, "Save PNG", SurfaceHoverColor, () => Editor.SaveProvincePng(), 32f);
        CreateButton(pngRow.transform, "Load PNG", SurfaceHoverColor, () => Editor.LoadProvincePng(), 32f);
    }

    void CreateFileActions(Transform parent)
    {
        GameObject section = CreateSection(parent, "Files");

        GameObject editRow = CreateRow(section.transform, "Undo Row", 8f);
        undoButton = CreateButton(editRow.transform, "Undo", SurfaceHoverColor, () => Editor.Undo(), 30f);
        redoButton = CreateButton(editRow.transform, "Redo", SurfaceHoverColor, () => Editor.Redo(), 30f);

        GameObject fileRow = CreateRow(section.transform, "File Row", 8f);
        CreateButton(fileRow.transform, "Save JSON", AccentColor, () => Editor.SaveGeometry(), 32f);
        CreateButton(fileRow.transform, "Load JSON", SurfaceHoverColor, () => Editor.LoadGeometry(), 32f);

        Text label = CreateLabel(section.transform, "New Map", 13, FontStyle.Bold, TextAnchor.MiddleLeft);
        label.GetComponent<LayoutElement>().preferredHeight = 18f;

        GameObject mapRow = CreateRow(section.transform, "New Map Row", 8f);
        widthInput = CreateInput(mapRow.transform, "Width", "16");
        heightInput = CreateInput(mapRow.transform, "Height", "12");
        CreateButton(mapRow.transform, "Create", AccentColor, CreateBlankMapFromFields, 32f).GetComponent<LayoutElement>().preferredWidth = 86f;
    }

    void CreateBlankMapFromFields()
    {
        int width = ParsePositiveInt(widthInput.text, 16);
        int height = ParsePositiveInt(heightInput.text, 12);
        Editor.CreateBlankMap(width, height);
        widthInput.SetTextWithoutNotify(Editor.Grid.CurrentWidth.ToString());
        heightInput.SetTextWithoutNotify(Editor.Grid.CurrentHeight.ToString());
    }

    int ParsePositiveInt(string value, int fallback)
    {
        return int.TryParse(value, out int parsed) ? Mathf.Max(1, parsed) : fallback;
    }

    void Refresh()
    {
        if (Editor == null || root == null) return;

        titleText.text = Editor.Grid != null
            ? $"Map Builder  {Editor.Grid.CurrentWidth}x{Editor.Grid.CurrentHeight}"
            : "Map Builder";
        modeHintText.text = Editor.PaintMode == HexGrid.EditChannel.Terrain
            ? "Terrain painting · number keys select terrain"
            : "Province painting · Alt + click picks a province";

        brushSlider.maxValue = Editor.MaxBrushSize;
        brushSlider.SetValueWithoutNotify(Editor.BrushSize);
        brushValueText.text = Editor.BrushSize == 0 ? "1 cell" : $"+{Editor.BrushSize}";

        terrainModeText.text = Editor.PaintMode == HexGrid.EditChannel.Terrain ? "● Terrain" : "Terrain";
        provinceModeText.text = Editor.PaintMode == HexGrid.EditChannel.Province ? "● Province" : "Province";

        bool provinceMode = Editor.PaintMode == HexGrid.EditChannel.Province;
        provinceActions.SetActive(provinceMode);
        if (Editor.Grid != null)
        {
            landOnlyText.text = Editor.Grid.PaintLandOnly ? "☑ Paint land only" : "☐ Paint land only";
            protectText.text = Editor.Grid.ProtectOtherProvinces ? "☑ Protect other provinces" : "☐ Protect other provinces";
        }

        undoButton.interactable = Editor.CanUndo;
        redoButton.interactable = Editor.CanRedo;
        removeProvinceButton.interactable = provinceMode && Editor.ActiveProvince >= 0 && Editor.ActiveProvince < Editor.ProvinceCount;

        string detail = string.IsNullOrEmpty(Editor.Status) ? "Ready" : Editor.Status;
        statusText.text = $"{detail}\n{Editor.SavePath}";

        bool rebuildList = Editor.PaintMode != (HexGrid.EditChannel)lastMode
            || (Editor.TerrainTypes?.Length ?? 0) != lastTerrainCount
            || Editor.ProvinceCount != lastProvinceCount
            || Editor.ActiveTerrain != lastActiveTerrain
            || Editor.ActiveProvince != lastActiveProvince
            || (Editor.Grid != null && Editor.Grid.ProvinceEditVersion != lastProvinceVersion);
        if (rebuildList) RebuildPaletteList();

        lastMode = (int)Editor.PaintMode;
        lastTerrainCount = Editor.TerrainTypes?.Length ?? 0;
        lastProvinceCount = Editor.ProvinceCount;
        lastActiveTerrain = Editor.ActiveTerrain;
        lastActiveProvince = Editor.ActiveProvince;
        lastProvinceVersion = Editor.Grid != null ? Editor.Grid.ProvinceEditVersion : -1;
        lastBrush = Editor.BrushSize;
        dirty = false;
    }

    void RebuildPaletteList()
    {
        ClearChildren(paletteContent);

        if (Editor.PaintMode == HexGrid.EditChannel.Terrain)
        {
            TerrainType[] types = Editor.TerrainTypes;
            if (types == null) return;
            for (int i = 0; i < types.Length; i++)
            {
                int index = i;
                string label = Editor.TryGetTerrainName(i, out string terrainName)
                    ? $"{i + 1}. {terrainName}"
                    : $"{i + 1}. Terrain";
                CreatePaletteButton(label, Editor.GetTerrainColor(i), i == Editor.ActiveTerrain, () => Editor.SelectTerrain(index));
            }
            return;
        }

        CreatePaletteButton("Unassigned", Color.black, Editor.ActiveProvince < 0, () => Editor.SelectProvince(-1));
        for (int i = 0; i < Editor.ProvinceCount; i++)
        {
            int index = i;
            CreatePaletteButton($"Province {i + 1}", Editor.GetProvinceColor(i), i == Editor.ActiveProvince, () => Editor.SelectProvince(index));
        }
    }

    void CreatePaletteButton(string label, Color swatchColor, bool selected, UnityEngine.Events.UnityAction onClick)
    {
        Button button = CreateButton(paletteContent, label, selected ? AccentColor : SurfaceColor, onClick, 30f);
        HorizontalLayoutGroup layout = button.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 10, 4, 4);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = false;
        layout.childForceExpandWidth = false;

        Text text = button.GetComponentInChildren<Text>();
        text.alignment = TextAnchor.MiddleLeft;
        text.transform.SetAsLastSibling();

        GameObject swatch = CreateObject("Swatch", button.transform, typeof(Image), typeof(LayoutElement));
        swatch.transform.SetAsFirstSibling();
        swatch.GetComponent<Image>().color = swatchColor;
        LayoutElement swatchLayout = swatch.GetComponent<LayoutElement>();
        swatchLayout.preferredWidth = 18f;
        swatchLayout.preferredHeight = 18f;

        LayoutElement textLayout = text.GetComponent<LayoutElement>();
        textLayout.flexibleWidth = 1f;
    }

    Button CreateButton(Transform parent, string label, Color color, UnityEngine.Events.UnityAction onClick, float height)
    {
        GameObject go = CreateObject(label + " Button", parent, typeof(Image), typeof(Button), typeof(LayoutElement));
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

        LayoutElement le = go.GetComponent<LayoutElement>();
        le.preferredHeight = height;
        le.flexibleWidth = 1f;

        Text text = CreateLabel(go.transform, label, 13, FontStyle.Bold, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform, Vector2.zero, Vector2.zero);
        return button;
    }

    Slider CreateSlider(Transform parent, float min, float max, float value, UnityEngine.Events.UnityAction<float> onChange)
    {
        GameObject go = CreateObject("Brush Slider", parent, typeof(RectTransform), typeof(Slider), typeof(LayoutElement), typeof(Image));
        go.GetComponent<Image>().color = new Color(0.06f, 0.07f, 0.08f, 1f);
        LayoutElement le = go.GetComponent<LayoutElement>();
        le.preferredHeight = 20f;
        le.flexibleWidth = 1f;

        GameObject fillArea = CreateObject("Fill Area", go.transform, typeof(RectTransform));
        RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0f, 0.25f);
        fillAreaRect.anchorMax = new Vector2(1f, 0.75f);
        fillAreaRect.offsetMin = new Vector2(8f, 0f);
        fillAreaRect.offsetMax = new Vector2(-8f, 0f);

        GameObject fill = CreateObject("Fill", fillArea.transform, typeof(Image));
        fill.GetComponent<Image>().color = AccentColor;
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        GameObject handle = CreateObject("Handle", go.transform, typeof(Image));
        Image handleImage = handle.GetComponent<Image>();
        handleImage.color = TextColor;
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(14f, 22f);

        Slider slider = go.GetComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = true;
        slider.value = value;
        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.targetGraphic = handleImage;
        slider.onValueChanged.AddListener(onChange);
        return slider;
    }

    InputField CreateInput(Transform parent, string placeholder, string value)
    {
        GameObject go = CreateObject(placeholder + " Input", parent, typeof(Image), typeof(InputField), typeof(LayoutElement));
        go.GetComponent<Image>().color = new Color(0.065f, 0.075f, 0.085f, 1f);
        LayoutElement le = go.GetComponent<LayoutElement>();
        le.preferredHeight = 32f;
        le.flexibleWidth = 1f;

        Text text = CreateLabel(go.transform, value, 13, FontStyle.Normal, TextAnchor.MiddleLeft);
        text.color = TextColor;
        Stretch(text.rectTransform, new Vector2(10f, 2f), new Vector2(-10f, -2f));

        Text ph = CreateLabel(go.transform, placeholder, 13, FontStyle.Normal, TextAnchor.MiddleLeft);
        ph.color = MutedTextColor;
        Stretch(ph.rectTransform, new Vector2(10f, 2f), new Vector2(-10f, -2f));

        InputField input = go.GetComponent<InputField>();
        input.textComponent = text;
        input.placeholder = ph;
        input.text = value;
        input.contentType = InputField.ContentType.IntegerNumber;
        return input;
    }

    GameObject CreateSection(Transform parent, string name)
    {
        GameObject section = CreateObject(name, parent, typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        section.GetComponent<Image>().color = new Color(0.075f, 0.085f, 0.096f, 0.88f);
        VerticalLayoutGroup layout = section.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 10, 10);
        layout.spacing = 7f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        LayoutElement le = section.GetComponent<LayoutElement>();
        le.preferredHeight = -1f;
        return section;
    }

    GameObject CreateRow(Transform parent, string name, float spacing)
    {
        GameObject row = CreateObject(name, parent, typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        row.GetComponent<LayoutElement>().preferredHeight = 36f;
        return row;
    }

    Text CreateLabel(Transform parent, string text, int size, FontStyle style, TextAnchor anchor)
    {
        GameObject go = CreateObject("Text", parent, typeof(Text), typeof(LayoutElement));
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
        go.GetComponent<LayoutElement>().preferredHeight = Mathf.Max(22f, size + 8f);
        return label;
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

    void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
            DestroySafe(parent.GetChild(i).gameObject);
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
