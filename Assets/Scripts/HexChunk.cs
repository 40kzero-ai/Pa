using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 셀의 한 묶음(청크)을 받아 "흐트린 헥스 메시"로 삼각형화한다.
/// 맵 전체가 아니라 청크 단위로 다시 그릴 수 있어, 큰 맵에서도 일부 갱신이 가볍다.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class HexChunk : MonoBehaviour
{
    readonly List<HexCell> cells = new List<HexCell>();

    Mesh mesh;
    readonly List<Vector3> vertices = new List<Vector3>();
    readonly List<int> triangles = new List<int>();
    readonly List<Color> colors = new List<Color>();

    void Awake()
    {
        mesh = new Mesh { name = "Hex Chunk Mesh" };
        GetComponent<MeshFilter>().mesh = mesh;
    }

    public void AddCell(HexCell cell) => cells.Add(cell);

    /// <summary>이 청크가 가진 모든 셀을 메시로 다시 만든다.</summary>
    public void Triangulate()
    {
        vertices.Clear();
        triangles.Clear();
        colors.Clear();

        for (int i = 0; i < cells.Count; i++)
            TriangulateCell(cells[i]);

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetColors(colors);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    // 한 셀을 중심에서 뻗는 6개 삼각형(팬)으로 만든다.
    void TriangulateCell(HexCell cell)
    {
        Vector3 center = cell.Position;
        for (int d = 0; d < 6; d++)
        {
            AddTriangle(
                center,
                center + HexMetrics.Corners[d],
                center + HexMetrics.Corners[d + 1]);
            AddTriangleColor(cell.Color);
        }
    }

    // 정점을 추가할 때 흐트린다. (공유 정점은 월드 좌표가 같아 동일하게 이동 → 틈 없음)
    void AddTriangle(Vector3 v1, Vector3 v2, Vector3 v3)
    {
        int index = vertices.Count;
        vertices.Add(HexMetrics.Perturb(v1));
        vertices.Add(HexMetrics.Perturb(v2));
        vertices.Add(HexMetrics.Perturb(v3));
        triangles.Add(index);
        triangles.Add(index + 1);
        triangles.Add(index + 2);
    }

    void AddTriangleColor(Color c)
    {
        colors.Add(c);
        colors.Add(c);
        colors.Add(c);
    }
}
