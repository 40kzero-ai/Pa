using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

/// <summary>
/// geometry.json을 읽어 논리 셀 격자를 만들고, 청크 메시를 생성한 뒤
/// "지형 인덱스/팔레트 텍스처"로 화면을 그리는 진입점.
///
/// 핵심 설계:
///  - 색: 셰이더가 _TerrainTex(셀별 인덱스)+_PaletteTex(인덱스→색)에서 point 샘플 → 칼 같은 셀 경계.
///  - 고도(y): CPU 메시 정점에 직접 반영(HeightScale). MeshCollider가 실제 지형을 따라가 피킹 정확.
///  - 페인팅: 메시 재생성이 아니라 텍스처 픽셀 쓰기 → 즉시 반영.
///  - 빌드: 코루틴으로 프레임 분산(로그가 순차로 뜨고 화면이 안 멈춤) + 콜라이더는 Physics.BakeMesh
///    병렬 잡으로 미리 구워 부착 비용을 줄인다.
/// </summary>
public class HexGrid : MonoBehaviour
{
    [Header("입력")]
    public TextAsset GeometryJson;

    [Tooltip("HexTerrainTextured 셰이더로 만든 머티리얼.")]
    public Material TerrainMaterial;

    [Tooltip("비워두면 씬에서 자동으로 찾는다.")]
    public HexCameraController CameraController;

    public bool AutoFrameCamera = true;

    [Header("고도(y축)")]
    [Tooltip("정점 y = 셀 고도 × 이 값. y축을 쓰기 전엔 0(평평).")]
    public float HeightScale = 0f;

    [Header("청크 설정")]
    [Tooltip("청크 가로 셀 수. 1000x1000이면 64~128 권장(청크/콜라이더 수 ↓).")]
    [Min(1)] public int ChunkSizeX = 64;
    [Tooltip("청크 세로 셀 수.")]
    [Min(1)] public int ChunkSizeZ = 64;

    [Header("빌드")]
    [Tooltip("한 프레임에 지오메트리를 만들 청크 수. 작을수록 부드럽지만 총 시간이 약간 늘어남.")]
    [Min(1)] public int ChunksPerFrame = 64;

    GeometryData data;
    HexCell[] cells;
    HexChunk[] chunks;
    int chunkCountX, chunkCountZ;
    bool building;
    Coroutine buildCo;

    Material terrainMatInstance;
    Texture2D terrainTex;   // RFloat: 셀별 지형 인덱스(색 전용)
    Texture2D paletteTex;   // RGBA32: 인덱스→색

    public TerrainType[] TerrainTypes => data != null ? data.terrainTypes : null;
    public int CurrentWidth => data != null ? data.grid.width : 0;
    public int CurrentHeight => data != null ? data.grid.height : 0;
    public bool IsBuilding => building;

    // ── 되돌리기 ──
    struct CellChange { public HexCell Cell; public int OldTerrain; public int NewTerrain; }
    class EditEntry { public readonly List<CellChange> Changes = new List<CellChange>(); }
    readonly Stack<EditEntry> undoStack = new Stack<EditEntry>();
    readonly Stack<EditEntry> redoStack = new Stack<EditEntry>();
    Dictionary<HexCell, int> strokeStart;
    bool recording;
    public bool CanUndo => undoStack.Count > 0;
    public bool CanRedo => redoStack.Count > 0;

    void Start()
    {
        ClampChunkSizes();
        if (CameraController == null)
            CameraController = FindFirstObjectByType<HexCameraController>();

        if (GeometryJson == null) { Debug.LogError("HexGrid: GeometryJson이 비어 있습니다."); return; }
        Build(HexGeometryLoader.Load(GeometryJson.text));
    }

    void OnValidate() => ClampChunkSizes();

    void ClampChunkSizes()
    {
        ChunkSizeX = Mathf.Max(1, ChunkSizeX);
        ChunkSizeZ = Mathf.Max(1, ChunkSizeZ);
        ChunksPerFrame = Mathf.Max(1, ChunksPerFrame);
    }

