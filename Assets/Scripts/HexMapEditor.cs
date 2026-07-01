using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Runtime map editing controller.
/// UI is handled by HexMapEditorUI with real uGUI objects; this component keeps
/// input, painting, preview, shortcuts, and editor commands in one place.
/// </summary>
[DisallowMultipleComponent]
public class HexMapEditor : MonoBehaviour
{
    public HexGrid Grid;

    [Header("Startup")]
    public bool AutoCreateGameFlow = true;
    public bool StartInEditorMode = false;

    [Tooltip("Brush preview material. If empty, Custom/HexHighlight is used.")]
    public Material HighlightMaterial;
    public float PreviewYOffset = 0.2f;

    [Header("New Map Limits")]
    [Min(1)] public int MaxNewMapWidth = 5000;
    [Min(1)] public int MaxNewMapHeight = 5000;

    [Header("Brush")]
    [Min(1)] public int MaxBrushSize = 50;
    [Tooltip("Brushes larger than this skip the translucent preview mesh.")]
    [Min(0)] public int PreviewMaxBrush = 50;

    [Header("Edge Pan While Painting")]
    public bool EnablePaintEdgePan = true;
    public float EdgePanMargin = 48f;
    public float EdgePanSpeed = 2f;

    int activeTerrain = 1;
    int brushSize = 1;
    HexGrid.EditChannel paintMode = HexGrid.EditChannel.Terrain;
    int activeProvince = 0; // -1 = unassigned province paint

    Vector3 lastPaintPos;
    bool hasLastPaint;
    string status = "";

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

    static readonly Plane GroundPlane = new Plane(Vector3.up, Vector3.zero);

    static readonly Key[] DigitKeys =
    {
        Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4,
        Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9
    };

    public event Action StateChanged;

    public HexGrid.EditChannel PaintMode => paintMode;
    public int ActiveTerrain => activeTerrain;
    public int ActiveProvince => activeProvince;
    public int BrushSize => brushSize;
    public string Status => status;
    public string SavePath => System.IO.Path.Combine(Application.persistentDataPath, "edited_geometry.json");
    public string ProvincePngPath => System.IO.Path.Combine(Application.persistentDataPath, "provinces.png");
    public TerrainType[] TerrainTypes => Grid != null ? Grid.TerrainTypes : null;
    public int ProvinceCount => Grid != null ? Grid.ProvinceCount : 0;
    public bool CanUndo => Grid != null && Grid.CanUndo;
    public bool CanRedo => Grid != null && Grid.CanRedo;
    public bool IsEditorModeActive { get; private set; }

    void Awake()
    {
        if (AutoCreateGameFlow && GetComponent<HexGameFlow>() == null)
            gameObject.AddComponent<HexGameFlow>();
    }

    void Start()
    {
        ClampNewMapLimits();
        if (Grid == null) Grid = FindFirstObjectByType<HexGrid>();
        if (Grid != null) SetupPreview();
        if (StartInEditorMode) EnterEditorMode();
        else ExitEditorMode();
        NotifyStateChanged();
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
        PreviewMaxBrush = Mathf.Max(0, PreviewMaxBrush);
        brushSize = Mathf.Clamp(brushSize, 0, MaxBrushSize);
    }

    void EnsureObjectUi()
    {
        HexMapEditorUI ui = GetComponent<HexMapEditorUI>();
        if (ui == null) ui = gameObject.AddComponent<HexMapEditorUI>();
        ui.Bind(this);
    }

