using UnityEngine;

/// <summary>헥스의 6방향. 순서가 Opposite 계산에 쓰이므로 바꾸지 말 것.</summary>
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
///
/// 성능: 이웃을 셀마다 배열로 들고 있으면 100만 셀에서 배열 100만 개가 추가로 할당되어
/// 생성이 크게 느려진다. 이웃은 좌표로 즉시 계산 가능하므로 HexGrid.NeighborOf()에서 구한다.
/// </summary>
public class HexCell
{
    public int X;                  // 열(column)
    public int Z;                  // 행(row)
    public Vector3 Position;       // 월드 중심 좌표
    public int TerrainType;        // terrainTypes 인덱스
    public int Elevation;          // 고도
    public int ProvinceIndex = -1; // 소속 프로빈스 인덱스 (-1이면 없음)
    public Color Color = Color.gray; // (호환용, 렌더링엔 미사용 — 색은 텍스처가 담당)

    // 이 셀이 속한 청크.
    public HexChunk Chunk;
}
