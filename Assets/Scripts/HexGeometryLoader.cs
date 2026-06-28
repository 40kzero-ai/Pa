using UnityEngine;

/// <summary>geometry.json을 GeometryData로 읽어들이는 로더.</summary>
public static class HexGeometryLoader
{
    /// <summary>JSON 문자열에서 로드.</summary>
    public static GeometryData Load(string json)
    {
        GeometryData data = JsonUtility.FromJson<GeometryData>(json);
        Validate(data);
        return data;
    }

    /// <summary>
    /// 외부 파일 경로에서 로드 (모드 폴더, 시나리오 폴더 등).
    /// 예: Path.Combine(Application.streamingAssetsPath, "scenarios/1836/geometry.json")
    /// 이렇게 외부 파일로 두면 시나리오 = 폴더 하나가 되어 모딩이 자연스럽게 풀린다.
    /// </summary>
    public static GeometryData LoadFromFile(string path)
    {
        string json = System.IO.File.ReadAllText(path);
        return Load(json);
    }

    static void Validate(GeometryData data)
    {
        if (data == null || data.grid == null)
            throw new System.Exception("geometry.json 파싱 실패: 형식을 확인하세요.");

        int expected = data.grid.width * data.grid.height;
        if (data.terrain == null || data.terrain.Length != expected)
            throw new System.Exception(
                $"terrain 길이({data.terrain?.Length})가 width*height({expected})와 일치하지 않습니다.");
    }
}
