using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// geometry.json을 읽어 논리 셀 격자를 만들고, 청크로 나눠 메시를 생성하는 진입점.
/// 에디터 확장: 브러시 페인트(PaintAt), 저장(SaveToFile), 임의 크기 새 맵(CreateBlankMap).
/// 카메라 컨트롤러가 있으면 맵 (재)생성 시 자동으로 맞춰준다.
/// </summary>
public class HexGrid : MonoBehaviour
{
    [Header("입력")]
    [Tooltip("geometry.json을 .json 파일로 Assets에 넣고 여기에 드래그(TextAsset).")]
    public TextAsset GeometryJson;

    [Tooltip("정점 컬러를 읽는 머티리얼. 동봉한 VertexColorUnlit 셰이더로 만든 머티리얼.")]
    public Material TerrainMaterial;

    [Tooltip("비워두면 씬에서 자동으로 찾는다. 없으면 Camera.main을 직접 이동.")]
    public HexCameraController CameraController;

    [Tooltip("카메라 컨트롤러가 없을 때만 쓰는 단순 자동 프레이밍.")]
    public bool AutoFrameCamera = true;

    GeometryData data;
    HexCell[] cells;
    HexChunk[] chunks;
    int chunkCountX, chunkCountZ;

    public TerrainType[] TerrainTypes => data != null ? data.terrainTypes : null;
    public int CurrentWidth => data != null ? data.grid.width : 0;
    public int CurrentHeight => data != null ? data.grid.height : 0;

    // ── 되돌리기(Undo)/다시(Redo): 한 번의 드래그(붓질)를 한 단계로 묶는다 ──
    struct CellChange { public HexCell Cell; public int OldTerrain; public int NewTerrain; }
    class EditEntry { public readonly List<CellChange> Changes = new List<CellChange>(); }

    readonly Stack<EditEntry> undoStack = new Stack<EditEntry>();
    readonly Stack<EditEntry> redoStack = new Stack<EditEntry>();
    Dictionary<HexCell, int> strokeStart; // 이번 붓질에서 처음 바뀐 셀의 "이전 지형"
    bool recording;

    public bool CanUndo => undoStack.Count > 0;
    public bool CanRedo => redoStack.Count > 0;

    void Start()
    {
        if (CameraController == null)
            CameraController = FindObjectOfType<HexCameraController>();

        if (GeometryJson == null)
        {
            Debug.LogError("HexGrid: GeometryJson이 비어 있습니다.");
            return;
        }
        Build(HexGeometryLoader.Load(GeometryJson.text));
    }

    // ───────────────────────── 빌드 / 새 맵 ─────────────────────────

    public void Build(GeometryData newData)
    {
        data = newData;
        Debug.Log($"맵 빌드: {data.grid.width}x{data.grid.height} / " +
                  $"지형 {data.terrainTypes?.Length ?? 0}종 / " +
                  $"프로빈스 {data.provinces?.Length ?? 0}개");

        // 새 맵은 이전 셀 참조가 무효가 되므로 되돌리기 기록을 비운다.
        undoStack.Clear();
        redoStack.Clear();
        recording = false;
        strokeStart?.Clear();

        if (chunks != null)
            for (int i = 0; i < chunks.Length; i++)
                if (chunks[i] != null) Destroy(chunks[i].gameObject);

        CreateChunks();
        CreateCells();

        for (int i = 0; i < chunks.Length; i++)
            chunks[i].Triangulate();

        FrameCamera();
    }

