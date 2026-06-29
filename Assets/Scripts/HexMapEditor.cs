using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem; // 새 Input System

/// <summary>
/// 인게임 맵 에디터 (새 Input System).
/// - 좌클릭/드래그: 선택 지형으로 칠하기 (지우개는 ocean=0 선택 후 칠하기)
/// - 좌클릭으로 칠하는 중 화면 가장자리에 가까워지면 자동으로 카메라 이동
/// - 숫자키 0~: 지형 선택
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

    const float AreaWidth = 190f;
    const float OriginX = 12f;
    const float PanelTop = 12f;
    const float PanelHeight = 720f;

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

    // 미리보기
    GameObject previewGO;
    MeshFilter previewFilter;
    Mesh previewMesh;
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

    void Start()
    {
        ClampNewMapLimits();
        if (Grid != null) SetupPreview();
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
        brushSize = Mathf.Clamp(brushSize, 1, MaxBrushSize);
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
        previewFilter.mesh = previewMesh;
        previewGO.SetActive(false);
    }

    void Update()
    {
        if (Grid == null) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            for (int i = 0; i < DigitKeys.Length; i++)
                if (keyboard[DigitKeys[i]].wasPressedThisFrame)
                    activeTerrain = i;

            // Ctrl/Cmd + Z = 되돌리기, +Shift 또는 Ctrl+Y = 다시
            bool mod = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed
                     || keyboard.leftMetaKey.isPressed || keyboard.rightMetaKey.isPressed;
            bool shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;

            if (mod && keyboard.zKey.wasPressedThisFrame)
            {
                if (shift) Grid.Redo(); else Grid.Undo();
            }
            else if (mod && keyboard.yKey.wasPressedThisFrame)
            {
                Grid.Redo();
            }
        }

        Mouse mouse = Mouse.current;
        if (mouse != null)
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
        return screenPos.x >= OriginX
            && screenPos.x <= OriginX + AreaWidth * UIScale
            && guiY >= PanelTop
            && guiY <= PanelTop + PanelHeight * UIScale;
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
                v1.y += PreviewYOffset; v2.y += PreviewYOffset; v3.y += PreviewYOffset;

                int idx = verts.Count;
                verts.Add(v1); verts.Add(v2); verts.Add(v3);
                tris.Add(idx); tris.Add(idx + 1); tris.Add(idx + 2);
            }
        }

        previewMesh.Clear();
        previewMesh.SetVertices(verts);
        previewMesh.SetTriangles(tris, 0);
        previewMesh.RecalculateBounds();
    }

    // ───────────────────────── UI ─────────────────────────

    void OnGUI()
    {
        TerrainType[] types = Grid != null ? Grid.TerrainTypes : null;
        if (types == null) return;

        if (header == null)
            header = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };

        Matrix4x4 prev = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(new Vector3(OriginX, PanelTop, 0f),
                                   Quaternion.identity, Vector3.one * UIScale);

        GUILayout.BeginArea(new Rect(0, 0, AreaWidth, PanelHeight), GUI.skin.box);

        GUILayout.Label($"맵 에디터  ({Grid.CurrentWidth}×{Grid.CurrentHeight})", header);
        GUILayout.Label("좌클릭 칠 · 가장자리 자동 이동 · 우드래그 이동 · 휠 줌");
        GUILayout.Space(4);

        // 편집 모드 토글: 지형 / 프로빈스
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(paintMode == HexGrid.EditChannel.Terrain ? "● 지형" : "○ 지형", GUILayout.Height(24)))
            paintMode = HexGrid.EditChannel.Terrain;
        if (GUILayout.Button(paintMode == HexGrid.EditChannel.Province ? "● 프로빈스" : "○ 프로빈스", GUILayout.Height(24)))
            paintMode = HexGrid.EditChannel.Province;
        GUILayout.EndHorizontal();
        GUILayout.Space(4);

        Color prevBg = GUI.backgroundColor;
        if (paintMode == HexGrid.EditChannel.Terrain)
        {
            GUILayout.Label("지형  (숫자키 0~)");
            for (int i = 0; i < types.Length; i++)
            {
                GUI.backgroundColor = ParseColor(types[i].color);
                string mark = i == activeTerrain ? "●  " : "○  ";
                if (GUILayout.Button(mark + i + "   " + types[i].id, GUILayout.Height(24)))
                    activeTerrain = i;
            }
            GUI.backgroundColor = prevBg;
        }
        else
        {
            GUILayout.Label("프로빈스");
            int pc = Grid.ProvinceCount;
            for (int i = 0; i < pc; i++)
            {
                GUI.backgroundColor = Grid.GetProvinceColor(i);
                string mark = i == activeProvince ? "●  " : "○  ";
                if (GUILayout.Button(mark + "프로빈스 " + i, GUILayout.Height(22)))
                    activeProvince = i;
            }
            GUI.backgroundColor = prevBg;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ 새 프로빈스", GUILayout.Height(22)))
                activeProvince = Grid.AddProvince();
            GUI.backgroundColor = activeProvince < 0 ? Color.white : prevBg;
            if (GUILayout.Button(activeProvince < 0 ? "● 지우개" : "○ 지우개", GUILayout.Height(22)))
                activeProvince = -1; // 무소속으로 칠하기
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

        GUILayout.Space(8);

        GUILayout.Label($"브러시 크기: {brushSize}  (1~{MaxBrushSize})");
        brushSize = Mathf.RoundToInt(GUILayout.HorizontalSlider(brushSize, 1, MaxBrushSize, GUILayout.Height(18)));

        GUILayout.Space(8);

        GUILayout.Label("UI 크기");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("−", GUILayout.Width(30), GUILayout.Height(24)))
            UIScale = Mathf.Max(1f, UIScale - 0.2f);
        GUILayout.Label(UIScale.ToString("0.0"), GUILayout.Width(36));
        if (GUILayout.Button("+", GUILayout.Width(30), GUILayout.Height(24)))
            UIScale = Mathf.Min(3f, UIScale + 0.2f);
        GUILayout.EndHorizontal();

        GUILayout.Space(8);

        GUILayout.Label($"새 맵 크기 (W × H, 최대 {MaxNewMapWidth}×{MaxNewMapHeight})");
        GUILayout.BeginHorizontal();
        widthText = GUILayout.TextField(widthText, GUILayout.Width(46), GUILayout.Height(22));
        GUILayout.Label("×", GUILayout.Width(14));
        heightText = GUILayout.TextField(heightText, GUILayout.Width(46), GUILayout.Height(22));
        GUILayout.EndHorizontal();
        if (GUILayout.Button("새 맵 생성", GUILayout.Height(26)))
            if (int.TryParse(widthText, out int w) && int.TryParse(heightText, out int h))
                Grid.CreateBlankMap(w, h, MaxNewMapWidth, MaxNewMapHeight);

        GUILayout.Space(10);

        GUILayout.Label("되돌리기");
        GUILayout.BeginHorizontal();
        GUI.enabled = Grid.CanUndo;
        if (GUILayout.Button("↶ Ctrl+Z", GUILayout.Height(24))) Grid.Undo();
        GUI.enabled = Grid.CanRedo;
        if (GUILayout.Button("↷ Ctrl+Y", GUILayout.Height(24))) Grid.Redo();
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.Space(10);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("저장", GUILayout.Height(28)))
        {
            Grid.SaveToFile(SavePath);
            status = "저장됨";
        }
        if (GUILayout.Button("불러오기", GUILayout.Height(28)))
        {
            status = Grid.LoadFromFile(SavePath) ? "불러옴" : "저장 파일 없음";
        }
        GUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(status))
            GUILayout.Label(status);

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
