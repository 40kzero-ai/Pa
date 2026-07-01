using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem; // 새 Input System
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 인게임 맵 에디터 (새 Input System).
/// - 좌클릭/드래그: 선택 지형으로 칠하기 (지우개는 ocean=0 선택 후 칠하기)
/// - 좌클릭으로 칠하는 중 화면 가장자리에 가까워지면 자동으로 카메라 이동
/// - 숫자키 1~: 지형/프로빈스 선택 (모드에 따라 1번부터 매핑)
/// - 프로빈스 모드에서 Alt+클릭: 맵에서 클릭한 셀의 프로빈스를 자동으로 찾아 선택(스포이드)
/// - Ctrl/Cmd+S: 저장
/// - Ctrl/Cmd+Shift+S: 프로빈스 PNG 저장
/// - 마우스를 올리면 브러시가 칠할 영역을 반투명으로 미리보기
/// - 좌측 패널: 지형 견본, 브러시 크기, UI 크기(±), 새 맵 생성(W×H), 저장
/// (카메라 줌/이동은 HexCameraController가 담당: 휠 줌, 우드래그 팬)
///
/// 클릭 지점은 y=0 평면 레이캐스트로 구한다(1단계는 평면). 3단계에서 하이트맵 고도가
/// 들어가면 높이 기반 피킹으로 교체한다.
/// </summary>
public class HexMapEditor : MonoBehaviour
{
    public HexGrid Grid;

    [Tooltip("UI 패널 배율. 패널의 ± 버튼으로도 조절.")]
    [Range(1f, 3f)] public float UIScale = 1.4f;

    [Tooltip("브러시 미리보기 머티리얼. 비우면 Custom/HexHighlight로 자동 생성.")]
    public Material HighlightMaterial;
    public float PreviewYOffset = 0.2f;

    [Header("새 맵 생성 제한")]
    [Min(1)] public int MaxNewMapWidth = 200;
    [Min(1)] public int MaxNewMapHeight = 200;

    [Header("브러시 설정")]
    [Min(1)] public int MaxBrushSize = 30;
    [Tooltip("이 크기를 넘는 브러시는 미리보기(반투명 영역)를 생략해 렉을 막는다.")]
    [Min(0)] public int PreviewMaxBrush = 30;

    [Header("화면 가장자리 자동 이동")]
    [Tooltip("좌클릭으로 칠하는 중 마우스가 화면 가장자리에 가까워지면 카메라를 자동 이동합니다.")]
    public bool EnablePaintEdgePan = true;
    [Tooltip("화면 가장자리에서 이 픽셀 거리 안으로 들어오면 자동 이동합니다.")]
    public float EdgePanMargin = 48f;
    [Tooltip("자동 이동 속도. HexCameraController의 현재 줌 거리에 비례합니다.")]
    public float EdgePanSpeed = 0.9f;

    const float AreaWidth = 260f;
    const float ScrollbarGutter = 20f; // 스크롤바를 콘텐츠 오른쪽 바깥으로 밀어내는 여유 폭
    const float PanelAreaWidth = AreaWidth + ScrollbarGutter;
    const float OriginX = 12f;
    const float PanelTop = 12f;
    const float PanelHeight = 560f;
    const float ListHeight = 196f;   // 목록 스크롤 영역 높이(고정) — 항목이 많아도 이 안에서 스크롤

    int activeTerrain = 1;
    int brushSize = 1;
    HexGrid.EditChannel paintMode = HexGrid.EditChannel.Terrain;
    int activeProvince = 0; // -1 = 무소속(지우개)

    // 드래그 보간용
    Vector3 lastPaintPos;
    bool hasLastPaint;
    string widthText = "16";
    string heightText = "12";

    GUIStyle header;
    GUIStyle content;            // 패널 콘텐츠 좌우 패딩(스크롤바와 균형)
    Vector2 listScroll;          // (미사용 예약)
    Vector2 panelScroll;         // 패널 전체 스크롤 위치
    float lastPanelH;            // 이번 프레임 패널 높이(히트테스트용)
    bool showFileSection = true; // 파일·설정 섹션 접기
    bool showProvinceList = true; // 프로빈스 목록 접기

    // 미리보기
    GameObject previewGO;
    MeshFilter previewFilter;
    Mesh previewMesh;
    GameObject provinceHighlightGO;
    MeshFilter provinceHighlightFilter;
    Mesh provinceHighlightMesh;
    GameObject provinceBorderGO;
    MeshFilter provinceBorderFilter;
    Mesh provinceBorderMesh;
    int lastHighlightedProvince = int.MinValue;
    int lastProvinceHighlightVersion = -1;
    HexCell lastCenter;
    int lastBrush = -1;

    string status = "";
    string SavePath => System.IO.Path.Combine(Application.persistentDataPath, "edited_geometry.json");
    string ProvincePngPath => System.IO.Path.Combine(Application.persistentDataPath, "provinces.png");

    static readonly Key[] DigitKeys =
    {
        Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4,
        Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9
    };

    RectTransform uiRoot;
    Text statusText;
    Text titleText;
    Text brushLabel;
    Slider brushSlider;
    Transform listContent;
    GameObject provinceActions;
    InputField widthInput;
    InputField heightInput;

