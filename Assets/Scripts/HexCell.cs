using UnityEngine;

/// <summary>헥스의 6방향 이웃. 순서가 Opposite 계산에 쓰이므로 바꾸지 말 것.</summary>
public enum HexDirection { NE, E, SE, SW, W, NW }

public static class HexDirectionExtensions
{
    /// <summary>반대 방향(NE↔SW, E↔W, SE↔NW).</summary>
    public static HexDirection Opposite(this HexDirection d)
    {
        return (int)d < 3 ? (d + 3) : (d - 3);
    }
}

/// <summary>
/// 격자 위의 한 셀(논리 단위). 렌더링과 무관한 게임플레이 데이터를 담는다.
/// X/Z는 offset 좌표, Position은 월드 좌표.
/// </summary>
public class HexCell
{
    public int X;                  // 열(column)
    public int Z;                  // 행(row)
    public Vector3 Position;       // 월드 중심 좌표
    public int TerrainType;        // terrainTypes 인덱스
    public int Elevation;          // 고도 (이 스켈레톤에선 데이터만 보관, 렌더링은 평면)
    public int ProvinceIndex = -1; // 소속 프로빈스 인덱스 (-1이면 없음)
    public Color Color = Color.gray;

    readonly HexCell[] neighbors = new HexCell[6];

    public HexCell GetNeighbor(HexDirection d) => neighbors[(int)d];

    /// <summary>이웃을 설정하면 상대편 셀의 반대 방향도 자동으로 연결된다.</summary>
    public void SetNeighbor(HexDirection d, HexCell cell)
    {
        neighbors[(int)d] = cell;
        cell.neighbors[(int)d.Opposite()] = this;
    }
}