    void SetupPreview()
    {
        if (previewGO != null || Grid == null) return;

        previewGO = new GameObject("BrushPreview");
        previewGO.transform.SetParent(Grid.transform, false);
        previewFilter = previewGO.AddComponent<MeshFilter>();
        MeshRenderer renderer = previewGO.AddComponent<MeshRenderer>();

        if (HighlightMaterial == null)
        {
            Shader sh = Shader.Find("Custom/HexHighlight");
            if (sh == null)
            {
                Debug.LogWarning("Custom/HexHighlight shader was not found. Falling back to Sprites/Default.");
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
        MeshRenderer provinceHighlightRenderer = provinceHighlightGO.AddComponent<MeshRenderer>();
        Material provinceHighlightMaterial = new Material(HighlightMaterial) { color = new Color(0.1f, 1f, 0.45f, 0.38f) };
        provinceHighlightRenderer.sharedMaterial = provinceHighlightMaterial;

        provinceHighlightMesh = new Mesh { name = "Selected Province Highlight" };
        provinceHighlightMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        provinceHighlightFilter.mesh = provinceHighlightMesh;
        provinceHighlightGO.SetActive(false);

        provinceBorderGO = new GameObject("SelectedProvinceBorder");
        provinceBorderGO.transform.SetParent(Grid.transform, false);
        provinceBorderFilter = provinceBorderGO.AddComponent<MeshFilter>();
        MeshRenderer provinceBorderRenderer = provinceBorderGO.AddComponent<MeshRenderer>();
        Material provinceBorderMaterial = new Material(HighlightMaterial) { color = new Color(1f, 0.92f, 0.65f, 0.95f) };
        provinceBorderRenderer.sharedMaterial = provinceBorderMaterial;

        provinceBorderMesh = new Mesh { name = "Selected Province Border" };
        provinceBorderMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        provinceBorderFilter.mesh = provinceBorderMesh;
        provinceBorderGO.SetActive(false);
    }

    void Update()
    {
        if (Grid == null) return;
        if (!IsEditorModeActive)
        {
            if (Grid.CameraController != null) Grid.CameraController.BlockMouseOverUI = false;
            return;
        }

        ClampSelections();
        HandleKeyboard();

        Mouse mouse = Mouse.current;
        HandleMouse(mouse);

        UpdateProvinceHighlight();
        UpdatePreview(mouse);
    }

    void HandleKeyboard()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        for (int i = 1; i < DigitKeys.Length; i++)
        {
            if (!keyboard[DigitKeys[i]].wasPressedThisFrame) continue;
            int selection = i - 1;
            if (paintMode == HexGrid.EditChannel.Terrain) SelectTerrain(selection);
            else SelectProvince(selection);
        }

        bool mod = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed
                || keyboard.leftMetaKey.isPressed || keyboard.rightMetaKey.isPressed;
        bool shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;

        if (!mod) return;

        if (keyboard.zKey.wasPressedThisFrame)
        {
            if (shift) Redo(); else Undo();
        }
        else if (keyboard.yKey.wasPressedThisFrame)
        {
            Redo();
        }
        else if (keyboard.sKey.wasPressedThisFrame)
        {
            if (shift) SaveProvincePng();
            else SaveGeometry();
        }
    }

    void HandleMouse(Mouse mouse)
    {
        if (mouse == null) return;

        Vector2 screenPos = mouse.position.ReadValue();
        bool pointerOverUi = IsPointerOverEditorUi(screenPos);
        if (Grid.CameraController != null)
            Grid.CameraController.BlockMouseOverUI = pointerOverUi;

        Keyboard keyboard = Keyboard.current;
        bool altHeld = keyboard != null && (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed);
        bool pickMode = paintMode == HexGrid.EditChannel.Province && altHeld;

        if (pickMode)
        {
            if (mouse.leftButton.wasPressedThisFrame)
                PickProvinceAt(screenPos);
            hasLastPaint = false;
            return;
        }

        if (mouse.leftButton.wasPressedThisFrame)
        {
            Grid.BeginStroke(paintMode);
            hasLastPaint = false;
        }

        if (mouse.leftButton.isPressed)
        {
            TryPaint(screenPos);
            TryEdgePanWhilePainting(screenPos);
        }

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            Grid.EndStroke();
            hasLastPaint = false;
            NotifyStateChanged();
        }
    }

    public bool IsPointerOverEditorUi(Vector2 screenPos)
    {
        if (EventSystem.current == null) return false;
        return EventSystem.current.IsPointerOverGameObject(-1);
    }

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
        if (IsPointerOverEditorUi(screenPos)) return;
        if (!TryGetGroundPoint(screenPos, out Vector3 point)) return;

        if (!hasLastPaint)
        {
            Stamp(point);
            lastPaintPos = point;
            hasLastPaint = true;
            return;
        }

