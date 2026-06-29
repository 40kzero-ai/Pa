using UnityEngine;

/// <summary>
/// 헥스 격자의 기하 상수를 담당하는 정적 클래스.
/// pointy-top(꼭짓점이 남북을 향함) 기준이며, 좌표는 offset(짝수/홀수 행) 방식.
/// </summary>
public static class HexMetrics
{
    public const float OuterRadius = 10f;                         // 중심 → 꼭짓점
    public const float InnerRadius = OuterRadius * 0.866025404f;  // 중심 → 변의 중점 (= OuterRadius * √3/2)

    // 정점 흐트리기 강도. 너무 크면 셀이 격자에서 너무 벗어나
    // 어느 셀을 편집/클릭하는지 판정하거나 셀 안에 콘텐츠를 배치하기 어려워진다.
    public const float CellPerturbStrength = 4f;
    public const float NoiseScale = 0.02f;                        // 노이즈 샘플링 스케일

    // 청크 한 칸이 담는 셀 개수 (이 단위로 메시를 나눠 부분 갱신을 가능하게 함)
    public const int ChunkSizeX = 5;
    public const int ChunkSizeZ = 5;

    // pointy-top 헥스의 6개 꼭짓점. 마지막에 0번을 한 번 더 넣어 인덱스 래핑을 피한다.
    public static readonly Vector3[] Corners =
    {
        new Vector3(0f,            0f,  OuterRadius),
        new Vector3(InnerRadius,   0f,  0.5f * OuterRadius),
        new Vector3(InnerRadius,   0f, -0.5f * OuterRadius),
        new Vector3(0f,            0f, -OuterRadius),
        new Vector3(-InnerRadius,  0f, -0.5f * OuterRadius),
        new Vector3(-InnerRadius,  0f,  0.5f * OuterRadius),
        new Vector3(0f,            0f,  OuterRadius),
    };
}
