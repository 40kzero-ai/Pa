using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// [1단계: 빅토리아3식 텍스처 구동]
/// 셀마다 메시를 만들지 않는다. 표면은 평면 쿼드 1장이고, 프래그먼트 셰이더가 픽셀마다
/// "어느 헥스 셀인지"를 좌표 역산해 지형/프로빈스 텍스처를 샘플한다. 600만 셀이어도
/// 메시는 쿼드 1개, 콜라이더 0, 빌드는 텍스처 업로드 수준으로 빨라진다.
///
/// 데이터 진실값은 배열(data.terrain / data.provinceMap). 텍스처는 그 거울이고, 페인팅은
/// 배열 + 텍스처 픽셀을 함께 갱신한다(즉시 반영). PNG 입출력은 2단계에서 추가.
/// </summary>
public class HexGrid : MonoBehaviour
{
    [Header("입력")]
    public TextAsset GeometryJson;

    [Tooltip("HexTerrainProvince 셰이더로 만든 머티리얼.")]
    public Material TerrainMaterial;

    [Tooltip("비워두면 씬에서 자동으로 찾는다.")]
    public HexCameraController CameraController;
    public bool AutoFrameCamera = true;

    [Header("부팅")]
    [SerializeField] bool buildOnStart = true;


    [Header("프로빈스 표시")]
    [Range(0f, 1f)] public float ProvinceTint = 0.75f;
    public bool ShowBorders = true;
    public Color BorderColor = new Color(0.05f, 0.04f, 0.03f, 1f);
    public float BorderWidth = 1.5f;
    [Tooltip("켜면 프로빈스를 바다(ocean) 셀에는 칠하지 않는다.")]
    public bool PaintLandOnly = true;
    [Tooltip("켜면 이미 다른 프로빈스가 있는 셀은 덮어쓰지 않는다(빈 땅에만 칠함). 지우개(-1)는 예외.")]
    public bool ProtectOtherProvinces = false;

    [Header("고도(y축, 3단계 예약)")]
    public float HeightScale = 0f;
    [Tooltip("하이트맵 변위를 위한 평면 분할 수. 평평한 1단계에선 1로 충분.")]
    [Min(1)] public int SurfaceSubdivisions = 1;

    GeometryData data;
    HexCell[] cells;

    GameObject surfaceGO;
    Mesh surfaceMesh;
    Material matInstance;
    Texture2D terrainTex, provinceTex, terrainPalette, provincePalette;

    public TerrainType[] TerrainTypes => data != null ? data.terrainTypes : null;
    public int CurrentWidth => data != null ? data.grid.width : 0;
    public int CurrentHeight => data != null ? data.grid.height : 0;
    public int ProvinceEditVersion { get; private set; }
    public bool IsBuilding { get; private set; }
    public event System.Action BuildCompleted;
    public struct ProvinceEdge { public Vector3 A; public Vector3 B; }

    // ── 되돌리기(지형/프로빈스 페인팅 단위) ──
    public enum EditChannel { Terrain, Province }
    struct CellChange { public HexCell Cell; public int Old; public int New; }
    class EditEntry { public EditChannel Channel; public readonly List<CellChange> Changes = new List<CellChange>(); }
    readonly Stack<EditEntry> undoStack = new Stack<EditEntry>();
    readonly Stack<EditEntry> redoStack = new Stack<EditEntry>();
    Dictionary<HexCell, int> strokeOld;
    EditChannel strokeChannel;
    bool recording;
    public bool CanUndo => undoStack.Count > 0;
    public bool CanRedo => redoStack.Count > 0;

    void Start()
    {
        if (!buildOnStart) return;
        if (CameraController == null) CameraController = FindFirstObjectByType<HexCameraController>();
        if (GeometryJson == null) { Debug.LogError("HexGrid: GeometryJson이 비어 있습니다."); return; }
        Build(HexGeometryLoader.Load(GeometryJson.text));
    }

    // ───────────────────────── 빌드 ─────────────────────────

    Coroutine buildRoutine;
    bool building; // 빌드 진행 중에는 data와 cells가 일시적으로 불일치할 수 있어 피킹을 막는다

    public void Build(GeometryData newData)
    {
        // 진행 중인 빌드가 있으면 중단(연속 빌드 요청 대비)
        if (buildRoutine != null) { StopCoroutine(buildRoutine); buildRoutine = null; }

        // 활성 상태면 코루틴으로 단계별 진행(각 단계 로그가 완료될 때마다 바로 콘솔에 표시됨).
        // 비활성/에디트 타임이면 즉시 동기 완주.
        if (isActiveAndEnabled)
            buildRoutine = StartCoroutine(BuildRoutine(newData));
        else
        {
            var e = BuildRoutine(newData);
            while (e.MoveNext()) { }
        }
    }

