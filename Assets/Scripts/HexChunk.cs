using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 셀 묶음(청크)을 헥스 메시로 만든다.
///
/// 설계(텍스처 구동 + CPU 고도 + 병렬 콜라이더 베이크):
///  - 색: 셰이더가 텍스처에서 읽음. 메시엔 셀별 uv만 실어 셀 경계가 칼같이 떨어진다.
///  - 고도(y): CPU에서 정점 y에 직접 반영(셀 고도 × HeightScale). MeshCollider가 실제 지형을
///    따라가 클릭 피킹이 고도와 무관하게 정확. (셰이더 _HeightScale은 0)
///  - 콜라이더: 쿠킹이 가장 비싸므로, 지오메트리 생성(BuildGeometry)과 콜라이더 부착(AssignCollider)을
///    분리한다. HexGrid가 모든 청크의 메시를 만든 뒤 Physics.BakeMesh를 병렬 잡으로 미리 구워두면,
///    AssignCollider의 sharedMesh 대입이 싸진다(이미 구워진 메시를 붙이기만 함).
///  - 색 페인팅에선 메시/콜라이더를 건드리지 않는다(텍스처 픽셀만 갱신).
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class HexChunk : MonoBehaviour
{
    readonly List<HexCell> cells = new List<HexCell>();

    Mesh mesh;
    MeshCollider meshCollider;
    int gridWidth = 1, gridHeight = 1;
    float heightScale = 0f;

    void Awake()
    {
        mesh = new Mesh { name = "Hex Chunk Mesh", indexFormat = IndexFormat.UInt32 };
        GetComponent<MeshFilter>().mesh = mesh;
        meshCollider = GetComponent<MeshCollider>();
    }

    public void AddCell(HexCell cell) => cells.Add(cell);

    public void SetGridSize(int width, int height)
    {
        gridWidth = Mathf.Max(1, width);
        gridHeight = Mathf.Max(1, height);
    }

    public void SetHeightScale(float scale) => heightScale = scale;

    /// <summary>병렬 베이크용 메시 EntityId (Unity 6.3+의 BakeMesh가 요구).</summary>
    public EntityId MeshEntityId => mesh.GetEntityId();

    /// <summary>
    /// 청크 메시(정점/uv/삼각형)를 만든다. 콜라이더는 여기서 부착하지 않는다(병렬 베이크 후 AssignCollider).
    /// 셀당 정점 7개(중심 1 + 코너 6) 팬. 모든 정점이 같은 uv·같은 y → 색 칼 경계, 셀마다 평평한 단.
    /// </summary>
    public void BuildGeometry()
    {
        int n = cells.Count;
        var vertices  = new List<Vector3>(n * 7);
        var uvs       = new List<Vector2>(n * 7);
        var triangles = new List<int>(n * 18);

        float invW = 1f / gridWidth;
        float invH = 1f / gridHeight;

        for (int i = 0; i < n; i++)
        {
            HexCell cell = cells[i];
            Vector3 c = cell.Position;
            c.y = cell.Elevation * heightScale;

            Vector2 uv = new Vector2((cell.X + 0.5f) * invW, (cell.Z + 0.5f) * invH);

            int b = vertices.Count;
            vertices.Add(c); uvs.Add(uv);
            for (int d = 0; d < 6; d++)
            {
                Vector3 corner = c + HexMetrics.Corners[d];
                corner.y = c.y;
                vertices.Add(corner);
                uvs.Add(uv);
            }
            for (int d = 0; d < 6; d++)
            {
                triangles.Add(b);
                triangles.Add(b + 1 + d);
                triangles.Add(b + 1 + (d + 1) % 6);
            }
        }

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
    }

    /// <summary>이미 BakeMesh로 구워진 메시를 콜라이더에 붙인다(대입 비용이 작음).</summary>
    public void AssignCollider()
    {
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = mesh;
    }
}
