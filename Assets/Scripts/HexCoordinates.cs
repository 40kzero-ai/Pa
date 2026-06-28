using UnityEngine;

/// <summary>
/// 헥스 좌표 변환 유틸. 월드 좌표에서 어느 셀을 클릭했는지 알아낼 때 쓴다.
/// 메시는 흐트러져 있어도, 흐트림을 "작게" 유지하면(HexMetrics.CellPerturbStrength)
/// 클릭 지점에서 가장 가까운 "논리 셀"을 정확히 집어낼 수 있다.
/// </summary>
[System.Serializable]
public struct HexCoordinates
{
    public int X { get; private set; }
    public int Z { get; private set; }

    public HexCoordinates(int x, int z) { X = x; Z = z; }

    public static HexCoordinates FromOffsetCoordinates(int x, int z)
    {
        return new HexCoordinates(x - z / 2, z);
    }

    /// <summary>월드(로컬) 좌표 → 헥스 좌표. 큐브 라운딩으로 가장 가까운 셀을 고른다.</summary>
    public static HexCoordinates FromPosition(Vector3 position)
    {
        float x = position.x / (HexMetrics.InnerRadius * 2f);
        float y = -x;

        float offset = position.z / (HexMetrics.OuterRadius * 3f);
        x -= offset;
        y -= offset;

        int iX = Mathf.RoundToInt(x);
        int iY = Mathf.RoundToInt(y);
        int iZ = Mathf.RoundToInt(-x - y);

        if (iX + iY + iZ != 0)
        {
            float dX = Mathf.Abs(x - iX);
            float dY = Mathf.Abs(y - iY);
            float dZ = Mathf.Abs(-x - y - iZ);

            if (dX > dY && dX > dZ) iX = -iY - iZ;
            else if (dZ > dY) iZ = -iX - iY;
        }

        return new HexCoordinates(iX, iZ);
    }
}
