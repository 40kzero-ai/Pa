using UnityEngine;

/// <summary>
/// geometry.json을 읽어 논리 셀 격자를 만들고, 청크로 나눠 메시를 생성하는 진입점.
/// 빈 GameObject에 이 컴포넌트를 붙이고, 인스펙터에서 JSON과 머티리얼을 지정하면 동작한다.
/// </summary>
public class HexGrid : MonoBehaviour
{
    [Header("입력")]
    [Tooltip("geometry.json을 .json 파일로 Assets에 넣고 여기에 드래그(TextAsset로 인식됨).")]
    public TextAsset GeometryJson;

    [Tooltip("정점 컬러를 읽는 머티리얼. 동봉한 VertexColorUnlit 셰이더로 만든 머티리얼을 지정.")]
    public Material TerrainMaterial;

    GeometryData data;
    HexCell[] cells;
    HexChunk[] chunks;
    int chunkCountX, chunkCountZ;

    void Start()
    {
        if (GeometryJson == null)
        {
            Debug.LogError("HexGrid: GeometryJson이 비어 있습니다. 인스펙터에 JSON을 지정하세요.");
            return;
        }

        // 1) 데이터 로드 (모드라면 HexGeometryLoader.LoadFromFile(path)로 외부 폴더에서 읽으면 됨)
        data = HexGeometryLoader.Load(GeometryJson.text);
        Debug.Log($"맵 로드: {data.grid.width}x{data.grid.height} / " +
                  $"지형 {data.terrainTypes?.Length ?? 0}종 / " +
                  $"프로빈스 {data.provinces?.Length ?? 0}개");

        // 2) 청크 → 셀 순으로 생성 (셀이 청크에 등록되어야 하므로 청크 먼저)
        CreateChunks();
        CreateCells();

        // 3) 각 청크를 메시로 삼각형화
        for (int i = 0; i < chunks.Length; i++)
            chunks[i].Triangulate();
    }

    void CreateChunks()
    {
        chunkCountX = Mathf.CeilToInt((float)data.grid.width / HexMetrics.ChunkSizeX);
        chunkCountZ = Mathf.CeilToInt((float)data.grid.height / HexMetrics.ChunkSizeZ);
        chunks = new HexChunk[chunkCountX * chunkCountZ];

        for (int z = 0, i = 0; z < chunkCountZ; z++)
        {
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

        // offset 좌표 → 월드 좌표. 홀수 행은 반 칸 오른쪽으로 밀린다.
        Vector3 position;
        position.x = (x + z * 0.5f - z / 2) * (HexMetrics.InnerRadius * 2f);
        position.y = 0f;
        position.z = z * (HexMetrics.OuterRadius * 1.5f);
        cell.Position = position;

        // 데이터 채우기
        cell.TerrainType = data.terrain[i];
        cell.Elevation = (data.elevation != null && data.elevation.Length == cells.Length)
            ? data.elevation[i] : 0;
        cell.ProvinceIndex = (data.provinceMap != null && data.provinceMap.Length == cells.Length)
            ? data.provinceMap[i] : -1;
        cell.Color = ResolveColor(cell.TerrainType);

        // 이웃 연결: 생성 순서상 W/SW/SE만 걸어두면 반대편(E/NE/NW)이 자동 연결된다.
        int w = data.grid.width;
        if (x > 0)
            cell.SetNeighbor(HexDirection.W, cells[i - 1]);
        if (z > 0)
        {
            if ((z & 1) == 0) // 짝수 행
            {
                cell.SetNeighbor(HexDirection.SE, cells[i - w]);
                if (x > 0) cell.SetNeighbor(HexDirection.SW, cells[i - w - 1]);
            }
            else              // 홀수 행
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
        chunks[cx + cz * chunkCountX].AddCell(cell);
    }
}