        float cellW = HexMetrics.InnerRadius * 2f;
        float step = Mathf.Max(cellW * 0.5f, brushSize * cellW * 0.75f);
        float dist = Vector3.Distance(lastPaintPos, point);
        int steps = Mathf.Clamp(Mathf.CeilToInt(dist / step), 1, 256);
        for (int i = 1; i <= steps; i++)
            Stamp(Vector3.Lerp(lastPaintPos, point, (float)i / steps));
        lastPaintPos = point;
    }

    void Stamp(Vector3 point)
    {
        if (paintMode == HexGrid.EditChannel.Terrain)
            Grid.PaintAt(point, activeTerrain, brushSize);
        else
            Grid.PaintProvinceAt(point, activeProvince, brushSize);
    }

    void PickProvinceAt(Vector2 screenPos)
    {
        if (IsPointerOverEditorUi(screenPos)) return;
        if (!TryGetGroundPoint(screenPos, out Vector3 point)) return;

        HexCell cell = Grid.GetCell(point);
        if (cell == null) return;

        activeProvince = cell.ProvinceIndex;
        SetStatus(cell.ProvinceIndex >= 0
            ? $"프로빈스 {cell.ProvinceIndex + 1} 선택"
            : "무소속 선택");
    }

    void TryEdgePanWhilePainting(Vector2 screenPos)
    {
        if (!EnablePaintEdgePan || IsPointerOverEditorUi(screenPos)) return;
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

    void UpdatePreview(Mouse mouse)
    {
        if (previewGO == null || mouse == null) { HidePreview(); return; }
        if (Grid.TerrainTypes == null) { HidePreview(); return; }

        Vector2 screenPos = mouse.position.ReadValue();
        if (IsPointerOverEditorUi(screenPos)) { HidePreview(); return; }
        if (!TryGetGroundPoint(screenPos, out Vector3 point)) { HidePreview(); return; }

        HexCell center = Grid.GetCell(point);
        if (center == null) { HidePreview(); return; }
        if (brushSize > PreviewMaxBrush) { HidePreview(); return; }

        if (center == lastCenter && brushSize == lastBrush)
        {
            if (!previewGO.activeSelf) previewGO.SetActive(true);
            return;
        }

        lastCenter = center;
        lastBrush = brushSize;

        BuildCellOverlayMesh(Grid.GetBrushCells(point, brushSize), previewMesh, PreviewYOffset);
        previewGO.SetActive(true);
    }

    void HidePreview()
    {
        if (previewGO != null && previewGO.activeSelf) previewGO.SetActive(false);
        lastCenter = null;
        lastBrush = -1;
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

        foreach (HexGrid.ProvinceEdge edge in edges)
        {
            Vector3 a = edge.A;
            Vector3 b = edge.B;
            a.y += yOffset;
            b.y += yOffset;

            Vector3 dir = b - a;
            if (dir.sqrMagnitude <= 0.0001f) continue;
            dir.Normalize();
            Vector3 side = new Vector3(-dir.z, 0f, dir.x) * halfWidth;

            int idx = verts.Count;
            verts.Add(a - side);
            verts.Add(a + side);
            verts.Add(b + side);
            verts.Add(b - side);
            tris.Add(idx);
            tris.Add(idx + 1);
            tris.Add(idx + 2);
            tris.Add(idx);
            tris.Add(idx + 2);
            tris.Add(idx + 3);
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
                v1.y += yOffset;
                v2.y += yOffset;
                v3.y += yOffset;

                int idx = verts.Count;
                verts.Add(v1);
                verts.Add(v2);
                verts.Add(v3);
                tris.Add(idx);
                tris.Add(idx + 1);
                tris.Add(idx + 2);
            }
        }

        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
    }

    public void SetMode(HexGrid.EditChannel mode)
    {
        if (paintMode == mode) return;
        paintMode = mode;
        HidePreview();
        NotifyStateChanged();
    }

    public void SetBrushSize(float value)
    {
        int next = Mathf.Clamp(Mathf.RoundToInt(value), 0, MaxBrushSize);
        if (brushSize == next) return;
        brushSize = next;
        lastBrush = -1;
        NotifyStateChanged();
    }

    public void SelectTerrain(int index)
    {
        TerrainType[] types = TerrainTypes;
        if (types == null || index < 0 || index >= types.Length) return;
        activeTerrain = index;
        NotifyStateChanged();
    }

    public void SelectProvince(int index)
    {
        if (index >= ProvinceCount) return;
        activeProvince = index;
        NotifyStateChanged();
    }

    public void AddProvince()
    {
        if (Grid == null) return;
        activeProvince = Grid.AddProvince();
        SetStatus($"프로빈스 {activeProvince + 1} 추가");
    }

    public void RemoveActiveProvince()
    {
        if (Grid == null || activeProvince < 0 || activeProvince >= Grid.ProvinceCount) return;
        int removed = activeProvince;
        if (!Grid.RemoveProvince(removed)) return;

        int count = Grid.ProvinceCount;
        activeProvince = count > 0 ? Mathf.Min(removed, count - 1) : -1;
        SetStatus($"프로빈스 {removed + 1} 제거");
    }

    public void TogglePaintLandOnly()
    {
        if (Grid == null) return;
        Grid.PaintLandOnly = !Grid.PaintLandOnly;
        NotifyStateChanged();
    }

    public void ToggleProtectOtherProvinces()
    {
        if (Grid == null) return;
        Grid.ProtectOtherProvinces = !Grid.ProtectOtherProvinces;
        NotifyStateChanged();
    }

    public void Undo()
    {
        if (Grid == null) return;
        Grid.Undo();
        NotifyStateChanged();
    }

    public void Redo()
    {
        if (Grid == null) return;
        Grid.Redo();
        NotifyStateChanged();
    }

    public void SaveGeometry()
    {
        if (Grid == null) return;
        Grid.SaveToFile(SavePath);
        SetStatus("맵 JSON 저장 완료");
    }

    public void LoadGeometry()
    {
        if (Grid == null) return;
        SetStatus(Grid.LoadFromFile(SavePath) ? "맵 JSON 불러오기 완료" : "저장된 맵 JSON 없음");
    }

    public void SaveProvincePng()
    {
        if (Grid == null) return;
        Grid.SaveProvincePNG(ProvincePngPath);
        SetStatus("프로빈스 PNG 저장 완료");
    }

    public void LoadProvincePng()
    {
        if (Grid == null) return;
        SetStatus(Grid.LoadProvincePNG(ProvincePngPath) ? "프로빈스 PNG 불러오기 완료" : "PNG 없음 또는 크기 불일치");
    }

    public void CreateBlankMap(int width, int height)
    {
        if (Grid == null) return;
        Grid.CreateBlankMap(width, height, MaxNewMapWidth, MaxNewMapHeight);
        activeTerrain = Mathf.Clamp(activeTerrain, 0, Mathf.Max(0, (TerrainTypes?.Length ?? 1) - 1));
        activeProvince = Mathf.Clamp(activeProvince, -1, Mathf.Max(-1, ProvinceCount - 1));
        SetStatus($"새 맵 생성: {Grid.CurrentWidth}x{Grid.CurrentHeight}");
    }

    public Color GetTerrainColor(int index)
    {
        TerrainType[] types = TerrainTypes;
        if (types == null || index < 0 || index >= types.Length) return Color.gray;
        return ParseColor(types[index].color);
    }

    public Color GetProvinceColor(int index)
    {
        return Grid != null && index >= 0 && index < Grid.ProvinceCount
            ? Grid.GetProvinceColor(index)
            : Color.white;
    }

    public bool TryGetTerrainName(int index, out string name)
    {
        TerrainType[] types = TerrainTypes;
        if (types == null || index < 0 || index >= types.Length)
        {
            name = "";
            return false;
        }
        name = string.IsNullOrEmpty(types[index].id) ? $"Terrain {index + 1}" : types[index].id;
        return true;
    }

    public void NotifyStateChanged()
    {
        ClampSelections();
        StateChanged?.Invoke();
    }

    void SetStatus(string value)
    {
        status = value;
        NotifyStateChanged();
    }

    void ClampSelections()
    {
        TerrainType[] types = TerrainTypes;
        if (types != null && types.Length > 0)
            activeTerrain = Mathf.Clamp(activeTerrain, 0, types.Length - 1);
        else
            activeTerrain = 0;

        activeProvince = Mathf.Clamp(activeProvince, -1, Mathf.Max(-1, ProvinceCount - 1));
        brushSize = Mathf.Clamp(brushSize, 0, MaxBrushSize);
    }

    public static Color ParseColor(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return Color.gray;
        if (!hex.StartsWith("#")) hex = "#" + hex;
        return ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.gray;
    }

    public void EnterEditorMode()
    {
        IsEditorModeActive = true;
        EnsureObjectUi();
        HexMapEditorUI ui = GetComponent<HexMapEditorUI>();
        if (ui != null)
        {
            ui.enabled = true;
            ui.Bind(this);
        }
        NotifyStateChanged();
    }

    public void ExitEditorMode()
    {
        IsEditorModeActive = false;
        HidePreview();
        if (provinceHighlightGO != null) provinceHighlightGO.SetActive(false);
        if (provinceBorderGO != null) provinceBorderGO.SetActive(false);
        if (Grid != null && Grid.CameraController != null) Grid.CameraController.BlockMouseOverUI = false;

        HexMapEditorUI ui = GetComponent<HexMapEditorUI>();
        if (ui != null) ui.enabled = false;
        NotifyStateChanged();
    }
}
