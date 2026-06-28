# Hex Map Skeleton (Unity)

`geometry.json`(맵의 "판")을 읽어 **논리 헥스 격자**를 만들고, **청크 단위로 정점을 흐트린(perturbed) 메시**를 생성하는 최소 골격입니다. 우리가 설계한 "지오메트리(데이터) ↔ 렌더링(메시) 분리"와 "데이터 주도 + 모드 친화" 구조를 그대로 따릅니다.

## 들어있는 파일

```
Scripts/
  HexMetrics.cs          # 헥스 기하 상수 + 정점 흐트리기(Perlin)
  HexCell.cs             # 논리 셀 + 방향(이웃) 정의
  GeometryData.cs        # geometry.json 직렬화 클래스
  HexGeometryLoader.cs   # JSON → GeometryData 로더
  HexChunk.cs            # 셀 묶음을 흐트린 메시로 삼각형화
  HexGrid.cs             # 진입점: 로드 → 셀/청크 생성 → 메시 빌드
Shaders/
  VertexColorUnlit.shader  # 정점 컬러 출력 (Built-in RP용)
StreamingAssets/
  geometry.json          # 8x6 샘플 섬 맵
```

## 설치 & 실행 (5분)

1. `Scripts/`의 .cs 파일들을 프로젝트의 `Assets/Scripts/`에 복사.
2. `VertexColorUnlit.shader`를 `Assets/`에 복사 → 우클릭 **Create > Material**로 머티리얼을 하나 만들고, 그 머티리얼의 셰이더를 **Custom/VertexColorUnlit**로 지정.
3. `geometry.json`을 `Assets/` 아래 아무 곳에나 복사. (`.json`은 유니티가 자동으로 **TextAsset**으로 인식)
4. 빈 GameObject 생성 → **HexGrid** 컴포넌트 추가 → 인스펙터에서
   - `Geometry Json` 칸에 `geometry.json`을 드래그
   - `Terrain Material` 칸에 2번에서 만든 머티리얼을 드래그
5. **Play**. 카메라를 위에서 내려다보게 두면, 흐트러진 헥스 지형이 보입니다. Console에는 로드된 맵 크기·지형·프로빈스 개수가 찍힙니다.

> 카메라 팁: Position 대략 (35, 60, 20), Rotation (70, 0, 0) 정도로 두면 샘플 맵이 화면에 들어옵니다.

## 핵심 동작

- **정점 흐트리기**: `HexMetrics.Perturb`가 정점을 "월드 좌표 기반" 펄린 노이즈로 이동시킵니다. 인접 셀이 공유하는 정점은 월드 좌표가 같아 양쪽에서 똑같이 움직이므로 메시에 틈이 생기지 않습니다. 강도는 `CellPerturbStrength`(기본 4)로 조절하세요. 너무 키우면 셀이 격자에서 벗어나 클릭 판정·콘텐츠 배치가 어려워집니다.
- **청크**: 맵을 `ChunkSizeX × ChunkSizeZ`(기본 5×5) 단위로 나눠 메시를 따로 만듭니다. 나중에 한 셀만 바뀌어도 그 청크만 다시 `Triangulate()`하면 됩니다.
- **데이터 주도**: 지형·고도·프로빈스가 전부 `geometry.json`에 있습니다. 코드에 하드코딩된 맵 크기/지형이 없으므로, JSON만 바꾸면 다른 맵이 됩니다 → 시나리오/모드가 자연스럽게 따라옵니다.

## 알아둘 점 (JsonUtility 한계)

유니티 기본 `JsonUtility`는 **2차원/가변 배열(`string[][]`)과 최상위 배열을 지원하지 않습니다.** 그래서 `terrain`을 `[[...],[...]]`(중첩)이 아니라 `width*height` 길이의 **1차원 배열(row-major)**로 두었습니다. 중첩 배열이나 더 풍부한 스키마(주석, 딕셔너리 등)를 원하면 **Newtonsoft.Json**(Package Manager: `com.unity.nuget.newtonsoft-json`)으로 교체하면 됩니다. `HexGeometryLoader`의 파싱 한 줄만 바꾸면 끝입니다.

## URP를 쓴다면

동봉한 `VertexColorUnlit.shader`는 **Built-in 파이프라인용**입니다. URP에서는 자홍색(magenta)으로 보일 수 있어요. URP라면 **Shader Graph**로 같은 걸 만드세요:
**Create > Shader Graph > URP > Unlit** → `Vertex Color` 노드를 Base Color에 연결 → 그 셰이더로 머티리얼 생성. 나머지는 동일합니다.

## 모드/시나리오로 확장

`HexGrid.Start`의 로드 한 줄을 `HexGeometryLoader.LoadFromFile(path)`로 바꾸면, `Assets` 밖의 외부 폴더(예: `StreamingAssets/scenarios/내모드/geometry.json`)에서 읽을 수 있습니다. 그러면 **시나리오 = 폴더 하나**가 되고, 모더는 같은 형식의 폴더를 떨어뜨리기만 하면 됩니다.

## 다음 단계 (난이도 순)

1. **지형 블렌딩**: 지금은 셀마다 단색입니다. 텍스처 배열 + 스플랫 맵으로 경계를 섞으면 격자 티가 더 사라집니다. (Catlike Hex Map 14편)
2. **고도/테라스**: `Elevation`을 메시 Y에 반영하고, 셀 사이를 다리·테라스로 이어 틈을 메웁니다. (Catlike Hex Map 4~5편)
3. **국경선/강/도로**: 셀 경계를 따라 그리되 노이즈로 가장자리를 거칠게. (Catlike Hex Map 6~7편)
4. **에디터 모드**: 클릭한 셀의 `TerrainType`/소유권을 바꾸고 같은 JSON 포맷으로 저장 → 인게임 맵 에디터.
5. **setup.json**: 이 지오메트리 위에 소유권·인구·외교를 얹는 "시작 상태" 레이어 추가.

참고 자료: Catlike Coding Hex Map 시리즈 — https://catlikecoding.com/unity/tutorials/hex-map/