    void Start()
    {
        ClampNewMapLimits();
        if (Grid != null) SetupPreview();
        BuildPrefabStyleMenuUI();
        RefreshMenuUI();
    }

    void OnValidate()
    {
        ClampNewMapLimits();
    }

    void ClampNewMapLimits()
    {
        MaxNewMapWidth = Mathf.Max(1, MaxNewMapWidth);
        MaxNewMapHeight = Mathf.Max(1, MaxNewMapHeight);
        MaxBrushSize = Mathf.Max(0, MaxBrushSize);
        brushSize = Mathf.Clamp(brushSize, 0, MaxBrushSize); // 최소 0 = 한 칸
    }

    void SetupPreview()
    {
        previewGO = new GameObject("BrushPreview");
        previewGO.transform.SetParent(Grid.transform, false); // 격자와 같은 좌표공간
        previewFilter = previewGO.AddComponent<MeshFilter>();
        var renderer = previewGO.AddComponent<MeshRenderer>();

        if (HighlightMaterial == null)
        {
            Shader sh = Shader.Find("Custom/HexHighlight");
            if (sh == null)
            {
                Debug.LogWarning("Custom/HexHighlight 셰이더를 찾지 못했습니다. " +
                                 "URP라면 Shader Graph로 반투명 단색 셰이더를 만들어 HighlightMaterial에 지정하세요.");
                sh = Shader.Find("Sprites/Default");
            }
            HighlightMaterial = new Material(sh) { color = new Color(1f, 1f, 1f, 0.35f) };
        }
        renderer.sharedMaterial = HighlightMaterial;

        previewMesh = new Mesh { name = "Brush Preview" };
        previewMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        previewFilter.mesh = previewMesh;
        previewGO.SetActive(false);

        provinceHighlightGO = new GameObject("SelectedProvinceHighlight");
        provinceHighlightGO.transform.SetParent(Grid.transform, false);
        provinceHighlightFilter = provinceHighlightGO.AddComponent<MeshFilter>();
        var provinceHighlightRenderer = provinceHighlightGO.AddComponent<MeshRenderer>();
        var provinceHighlightMaterial = new Material(HighlightMaterial) { color = new Color(0.1f, 1f, 0.45f, 0.38f) };
        provinceHighlightRenderer.sharedMaterial = provinceHighlightMaterial;

        provinceHighlightMesh = new Mesh { name = "Selected Province Highlight" };
        provinceHighlightMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        provinceHighlightFilter.mesh = provinceHighlightMesh;
        provinceHighlightGO.SetActive(false);

        provinceBorderGO = new GameObject("SelectedProvinceBorder");
        provinceBorderGO.transform.SetParent(Grid.transform, false);
        provinceBorderFilter = provinceBorderGO.AddComponent<MeshFilter>();
        var provinceBorderRenderer = provinceBorderGO.AddComponent<MeshRenderer>();
        var provinceBorderMaterial = new Material(HighlightMaterial) { color = new Color(1f, 0.92f, 0.65f, 0.95f) };
        provinceBorderRenderer.sharedMaterial = provinceBorderMaterial;

        provinceBorderMesh = new Mesh { name = "Selected Province Border" };
        provinceBorderMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        provinceBorderFilter.mesh = provinceBorderMesh;
        provinceBorderGO.SetActive(false);
    }

    void Update()
    {
        if (Grid == null) return;

        bool altHeld = false;
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            // 숫자키 1~9 → 인덱스 0~8 (1부터 시작). 현재 모드에 따라 지형/프로빈스를 선택한다.
            for (int i = 1; i < DigitKeys.Length; i++)
            {
                if (!keyboard[DigitKeys[i]].wasPressedThisFrame) continue;
                int sel = i - 1;
                if (paintMode == HexGrid.EditChannel.Terrain)
                {
                    TerrainType[] t = Grid.TerrainTypes;
                    if (t != null && sel < t.Length) activeTerrain = sel;
                }
                else
                {
                    if (sel < Grid.ProvinceCount) activeProvince = sel;
                }
            }

            // Ctrl/Cmd + Z = 되돌리기, +Shift 또는 Ctrl+Y = 다시, Ctrl/Cmd + S = 저장, Ctrl/Cmd + Shift + S = PNG 저장
            bool mod = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed
                     || keyboard.leftMetaKey.isPressed || keyboard.rightMetaKey.isPressed;
            bool shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            altHeld = keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed;

            if (mod && keyboard.zKey.wasPressedThisFrame)
            {
                if (shift) Grid.Redo(); else Grid.Undo();
            }
            else if (mod && keyboard.yKey.wasPressedThisFrame)
            {
                Grid.Redo();
            }
            else if (mod && keyboard.sKey.wasPressedThisFrame)
            {
                if (shift)
                {
                    Grid.SaveProvincePNG(ProvincePngPath);
                    status = "프로빈스 PNG 저장됨 (Ctrl+Shift+S)";
                }
                else
                {
                    Grid.SaveToFile(SavePath);
                    status = "저장됨 (Ctrl+S)";
                }
            }
        }

        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            // 패널 위에 커서가 있으면 카메라 휠 줌/우드래그 팬을 막는다(맵으로 새지 않게).
            if (Grid.CameraController != null)
                Grid.CameraController.BlockMouseOverUI = IsPointerOverPanel(mouse.position.ReadValue());

