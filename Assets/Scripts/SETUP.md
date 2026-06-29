# 텍스처 구동 헥스 지형 — 적용 가이드

기존 "셀마다 정점 컬러 + 페인팅할 때 청크 재삼각형화 + 청크마다 MeshCollider" 구조를
**텍스처 구동**으로 바꾼다. 결과:

- 색은 셰이더가 텍스처에서 읽음 → **셀 경계가 칼같이** 유지 (기존 룩 동일)
- 페인팅 = **텍스처 픽셀 1개 쓰기** → 메시 재생성 0회, 100만 셀에서도 즉시 반영
- 고도(y)는 **셰이더 버텍스 변위**로 확장 → 데이터만 채우고 `HeightScale`만 올리면 됨
- MeshCollider 제거 → 생성 시 가장 비쌌던 **콜라이더 쿠킹이 사라짐**

---

## 1. 파일 교체 / 추가

| 파일 | 동작 |
|---|---|
| `Assets/Scripts/HexGrid.cs` | **교체** |
| `Assets/Scripts/HexChunk.cs` | **교체** |
| `Assets/Shader/HexTerrainTextured.shader` | **추가** |

`HexCell.cs`, `HexMetrics.cs`, `GeometryData.cs`, `HexCoordinates.cs` 등은 그대로 둔다.
`HexCell.Color` 필드는 남겨둬도 무방(이제 렌더링에는 안 쓰임).

## 2. 머티리얼 만들기

1. `Assets/Shader/HexTerrainTextured.shader` 임포트 후
2. Project 창에서 우클릭 → Create → Material, 이름 예: `HexTerrainTextured_Mat`
3. 그 머티리얼의 Shader를 `Custom/HexTerrainTextured`로 지정
4. 씬의 **HexGrid** 컴포넌트 인스펙터에서 `Terrain Material` 칸에 이 머티리얼을 연결
   (텍스처들은 런타임에 자동으로 채워지므로 인스펙터에서 비워둬도 됨)

> 기존 `VertexColorUnlit` 머티리얼은 더 이상 지형에 쓰지 않는다.
> 다만 브러시 미리보기/하이라이트용 `HighlightMaterial`은 별개이므로 그대로 둔다.

## 3. HexMapEditor.cs 3군데 수정 (평면 레이캐스트)

맵이 y=0 평면이라 콜라이더 없이 수학 평면으로 클릭 지점을 구한다.

**(A) 필드 추가** — `public HexGrid Grid;` 바로 아래:

```csharp
    // 맵은 y=0 평면이므로 콜라이더 없이 수학 평면으로 클릭 지점을 구한다(고도 추가 전까지).
    static readonly Plane GroundPlane = new Plane(Vector3.up, Vector3.zero);
```

**(B) 페인트 레이캐스트** — 기존:

```csharp
        Ray ray = cam.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit))
            Grid.PaintAt(hit.point, activeTerrain, brushSize);
```

→ 교체:

```csharp
        Ray ray = cam.ScreenPointToRay(screenPos);
        if (GroundPlane.Raycast(ray, out float enter))
            Grid.PaintAt(ray.GetPoint(enter), activeTerrain, brushSize);
```

**(C) 미리보기 레이캐스트** — 기존:

```csharp
        Ray ray = cam.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit)) { HidePreview(); return; }

        HexCell center = Grid.GetCell(hit.point);
        if (center == null) { HidePreview(); return; }
```

→ 교체:

```csharp
        Ray ray = cam.ScreenPointToRay(screenPos);
        if (!GroundPlane.Raycast(ray, out float enter)) { HidePreview(); return; }

        HexCell center = Grid.GetCell(ray.GetPoint(enter));
        if (center == null) { HidePreview(); return; }
```

이게 전부다. 다른 `Grid.*` 호출(Undo/Redo/PaintAt/GetCell/GetBrushCells/CreateBlankMap 등)은
시그니처가 그대로라 수정 불필요.

---

## 왜 빨라지나 (요약)

| 항목 | 이전 | 이후 |
|---|---|---|
| 페인팅 1회 | 영향 청크 `Triangulate()` + 콜라이더 재쿠킹 | 픽셀 N개 쓰고 `Apply` 1회 |
| 최초 생성 | 청크마다 메시 + **MeshCollider 쿠킹** | 청크마다 메시 1회, 콜라이더 없음 |
| 클릭 판정 | `Physics.Raycast`(콜라이더 필요) | `Plane.Raycast`(공짜) |
| 셀당 정점 | 18 (삼각형 6개 분리) | 7 (삼각형 팬) |
| 정점 컬러 | `Color` 16B × 18 | 없음(텍스처) |

---

## y축(고도) 추가할 때 (나중에)

1. `geometry.json`의 `elevation`(길이 = width*height) 채우기, 또는 런타임에 `data.elevation` 세팅
2. HexGrid 인스펙터의 **Height Scale**을 0 → 원하는 배율로
3. 고도를 편집했다면 `grid.RefreshHeightTexture()` 호출

그러면 셰이더가 셀별 고도를 y로 변위한다(셀마다 평평한 단/plateau 형태).

### 단(plateau) 사이 수직 틈 처리
높이가 다른 이웃 셀 사이에는 수직 절벽 면(스커트)이 비어 보일 수 있다. 위에서 내려보는
카메라면 대개 거슬리지 않지만, 깔끔하게 막으려면 둘 중 하나:

- **스커트 지오메트리**: 각 셀 변에서 이웃과 높이가 다르면 아래로 수직 쿼드를 한 장 깔기
  (메시에 추가, 색은 같은 uv라 자동으로 셀 색).
- **부드러운 지형으로 전환**: 고도 텍스처를 Bilinear로 바꾸고, 정점 uv를 "셀 중심"이 아니라
  "정점의 실제 격자 좌표"로 주면 이웃 코너가 같은 높이를 샘플해 이음매 없이 연결된다.
  (색은 여전히 셀 중심 uv로 point 샘플 → 색은 칼 경계 유지, 표면만 부드럽게)

원하는 룩이 정해지면 그 방식에 맞춰 스커트/uv 코드를 추가로 짜주면 된다.