    // 단계마다 yield로 한 프레임 양보 → Debug.Log가 한꺼번에가 아니라 단계 완료 시점마다 표시된다.
    System.Collections.IEnumerator BuildRoutine(GeometryData newData)
    {
        var total = System.Diagnostics.Stopwatch.StartNew();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        building = true;
        IsBuilding = true;
        data = newData;
        oceanIndexCache = -2; // 지형 종류가 바뀔 수 있으니 ocean 인덱스 캐시 무효화
        Debug.Log($"맵 빌드 시작: {data.grid.width}x{data.grid.height} / 지형 {data.terrainTypes?.Length ?? 0}종 / 프로빈스 {data.provinces?.Length ?? 0}개");

        undoStack.Clear(); redoStack.Clear(); recording = false; strokeOld?.Clear();
        yield return null;

        BuildPalettes();
        Debug.Log($"[HexGrid] 팔레트: {sw.Elapsed.TotalMilliseconds:F1} ms"); sw.Restart();
        yield return null;

        BuildDataTextures();
        Debug.Log($"[HexGrid] 지형/프로빈스 텍스처: {sw.Elapsed.TotalMilliseconds:F1} ms"); sw.Restart();
        yield return null;

        CreateCells();
        Debug.Log($"[HexGrid] 논리 셀(브러시/피킹용): {sw.Elapsed.TotalMilliseconds:F1} ms"); sw.Restart();
        yield return null;

        CreateSurface();
        Debug.Log($"[HexGrid] 평면 메시: {sw.Elapsed.TotalMilliseconds:F1} ms"); sw.Restart();
        yield return null;

        SetupMaterial();
        Debug.Log($"[HexGrid] 머티리얼: {sw.Elapsed.TotalMilliseconds:F1} ms"); sw.Restart();
        yield return null;

        FrameCamera();
        Debug.Log($"[HexGrid] 카메라: {sw.Elapsed.TotalMilliseconds:F1} ms"); sw.Restart();

        ProvinceEditVersion++;

        building = false;
        IsBuilding = false;
        BuildCompleted?.Invoke();

        total.Stop();
        int w = data.grid.width, h = data.grid.height;
        Debug.Log($"[HexGrid] ▶ 총 빌드: {total.Elapsed.TotalMilliseconds:F1} ms  ({w}x{h}, 셀 {w * h:N0}개)");
        buildRoutine = null;
    }

    public void CreateBlankMap(int width, int height) => CreateBlankMap(width, height, 200, 200);

    public void CreateBlankMap(int width, int height, int maxWidth, int maxHeight)
    {
        width = Mathf.Clamp(width, 1, Mathf.Max(1, maxWidth));
        height = Mathf.Clamp(height, 1, Mathf.Max(1, maxHeight));

        var blank = new GeometryData
        {
            formatVersion = 1,
            grid = new GridInfo { width = width, height = height },
            terrainTypes = (data?.terrainTypes != null && data.terrainTypes.Length > 0) ? data.terrainTypes : DefaultTerrainTypes(),
            terrain = new int[width * height],
            elevation = null,
            provinces = data?.provinces,
            provinceMap = null
        };
        Build(blank);
    }

    static TerrainType[] DefaultTerrainTypes() => new[]
    {
        new TerrainType { id = "ocean",    color = "3A6EA5" },
        new TerrainType { id = "plains",   color = "8FB36B" },
        new TerrainType { id = "forest",   color = "4E7A4A" },
        new TerrainType { id = "hills",    color = "B0A06A" },
        new TerrainType { id = "mountain", color = "8C7B6B" },
    };

    // ───────────────────────── 편집 API ─────────────────────────

    public HexCell GetCell(Vector3 worldPosition)
    {
        if (cells == null || data == null || building) return null;
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        HexCoordinates c = HexCoordinates.FromPosition(local);
        int offsetX = c.X + c.Z / 2;
        if (c.Z < 0 || c.Z >= data.grid.height || offsetX < 0 || offsetX >= data.grid.width) return null;
        int index = offsetX + c.Z * data.grid.width;
        if (index < 0 || index >= cells.Length) return null; // data와 cells 크기 불일치 방어
        return cells[index];
    }

    /// <summary>지형 페인팅: 지형 텍스처 픽셀만 갱신(즉시 반영).</summary>
    public void PaintAt(Vector3 worldPosition, int terrainType, int brushRadius)
    {
        if (terrainTex == null) return;
        HexCell center = GetCell(worldPosition);
        if (center == null) return;

        bool changed = false;
        foreach (HexCell cell in GetCellsInRange(center, brushRadius))
        {
            if (cell.TerrainType == terrainType) continue;
            if (recording && strokeChannel == EditChannel.Terrain && !strokeOld.ContainsKey(cell))
                strokeOld[cell] = cell.TerrainType;
            SetTerrain(cell, terrainType);
            changed = true;
        }
        if (changed) terrainTex.Apply(false);
    }

