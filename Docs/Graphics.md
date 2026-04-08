# Verity 그래픽스 문서

이 문서는 렌더링 파이프라인과 그래픽스 관련 스크립팅 API를 다룹니다.

범위는 다음과 같습니다.

- 카메라
- 스프라이트/폴리곤/타일맵 렌더러
- 2D 조명
- sorting layer
- 후처리 설정
- 현재 렌더링 구조의 제약

---

## 1. 렌더링 구조 개요

현재 Verity 렌더링은 CPU 정렬 기반 immediate draw 모델에 가깝습니다.

### 주요 단계

1. 카메라와 viewport 결정
2. frame lighting / shadow 데이터 준비
3. 렌더러 수집
4. sorting layer / order / hierarchy 기준 정렬
5. sprite, tilemap, polygon draw
6. gizmo draw
7. post-process chain 적용

### 이 구조의 존재 이유

- 구현 복잡도를 억제하면서 다양한 2D 렌더러를 같은 파이프라인에 얹기 위해
- 디버깅과 편집기 통합을 쉽게 하기 위해

### 현재 중요한 제약

- draw-call batching이 아직 없습니다.
- 따라서 오브젝트 수가 많아지면 CPU submit 비용이 계속 증가합니다.

---

## 2. 공용 enum

| 타입 | 값 |
| :--- | :--- |
| `CameraRenderDetail` | `Outline`, `Basic`, `Lighting`, `PostProcess` |
| `Light2DType` | `Direction`, `Spot`, `World`, `Sprite` |
| `Light2DFalloff` | `Soft`, `Hard` |
| `Light2DMaskSource` | `PhysicsGroup`, `SortingLayer` |
| `Light2DSelectionMode` | `Direct`, `Filter` |
| `SortAxis` | `Y`, `X`, `Z` |

또한 그림자 관련 열거형으로 `ShadowCasterSourceMode`, `ShadowSelfMode`가 사용됩니다.

---

## 3. `Camera`

`Camera`는 2D 투영, viewport, 화면-월드 좌표 변환의 기준이 됩니다.

### 존재 이유

- 렌더 결과를 특정 시점과 투영 기준으로 정의하기 위해
- 입력과 UI, gizmo, post-process가 공통으로 참조할 카메라 상태가 필요해서

### 정적 프로퍼티

- `static Camera? Main`

### 주요 프로퍼티

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `OrthographicSize` | `float` | 직교 카메라 half-height 기준 크기 |
| `BackgroundColor` | `Color` | 배경색 |
| `LetterboxColor` | `Color` | 레터박스 색 |
| `Zoom` | `float` | 줌 배율 |
| `Position` | `Vector2` | 에디터/비부착 상태용 위치 |
| `Rotation` | `float` | 에디터/비부착 상태용 회전 |
| `FixedAspectRatio` | `bool` | 고정 종횡비 사용 여부 |
| `AspectWidth` | `float` | 목표 가로 비율 |
| `AspectHeight` | `float` | 목표 세로 비율 |
| `PostProcess` | `PostProcessSettings` | 후처리 설정 |
| `RenderDetail` | `CameraRenderDetail` | 렌더 세부 수준 |
| `ShowGizmos` | `bool` | gizmo 표시 여부 |
| `ViewportX/Y/Width/Height` | `int` | viewport 정보 |
| `TargetAspectRatio` | `float` | 목표 종횡비 |
| `CurrentAspectRatio` | `float` | 현재 viewport 종횡비 |
| `VisibleHalfHeight` | `float` | 현재 보이는 half-height |
| `VisibleHalfWidth` | `float` | 현재 보이는 half-width |

### 메서드

| 시그니처 | 설명 |
| :--- | :--- |
| `void SetViewportRect(int x, int y, int w, int h)` | viewport 사각형 지정 |
| `void SetViewportSize(int w, int h)` | viewport 크기만 지정 |
| `Matrix4x4 GetProjectionMatrix()` | 현재 종횡비 기준 투영행렬 |
| `Matrix4x4 GetProjectionMatrix(float viewportAspect)` | 지정 종횡비 기준 투영행렬 |
| `Matrix4x4 GetViewMatrix()` | 뷰 행렬 |
| `Vector2 ScreenToWorld(Vector2 screenPos)` | 스크린 좌표를 월드로 변환 |
| `Vector2 WorldToScreen(Vector2 worldPos)` | 월드 좌표를 스크린으로 변환 |

---

## 4. 렌더러 컴포넌트

## 4.1 `SpriteRenderer`

`SpriteRenderer`는 가장 기본적인 2D 이미지 렌더러입니다.

### 존재 이유

