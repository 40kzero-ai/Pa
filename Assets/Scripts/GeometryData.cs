using System;

/// <summary>
/// geometry.json("판")을 그대로 매핑하는 직렬화 클래스들.
///
/// 주의: Unity의 JsonUtility는 2차원·가변 배열(string[][])과 최상위 배열을 지원하지 않는다.
/// 그래서 terrain 같은 격자 데이터는 width*height 길이의 1차원 배열(row-major)로 저장한다.
/// </summary>
[Serializable]
public class GeometryData
{
    public int formatVersion;
    public GridInfo grid;
    public TerrainType[] terrainTypes;
    public int[] terrain;            // 길이 = grid.width * grid.height (row-major)
    public int[] elevation;          // 선택 (없으면 null)
    public ProvinceInfo[] provinces; // 선택
    public int[] provinceMap;        // 선택, 셀별 프로빈스 인덱스 (-1이면 없음)
}

[Serializable]
public class GridInfo
{
    public int width;
    public int height;
}

[Serializable]
public class TerrainType
{
    public string id;
    public string color; // "RRGGBB" 형식 (예: "8FB36B")
}

[Serializable]
public class ProvinceInfo
{
    public string id;
    public string nameKey;
    public string color; // "RRGGBB" 표시색 = PNG 색=ID. 비어 있으면 인덱스로 자동 생성.
}