    public void EditCellAt(Vector3 worldPosition, int terrainType) => PaintAt(worldPosition, terrainType, 0);

    /// <summary>프로빈스 페인팅: provinceMap 배열 + 프로빈스 텍스처(idx+1) 갱신. (-1 = 프로빈스 지움)
    /// PaintLandOnly가 켜져 있으면 바다(ocean) 셀은 건너뛴다.</summary>
    public void PaintProvinceAt(Vector3 worldPosition, int provinceIndex, int brushRadius)
    {
        if (provinceTex == null) return;
        HexCell center = GetCell(worldPosition);
        if (center == null) return;

        int ocean = OceanTerrainIndex;
        EnsureProvinceMap();
        bool changed = false;
        foreach (HexCell cell in GetCellsInRange(center, brushRadius))
        {
            if (PaintLandOnly && cell.TerrainType == ocean) continue; // 바다엔 안 칠함
            // 다른 프로빈스 덮어쓰기 방지(지우개 -1은 예외로 항상 지움)
            if (ProtectOtherProvinces && provinceIndex >= 0 &&
                cell.ProvinceIndex >= 0 && cell.ProvinceIndex != provinceIndex) continue;
            if (cell.ProvinceIndex == provinceIndex) continue;
            if (recording && strokeChannel == EditChannel.Province && !strokeOld.ContainsKey(cell))
                strokeOld[cell] = cell.ProvinceIndex;
            SetProvince(cell, provinceIndex);
            changed = true;
        }
        if (changed)
        {
            provinceTex.Apply(false);
            ProvinceEditVersion++;
        }
    }

    // id가 "ocean"인 지형 인덱스(없으면 0). 캐시.
    int oceanIndexCache = -2;
    int OceanTerrainIndex
    {
        get
        {
            if (oceanIndexCache != -2) return oceanIndexCache;
            oceanIndexCache = 0;
            if (data?.terrainTypes != null)
                for (int i = 0; i < data.terrainTypes.Length; i++)
                    if (data.terrainTypes[i].id != null &&
                        data.terrainTypes[i].id.Equals("ocean", System.StringComparison.OrdinalIgnoreCase))
                    { oceanIndexCache = i; break; }
            return oceanIndexCache;
        }
    }

    void SetTerrain(HexCell cell, int t)
    {
        cell.TerrainType = t;
        data.terrain[cell.Z * data.grid.width + cell.X] = t;
        terrainTex.SetPixel(cell.X, cell.Z, new Color(t, 0, 0, 0));
    }

    void SetProvince(HexCell cell, int p)
    {
        cell.ProvinceIndex = p;
        data.provinceMap[cell.Z * data.grid.width + cell.X] = p;
        provinceTex.SetPixel(cell.X, cell.Z, new Color(p < 0 ? 0 : p + 1, 0, 0, 0));
    }

    void EnsureProvinceMap()
    {
        int len = data.grid.width * data.grid.height;
        if (data.provinceMap == null || data.provinceMap.Length != len)
        {
            data.provinceMap = new int[len];
            for (int i = 0; i < len; i++) data.provinceMap[i] = -1;
        }
    }

    public void BeginStroke(EditChannel channel)
    {
        recording = true; strokeChannel = channel;
        strokeOld ??= new Dictionary<HexCell, int>();
        strokeOld.Clear();
    }
    // 호환용(지형). 새 코드는 BeginStroke(channel)를 쓰세요.
    public void BeginStroke() => BeginStroke(EditChannel.Terrain);

    public void EndStroke()
    {
        recording = false;
        if (strokeOld == null || strokeOld.Count == 0) return;
        var entry = new EditEntry { Channel = strokeChannel };
        foreach (var kv in strokeOld)
        {
            int now = strokeChannel == EditChannel.Terrain ? kv.Key.TerrainType : kv.Key.ProvinceIndex;
            if (kv.Value != now) entry.Changes.Add(new CellChange { Cell = kv.Key, Old = kv.Value, New = now });
        }
        strokeOld.Clear();
        if (entry.Changes.Count == 0) return;
        undoStack.Push(entry); redoStack.Clear();
    }

    public void Undo() { if (undoStack.Count == 0) return; var e = undoStack.Pop(); ApplyEntry(e, true); redoStack.Push(e); }
    public void Redo() { if (redoStack.Count == 0) return; var e = redoStack.Pop(); ApplyEntry(e, false); undoStack.Push(e); }