    // ───────────────────────── 빌드 ─────────────────────────

    public void Build(GeometryData newData)
    {
        if (buildCo != null) StopCoroutine(buildCo);
        buildCo = StartCoroutine(BuildRoutine(newData));
    }

    IEnumerator BuildRoutine(GeometryData newData)
    {
        building = true;
        ClampChunkSizes();
        data = newData;

        var total = System.Diagnostics.Stopwatch.StartNew();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        void Mark(string name)
        {
            sw.Stop();
            Debug.Log($"[HexGrid] {name}: {sw.Elapsed.TotalMilliseconds:F1} ms");
            sw.Restart();
        }

        Debug.Log($"맵 빌드 시작: {data.grid.width}x{data.grid.height} / 지형 {data.terrainTypes?.Length ?? 0}종");

        undoStack.Clear(); redoStack.Clear(); recording = false; strokeStart?.Clear();

        if (chunks != null)
            for (int i = 0; i < chunks.Length; i++)
                if (chunks[i] != null) Destroy(chunks[i].gameObject);
        Mark("이전 청크 제거"); yield return null;

        BuildPalette();      Mark("팔레트 텍스처");        yield return null;
        CreateChunks();      Mark("청크 GameObject 생성"); yield return null;
        CreateCells();       Mark("논리 셀 생성");          yield return null;
        BuildDataTextures(); Mark("지형 텍스처 업로드");    yield return null;
        SetupMaterial();     Mark("머티리얼 셋업");         yield return null;

        // 지오메트리: 프레임당 ChunksPerFrame개씩 (화면 안 멈추고 로그도 순차)
        int w = data.grid.width, h = data.grid.height;
        for (int i = 0; i < chunks.Length; i++)
        {
            chunks[i].SetGridSize(w, h);
            chunks[i].SetHeightScale(HeightScale);
            chunks[i].BuildGeometry();
            if ((i + 1) % ChunksPerFrame == 0) yield return null;
        }
        Mark($"메시 지오메트리 ({chunks.Length}개 청크)"); yield return null;

        // 콜라이더: 병렬 베이크 후 부착 (쿠킹을 코어 수만큼 분산)
        BakeCollidersParallel();
        for (int i = 0; i < chunks.Length; i++)
        {
            chunks[i].AssignCollider();
            if ((i + 1) % ChunksPerFrame == 0) yield return null;
        }
        Mark("콜라이더 베이크/부착"); yield return null;

        FrameCamera(); Mark("카메라 프레이밍");

        total.Stop();
        Debug.Log($"[HexGrid] ▶ 총 빌드: {total.Elapsed.TotalMilliseconds:F1} ms  ({w}x{h}, 셀 {w * h:N0}개)");
        building = false;
        buildCo = null;
    }

