# Map Editor 업그레이드

앞서 만든 Hex Map Skeleton에 **인게임 맵 에디터**를 얹는 추가분입니다. 클릭으로 지형을 칠하고, 같은 `geometry.json` 포맷으로 저장합니다. "에디터 = 게임을 편집 모드로 켠 것"이라는 원칙 그대로, 게임이 읽는 바로 그 데이터를 수정합니다.

## 적용 방법

기존 스켈레톤 폴더에 이 `Scripts/`의 파일들을 넣되:

- **교체** (덮어쓰기): `HexCell.cs`, `HexChunk.cs`, `HexGrid.cs`
- **추가** (새 파일): `HexCoordinates.cs`, `HexMapEditor.cs`

`HexMetrics.cs`, `HexGeometryLoader.cs`, `GeometryData.cs`, 셰이더, `geometry.json`은 그대로 둡니다.

## 씬 설정

1. 기존처럼 HexGrid GameObject가 동작하는 상태에서,
2. 아무 GameObject(같은 것이어도 됨)에 **HexMapEditor** 컴포넌트를 추가하고,
3. 인스펙터의 `Grid` 칸에 HexGrid를 드래그.
4. 카메라에 **MainCamera** 태그가 있는지 확인(기본 Main Camera엔 이미 있음).

## 조작

- **왼쪽 클릭 / 드래그**: 선택한 지형으로 칠하기
- **칠하는 중 화면 가장자리 접근**: 카메라 자동 이동
- **숫자키 0~**: 지형 선택 (0 ocean, 1 plains, 2 forest, 3 hills, 4 mountain)
- **좌측 패널**: 버튼으로 지형 선택 + "맵 저장(JSON)" 버튼

## 동작 원리 (요약)

1. **클릭 → 셀 판정**: 청크에 MeshCollider가 붙어 있어 레이캐스트로 표면을 맞힌다. 맞은 월드 좌표를 `HexCoordinates.FromPosition`(큐브 라운딩)으로 변환해 가장 가까운 논리 셀을 찾는다.
2. **수정 → 부분 갱신**: 셀의 `TerrainType`/색과 `data.terrain[]`을 함께 바꾸고, 그 셀이 속한 **청크 하나만** 다시 `Triangulate()` 한다. 맵 전체를 다시 그리지 않아 빠르다.
3. **저장 → 같은 포맷**: `data`를 진실의 원천으로 함께 갱신해 두었으므로, 저장은 `JsonUtility.ToJson(data)` 한 줄이다. 게임이 읽는 바로 그 JSON으로 다시 쓴다.

## 저장 위치

"맵 저장" 버튼은 `Application.persistentDataPath/edited_geometry.json`에 씁니다(모든 플랫폼에서 쓰기 가능). 경로는 Console에 찍힙니다. 그 파일을 원본 `geometry.json` 자리에 복사하면 편집 결과가 반영됩니다. (StreamingAssets는 플랫폼에 따라 런타임 쓰기가 막혀 있어 persistentDataPath를 씁니다.)

## 다음 단계

- **브러시 크기**: 클릭한 셀의 이웃까지 함께 칠하기(`HexCell.GetNeighbor`로 반경 확장). 이웃이 다른 청크면 그 청크들도 갱신.
- **다른 속성 편집**: 고도(Elevation), 프로빈스 지정(ProvinceIndex), 강·도로 토글 등 모드 전환식 에디터로 확장.
- **Undo/Redo**: 편집을 명령(command) 객체로 쌓아 되돌리기.
- **불러오기**: `HexGeometryLoader.LoadFromFile`로 저장한 맵을 다시 열어 이어서 편집.
- **setup.json 에디터**: 지오메트리 위에 소유권·인구를 칠하는 별도 모드 → 시나리오 제작 도구 완성.