    void ApplyEntry(EditEntry entry, bool undo)
    {
        foreach (CellChange ch in entry.Changes)
        {
            int v = undo ? ch.Old : ch.New;
            if (entry.Channel == EditChannel.Terrain) SetTerrain(ch.Cell, v);
            else SetProvince(ch.Cell, v);
        }
        if (entry.Channel == EditChannel.Terrain) terrainTex.Apply(false);
        else
        {
            provinceTex.Apply(false);
            ProvinceEditVersion++;
        }
    }

    public List<HexCell> GetBrushCells(Vector3 worldPosition, int brushRadius)
    {
        HexCell center = GetCell(worldPosition);
        return center == null ? new List<HexCell>() : GetCellsInRange(center, brushRadius);
    }

    public List<HexCell> GetProvinceCells(int provinceIndex)
    {
        var result = new List<HexCell>();
        if (cells == null || building || provinceIndex < 0) return result;
        foreach (HexCell cell in cells)
            if (cell.ProvinceIndex == provinceIndex) result.Add(cell);
        return result;
    }

    public List<ProvinceEdge> GetProvinceBoundaryEdges(int provinceIndex)
    {
        var result = new List<ProvinceEdge>();
        if (cells == null || building || provinceIndex < 0) return result;

        foreach (HexCell cell in cells)
        {
            if (cell.ProvinceIndex != provinceIndex) continue;
            for (int d = 0; d < 6; d++)
            {
                HexCell neighbor = NeighborOf(cell, (HexDirection)d);
                if (neighbor != null && neighbor.ProvinceIndex == provinceIndex) continue;

                Vector3 c = cell.Position;
                result.Add(new ProvinceEdge
                {
                    A = c + HexMetrics.Corners[d],
                    B = c + HexMetrics.Corners[d + 1]
                });
            }
        }

        return result;
    }

    // 반경 range 안의 셀을 모은다. BFS 대신 큐브(axial) 디스크 범위를 직접 순회해
    // HashSet/프런티어 할당 없이 빠르게 모은다(큰 브러시에서 특히 유리).
    List<HexCell> GetCellsInRange(HexCell center, int range)
    {
        var result = new List<HexCell>();
        if (range < 0) return result;

        int w = data.grid.width, h = data.grid.height;
        int aq0 = center.X - center.Z / 2;   // 오프셋 → axial q (이 격자에선 floor와 일치, z>=0)
        int ar0 = center.Z;

        for (int dq = -range; dq <= range; dq++)
        {
            int rlo = Mathf.Max(-range, -dq - range);
            int rhi = Mathf.Min(range, -dq + range);
            for (int dr = rlo; dr <= rhi; dr++)
            {
                int ar = ar0 + dr;
                if (ar < 0 || ar >= h) continue;
                int x = (aq0 + dq) + ar / 2;  // axial → 오프셋 x
                if (x < 0 || x >= w) continue;
                result.Add(cells[x + ar * w]);
            }
        }
        return result;
    }

    HexCell NeighborOf(HexCell c, HexDirection d)
    {
        int x = c.X, z = c.Z, nx = x, nz = z;
        bool even = (z & 1) == 0;
        switch (d)
        {
            case HexDirection.E: nx = x + 1; break;
            case HexDirection.W: nx = x - 1; break;
            case HexDirection.NE: nx = even ? x : x + 1; nz = z + 1; break;
            case HexDirection.NW: nx = even ? x - 1 : x; nz = z + 1; break;
            case HexDirection.SE: nx = even ? x : x + 1; nz = z - 1; break;
            case HexDirection.SW: nx = even ? x - 1 : x; nz = z - 1; break;
        }
        int w = data.grid.width, h = data.grid.height;
        if (nx < 0 || nx >= w || nz < 0 || nz >= h) return null;
        return cells[nx + nz * w];
    }

    public GeometryData ExportData()
    {
        return data;
    }

    [System.Obsolete("Use SaveManager.SaveGeometry with ExportData instead.")]
    public void SaveToFile(string path)
    {
        string json = JsonUtility.ToJson(data, true);
        System.IO.File.WriteAllText(path, json);
        Debug.Log($"맵 저장 완료: {path}");
    }

    [System.Obsolete("Use SaveManager.LoadGeometry and Build instead.")]
    public bool LoadFromFile(string path)
    {
        if (!System.IO.File.Exists(path)) { Debug.LogWarning($"불러올 파일이 없습니다: {path}"); return false; }
        try { Build(HexGeometryLoader.LoadFromFile(path)); Debug.Log($"맵 불러옴: {path}"); return true; }
        catch (System.Exception e) { Debug.LogError($"맵 불러오기 실패: {e.Message}"); return false; }
    }

    // ───────────────────────── 텍스처/팔레트 ─────────────────────────

