using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 셀의 한 묶음(청크)을 받아 헥스 메시로 삼각형화한다.
/// MeshCollider를 함께 두어, 마우스 레이캐스트로 클릭한 셀을 집어낼 수 있게 한다.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class HexChunk : MonoBehaviour
{
    readonly List<HexCell> cells = new List<HexCell>();

    Mesh mesh;
    MeshCollider meshCollider;
    readonly List<Vector3> vertices = new List<Vector3>();
    readonly List<int> triangles = new List<int>();
    readonly List<Color> colors = new List<Color>();

    void Awake()
    {
        mesh = new Mesh { name = "Hex Chunk Mesh", indexFormat = IndexFormat.UInt32 };
        GetComponent<MeshFilter>().mesh = mesh;
        meshCollider = GetComponent<MeshCollider>();
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

        // 콜라이더 갱신(다시 그릴 때마다 클릭 판정도 최신 메시를 따르게)
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = mesh;
    }

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

    void AddTriangle(Vector3 v1, Vector3 v2, Vector3 v3)
    {
        int index = vertices.Count;
        vertices.Add(v1);
        vertices.Add(v2);
        vertices.Add(v3);
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