    public void CreateBlankMap(int width, int height)
    {
        width = Mathf.Clamp(width, 1, 200);
        height = Mathf.Clamp(height, 1, 200);

        var blank = new GeometryData
        {
            formatVersion = 1,
            grid = new GridInfo { width = width, height = height },
            terrainTypes = (data?.terrainTypes != null && data.terrainTypes.Length > 0)
                ? data.terrainTypes
                : DefaultTerrainTypes(),
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
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        HexCoordinates c = HexCoordinates.FromPosition(local);

        int offsetX = c.X + c.Z / 2;
        if (c.Z < 0 || c.Z >= data.grid.height || offsetX < 0 || offsetX >= data.grid.width)
            return null;

        return cells[offsetX + c.Z * data.grid.width];
    }

    /// <summary>월드 좌표 위치를 중심으로 brushRadius 범위의 셀을 칠한다. (브러시 페인트)</summary>
    public void PaintAt(Vector3 worldPosition, int terrainType, int brushRadius)
    {
        HexCell center = GetCell(worldPosition);
        if (center == null) return;

        // 영향받은 청크를 모아 청크당 한 번만 다시 그린다(중복 갱신 방지).
        var dirtyChunks = new HashSet<HexChunk>();
        foreach (HexCell cell in GetCellsInRange(center, brushRadius))
        {
            if (cell.TerrainType == terrainType) continue;

            // 되돌리기용: 이번 붓질에서 이 셀이 처음 바뀌면 "이전 지형"을 기록
            if (recording && !strokeStart.ContainsKey(cell))
                strokeStart[cell] = cell.TerrainType;

            cell.TerrainType = terrainType;
            cell.Color = ResolveColor(terrainType);
            data.terrain[cell.Z * data.grid.width + cell.X] = terrainType;
            dirtyChunks.Add(cell.Chunk);
        }

        foreach (HexChunk ch in dirtyChunks)
            ch.Triangulate();
    }

    // 단일 셀 편집(호환용)
    public void EditCellAt(Vector3 worldPosition, int terrainType) => PaintAt(worldPosition, terrainType, 0);

    // ── 붓질 단위 기록 ──

    /// <summary>드래그 시작 시 호출. 이 시점부터 바뀌는 셀을 한 묶음으로 기록한다.</summary>
    public void BeginStroke()
    {
        recording = true;
        strokeStart ??= new Dictionary<HexCell, int>();
        strokeStart.Clear();
    }

    /// <summary>드래그 끝에 호출. 바뀐 게 있으면 되돌리기 한 단계로 쌓는다.</summary>
    public void EndStroke()
    {
        recording = false;
        if (strokeStart == null || strokeStart.Count == 0) return;

        var entry = new EditEntry();
        foreach (var kv in strokeStart)
        {
            int oldT = kv.Value;
            int newT = kv.Key.TerrainType;
            if (oldT != newT)
                entry.Changes.Add(new CellChange { Cell = kv.Key, OldTerrain = oldT, NewTerrain = newT });
        }
        strokeStart.Clear();

        if (entry.Changes.Count == 0) return;
        undoStack.Push(entry);
        redoStack.Clear(); // 새 편집이 생기면 다시 실행 기록은 무효
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
        var dirty = new HashSet<HexChunk>();
        foreach (CellChange ch in entry.Changes)
        {
            int t = undo ? ch.OldTerrain : ch.NewTerrain;
            ch.Cell.TerrainType = t;
            ch.Cell.Color = ResolveColor(t);
            data.terrain[ch.Cell.Z * data.grid.width + ch.Cell.X] = t;
            dirty.Add(ch.Cell.Chunk);
        }
        foreach (HexChunk c in dirty)
            c.Triangulate();
    }

    /// <summary>브러시 미리보기용: 위치 중심 반경 안의 셀 목록(맵 밖이면 빈 목록).</summary>
    public List<HexCell> GetBrushCells(Vector3 worldPosition, int brushRadius)
    {
        HexCell center = GetCell(worldPosition);
        if (center == null) return new List<HexCell>();
        return GetCellsInRange(center, brushRadius);
    }

    // 이웃 탐색(BFS)으로 반경 range 안의 셀(헥스 원판)을 모은다.
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
                    HexCell n = c.GetNeighbor((HexDirection)d);
                    if (n != null && visited.Add(n))
                    {
                        next.Add(n);
                        result.Add(n);
                    }
                }
            frontier = next;
        }
        return result;
    }

    public void SaveToFile(string path)
    {
        string json = JsonUtility.ToJson(data, true);
        System.IO.File.WriteAllText(path, json);
        Debug.Log($"맵 저장 완료: {path}");
    }

    /// <summary>저장한 geometry.json을 다시 읽어 맵을 재구성한다. 성공하면 true.</summary>
    public bool LoadFromFile(string path)
    {
        if (!System.IO.File.Exists(path))
        {
            Debug.LogWarning($"불러올 파일이 없습니다: {path}");
            return false;
        }
        try
        {
            Build(HexGeometryLoader.LoadFromFile(path)); // Build이 되돌리기 기록도 비운다
            Debug.Log($"맵 불러옴: {path}");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"맵 불러오기 실패: {e.Message}");
            return false;
        }
    }

    // ───────────────────────── 생성 내부 ─────────────────────────

    void CreateChunks()
    {
        chunkCountX = Mathf.CeilToInt((float)data.grid.width / HexMetrics.ChunkSizeX);
        chunkCountZ = Mathf.CeilToInt((float)data.grid.height / HexMetrics.ChunkSizeZ);
        chunks = new HexChunk[chunkCountX * chunkCountZ];

        for (int z = 0, i = 0; z < chunkCountZ; z++)
            for (int x = 0; x < chunkCountX; x++, i++)
            {
                var go = new GameObject($"Chunk {x}_{z}");
                go.transform.SetParent(transform, false);
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = TerrainMaterial;
                go.AddComponent<MeshFilter>();
                chunks[i] = go.AddComponent<HexChunk>();
            }
    }

    void CreateCells()
    {
        int w = data.grid.width;
        int h = data.grid.height;
        cells = new HexCell[w * h];

        for (int z = 0, i = 0; z < h; z++)
            for (int x = 0; x < w; x++, i++)
                cells[i] = CreateCell(x, z, i);
    }

    HexCell CreateCell(int x, int z, int i)
    {
        var cell = new HexCell { X = x, Z = z };

        Vector3 position;
        position.x = (x + z * 0.5f - z / 2) * (HexMetrics.InnerRadius * 2f);
        position.y = 0f;
        position.z = z * (HexMetrics.OuterRadius * 1.5f);
        cell.Position = position;

        cell.TerrainType = data.terrain[i];
        cell.Elevation = (data.elevation != null && data.elevation.Length == cells.Length)
            ? data.elevation[i] : 0;
        cell.ProvinceIndex = (data.provinceMap != null && data.provinceMap.Length == cells.Length)
            ? data.provinceMap[i] : -1;
        cell.Color = ResolveColor(cell.TerrainType);

        int w = data.grid.width;
        if (x > 0)
            cell.SetNeighbor(HexDirection.W, cells[i - 1]);
        if (z > 0)
        {
            if ((z & 1) == 0)
            {
                cell.SetNeighbor(HexDirection.SE, cells[i - w]);
                if (x > 0) cell.SetNeighbor(HexDirection.SW, cells[i - w - 1]);
            }
            else
            {
                cell.SetNeighbor(HexDirection.SW, cells[i - w]);
                if (x < w - 1) cell.SetNeighbor(HexDirection.SE, cells[i - w + 1]);
            }
        }

        AddCellToChunk(x, z, cell);
        return cell;
    }

    Color ResolveColor(int terrainType)
    {
        if (data.terrainTypes == null || terrainType < 0 || terrainType >= data.terrainTypes.Length)
            return Color.gray;

        string hex = data.terrainTypes[terrainType].color;
        if (!hex.StartsWith("#")) hex = "#" + hex;
        return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
    }

    void AddCellToChunk(int x, int z, HexCell cell)
    {
        int cx = x / HexMetrics.ChunkSizeX;
        int cz = z / HexMetrics.ChunkSizeZ;
        HexChunk chunk = chunks[cx + cz * chunkCountX];
        chunk.AddCell(cell);
        cell.Chunk = chunk;
    }

    void FrameCamera()
    {
        if (cells == null || cells.Length == 0) return;

        Vector3 min = cells[0].Position, max = cells[0].Position;
        for (int i = 1; i < cells.Length; i++)
        {
            min = Vector3.Min(min, cells[i].Position);
            max = Vector3.Max(max, cells[i].Position);
        }
        Vector3 center = (min + max) * 0.5f;
        float extent = Mathf.Max(max.x - min.x, max.z - min.z);

        // 컨트롤러가 있으면 위임(줌/팬과 일관). 없으면 단순 자동 프레이밍.
        if (CameraController != null)
        {
            CameraController.FrameMap(center, extent);
            return;
        }
        if (!AutoFrameCamera) return;

        Camera cam = Camera.main;
        if (cam == null) return;
        float height = extent * 0.9f + 30f;
        cam.transform.position = new Vector3(center.x, height, center.z - extent * 0.1f);
        cam.transform.rotation = Quaternion.Euler(80f, 0f, 0f);
    }
}