- sprite 에셋을 엔티티에 직접 부착해서 그릴 수 있어야 하기 때문에

### 프로퍼티

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `Sprite` | `Sprite` | 그릴 sprite 참조 |
| `Style` | `StyleAsset` | 머티리얼/셰이더 스타일 |
| `Texture` | `TextureObjectUploaded?` | 런타임 텍스처 캐시 |
| `StyleRuntime` | `StyleRuntime?` | 런타임 스타일 캐시 |
| `Color` | `Color` | tint 색상 |
| `SortingLayerName` | `string` | sorting layer 이름 |
| `OrderInLayer` | `int` | layer 내부 순서 |
| `Pivot` | `Vector2` | 사용자 지정 pivot |
| `UseSpritePivot` | `bool` | sprite import pivot 사용 여부 |
| `Size` | `Vector2` | 월드 크기 |
| `FlipX` | `bool` | 가로 반전 |
| `FlipY` | `bool` | 세로 반전 |
| `CastShadows` | `bool` | 그림자 caster 여부 |
| `ShadowSourceMode` | `ShadowCasterSourceMode` | 그림자 형상 소스 |
| `ShadowSelfMode` | `ShadowSelfMode` | 자기 그림자 처리 모드 |
| `ShadowAlphaThreshold` | `float` | sprite 그림자 알파 threshold |

### 메서드

- `void ApplyNativeAspectRatio()`

### 구현상 중요한 규칙

- sprite가 바뀌면 텍스처 캐시가 무효화됩니다.
- UV 계산은 렌더 파이프라인의 sprite slice 캐시를 탑니다.

## 4.2 `PolygonRenderer`

`PolygonRenderer`는 선/채우기 기반 폴리곤 렌더러입니다.

### 프로퍼티

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `Vertices` | `List<Vector2>` | 로컬 정점 |
| `Color` | `Color` | 색상 |
| `Thickness` | `float` | 선 두께 |
| `IsClosed` | `bool` | 폐곡선 여부 |
| `Fill` | `bool` | 채우기 여부 |
| `SortingLayerName` | `string` | sorting layer |
| `OrderInLayer` | `int` | 내부 순서 |
| `CastShadows` | `bool` | 그림자 여부 |
| `ShadowSourceMode` | `ShadowCasterSourceMode` | 그림자 소스 |
| `ShadowSelfMode` | `ShadowSelfMode` | 자기 그림자 처리 |

### 메서드

- `Vector2[] GetWorldVertices()`
- `void SyncWithShape()`
- `bool IsSelfIntersecting()`
- `int[] Triangulate()`

### 존재 이유

- 도형 렌더링과 물리 shape 동기화를 지원하기 위해

## 4.3 `TilemapRenderer`

`TilemapRenderer`는 `Tilemap` 데이터를 실제 draw call로 풀어내는 렌더러입니다.

### 프로퍼티

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `SortingLayerName` | `string` | sorting layer |
| `OrderInLayer` | `int` | 정렬 순서 |
| `CastShadows` | `bool` | 그림자 캐스터 여부 |
| `ShadowSourceMode` | `ShadowCasterSourceMode` | 그림자 소스 |
| `ShadowSelfMode` | `ShadowSelfMode` | 자기 그림자 처리 |
| `ResolvedLayerIndex` | `int` | 실제 layer index |

### 메서드

- `void ClearTextureCache()`
- `void Render(RenderPipeline pipeline, Camera camera, Matrix4x4 projection, Matrix4x4 view, FramebufferObject.Uploaded? targetFbo)`

### 구현상 중요한 규칙

- visible region culling 후 보이는 타일만 그립니다.
- 타일 sprite UV도 파이프라인 캐시를 공유합니다.

---

## 5. `Light2D`

`Light2D`는 2D 장면 조명을 담당합니다.

### 존재 이유

- 스프라이트/폴리곤/배경에 대해 통일된 2D 조명 데이터를 제공하기 위해

### 프로퍼티

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `Type` | `Light2DType` | 조명 타입 |
| `Falloff` | `Light2DFalloff` | 감쇠 방식 |
| `Color` | `Color` | 광원 색상 |
| `Intensity` | `float` | 세기 |
| `Distance` | `float` | 영향 거리 |
| `Smoothness` | `float` | 경계 부드러움 |
| `Spread` | `float` | spot spread |
| `AffectsCameraBackground` | `bool` | 배경에도 영향 줄지 |
| `AffectedSortingLayerSelectionMode` | `Light2DSelectionMode` | 영향을 받을 sorting layer 선택 방식 |
| `AffectedSortingLayerMask` | `ulong` | sorting layer 마스크 |
| `AffectedSortingLayerFilter` | `Filter?` | sorting layer filter |
| `CastShadows` | `bool` | 그림자 사용 여부 |
| `ShadowStrength` | `float` | 그림자 강도 |
| `ShadowLayerSource` | `Light2DMaskSource` | 그림자 receiver 판정 기준 |
| `ShadowReceiverSelectionMode` | `Light2DSelectionMode` | 그림자 receiver 선택 방식 |
| `ShadowReceiverMask` | `ulong` | 그림자 receiver 마스크 |
| `ShadowReceiverFilter` | `Filter?` | 그림자 receiver filter |