            // 프로빈스 모드 + Alt: 맵에서 클릭한 셀의 프로빈스를 자동으로 찾아 선택(스포이드)
            bool pickMode = paintMode == HexGrid.EditChannel.Province && altHeld;
            if (pickMode)
            {
                if (mouse.leftButton.wasPressedThisFrame)
                    PickProvinceAt(mouse.position.ReadValue());
                hasLastPaint = false;
            }
            else
            {
                if (mouse.leftButton.wasPressedThisFrame) { Grid.BeginStroke(paintMode); hasLastPaint = false; }
                if (mouse.leftButton.isPressed)
                {
                    Vector2 screenPos = mouse.position.ReadValue();
                    TryPaint(screenPos);
                    TryEdgePanWhilePainting(screenPos);
                }
                if (mouse.leftButton.wasReleasedThisFrame) { Grid.EndStroke(); hasLastPaint = false; }
            }
        }

        UpdateProvinceHighlight();
        UpdatePreview(mouse);
    }

    /// <summary>
    /// 포인터가 좌측 UI 패널의 실제 사각형 안에 있는지 판정한다.
    /// OnGUI는 좌상단 원점, 새 Input System의 마우스는 좌하단 원점이라 y를 변환한다.
    /// 패널 아래쪽 빈 공간에서는 false → 거기서도 정상적으로 칠할 수 있다.
    /// </summary>
    bool IsPointerOverPanel(Vector2 screenPos)
    {
        float guiY = Screen.height - screenPos.y; // 좌하단 → 좌상단 좌표
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return true;
        if (uiRoot != null)
            return RectTransformUtility.RectangleContainsScreenPoint(uiRoot, screenPos, null);

        return screenPos.x >= OriginX
            && screenPos.x <= OriginX + PanelAreaWidth * UIScale
            && guiY >= PanelTop
            && guiY <= PanelTop + lastPanelH * UIScale;
    }

    // 1단계: 표면이 평면(y=0)이라 콜라이더 없이 수학 평면으로 클릭 지점을 구한다.
    // (3단계에서 하이트맵 고도가 들어가면 높이 기반 피킹으로 교체)
    static readonly Plane GroundPlane = new Plane(Vector3.up, Vector3.zero);

    /// <summary>화면 좌표 → y=0 평면 교차점. 실패 시 false.</summary>
    bool TryGetGroundPoint(Vector2 screenPos, out Vector3 point)
    {
        point = default;
        Camera cam = Camera.main;
        if (cam == null) return false;

        Ray ray = cam.ScreenPointToRay(screenPos);
        if (!GroundPlane.Raycast(ray, out float enter)) return false;

        point = ray.GetPoint(enter);
        return true;
    }

    void TryPaint(Vector2 screenPos)
    {
        if (IsPointerOverPanel(screenPos)) return; // 패널 위 클릭만 무시(그 아래 빈 공간은 칠 가능)
        if (!TryGetGroundPoint(screenPos, out Vector3 point)) return;

        if (!hasLastPaint)
        {
            Stamp(point);
            lastPaintPos = point;
            hasLastPaint = true;
        }
        else
        {
            // 이전 지점 → 현재 지점 사이를 보간해 칠한다(빠르게 그어도 씹히지 않음).
            float cellW = HexMetrics.InnerRadius * 2f;
            float step = Mathf.Max(cellW * 0.5f, brushSize * cellW * 0.75f); // 브러시가 클수록 큰 간격
            float dist = Vector3.Distance(lastPaintPos, point);
            int steps = Mathf.Clamp(Mathf.CeilToInt(dist / step), 1, 256);
            for (int i = 1; i <= steps; i++)
                Stamp(Vector3.Lerp(lastPaintPos, point, (float)i / steps));
            lastPaintPos = point;
        }
    }

    void Stamp(Vector3 point)
    {
        if (paintMode == HexGrid.EditChannel.Terrain)
            Grid.PaintAt(point, activeTerrain, brushSize);
        else
            Grid.PaintProvinceAt(point, activeProvince, brushSize);
    }

    /// <summary>맵에서 클릭한 셀이 어느 프로빈스에 속하는지 자동으로 찾아 선택한다(스포이드).
    /// 무소속(-1) 셀이면 지우개로 선택된다.</summary>
    void PickProvinceAt(Vector2 screenPos)
    {
        if (IsPointerOverPanel(screenPos)) return;
        if (!TryGetGroundPoint(screenPos, out Vector3 point)) return;

        HexCell cell = Grid.GetCell(point);
        if (cell == null) return;

        activeProvince = cell.ProvinceIndex; // -1 = 무소속(지우개)
        status = cell.ProvinceIndex >= 0
            ? $"프로빈스 {cell.ProvinceIndex + 1} 선택 (스포이드)"
            : "무소속 셀 (스포이드)";
    }

    void TryEdgePanWhilePainting(Vector2 screenPos)
    {
        if (!EnablePaintEdgePan || IsPointerOverPanel(screenPos)) return;
        if (Grid.CameraController == null)
            Grid.CameraController = FindFirstObjectByType<HexCameraController>();
        if (Grid.CameraController == null) return;

        Vector2 direction = Vector2.zero;
        if (screenPos.x <= EdgePanMargin) direction.x -= 1f;
        else if (screenPos.x >= Screen.width - EdgePanMargin) direction.x += 1f;

        if (screenPos.y <= EdgePanMargin) direction.y -= 1f;
        else if (screenPos.y >= Screen.height - EdgePanMargin) direction.y += 1f;

        if (direction != Vector2.zero)
            Grid.CameraController.Pan(direction.normalized, EdgePanSpeed);
    }

    // ───────────────────────── 브러시 미리보기 ─────────────────────────

    void UpdatePreview(Mouse mouse)
    {
        if (previewGO == null || mouse == null) { HidePreview(); return; }
        if (Grid.TerrainTypes == null) { HidePreview(); return; } // 아직 빌드 전

        Vector2 screenPos = mouse.position.ReadValue();
        if (IsPointerOverPanel(screenPos)) { HidePreview(); return; }

        if (!TryGetGroundPoint(screenPos, out Vector3 point)) { HidePreview(); return; }

        HexCell center = Grid.GetCell(point);
        if (center == null) { HidePreview(); return; }

        // 큰 브러시는 미리보기 메시(범위 내 모든 셀)가 무거워 렉을 유발 → 일정 크기 이상이면 미리보기 생략
        if (brushSize > PreviewMaxBrush) { HidePreview(); return; }

        if (center == lastCenter && brushSize == lastBrush)
        {
            if (!previewGO.activeSelf) previewGO.SetActive(true);
            return;
        }
        lastCenter = center;
        lastBrush = brushSize;

        BuildPreview(Grid.GetBrushCells(point, brushSize));
        previewGO.SetActive(true);
    }

    void HidePreview()
    {
        if (previewGO != null && previewGO.activeSelf) previewGO.SetActive(false);
        lastCenter = null;
        lastBrush = -1;
    }

    void BuildPreview(List<HexCell> cells)
    {
        BuildCellOverlayMesh(cells, previewMesh, PreviewYOffset);
    }

    void UpdateProvinceHighlight()
    {
        if (provinceHighlightGO == null || provinceBorderGO == null || Grid == null) return;
        if (paintMode != HexGrid.EditChannel.Province || activeProvince < 0 || activeProvince >= Grid.ProvinceCount)
        {
            provinceHighlightGO.SetActive(false);
            provinceBorderGO.SetActive(false);
            lastHighlightedProvince = int.MinValue;
            lastProvinceHighlightVersion = -1;
            return;
        }

        if (activeProvince == lastHighlightedProvince && Grid.ProvinceEditVersion == lastProvinceHighlightVersion)
        {
            if (!provinceHighlightGO.activeSelf) provinceHighlightGO.SetActive(true);
            if (!provinceBorderGO.activeSelf) provinceBorderGO.SetActive(true);
            return;
        }

        BuildCellOverlayMesh(Grid.GetProvinceCells(activeProvince), provinceHighlightMesh, PreviewYOffset * 0.5f);
        BuildProvinceBorderMesh(Grid.GetProvinceBoundaryEdges(activeProvince), provinceBorderMesh, PreviewYOffset * 1.1f, 1.4f);
        lastHighlightedProvince = activeProvince;
        lastProvinceHighlightVersion = Grid.ProvinceEditVersion;
        provinceHighlightGO.SetActive(true);
        provinceBorderGO.SetActive(true);
    }

    void BuildProvinceBorderMesh(List<HexGrid.ProvinceEdge> edges, Mesh mesh, float yOffset, float width)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        float halfWidth = width * 0.5f;

        foreach (var edge in edges)
        {
            Vector3 a = edge.A;
            Vector3 b = edge.B;
            a.y += yOffset; b.y += yOffset;

            Vector3 dir = b - a;
            if (dir.sqrMagnitude <= 0.0001f) continue;
            dir.Normalize();
            Vector3 side = new Vector3(-dir.z, 0f, dir.x) * halfWidth;

            int idx = verts.Count;
            verts.Add(a - side);
            verts.Add(a + side);
            verts.Add(b + side);
            verts.Add(b - side);
            tris.Add(idx); tris.Add(idx + 1); tris.Add(idx + 2);
            tris.Add(idx); tris.Add(idx + 2); tris.Add(idx + 3);
        }

        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
    }

    void BuildCellOverlayMesh(List<HexCell> cells, Mesh mesh, float yOffset)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();

        foreach (HexCell cell in cells)
        {
            Vector3 c = cell.Position;
            for (int d = 0; d < 6; d++)
            {
                Vector3 v1 = c;
                Vector3 v2 = c + HexMetrics.Corners[d];
                Vector3 v3 = c + HexMetrics.Corners[d + 1];
                v1.y += yOffset; v2.y += yOffset; v3.y += yOffset;

                int idx = verts.Count;
                verts.Add(v1); verts.Add(v2); verts.Add(v3);
                tris.Add(idx); tris.Add(idx + 1); tris.Add(idx + 2);
            }
        }

        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
    }


    void BuildPrefabStyleMenuUI()
    {
        if (uiRoot != null) return;
        if (EventSystem.current == null)
        {
            var eventSystem = GameObject.Find("EventSystem") ?? new GameObject("EventSystem");
            EnsureComponent<EventSystem>(eventSystem);
            EnsureComponent<InputSystemUIInputModule>(eventSystem);
            eventSystem.transform.SetAsLastSibling();
        }

        var canvasGO = GameObject.Find("VictoriaStyleMainMenuCanvas")
            ?? new GameObject("VictoriaStyleMainMenuCanvas", typeof(RectTransform));
        var canvas = EnsureComponent<Canvas>(canvasGO);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        EnsureComponent<GraphicRaycaster>(canvasGO);
        var scaler = EnsureComponent<CanvasScaler>(canvasGO);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var panelTransform = canvasGO.transform.Find("MainMenuPrefabRoot");
        var panel = panelTransform != null ? panelTransform.gameObject : CreateUIObject("MainMenuPrefabRoot", canvasGO.transform);
        panel.transform.SetParent(canvasGO.transform, false);
        foreach (Transform child in panel.transform) Destroy(child.gameObject);
        uiRoot = EnsureComponent<RectTransform>(panel);
        uiRoot.anchorMin = new Vector2(0f, 0f);
        uiRoot.anchorMax = new Vector2(0f, 1f);
        uiRoot.pivot = new Vector2(0f, 0.5f);
        uiRoot.sizeDelta = new Vector2(360f, 0f);
        uiRoot.anchoredPosition = Vector2.zero;
        var bg = EnsureComponent<Image>(panel);
        bg.color = new Color(0.055f, 0.066f, 0.078f, 0.94f);
        var layout = EnsureComponent<VerticalLayoutGroup>(panel);
        layout.padding = new RectOffset(22, 22, 22, 22);
        layout.spacing = 10f;
        layout.childControlHeight = false;
        layout.childForceExpandHeight = false;
        EnsureComponent<ContentSizeFitter>(panel).verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        titleText = AddText(panel.transform, "Title", "Pax Map Command", 24, FontStyle.Bold);
        AddText(panel.transform, "Subtitle", "지도 제작 · 프로빈스 편집 · 파일 관리", 13, FontStyle.Normal, new Color(0.78f, 0.82f, 0.86f));

        var modes = AddRow(panel.transform, "ModeTabs");
        AddButton(modes, "지형", () => { paintMode = HexGrid.EditChannel.Terrain; RefreshMenuUI(); });
        AddButton(modes, "프로빈스", () => { paintMode = HexGrid.EditChannel.Province; RefreshMenuUI(); });

        brushLabel = AddText(panel.transform, "BrushLabel", "", 14, FontStyle.Bold);
        var sliderGO = CreateUIObject("BrushSlider", panel.transform);
        brushSlider = sliderGO.AddComponent<Slider>();
        brushSlider.minValue = 0; brushSlider.maxValue = MaxBrushSize; brushSlider.wholeNumbers = true; brushSlider.value = brushSize;
        BuildSliderVisuals(brushSlider);
        brushSlider.onValueChanged.AddListener(v => { brushSize = Mathf.RoundToInt(v); RefreshMenuUI(false); });
        SetLayout(sliderGO, 0, 28);

        var scrollGO = CreateUIObject("SelectionScrollView", panel.transform);
        SetLayout(scrollGO, 0, 410);
        var scrollRect = scrollGO.AddComponent<ScrollRect>();
        var viewport = CreateUIObject("Viewport", scrollGO.transform);
        viewport.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.16f);
        viewport.AddComponent<Mask>().showMaskGraphic = false;
        Stretch(viewport.GetComponent<RectTransform>());
        listContent = CreateUIObject("Content", viewport.transform).transform;
        var contentRt = listContent.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0, 1); contentRt.anchorMax = new Vector2(1, 1); contentRt.pivot = new Vector2(0.5f, 1); contentRt.sizeDelta = Vector2.zero;
        var contentLayout = listContent.gameObject.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(8, 8, 8, 8); contentLayout.spacing = 6; contentLayout.childControlHeight = false;
        listContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scrollRect.viewport = viewport.GetComponent<RectTransform>(); scrollRect.content = contentRt; scrollRect.horizontal = false;

        provinceActions = CreateUIObject("ProvinceActions", panel.transform);
        var pa = provinceActions.AddComponent<VerticalLayoutGroup>(); pa.spacing = 6; pa.childControlHeight = false;
        AddButton(provinceActions.transform, "+ 새 프로빈스", () => { activeProvince = Grid.AddProvince(); RefreshMenuUI(); });
        AddButton(provinceActions.transform, "- 선택 제거", RemoveActiveProvince);
        AddButton(provinceActions.transform, "지우개", () => { activeProvince = -1; RefreshMenuUI(); });
        AddButton(provinceActions.transform, "육지에만 칠하기 전환", () => { Grid.PaintLandOnly = !Grid.PaintLandOnly; RefreshMenuUI(); });
        AddButton(provinceActions.transform, "다른 프로빈스 보호 전환", () => { Grid.ProtectOtherProvinces = !Grid.ProtectOtherProvinces; RefreshMenuUI(); });
        AddButton(provinceActions.transform, "프로빈스 PNG 저장", () => { Grid.SaveProvincePNG(ProvincePngPath); SetStatus("프로빈스 PNG 저장됨"); });
        AddButton(provinceActions.transform, "프로빈스 PNG 불러오기", () => { SetStatus(Grid.LoadProvincePNG(ProvincePngPath) ? "프로빈스 PNG 불러옴" : "PNG 없음/크기불일치"); RefreshMenuUI(); });

        var fileRow = AddRow(panel.transform, "FileRow");
        AddButton(fileRow, "↶", () => { Grid.Undo(); RefreshMenuUI(); });
        AddButton(fileRow, "↷", () => { Grid.Redo(); RefreshMenuUI(); });
        AddButton(fileRow, "저장", () => { Grid.SaveToFile(SavePath); SetStatus("저장됨"); });
        AddButton(fileRow, "불러오기", () => { SetStatus(Grid.LoadFromFile(SavePath) ? "불러옴" : "저장 파일 없음"); RefreshMenuUI(); });

        var mapRow = AddRow(panel.transform, "NewMapRow");
        widthInput = AddInput(mapRow, widthText); heightInput = AddInput(mapRow, heightText);
        AddButton(mapRow, "새 맵 생성", CreateMapFromInputs);
        statusText = AddText(panel.transform, "Status", "", 13, FontStyle.Italic, new Color(0.95f, 0.82f, 0.48f));
    }

    void RefreshMenuUI(bool rebuildList = true)
    {
        if (Grid == null || uiRoot == null) return;
        if (titleText != null) titleText.text = $"Pax Map Command  {Grid.CurrentWidth}×{Grid.CurrentHeight}";
        if (brushLabel != null) brushLabel.text = $"브러시 {brushSize}  · 0=한 칸";
        if (brushSlider != null && (int)brushSlider.value != brushSize) brushSlider.value = brushSize;
        if (provinceActions != null) provinceActions.SetActive(paintMode == HexGrid.EditChannel.Province);
        if (!rebuildList) return;
        foreach (Transform child in listContent) Destroy(child.gameObject);
        if (paintMode == HexGrid.EditChannel.Terrain)
        {
            AddText(listContent, "TerrainHeader", "지형 목록 (숫자키 1~)", 14, FontStyle.Bold);
            var types = Grid.TerrainTypes;
            for (int i = 0; types != null && i < types.Length; i++)
            {
                int idx = i;
                AddButton(listContent, (idx == activeTerrain ? "● " : "○ ") + (idx + 1) + "  " + types[idx].id, () => { activeTerrain = idx; RefreshMenuUI(); }, ParseColor(types[idx].color));
            }
        }
        else
        {
            AddText(listContent, "ProvinceHeader", "프로빈스 목록 · Alt+클릭 스포이드", 14, FontStyle.Bold);
            for (int i = 0; i < Grid.ProvinceCount; i++)
            {
                int idx = i;
                AddButton(listContent, (idx == activeProvince ? "● " : "○ ") + "프로빈스 " + (idx + 1), () => { activeProvince = idx; RefreshMenuUI(); }, Grid.GetProvinceColor(idx));
            }
        }
    }

    void RemoveActiveProvince()
    {
        if (activeProvince < 0 || activeProvince >= Grid.ProvinceCount) return;
        int removed = activeProvince;
        if (Grid.RemoveProvince(removed)) activeProvince = Grid.ProvinceCount > 0 ? Mathf.Min(removed, Grid.ProvinceCount - 1) : -1;
        SetStatus($"프로빈스 {removed + 1} 제거됨"); RefreshMenuUI();
    }

    void CreateMapFromInputs()
    {
        if (int.TryParse(widthInput.text, out int w) && int.TryParse(heightInput.text, out int h)) { Grid.CreateBlankMap(w, h, MaxNewMapWidth, MaxNewMapHeight); RefreshMenuUI(); }
    }

    void SetStatus(string message) { status = message; if (statusText != null) statusText.text = message; }

    static T EnsureComponent<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        if (component != null) return component;
        return go.AddComponent<T>();
    }
    static GameObject CreateUIObject(string name, Transform parent) { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); return go; }
    static void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }
    static void SetLayout(GameObject go, float minW, float minH) { var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>(); le.minWidth = minW; le.minHeight = minH; }
    Text AddText(Transform parent, string name, string value, int size, FontStyle style, Color? color = null) { var go = CreateUIObject(name, parent); var t = go.AddComponent<Text>(); t.text = value; t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); t.fontSize = size; t.fontStyle = style; t.color = color ?? Color.white; SetLayout(go, 0, size + 8); return t; }
    Transform AddRow(Transform parent, string name) { var go = CreateUIObject(name, parent); var lg = go.AddComponent<HorizontalLayoutGroup>(); lg.spacing = 6; lg.childForceExpandWidth = true; lg.childControlWidth = true; SetLayout(go, 0, 34); return go.transform; }
    Button AddButton(Transform parent, string label, UnityEngine.Events.UnityAction action, Color? tint = null) { var go = CreateUIObject("Button_" + label, parent); var img = go.AddComponent<Image>(); img.color = tint ?? new Color(0.18f, 0.22f, 0.27f, 0.96f); var btn = go.AddComponent<Button>(); btn.targetGraphic = img; btn.onClick.AddListener(action); var txt = AddText(go.transform, "Text", label, 14, FontStyle.Bold); txt.alignment = TextAnchor.MiddleCenter; Stretch(txt.rectTransform); SetLayout(go, 0, 34); return btn; }
    InputField AddInput(Transform parent, string value) { var go = CreateUIObject("Input", parent); go.AddComponent<Image>().color = new Color(0.08f, 0.09f, 0.1f, 1f); var input = go.AddComponent<InputField>(); var text = AddText(go.transform, "Text", value, 14, FontStyle.Normal); text.alignment = TextAnchor.MiddleCenter; Stretch(text.rectTransform); input.textComponent = text; input.text = value; SetLayout(go, 70, 34); return input; }
    void BuildSliderVisuals(Slider slider) { var bg = slider.gameObject.AddComponent<Image>(); bg.color = new Color(0.12f, 0.14f, 0.16f, 1f); var fill = CreateUIObject("Fill", slider.transform).AddComponent<Image>(); fill.color = new Color(0.68f, 0.50f, 0.26f, 1f); Stretch(fill.rectTransform); slider.fillRect = fill.rectTransform; var handle = CreateUIObject("Handle", slider.transform).AddComponent<Image>(); handle.color = new Color(0.95f, 0.84f, 0.58f, 1f); handle.rectTransform.sizeDelta = new Vector2(18, 28); slider.handleRect = handle.rectTransform; }

    // ───────────────────────── UI ─────────────────────────

    void LegacyOnGUI_Disabled()
    {
        TerrainType[] types = Grid != null ? Grid.TerrainTypes : null;
        if (types == null) return;

        if (header == null)
            header = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
        if (content == null)
            // 콘텐츠 안쪽 여백. 스크롤바는 별도 여유 폭으로 오른쪽에 배치한다.
            content = new GUIStyle { padding = new RectOffset(16, 8, 4, 4) };

        Matrix4x4 prev = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(new Vector3(OriginX, PanelTop, 0f), Quaternion.identity, Vector3.one * UIScale);

        // 패널 높이를 화면에 맞춰 제한(매트릭스 스케일 고려). 내용이 길면 패널 전체가 스크롤된다.
        float availH = Screen.height / Mathf.Max(0.01f, UIScale) - PanelTop * 2f;
        lastPanelH = Mathf.Clamp(PanelHeight, 200f, availH);

        // 패널 박스는 기존 콘텐츠 폭까지만 그리고, 스크롤바는 박스 밖 오른쪽 거터에 둔다.
        GUI.Box(new Rect(0, 0, AreaWidth, lastPanelH), GUIContent.none, GUI.skin.box);
        GUILayout.BeginArea(new Rect(0, 0, PanelAreaWidth, lastPanelH), GUIStyle.none);
        // 가로 스크롤바 끄고, 세로는 항상 표시(레이아웃 일정하게)
        panelScroll = GUILayout.BeginScrollView(panelScroll, false, true,
            GUIStyle.none, GUI.skin.verticalScrollbar);

        // 콘텐츠 폭은 기존 크기로 유지하고, 스크롤바만 오른쪽 여유 공간으로 밀어낸다.
        GUILayout.BeginVertical(content, GUILayout.Width(AreaWidth));

        Color prevBg = GUI.backgroundColor;

        // ───── 헤더 · 모드 · 브러시 ─────
        GUILayout.Label($"맵 에디터  ({Grid.CurrentWidth}×{Grid.CurrentHeight})", header);

        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(paintMode == HexGrid.EditChannel.Terrain, "지형", "button", GUILayout.Height(24)))
            paintMode = HexGrid.EditChannel.Terrain;
        if (GUILayout.Toggle(paintMode == HexGrid.EditChannel.Province, "프로빈스", "button", GUILayout.Height(24)))
            paintMode = HexGrid.EditChannel.Province;
        GUILayout.EndHorizontal();

        GUILayout.Label($"브러시 {brushSize}   (0~{MaxBrushSize})  ·  0 = 한 칸");
        brushSize = Mathf.RoundToInt(GUILayout.HorizontalSlider(brushSize, 0, MaxBrushSize, GUILayout.Height(16)));
        GUILayout.Space(4);

        // ───── 목록 (지형 / 프로빈스) — 패널 전체 스크롤에 포함 ─────
        if (paintMode == HexGrid.EditChannel.Terrain)
        {
            GUILayout.Label("지형  (숫자키 1~)");
            for (int i = 0; i < types.Length; i++)
            {
                GUI.backgroundColor = ParseColor(types[i].color);
                if (GUILayout.Button((i == activeTerrain ? "●  " : "○  ") + (i + 1) + "   " + types[i].id, GUILayout.Height(24)))
                    activeTerrain = i;
            }
        }
        else
        {
            // 프로빈스 목록은 접을 수 있다(항목이 많을 때 패널을 간결하게).
            showProvinceList = GUILayout.Toggle(showProvinceList,
                (showProvinceList ? "▼ " : "▶ ") + $"프로빈스 목록  ({Grid.ProvinceCount}개, 숫자키 1~)",
                "button", GUILayout.Height(22));
            if (showProvinceList)
            {
                int pc = Grid.ProvinceCount;
                for (int i = 0; i < pc; i++)
                {
                    GUI.backgroundColor = Grid.GetProvinceColor(i);
                    if (GUILayout.Button((i == activeProvince ? "●  " : "○  ") + "프로빈스 " + (i + 1), GUILayout.Height(22)))
                        activeProvince = i;
                }
            }
        }
        GUI.backgroundColor = prevBg;

        // ───── 프로빈스 모드 액션 ─────
        if (paintMode == HexGrid.EditChannel.Province)
        {
            GUILayout.Label("Alt+클릭: 맵에서 프로빈스 선택 (스포이드)");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ 새 프로빈스", GUILayout.Height(22))) activeProvince = Grid.AddProvince();
            GUI.enabled = activeProvince >= 0 && activeProvince < Grid.ProvinceCount;
            if (GUILayout.Button("- 선택 제거", GUILayout.Height(22)))
            {
                int removed = activeProvince;
                if (Grid.RemoveProvince(removed))
                {
                    int pcAfter = Grid.ProvinceCount;
                    activeProvince = pcAfter > 0 ? Mathf.Min(removed, pcAfter - 1) : -1;
                    status = $"프로빈스 {removed + 1} 제거됨";
                }
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.backgroundColor = activeProvince < 0 ? Color.white : prevBg;
            if (GUILayout.Button(activeProvince < 0 ? "● 지우개" : "○ 지우개", GUILayout.Height(22))) activeProvince = -1;
            GUI.backgroundColor = prevBg;
            GUILayout.EndHorizontal();

            if (GUILayout.Button(Grid.PaintLandOnly ? "☑ 육지에만 칠하기" : "☐ 육지에만 칠하기", GUILayout.Height(22)))
                Grid.PaintLandOnly = !Grid.PaintLandOnly;
            if (GUILayout.Button(Grid.ProtectOtherProvinces ? "☑ 다른 프로빈스 보호" : "☐ 다른 프로빈스 보호", GUILayout.Height(22)))
                Grid.ProtectOtherProvinces = !Grid.ProtectOtherProvinces;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("PNG 저장", GUILayout.Height(22)))
            { Grid.SaveProvincePNG(ProvincePngPath); status = "프로빈스 PNG 저장됨"; }
            if (GUILayout.Button("PNG 불러오기", GUILayout.Height(22)))
            { status = Grid.LoadProvincePNG(ProvincePngPath) ? "프로빈스 PNG 불러옴" : "PNG 없음/크기불일치"; }
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(6);

        // ───── 파일·설정 (접기) ─────
        showFileSection = GUILayout.Toggle(showFileSection, showFileSection ? "▼ 파일 · 설정" : "▶ 파일 · 설정", "button", GUILayout.Height(22));
        if (showFileSection)
        {
            GUILayout.BeginHorizontal();
            GUI.enabled = Grid.CanUndo; if (GUILayout.Button("↶ 되돌리기", GUILayout.Height(22))) Grid.Undo();
            GUI.enabled = Grid.CanRedo; if (GUILayout.Button("↷ 다시", GUILayout.Height(22))) Grid.Redo();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("저장", GUILayout.Height(24))) { Grid.SaveToFile(SavePath); status = "저장됨"; }
            if (GUILayout.Button("불러오기", GUILayout.Height(24))) status = Grid.LoadFromFile(SavePath) ? "불러옴" : "저장 파일 없음";
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("새 맵", GUILayout.Width(34));
            widthText = GUILayout.TextField(widthText, GUILayout.Width(40), GUILayout.Height(20));
            GUILayout.Label("×", GUILayout.Width(12));
            heightText = GUILayout.TextField(heightText, GUILayout.Width(40), GUILayout.Height(20));
            if (GUILayout.Button("생성", GUILayout.Height(20)))
                if (int.TryParse(widthText, out int w) && int.TryParse(heightText, out int h))
                    Grid.CreateBlankMap(w, h, MaxNewMapWidth, MaxNewMapHeight);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"UI {UIScale:0.0}", GUILayout.Width(54));
            if (GUILayout.Button("−", GUILayout.Width(28), GUILayout.Height(20))) UIScale = Mathf.Max(1f, UIScale - 0.2f);
            if (GUILayout.Button("+", GUILayout.Width(28), GUILayout.Height(20))) UIScale = Mathf.Min(3f, UIScale + 0.2f);
            GUILayout.EndHorizontal();
        }

        if (!string.IsNullOrEmpty(status)) GUILayout.Label(status);

        GUILayout.EndVertical();
        GUILayout.EndScrollView();
        GUILayout.EndArea();
        GUI.matrix = prev;
    }

    static Color ParseColor(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return Color.gray;
        if (!hex.StartsWith("#")) hex = "#" + hex;
        return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.gray;
    }
}
