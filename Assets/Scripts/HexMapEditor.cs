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
/// </summary>
public class HexMapEditor : MonoBehaviour
{
    public HexGrid Grid;

    [Tooltip("UI 패널 배율. 패널의 ± 버튼으로도 조절.")]
    [Range(1f, 3f)] public float UIScale = 1.8f;

    [Tooltip("브러시 미리보기 머티리얼. 비우면 Custom/HexHighlight로 자동 생성.")]
    public Material HighlightMaterial;
    public float PreviewYOffset = 0.2f;

    [Header("새 맵 생성 제한")]
    [Min(1)] public int MaxNewMapWidth = 200;
    [Min(1)] public int MaxNewMapHeight = 200;

    [Header("브러시 설정")]
    [Min(0)] public int MaxBrushSize = 20;

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
    const float PanelHeight = 610f;

    int activeTerrain = 1;
    int brushSize = 0;
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
        brushSize = Mathf.Clamp(brushSize, 0, MaxBrushSize);
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
            if (mouse.leftButton.wasPressedThisFrame) Grid.BeginStroke();
            if (mouse.leftButton.isPressed)
            {
                Vector2 screenPos = mouse.position.ReadValue();
                TryPaint(screenPos);
                TryEdgePanWhilePainting(screenPos);
            }
            if (mouse.leftButton.wasReleasedThisFrame) Grid.EndStroke();
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

    void TryPaint(Vector2 screenPos)
    {
        if (IsPointerOverPanel(screenPos)) return; // 패널 위 클릭만 무시(그 아래 빈 공간은 칠 가능)

        Camera cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit))
            Grid.PaintAt(hit.point, activeTerrain, brushSize);
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

        Camera cam = Camera.main;
        if (cam == null) { HidePreview(); return; }

        Ray ray = cam.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit)) { HidePreview(); return; }

        HexCell center = Grid.GetCell(hit.point);
        if (center == null) { HidePreview(); return; }

        if (center == lastCenter && brushSize == lastBrush)
        {
            if (!previewGO.activeSelf) previewGO.SetActive(true);
            return;
        }
        lastCenter = center;
        lastBrush = brushSize;

        BuildPreview(Grid.GetBrushCells(hit.point, brushSize));
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

        GUILayout.Label("지형  (숫자키 0~)");
        Color prevBg = GUI.backgroundColor;
        for (int i = 0; i < types.Length; i++)
        {
            GUI.backgroundColor = ParseColor(types[i].color);
            string mark = i == activeTerrain ? "●  " : "○  ";
            if (GUILayout.Button(mark + i + "   " + types[i].id, GUILayout.Height(24)))
                activeTerrain = i;
        }
        GUI.backgroundColor = prevBg;

        GUILayout.Space(8);

        GUILayout.Label($"브러시 크기 (최대 {MaxBrushSize})");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("−", GUILayout.Width(30), GUILayout.Height(24)))
            brushSize = Mathf.Max(0, brushSize - 1);
        GUILayout.Label(brushSize.ToString(), GUILayout.Width(36));
        if (GUILayout.Button("+", GUILayout.Width(30), GUILayout.Height(24)))
            brushSize = Mathf.Min(MaxBrushSize, brushSize + 1);
        GUILayout.EndHorizontal();

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