### 존재 이유

- sorting layer와 physics group 둘 다를 마스크 기준으로 다뤄야 해서 조명 선택 규칙이 비교적 상세합니다.

---

## 6. `SortingLayer`

`SortingLayer`는 렌더 순서를 문자열 기반 계층으로 관리하는 전역 타입입니다.

### 프로퍼티

- `IReadOnlyList<string> Layers`

### 메서드

| 시그니처 | 설명 |
| :--- | :--- |
| `int GetLayerIndex(string layerName)` | 레이어 인덱스 반환 |
| `void AddLayer(string layerName)` | 레이어 추가 |
| `void InsertLayer(int index, string layerName)` | 특정 위치 삽입 |
| `void RemoveLayer(string layerName)` | 레이어 제거 |
| `void Reset()` | 기본 상태로 초기화 |
| `void SyncWithSettings(List<string> layers)` | 외부 설정과 동기화 |

### 존재 이유

- Z-depth 대신 2D 친화적인 명시적 정렬 계층을 제공하기 위해

---

## 7. 후처리 설정 타입

이 타입들은 대부분 데이터 컨테이너이며, 카메라 후처리 체인을 구성하기 위한 설정 집합입니다.

### `BloomSettings`

- `bool Enabled`
- `int Order`
- `float Intensity`
- `float Threshold`
- `float Scatter`
- `int BlurIterations`
- `int Downsample`

### `VignetteSettings`

- `bool Enabled`
- `int Order`
- `float Intensity`
- `float Smoothness`
- `float Roundness`
- `Color Color`

### `ColorAdjustmentsSettings`

- `bool Enabled`
- `int Order`
- `float Exposure`
- `float Contrast`
- `float Saturation`
- `Color Tint`

### `MotionBlurSettings`

- `bool Enabled`
- `int Order`
- `float Intensity`

### `DistortionSettings`

- `bool Enabled`
- `int Order`
- `float Intensity`
- `Vector2 Center`
- `float Scale`

### `ChromaticAberrationSettings`

- `bool Enabled`
- `int Order`
- `float Intensity`
- `Vector2 Center`

### `PixelateSettings`

- `bool Enabled`
- `int Order`
- `int Width`
- `int Height`

### `CustomPostProcessSettings`

- `bool Enabled`
- `int Order`
- `StyleAsset Style`

### `PostProcessSettings`

주요 프로퍼티:

- `bool Enabled`
- `BloomSettings? Bloom`
- `VignetteSettings? Vignette`
- `ColorAdjustmentsSettings? ColorAdjustments`
- `MotionBlurSettings? MotionBlur`
- `DistortionSettings? Distortion`
- `ChromaticAberrationSettings? ChromaticAberration`
- `PixelateSettings? Pixelate`
- `CustomPostProcessSettings? Custom`
- `List<CustomPostProcessSettings> Customs`

메서드:

- `List<CustomPostProcessSettings> GetCustomEffects()`
- `bool HasAnyEffect()`
- `bool HasAnyEnabledEffect()`

### 존재 이유

- 카메라가 여러 효과를 데이터 기반으로 조합할 수 있어야 하기 때문입니다.

---

## 8. `Fracture`

`Verity.Graphics.Physics.Fracture`는 그래픽스/물리 브릿지 성격의 특수 컴포넌트입니다.

### 프로퍼티

- `int FragmentCount`
- `float SizeVariance`
- `bool UsePhysics`
- `bool AutoPolygonShape`
- `float ExplosionForce`
- `float MassPerArea`
- `float FadeOutDelay`
- `float FadeOutDuration`

### 메서드

- `void Trigger()`

### 존재 이유

- 하나의 형상을 여러 조각으로 분해해 연출용 파편을 빠르게 만들기 위해

---

## 9. 현재 렌더링 제약

- draw-call batching이 아직 없습니다.
- polygon fill은 여전히 동적 vertex 처리 비용이 있습니다.
- 텍스트 렌더링 경로 중 일부는 무겁습니다.
- lighting/shadow uniform 전송은 여전히 draw call 수에 민감합니다.