    // 모든 청크 메시의 콜라이더를 병렬로 미리 굽는다. 이후 AssignCollider 대입이 싸진다.
    struct BakeColliderJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> MeshIds;
        public void Execute(int i) => Physics.BakeMesh(MeshIds[i], false);
    }

    void BakeCollidersParallel()
    {
        if (chunks == null || chunks.Length == 0) return;
        var ids = new NativeArray<int>(chunks.Length, Allocator.TempJob);
        for (int i = 0; i < chunks.Length; i++) ids[i] = chunks[i].MeshInstanceID;

        var job = new BakeColliderJob { MeshIds = ids };
        job.Schedule(ids.Length, 4).Complete();   // 4 = 배치 크기
        ids.Dispose();
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
            terrainTypes = (data?.terrainTypes != null && data.terrainTypes.Length > 0)
                ? data.terrainTypes : DefaultTerrainTypes(),
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
        if (cells == null || data == null) return null;
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        HexCoordinates c = HexCoordinates.FromPosition(local);

        int offsetX = c.X + c.Z / 2;
        if (c.Z < 0 || c.Z >= data.grid.height || offsetX < 0 || offsetX >= data.grid.width)
            return null;
        return cells[offsetX + c.Z * data.grid.width];
    }

    public void PaintAt(Vector3 worldPosition, int terrainType, int brushRadius)
    {
        if (building || terrainTex == null) return;
        HexCell center = GetCell(worldPosition);
        if (center == null) return;

        bool changed = false;
        foreach (HexCell cell in GetCellsInRange(center, brushRadius))
        {
            if (cell.TerrainType == terrainType) continue;
            if (recording && !strokeStart.ContainsKey(cell)) strokeStart[cell] = cell.TerrainType;

            cell.TerrainType = terrainType;
            data.terrain[cell.Z * data.grid.width + cell.X] = terrainType;
            WriteTerrainPixel(cell.X, cell.Z, terrainType);
            changed = true;
        }
        if (changed) terrainTex.Apply(false);   // 청크 재생성 0회, 텍스처 업로드 1회
    }

    public void EditCellAt(Vector3 worldPosition, int terrainType) => PaintAt(worldPosition, terrainType, 0);

    public void BeginStroke()
    {
        recording = true;
        strokeStart ??= new Dictionary<HexCell, int>();
        strokeStart.Clear();
    }

    public void EndStroke()
    {
        recording = false;
        if (strokeStart == null || strokeStart.Count == 0) return;

        var entry = new EditEntry();
        foreach (var kv in strokeStart)
        {
            int oldT = kv.Value, newT = kv.Key.TerrainType;
            if (oldT != newT)
                entry.Changes.Add(new CellChange { Cell = kv.Key, OldTerrain = oldT, NewTerrain = newT });
        }
        strokeStart.Clear();
        if (entry.Changes.Count == 0) return;
        undoStack.Push(entry);
        redoStack.Clear();
    }

    public void Undo()
    {
        if (undoStack.Count == 0) return;
        EditEntry entry = undoStack.Pop();
        ApplyEntry(entry, undo: true);
        redoStack.Push(entry);
    }

    public void Redo()
    {
        if (redoStack.Count == 0) return;
        EditEntry entry = redoStack.Pop();
        ApplyEntry(entry, undo: false);
        undoStack.Push(entry);
    }

    void ApplyEntry(EditEntry entry, bool undo)
    {
        foreach (CellChange ch in entry.Changes)
        {
            int t = undo ? ch.OldTerrain : ch.NewTerrain;
            ch.Cell.TerrainType = t;
            data.terrain[ch.Cell.Z * data.grid.width + ch.Cell.X] = t;
            WriteTerrainPixel(ch.Cell.X, ch.Cell.Z, t);
        }
        terrainTex.Apply(false);
    }

    public List<HexCell> GetBrushCells(Vector3 worldPosition, int brushRadius)
    {
        HexCell center = GetCell(worldPosition);
        if (center == null) return new List<HexCell>();
        return GetCellsInRange(center, brushRadius);
    }

    // 좌표 기반 이웃 탐색(셀이 이웃 배열을 들지 않으므로 여기서 계산)
    List<HexCell> GetCellsInRange(HexCell center, int range)
    {
        var result = new List<HexCell> { center };
        if (range <= 0) return result;

        var visited = new HashSet<HexCell> { center };
        var frontier = new List<HexCell> { center };
        for (int step = 0; step < range; step++)
        {
            var next = new List<HexCell>();
            foreach (HexCell c in frontier)
                for (int d = 0; d < 6; d++)
                {
                    HexCell n = NeighborOf(c, (HexDirection)d);
                    if (n != null && visited.Add(n)) { next.Add(n); result.Add(n); }
                }
            frontier = next;
        }
        return result;
    }

    HexCell NeighborOf(HexCell c, HexDirection d)
    {
        int x = c.X, z = c.Z, nx = x, nz = z;
        bool even = (z & 1) == 0;
        switch (d)
        {
            case HexDirection.E:  nx = x + 1; break;
            case HexDirection.W:  nx = x - 1; break;
            case HexDirection.NE: nx = even ? x     : x + 1; nz = z + 1; break;
            case HexDirection.NW: nx = even ? x - 1 : x;     nz = z + 1; break;
            case HexDirection.SE: nx = even ? x     : x + 1; nz = z - 1; break;
            case HexDirection.SW: nx = even ? x - 1 : x;     nz = z - 1; break;
        }
        int w = data.grid.width, h = data.grid.height;
        if (nx < 0 || nx >= w || nz < 0 || nz >= h) return null;
        return cells[nx + nz * w];
    }

    public void SaveToFile(string path)
    {
        string json = JsonUtility.ToJson(data, true);
        System.IO.File.WriteAllText(path, json);
        Debug.Log($"맵 저장 완료: {path}");
    }

    public bool LoadFromFile(string path)
    {
        if (!System.IO.File.Exists(path)) { Debug.LogWarning($"불러올 파일이 없습니다: {path}"); return false; }
        try { Build(HexGeometryLoader.LoadFromFile(path)); Debug.Log($"맵 불러옴: {path}"); return true; }
        catch (System.Exception e) { Debug.LogError($"맵 불러오기 실패: {e.Message}"); return false; }
    }

    // ───────────────────────── 텍스처/머티리얼 ─────────────────────────

    void BuildPalette()
    {
        TerrainType[] types = (data.terrainTypes != null && data.terrainTypes.Length > 0)
            ? data.terrainTypes : DefaultTerrainTypes();
        int n = Mathf.Max(1, types.Length);

        if (paletteTex == null || paletteTex.width != n)
        {
            if (paletteTex != null) Destroy(paletteTex);
            paletteTex = new Texture2D(n, 1, TextureFormat.RGBA32, false, false)
            { name = "HexPalette", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        }
        var px = new Color[n];
        for (int i = 0; i < n; i++) px[i] = HexColor(types, i);
        paletteTex.SetPixels(px); paletteTex.Apply(false);
    }

    void BuildDataTextures()
    {
        int w = data.grid.width, h = data.grid.height, len = w * h;
        if (terrainTex == null || terrainTex.width != w || terrainTex.height != h)
        {
            if (terrainTex != null) Destroy(terrainTex);
            terrainTex = new Texture2D(w, h, TextureFormat.RFloat, false, true)
            { name = "HexTerrainIndex", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        }
        var tPix = new Color[len];
        for (int i = 0; i < len; i++) tPix[i].r = data.terrain[i];
        terrainTex.SetPixels(tPix); terrainTex.Apply(false);
    }

    void SetupMaterial()
    {
        if (TerrainMaterial == null)
        {
            Debug.LogError("HexGrid: TerrainMaterial이 비어 있습니다. HexTerrainTextured 머티리얼을 연결하세요.");
            return;
        }
        if (terrainMatInstance == null) terrainMatInstance = new Material(TerrainMaterial);

        terrainMatInstance.SetTexture("_TerrainTex", terrainTex);
        terrainMatInstance.SetTexture("_PaletteTex", paletteTex);
        terrainMatInstance.SetFloat("_PaletteWidth", paletteTex.width);
        terrainMatInstance.SetFloat("_HeightScale", 0f); // 고도는 CPU가 담당 → 셰이더 변위 off

        for (int i = 0; i < chunks.Length; i++)
            chunks[i].GetComponent<MeshRenderer>().sharedMaterial = terrainMatInstance;
    }

    void WriteTerrainPixel(int x, int z, int terrainType)
        => terrainTex.SetPixel(x, z, new Color(terrainType, 0f, 0f, 0f));

    /// <summary>고도(cell.Elevation)를 바꾼 뒤 호출. 메시+콜라이더를 다시 만들어 화면·피킹에 반영.</summary>
    public void RebuildElevation()
    {
        if (chunks == null) return;
        int w = data.grid.width, h = data.grid.height;
        for (int i = 0; i < chunks.Length; i++)
        {
            chunks[i].SetGridSize(w, h);
            chunks[i].SetHeightScale(HeightScale);
            chunks[i].BuildGeometry();
        }
        BakeCollidersParallel();
        for (int i = 0; i < chunks.Length; i++) chunks[i].AssignCollider();
    }

    // ───────────────────────── 생성 내부 ─────────────────────────

    void CreateChunks()
    {
        chunkCountX = Mathf.CeilToInt((float)data.grid.width / ChunkSizeX);
        chunkCountZ = Mathf.CeilToInt((float)data.grid.height / ChunkSizeZ);
        chunks = new HexChunk[chunkCountX * chunkCountZ];

        for (int z = 0, i = 0; z < chunkCountZ; z++)
            for (int x = 0; x < chunkCountX; x++, i++)
            {
                var go = new GameObject($"Chunk {x}_{z}");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshRenderer>();
                go.AddComponent<MeshFilter>();
                chunks[i] = go.AddComponent<HexChunk>();
            }
    }

    void CreateCells()
    {
        int w = data.grid.width, h = data.grid.height;
        cells = new HexCell[w * h];
        bool hasElev = data.elevation != null && data.elevation.Length == cells.Length;
        bool hasProv = data.provinceMap != null && data.provinceMap.Length == cells.Length;

        for (int z = 0, i = 0; z < h; z++)
            for (int x = 0; x < w; x++, i++)
            {
                var cell = new HexCell
                {
                    X = x, Z = z,
                    Position = CellPosition(x, z),
                    TerrainType = data.terrain[i],
                    Elevation = hasElev ? data.elevation[i] : 0,
                    ProvinceIndex = hasProv ? data.provinceMap[i] : -1,
                };
                cells[i] = cell;
                AddCellToChunk(x, z, cell);
            }
    }

    static Vector3 CellPosition(int x, int z)
    {
        return new Vector3(
            (x + z * 0.5f - z / 2) * (HexMetrics.InnerRadius * 2f),
            0f,
            z * (HexMetrics.OuterRadius * 1.5f));
    }

    static Color HexColor(TerrainType[] types, int terrainType)
    {
        if (types == null || terrainType < 0 || terrainType >= types.Length) return Color.gray;
        string hex = types[terrainType].color;
        if (string.IsNullOrEmpty(hex)) return Color.gray;
        if (!hex.StartsWith("#")) hex = "#" + hex;
        return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
    }

    void AddCellToChunk(int x, int z, HexCell cell)
    {
        HexChunk chunk = chunks[(x / ChunkSizeX) + (z / ChunkSizeZ) * chunkCountX];
        chunk.AddCell(cell);
        cell.Chunk = chunk;
    }

    // 100만 셀 순회 없이 격자 네 모서리만으로 경계를 계산.
    void FrameCamera()
    {
        if (data == null) return;
        int w = data.grid.width, h = data.grid.height;

        Vector3 min = CellPosition(0, 0), max = min;
        foreach (var p in new[] { CellPosition(w - 1, 0), CellPosition(0, h - 1), CellPosition(w - 1, h - 1) })
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        float pad = HexMetrics.OuterRadius;
        min -= new Vector3(pad, 0, pad); max += new Vector3(pad, 0, pad);

        Vector3 center = (min + max) * 0.5f;
        float extent = Mathf.Max(max.x - min.x, max.z - min.z);

        if (CameraController != null) { CameraController.FrameMap(center, extent); return; }
        if (!AutoFrameCamera) return;

        Camera cam = Camera.main;
        if (cam == null) return;
        float height = extent * 0.9f + 30f;
        cam.transform.position = new Vector3(center.x, height, center.z - extent * 0.1f);
        cam.transform.rotation = Quaternion.Euler(80f, 0f, 0f);
    }
}