    void BuildPalettes()
    {
        TerrainType[] types = (data.terrainTypes != null && data.terrainTypes.Length > 0) ? data.terrainTypes : DefaultTerrainTypes();
        terrainPalette = MakePalette(terrainPalette, types.Length, "TerrainPalette");
        var tp = new Color[Mathf.Max(1, types.Length)];
        for (int i = 0; i < types.Length; i++) tp[i] = HexColor(types[i].color);
        terrainPalette.SetPixels(tp); terrainPalette.Apply(false);

        EnsureProvinceColors();
        int pc = Mathf.Max(1, data.provinces?.Length ?? 1);
        provincePalette = MakePalette(provincePalette, pc, "ProvincePalette");
        var pp = new Color[pc];
        for (int i = 0; i < pc; i++) pp[i] = ProvinceColorOf(i);
        provincePalette.SetPixels(pp); provincePalette.Apply(false);
    }

    Texture2D MakePalette(Texture2D existing, int n, string name)
    {
        n = Mathf.Max(1, n);
        if (existing != null && existing.width == n) return existing;
        if (existing != null) Destroy(existing);
        return new Texture2D(n, 1, TextureFormat.RGBA32, false, false)
        { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
    }

    void BuildDataTextures()
    {
        int w = data.grid.width, h = data.grid.height, len = w * h;
        terrainTex = MakeRFloat(terrainTex, w, h, "TerrainIndex");
        provinceTex = MakeRFloat(provinceTex, w, h, "ProvinceId");

        bool hasProv = data.provinceMap != null && data.provinceMap.Length == len;
        var tPix = new Color[len];
        var pPix = new Color[len];
        for (int i = 0; i < len; i++)
        {
            tPix[i].r = data.terrain[i];
            int pid = hasProv ? data.provinceMap[i] : -1;
            pPix[i].r = pid < 0 ? 0 : pid + 1;
        }
        terrainTex.SetPixels(tPix); terrainTex.Apply(false);
        provinceTex.SetPixels(pPix); provinceTex.Apply(false);
    }

    Texture2D MakeRFloat(Texture2D existing, int w, int h, string name)
    {
        if (existing != null && existing.width == w && existing.height == h) return existing;
        if (existing != null) Destroy(existing);
        return new Texture2D(w, h, TextureFormat.RFloat, false, true)
        { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
    }

    void SetupMaterial()
    {
        if (TerrainMaterial == null) { Debug.LogError("HexGrid: TerrainMaterial(HexTerrainProvince)이 비어 있습니다."); return; }
        if (matInstance == null) matInstance = new Material(TerrainMaterial);

        matInstance.SetTexture("_TerrainTex", terrainTex);
        matInstance.SetTexture("_TerrainPalette", terrainPalette);
        matInstance.SetTexture("_ProvinceTex", provinceTex);
        matInstance.SetTexture("_ProvincePalette", provincePalette);
        matInstance.SetFloat("_GridW", data.grid.width);
        matInstance.SetFloat("_GridH", data.grid.height);
        matInstance.SetFloat("_InnerR", HexMetrics.InnerRadius);
        matInstance.SetFloat("_OuterR", HexMetrics.OuterRadius);
        matInstance.SetFloat("_TerrainPaletteW", terrainPalette.width);
        matInstance.SetFloat("_ProvincePaletteW", provincePalette.width);
        matInstance.SetFloat("_ProvinceTint", ProvinceTint);
        matInstance.SetFloat("_ShowBorders", ShowBorders ? 1f : 0f);
        matInstance.SetColor("_BorderColor", BorderColor);
        matInstance.SetFloat("_BorderWidth", BorderWidth);

        surfaceGO.GetComponent<MeshRenderer>().sharedMaterial = matInstance;
    }

    // ───────────────────────── 프로빈스: 추가 / PNG 입출력 ─────────────────────────

    public int ProvinceCount => data?.provinces?.Length ?? 0;
    public Color GetProvinceColor(int index) => ProvinceColorOf(index);

    /// <summary>새 프로빈스를 추가하고 그 인덱스를 반환. 표시색(hex)을 지정 가능(없으면 충돌 없는 색 자동 생성).</summary>
    public int AddProvince(string id = null, string hex = null)
    {
        int n = ProvinceCount;
        var arr = new ProvinceInfo[n + 1];
        if (data.provinces != null) System.Array.Copy(data.provinces, arr, n);
        arr[n] = new ProvinceInfo { id = id ?? $"p_{n}", nameKey = id ?? $"PROV_{n}", color = hex };
        data.provinces = arr;
        if (string.IsNullOrEmpty(arr[n].color))
            arr[n].color = MakeUniqueProvinceColor(n, UsedColorKeys(n));
        RebuildProvincePalette();
        ProvinceEditVersion++;
        return n;
    }

    /// <summary>프로빈스를 제거하고, 셀의 표시 인덱스와 텍스처를 즉시 갱신한다.</summary>
    public bool RemoveProvince(int index)
    {
        int n = ProvinceCount;
        if (data?.provinces == null || index < 0 || index >= n) return false;

        var arr = new ProvinceInfo[n - 1];
        if (index > 0) System.Array.Copy(data.provinces, 0, arr, 0, index);
        if (index < n - 1) System.Array.Copy(data.provinces, index + 1, arr, index, n - index - 1);
        data.provinces = arr;

        EnsureProvinceMap();
        int len = data.grid.width * data.grid.height;
        for (int i = 0; i < len; i++)
        {
            int p = data.provinceMap[i];
            if (p == index) p = -1;
            else if (p > index) p--;
            data.provinceMap[i] = p;
            if (cells != null && i < cells.Length) cells[i].ProvinceIndex = p;
        }

        undoStack.Clear(); redoStack.Clear(); recording = false; strokeOld?.Clear();
        RebuildProvincePalette();
        if (provinceTex != null) UploadProvinceTex();
        ProvinceEditVersion++;
        return true;
    }

    // 비어 있는 프로빈스 표시색을, 다른 프로빈스·검정과 겹치지 않게 채운다.
    void EnsureProvinceColors()
    {
        if (data.provinces == null) return;
        var used = UsedColorKeys();   // 이미 배정된 색들
        for (int i = 0; i < data.provinces.Length; i++)
            if (string.IsNullOrEmpty(data.provinces[i].color))
                data.provinces[i].color = MakeUniqueProvinceColor(i, used); // used에 선택색 추가됨
    }

    // 현재 배정된 프로빈스 색들의 RGB 키 집합(excludeIndex 제외). 검정은 항상 예약.
    HashSet<int> UsedColorKeys(int excludeIndex = -1)
    {
        var used = new HashSet<int> { 0 }; // 0,0,0 = 무소속 예약
        if (data.provinces != null)
            for (int i = 0; i < data.provinces.Length; i++)
            {
                if (i == excludeIndex) continue;
                string h = data.provinces[i].color;
                if (string.IsNullOrEmpty(h)) continue;
                if (h[0] != '#') h = "#" + h;
                if (ColorUtility.TryParseHtmlString(h, out var c)) used.Add(RGBKey((Color32)c));
            }
        return used;
    }

    // seed로 후보색을 만들되, used와 겹치면 다음 후보로 밀어 빈 색을 찾는다. 찾은 색의 키를 used에 추가.
    string MakeUniqueProvinceColor(int seed, HashSet<int> used)
    {
        for (int a = 0; a < 8192; a++)
        {
            Color32 c = GenColor32(seed, a);
            int key = RGBKey(c);
            if (key == 0) continue;            // 검정 회피
            if (used.Add(key)) return ColorUtility.ToHtmlStringRGB(c);
        }
        // 극단적 폴백: 빈 RGB를 전수 탐색
        for (int k = 1; k < 0xFFFFFF; k++)
            if (used.Add(k))
                return ColorUtility.ToHtmlStringRGB(new Color32((byte)(k >> 16), (byte)(k >> 8), (byte)k, 255));
        return "FFFFFF";
    }

    // 표시색: 저장된 색을 우선 사용, 없으면 생성색(attempt 0).
    Color ProvinceColorOf(int index)
    {
        if (data?.provinces != null && index >= 0 && index < data.provinces.Length)
        {
            string hex = data.provinces[index].color;
            if (!string.IsNullOrEmpty(hex))
            {
                if (!hex.StartsWith("#")) hex = "#" + hex;
                if (ColorUtility.TryParseHtmlString(hex, out var c)) return c;
            }
        }
        return GenColor32(index, 0);
    }

    // 황금각 HSV. attempt로 hue/채도/명도를 흔들어 충돌 시 다른 색을 얻는다. V/S가 충분해 검정과 안 겹침.
    static Color32 GenColor32(int seed, int attempt)
    {
        float hue = Mathf.Repeat(seed * 0.61803398875f + attempt * 0.13731f, 1f);
        float s = 0.55f + 0.25f * Mathf.Repeat(attempt * 0.31f, 1f);
        float v = 0.70f + 0.25f * Mathf.Repeat(attempt * 0.21f, 1f);
        return (Color32)Color.HSVToRGB(hue, Mathf.Clamp01(s), Mathf.Clamp01(v));
    }

    void RebuildProvincePalette()
    {
        int pc = Mathf.Max(1, ProvinceCount);
        provincePalette = MakePalette(provincePalette, pc, "ProvincePalette");
        var pp = new Color[pc];
        for (int i = 0; i < pc; i++) pp[i] = ProvinceColorOf(i);
        provincePalette.SetPixels(pp); provincePalette.Apply(false);
        if (matInstance != null)
        {
            matInstance.SetTexture("_ProvincePalette", provincePalette);
            matInstance.SetFloat("_ProvincePaletteW", provincePalette.width);
        }
    }

    static int RGBKey(Color32 c) => (c.r << 16) | (c.g << 8) | c.b;

    [System.Serializable] class PaletteFile { public string[] colors; }
    static string SidecarPath(string pngPath) => System.IO.Path.ChangeExtension(pngPath, ".palette.json");

    /// <summary>프로빈스맵을 PNG(픽셀=프로빈스 표시색)로 저장 + 색↔인덱스 팔레트(.palette.json) 동봉.</summary>
    public void SaveProvincePNG(string path)
    {
        int w = data.grid.width, h = data.grid.height, len = w * h;
        EnsureProvinceMap();
        EnsureProvinceColors();

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
        var px = new Color32[len];
        for (int i = 0; i < len; i++)
        {
            int p = data.provinceMap[i];
            px[i] = p < 0 ? new Color32(0, 0, 0, 255) : (Color32)ProvinceColorOf(p); // 무소속=검정
        }
        tex.SetPixels32(px); tex.Apply(false);
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Destroy(tex);

        // 색↔인덱스 복원용 팔레트 동봉
        var pf = new PaletteFile { colors = new string[ProvinceCount] };
        for (int i = 0; i < ProvinceCount; i++) pf.colors[i] = ColorUtility.ToHtmlStringRGB(ProvinceColorOf(i));
        System.IO.File.WriteAllText(SidecarPath(path), JsonUtility.ToJson(pf, true));

        Debug.Log($"프로빈스 PNG 저장: {path} (+팔레트 {ProvinceCount}색)");
    }

    /// <summary>PNG(픽셀=색)에서 프로빈스맵을 불러옴. 동봉 팔레트로 색→인덱스를 복원하고,
    /// 팔레트에 없는 색(외부에서 새로 칠한 영역)은 새 프로빈스로 자동 등록한다.</summary>
    public bool LoadProvincePNG(string path)
    {
        if (!System.IO.File.Exists(path)) { Debug.LogWarning($"프로빈스 PNG 없음: {path}"); return false; }
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
        if (!tex.LoadImage(System.IO.File.ReadAllBytes(path))) { Destroy(tex); Debug.LogError("PNG 디코드 실패"); return false; }

        int w = data.grid.width, h = data.grid.height, len = w * h;
        if (tex.width != w || tex.height != h)
        {
            Debug.LogError($"PNG 크기 {tex.width}x{tex.height}가 맵 {w}x{h}과 다릅니다.");
            Destroy(tex); return false;
        }

        // 동봉 팔레트가 있으면 프로빈스 색을 그대로 맞춰 색→인덱스가 일치하게 한다.
        string sc = SidecarPath(path);
        if (System.IO.File.Exists(sc))
        {
            var pf = JsonUtility.FromJson<PaletteFile>(System.IO.File.ReadAllText(sc));
            if (pf?.colors != null)
            {
                while (ProvinceCount < pf.colors.Length) AddProvince();
                for (int i = 0; i < pf.colors.Length; i++) data.provinces[i].color = pf.colors[i];
            }
        }
        EnsureProvinceColors();

        // 현재 프로빈스들의 색 → 인덱스 맵
        var colorToIdx = new Dictionary<int, int>();
        for (int i = 0; i < ProvinceCount; i++) colorToIdx[RGBKey((Color32)ProvinceColorOf(i))] = i;

        EnsureProvinceMap();
        var pix = tex.GetPixels32();
        Destroy(tex);

        for (int i = 0; i < len; i++)
        {
            Color32 c = pix[i];
            if (c.r == 0 && c.g == 0 && c.b == 0) { data.provinceMap[i] = -1; continue; } // 검정=무소속
            int key = RGBKey(c);
            if (!colorToIdx.TryGetValue(key, out int idx))
            {
                idx = AddProvince(null, ColorUtility.ToHtmlStringRGB(c)); // 새 색 → 새 프로빈스
                colorToIdx[key] = idx;
            }
            data.provinceMap[i] = idx;
        }

        RebuildProvincePalette();
        UploadProvinceTex();
        ProvinceEditVersion++;
        RebindMaterialTextures();   // 머티리얼에 현재 텍스처/팔레트를 다시 물려 즉시 보이게
        if (cells != null) for (int i = 0; i < cells.Length; i++) cells[i].ProvinceIndex = data.provinceMap[i];

        undoStack.Clear(); redoStack.Clear();
        Debug.Log($"프로빈스 PNG 불러옴: {path} (프로빈스 {ProvinceCount}개)");
        return true;
    }

    // 머티리얼에 현재 텍스처/팔레트를 다시 바인딩(빌드 외 경로에서 갱신했을 때 즉시 반영되도록).
    void RebindMaterialTextures()
    {
        if (matInstance == null) return;
        matInstance.SetTexture("_TerrainTex", terrainTex);
        matInstance.SetTexture("_TerrainPalette", terrainPalette);
        matInstance.SetTexture("_ProvinceTex", provinceTex);
        matInstance.SetTexture("_ProvincePalette", provincePalette);
        matInstance.SetFloat("_TerrainPaletteW", terrainPalette.width);
        matInstance.SetFloat("_ProvincePaletteW", provincePalette.width);
        matInstance.SetFloat("_GridW", data.grid.width);
        matInstance.SetFloat("_GridH", data.grid.height);
    }

    void UploadProvinceTex()
    {
        int len = data.grid.width * data.grid.height;
        var pPix = new Color[len];
        for (int i = 0; i < len; i++) { int p = data.provinceMap[i]; pPix[i].r = p < 0 ? 0 : p + 1; }
        provinceTex.SetPixels(pPix); provinceTex.Apply(false);
    }

    // ───────────────────────── 평면 메시 / 셀 ─────────────────────────

    void CreateSurface()
    {
        if (surfaceGO == null)
        {
            surfaceGO = new GameObject("TerrainSurface");
            surfaceGO.transform.SetParent(transform, false);
            surfaceGO.AddComponent<MeshFilter>();
            surfaceGO.AddComponent<MeshRenderer>();
            surfaceMesh = new Mesh { name = "Terrain Surface" };
            surfaceGO.GetComponent<MeshFilter>().mesh = surfaceMesh;
        }

        GetBounds(out Vector3 min, out Vector3 max);
        int segX = Mathf.Max(1, SurfaceSubdivisions), segZ = segX;

        var verts = new List<Vector3>((segX + 1) * (segZ + 1));
        var tris = new List<int>(segX * segZ * 6);
        for (int z = 0; z <= segZ; z++)
            for (int x = 0; x <= segX; x++)
                verts.Add(new Vector3(Mathf.Lerp(min.x, max.x, (float)x / segX), 0f, Mathf.Lerp(min.z, max.z, (float)z / segZ)));
        for (int z = 0; z < segZ; z++)
            for (int x = 0; x < segX; x++)
            {
                int i0 = z * (segX + 1) + x, i1 = i0 + 1, i2 = i0 + segX + 1, i3 = i2 + 1;
                tris.Add(i0); tris.Add(i2); tris.Add(i1);
                tris.Add(i1); tris.Add(i2); tris.Add(i3);
            }

        surfaceMesh.Clear();
        surfaceMesh.SetVertices(verts);
        surfaceMesh.SetTriangles(tris, 0);
        surfaceMesh.RecalculateBounds();
    }

    void CreateCells()
    {
        int w = data.grid.width, h = data.grid.height;
        cells = new HexCell[w * h];
        bool hasElev = data.elevation != null && data.elevation.Length == cells.Length;
        bool hasProv = data.provinceMap != null && data.provinceMap.Length == cells.Length;

        for (int z = 0, i = 0; z < h; z++)
            for (int x = 0; x < w; x++, i++)
                cells[i] = new HexCell
                {
                    X = x,
                    Z = z,
                    Position = CellPosition(x, z),
                    TerrainType = data.terrain[i],
                    Elevation = hasElev ? data.elevation[i] : 0,
                    ProvinceIndex = hasProv ? data.provinceMap[i] : -1,
                };
    }

    static Vector3 CellPosition(int x, int z) => new Vector3(
        (x + z * 0.5f - z / 2) * (HexMetrics.InnerRadius * 2f), 0f, z * (HexMetrics.OuterRadius * 1.5f));

    void GetBounds(out Vector3 min, out Vector3 max)
    {
        int w = data.grid.width, h = data.grid.height;
        min = CellPosition(0, 0); max = min;
        foreach (var p in new[] { CellPosition(w - 1, 0), CellPosition(0, h - 1), CellPosition(w - 1, h - 1) })
        { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        float pad = HexMetrics.OuterRadius;
        min -= new Vector3(pad, 0, pad); max += new Vector3(pad, 0, pad);
    }

    static Color HexColor(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return Color.gray;
        if (!hex.StartsWith("#")) hex = "#" + hex;
        return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
    }

    void FrameCamera()
    {
        GetBounds(out Vector3 min, out Vector3 max);
        Vector3 center = (min + max) * 0.5f;
        float extent = Mathf.Max(max.x - min.x, max.z - min.z);

        if (CameraController != null) { CameraController.FrameMap(center, extent); return; }
        if (!AutoFrameCamera) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        cam.transform.position = new Vector3(center.x, extent * 0.9f + 30f, center.z - extent * 0.1f);
        cam.transform.rotation = Quaternion.Euler(80f, 0f, 0f);
    }
}