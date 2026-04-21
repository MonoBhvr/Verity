# Verity 그래픽스 문서

이 문서는 렌더링 파이프라인과 그래픽스 관련 스크립팅 API를 다룹니다.

범위는 다음과 같습니다.

- 카메라
- 텍스처 로딩/캐시 관리
- 스프라이트/폴리곤/타일맵 렌더러
- 2D 조명
- sorting layer
- 후처리 설정
- 현재 렌더링 구조의 제약
- 현재 UI 텍스트 렌더링 구조

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

---

## 10. 현재 UI 텍스트 렌더링 구조

현재 UI 텍스트 렌더링은 일반 월드 렌더링과 분리된 전용 경로를 사용합니다.

### 10.1 진입점

스크린 UI 텍스트는 [UiRenderer.cs](../Engine/Verity.Graphics/UiRenderer.cs)에서 각 노드의 텍스트를 추출한 뒤, `RenderPipeline.DrawText(...)`를 통해 [GlyphAtlasTextRenderer.cs](../Engine/Verity.Graphics/GlyphAtlasTextRenderer.cs)로 전달됩니다.

### 10.2 텍스트를 직접 그리는 노드

현재 텍스트를 직접 렌더할 수 있는 대표 노드는 다음과 같습니다.

- `Label`
- `RichText`
- `Button`
- `Toggle`
- `InputField`
- `TextArea`
- `Dropdown`
- `Tabs`
- `Tooltip`
- `Window`

### 10.3 폰트 경로

현재 텍스트 렌더러는 두 경로를 가집니다.

1. `.fontasset`를 사용하는 SDF 경로
2. 일반 폰트 파일이나 시스템 폰트를 사용하는 비트맵 fallback 경로

기본 UI는 현재 `.fontasset` 기반 SDF를 우선 사용합니다.

### 10.4 기본 UI 폰트

기본 UI 폰트는 엔진 내부에 번들된 자산을 사용합니다.

- [DefaultUI.fontasset](../Editor/Verity.Editor/EditorResources/Fonts/DefaultUI.fontasset)
- [DefaultUI_0.png](../Editor/Verity.Editor/EditorResources/Fonts/DefaultUI_0.png)
- [DefaultUI_1.png](../Editor/Verity.Editor/EditorResources/Fonts/DefaultUI_1.png)
- [DefaultUI_2.png](../Editor/Verity.Editor/EditorResources/Fonts/DefaultUI_2.png)

에디터가 프로젝트를 열 때 이 자산을 프로젝트 기본 UI 폰트로 연결하는 방식입니다.

### 10.5 SDF 처리 규칙

현재 SDF 셰이더는 단일 채널 distance field를 사용합니다.

핵심 처리 흐름은 다음과 같습니다.

1. atlas에서 거리값을 읽음
2. `0.5`를 기준으로 안쪽/바깥쪽을 해석
3. `uScreenPxRange`를 이용해 화면 픽셀 기준으로 알파를 계산

즉 단순 threshold 방식이 아니라, 화면 크기에 따라 range를 조정하는 구조입니다.

### 10.6 픽셀 스냅

현재 글리프 위치는 layout 단계에서 픽셀 스냅을 수행합니다.
이는 작은 글씨에서 반픽셀 배치로 생기는 흐림을 줄이기 위한 보정입니다.

### 10.7 atlas와 UV 규칙

최근 문제를 통해 확인된 현재 규칙은 다음과 같습니다.

- SDF atlas는 일반 텍스처처럼 무조건 Y-flip해서 로드하면 안 됩니다.
- glyph metadata는 top-origin 기준으로 해석해야 합니다.

이 규칙이 틀어지면 글자의 형상이 아예 다른 문자처럼 깨져 보일 수 있습니다.

---

## 11. Shader / Style 저작 가이드

이 섹션은 현재 Verity의 실제 구현 기준으로 custom shader와 style asset을 작성하는 방법을 설명합니다.

### 11.1 전체 구조

현재 그래픽스 경로에서 shader/style는 크게 세 층으로 나뉩니다.

1. `Shader2D`
   - 기본 2D draw shader wrapper입니다.
   - `uProjection`, `uView`, `uModel`, `uTexture`, `uColor`, `uUvMin`, `uUvMax` 같은 공통 uniform을 다룹니다.
2. `ShaderAsset` (`.shader`)
   - GLSL 소스 에셋입니다.
   - `RenderPipeline.ResolveShader(...)`가 파일을 읽어 실제 `Shader2D`로 컴파일합니다.
3. `StyleAsset` + `StyleData` (`.style`)
   - shader 경로와 uniform 값을 JSON으로 저장하는 에셋입니다.
   - `RenderPipeline.ResolveStyle(...)`가 `StyleRuntime`으로 변환한 뒤 draw 직전에 shader에 값을 넣습니다.

즉 현재 Verity에서 style은 독립 렌더러가 아니라, “어떤 shader를 쓰고 어떤 uniform 값을 넣을지”를 선언하는 데이터 레이어입니다.

### 11.2 `.shader` 파일 구조

`RenderPipeline.ResolveShader(...)`는 `.shader` 파일을 다음 규칙으로 읽습니다.

- `// VERTEX`와 `// FRAGMENT`가 둘 다 있으면 둘 사이를 vertex source, 이후를 fragment source로 사용합니다.
- `// FRAGMENT`만 있으면 fragment만 교체하고 vertex는 기본 vertex shader를 사용합니다.
- 아무 마커도 없으면 파일 전체를 fragment shader로 간주합니다.

가장 안전한 방식은 두 마커를 모두 넣는 것입니다.

```glsl
// VERTEX
#version 330 core
layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aTexCoord;

uniform mat4 uProjection;
uniform mat4 uView;
uniform mat4 uModel;

out vec2 vTexCoord;

void main()
{
    vTexCoord = aTexCoord;
    gl_Position = uProjection * uView * uModel * vec4(aPosition, 0.0, 1.0);
}

// FRAGMENT
#version 330 core
in vec2 vTexCoord;
uniform sampler2D uTexture;
uniform vec4 uColor;
out vec4 FragColor;

void main()
{
    FragColor = texture(uTexture, vTexCoord) * uColor;
}
```

### 11.3 Sprite/월드 shader에서 알아야 하는 기본 uniform

기본 2D quad 경로에서 엔진이 채워 주는 값은 다음과 같습니다.

- `uProjection`
- `uView`
- `uModel`
- `uTexture`
- `uColor`
- `uUvMin`
- `uUvMax`

특히 기본 `Shader2D` vertex shader는 `aTexCoord`를 그대로 쓰지 않고 `mix(uUvMin, uUvMax, aTexCoord)`로 실제 sprite slice UV를 계산합니다.
따라서 sprite atlas나 sliced sprite를 처리하려면 custom vertex shader에서도 이 규칙을 유지하는 편이 안전합니다.

또한 `SpriteRenderer`가 기본 shader를 그대로 쓸 때만 lighting/shadow uniform이 자동 적용됩니다.
custom shader로 완전히 교체하면 기본 조명 계산 경로를 그대로 받지 않습니다.

### 11.4 `.style` 파일 구조

`.style`은 `StyleData` JSON 직렬화 형식입니다.

```json
{
  "ShaderPath": "Assets/Pixelation.shader",
  "Floats": {
    "uPixelCount": 16
  },
  "Vector2s": {},
  "Vector3s": {},
  "Vector4s": {},
  "Colors": {},
  "Textures": {}
}
```

각 필드는 다음처럼 대응됩니다.

- `ShaderPath`: 사용할 `.shader` 경로
- `Floats`: `float uniform`
- `Vector2s`: `vec2 uniform`
- `Vector3s`: `vec3 uniform`
- `Vector4s`: `vec4 uniform`
- `Colors`: `vec4` 중 색으로 다룰 값
- `Textures`: `sampler2D uniform`

런타임에서는 `ResolveStyle(...)`가 이 JSON을 읽고 `StyleRuntime.Apply(...)`로 각 uniform을 shader program에 적용합니다.

### 11.5 custom shader 작성 절차

1. `.shader` 파일을 만든다.
2. 필요한 uniform 이름을 GLSL에 선언한다.
3. `.style` 파일에서 같은 이름의 값을 채운다.
4. `SpriteRenderer.Style` 또는 `CustomPostProcessSettings.Style`에 연결한다.
5. 에디터에서 style/shader 캐시를 refresh해 다시 로드한다.

현재 에디터의 `.style` inspector는 shader 파일에서 `uniform` 선언을 파싱해 편집 UI를 만듭니다.
이때 `uProjection`, `uView`, `uModel`, `uTexture`, `uColor`는 엔진 기본값으로 간주되어 custom parameter 목록에서 제외됩니다.

실무 규칙은 다음과 같습니다.

- sprite shader라면 `uTexture`, `uColor`를 유지하는 편이 기존 렌더러와 맞습니다.
- sprite slice 대응이 필요하면 vertex shader에서 `uUvMin`, `uUvMax`를 반영합니다.
- uniform 이름은 `.style` JSON 키와 정확히 일치해야 합니다.
- `sampler2D`를 추가했다면 `.style`의 `Textures`에 실제 텍스처 자산 경로를 넣어야 합니다.

### 11.6 custom style 작성 절차

현재 그래픽스 쪽 custom style은 사실상 “shader instance 설정 파일”에 가깝습니다.

- 같은 `.shader`를 여러 `.style`에서 공유할 수 있습니다.
- shader는 동일하고 `Floats`/`Colors`/`Textures`만 달리해서 서로 다른 시각 효과를 만들 수 있습니다.
- `RenderPipeline`은 style과 shader를 각각 캐시하므로, 파일 수정 뒤 결과가 안 바뀌면 cache refresh가 필요할 수 있습니다.

예를 들어 같은 pixelation shader를 두고:

- `uPixelCount = 8`이면 거친 픽셀화
- `uPixelCount = 64`이면 약한 픽셀화

처럼 style만 바꿔 여러 변형을 만들 수 있습니다.

### 11.7 SDF 텍스트 렌더링 파이프라인

현재 UI 텍스트는 `UiRenderer` → `RenderPipeline.DrawText(...)` → `GlyphAtlasTextRenderer.DrawText(...)` 흐름으로 들어갑니다.

SDF 경로에서는 다음 단계가 실행됩니다.

1. `TextRenderOptions.FontPath`가 `.fontasset`인지 확인합니다.
2. `GlyphAtlasTextRenderer.TryGetSdfFontFace(...)`가 `SdfFontAsset.Load(...)`로 메타데이터를 읽습니다.
3. `AtlasPages`에 적힌 PNG atlas를 `flipY: false`로 로드합니다.
4. `Glyphs`의 `X`, `Y`, `Width`, `Height`, `Advance`, `OffsetX`, `OffsetY`를 이용해 glyph quad를 배치합니다.
5. layout 후 각 glyph 위치를 픽셀 스냅합니다.
6. SDF 전용 fragment shader가 atlas의 `r` 채널 distance 값을 읽고 알파를 계산합니다.

핵심 fragment 계산은 다음 개념으로 이해하면 됩니다.

- `distanceValue = texture(uTexture, vTexCoord).r`
- 경계 기준값은 `0.5`
- `uScreenPxRange * (distanceValue - 0.5)`로 화면 픽셀 기준 거리로 환산
- 최종 알파는 `clamp(screenPxDistance + 0.5, 0.0, 1.0)`

즉 현재 Verity의 텍스트는 MSDF가 아니라 단일 채널 SDF이며, `uScreenPxRange`로 화면 크기에 맞춰 부드러운 가장자리를 유지합니다.

### 11.8 `.fontasset` 작성 시 알아둘 점

`SdfFontAsset`에는 최소한 다음 정보가 들어갑니다.

- `SamplingPointSize`
- `LineHeight`
- `SpaceAdvance`
- `Padding`
- `Spread`
- `Filter`
- `AtlasPages`
- `Glyphs`

여기서 중요한 값은 `Spread`입니다.
런타임의 `ComputeScreenPxRange(...)`는 대략 `Spread * (requestedFontSize / ReferenceFontSize)`를 사용하므로, 생성 시 spread가 너무 작으면 작은 확대/축소에서 경계 품질이 빨리 무너집니다.

또한 현재 구현은 다음 규칙을 전제로 합니다.

- atlas 텍스처는 Y-flip 없이 로드해야 합니다.
- glyph UV는 top-origin 기준으로 해석합니다.
- atlas page는 여러 장일 수 있으므로 `AtlasIndex`를 올바르게 기록해야 합니다.

### 11.9 post-process용 shader/style 작성 규칙

후처리 custom style은 sprite용 style과 비슷하지만 vertex 쪽 전제가 다릅니다.

- `CustomPostProcessSettings.Style`은 `ResolveStyle(settings.Style, PostProcessShaders.ScreenVertex, "postprocess")`로 로드됩니다.
- vertex shader를 직접 쓰지 않으면 엔진이 `PostProcessShaders.ScreenVertex`를 기본값으로 넣습니다.
- 이 경로는 fullscreen quad 기준이므로 `uProjection`, `uView`, `uModel`을 기대하지 않는 편이 맞습니다.

엔진이 custom post-process shader에 기본으로 넣어 주는 대표 입력은 다음과 같습니다.

- `uTexture`
- `uScene`
- `uSource`
- `uPreviousTexture`
- `uTime`
- `uDeltaTime`
- `uResolution`
- `uTexelSize`

따라서 화면 전체 후처리 효과를 만들 때는 fragment shader만 작성하고 `uScene` 또는 `uTexture`를 읽는 방식이 가장 단순합니다.

### 11.10 UI style과의 관계

저장소에는 그래픽 shader style(`.style`)과 별도로 UI style asset(`.uistyle`)도 있습니다.

- `.style`: `StyleData` 기반, shader uniform 값을 전달하는 그래픽스용 스타일
- `.uistyle`: `UiStyleAsset` 기반, `Colors` / `Numbers` / `Strings` / `States`를 저장하는 UI 테마 데이터

둘 다 이름은 style이지만 역할이 다릅니다.
이 문서의 custom shader/style 저작 가이드는 `.style`과 `.shader`를 기준으로 읽으면 됩니다.

---

## 12. 현재 텍스트 렌더링 한계

- 현재는 MSDF가 아니라 단일 채널 SDF입니다.
- 따라서 작은 글씨나 획이 복잡한 한글에서는 모서리 품질이 아주 날카롭지는 않습니다.
- 비트맵 fallback 경로도 여전히 존재합니다.
- locale별 폰트 fallback 체계는 아직 정식 기능으로 정리되지 않았습니다.

---

## 13. `TextureManager`

`TextureManager`는 이미지 바이트나 파일 경로를 GPU 텍스처(`TextureObjectUploaded`)로 올리고, 같은 입력을 반복 사용할 때 재업로드를 피하기 위한 런타임 텍스처 캐시 관리자입니다.

### 존재 이유

- sprite/UI/font atlas처럼 반복 참조되는 텍스처를 매번 디코딩하고 GPU에 다시 업로드하는 비용을 줄이기 위해
- 파일 기반 리소스와 메모리 기반 리소스를 같은 캐시 규칙으로 다루기 위해
- 임시 텍스처와 엔진 기본 텍스처(예: white pixel)를 일관된 API로 생성하기 위해

### 내부 동작 개요

- 생성자 `TextureManager(GraphicsDevice device)`는 `GraphicsDevice`를 보관하고, 용량 256개의 `LruCache<string, TextureObjectUploaded>`를 만듭니다.
- 캐시 키는 `BuildCacheKey(string baseKey, SpriteTextureFilter filter, bool flipY)`로 생성되며, 실제 키 형식은 `"{baseKey}|{filter}|flip:{flipY}"`입니다.
- 따라서 같은 원본이라도 `filter` 또는 `flipY`가 다르면 서로 다른 텍스처로 캐시됩니다.
- `LruCache`는 가장 오래 사용하지 않은 항목부터 제거하며, 제거되거나 `TextureManager.Dispose()`가 호출될 때 저장된 `TextureObjectUploaded`를 자동으로 `Dispose()`합니다.

### 로딩 / 캐싱 / 언로드 라이프사이클

1. `Load(...)`, `LoadFromMemory(...)`, `CreateFromRgba(...)` 중 하나로 텍스처를 요청합니다.
2. 입력값과 옵션(`filter`, `flipY`, 선택적 `cacheKey`)으로 캐시 키를 만듭니다.
3. 캐시에 이미 있으면 기존 `TextureObjectUploaded`를 그대로 반환합니다.
4. 캐시에 없으면 이미지를 RGBA 바이트로 준비한 뒤 `UploadPixels(...)`가 GPU 텍스처를 생성합니다.
5. 캐시 가능한 호출이면 새 텍스처를 `_cache.Set(...)`으로 저장하고 반환합니다.
6. 이후 같은 키로 다시 요청하면 GPU 재업로드 없이 캐시된 텍스처를 재사용합니다.
7. 더 이상 필요 없는 파일 기반 텍스처는 `Unload(path)`로 제거할 수 있고, 매니저 전체 수명 종료 시 `Dispose()`로 남은 캐시를 정리합니다.

### 캐시 무효화 조건

현재 구현 기준으로 캐시가 새 항목으로 갈라지거나 기존 텍스처가 제거되는 조건은 다음과 같습니다.

- **경로/기본 키가 다를 때**: `Load`는 절대 경로 기준, `LoadFromMemory`와 `CreateFromRgba`는 전달한 `cacheKey` 기준으로 별도 캐시 항목이 생성됩니다.
- **`SpriteTextureFilter`가 바뀔 때**: `Point`와 `Linear`는 서로 다른 캐시 키를 사용합니다.
- **`flipY` 값이 바뀔 때**: 같은 이미지라도 Y-flip 여부가 다르면 다른 캐시 항목으로 취급됩니다.
- **`Unload(path)` 호출 시**: `Load(path, ...)`로 만들어진 항목 중 `fullPath + "|"`로 시작하는 키만 제거되고 즉시 `Dispose()`됩니다.
- **LRU 용량 초과 시**: 캐시가 256개를 넘으면 가장 오래 사용하지 않은 텍스처가 자동 제거 및 `Dispose()`됩니다.
- **`Dispose()` 호출 시**: 캐시 전체가 정리되며 남아 있는 텍스처가 모두 해제됩니다.

주의할 점도 있습니다.

- `Unload(path)`는 파일 경로 기반 키만 대상으로 하므로, `LoadFromMemory(...)`나 `CreateFromRgba(..., cacheKey: ...)`로 만든 항목은 같은 문자열을 넣었더라도 자동으로 매칭되지 않습니다.
- `CreateFromRgba(..., cacheKey: null)`은 캐시에 넣지 않으므로 호출할 때마다 새 GPU 텍스처를 만듭니다.
- `LruCache.Set(...)`는 같은 키를 다시 설정할 때 기존 값을 즉시 `Dispose()`하지 않으므로, 현재 `TextureManager`는 동일 키에 대해 기존 캐시 히트가 나는 흐름을 전제로 사용됩니다.

### public API

| 시그니처 | 설명 |
| :--- | :--- |
| `TextureManager(GraphicsDevice device)` | 텍스처 업로드에 사용할 `GraphicsDevice`와 내부 LRU 캐시를 초기화합니다. |
| `TextureObjectUploaded Load(string path, SpriteTextureFilter filter = SpriteTextureFilter.Point, bool flipY = true)` | 파일 경로의 이미지를 읽어 RGBA로 디코딩하고 GPU 텍스처로 업로드합니다. 기본값은 `Point` 필터 + `flipY: true`입니다. |
| `TextureObjectUploaded LoadFromMemory(byte[] imageBytes, string cacheKey, SpriteTextureFilter filter = SpriteTextureFilter.Point, bool flipY = true)` | 메모리의 이미지 바이트를 텍스처로 만듭니다. 파일이 없는 런타임 생성/네트워크/에셋 패킹 경로에서 쓸 수 있습니다. |
| `TextureObjectUploaded CreateFromRgba(byte[] pixels, int width, int height, string? cacheKey = null, SpriteTextureFilter filter = SpriteTextureFilter.Point)` | 이미 RGBA로 준비된 픽셀 배열을 바로 업로드합니다. `cacheKey`를 주면 캐시되고, 주지 않으면 일회성 텍스처를 만듭니다. |
| `TextureObjectUploaded CreateWhitePixel()` | `__white_pixel__` 캐시 키를 쓰는 1x1 흰색 텍스처를 반환합니다. 단색 sprite, debug draw, 기본 fallback 텍스처 용도로 재사용하기 쉽습니다. |
| `(byte[] Pixels, int Width, int Height) GetRawPixels(string path, bool flipY = false)` | GPU 업로드 없이 파일 이미지를 RGBA 바이트로 읽어 옵니다. CPU 쪽 후처리나 별도 atlas 생성 전에 원본 픽셀이 필요할 때 사용합니다. |
| `void Unload(string path)` | 지정한 파일 경로에서 파생된 캐시 항목을 찾아 제거하고 텍스처를 해제합니다. |
| `void Dispose()` | 내부 LRU 캐시를 정리하고 남아 있는 텍스처를 모두 해제합니다. |

### 구현 세부 사항

- `Load(...)`와 `GetRawPixels(...)`는 모두 `Path.GetFullPath(...)`를 사용하므로 상대 경로 차이로 인한 중복 캐시를 줄입니다.
- 이미지 디코딩은 `StbImageSharp.ImageResult`를 사용하며, 항상 `ColorComponents.RedGreenBlueAlpha`로 변환합니다.
- `flipY`가 `true`이면 `FlipImageY(...)`가 한 줄(`width * 4` 바이트)씩 복사해 상하를 뒤집습니다.
- `UploadPixels(...)`는 `SpriteTextureFilter.Linear`를 `ETextureFilter.Linear`로, 그 외는 `ETextureFilter.Nearest`로 매핑합니다.
- 내부 텍스처 포맷은 현재 고정으로 `ETextureInternalType.Rgba8`입니다.

---

## 14. `ParticleEmitter` / `ParticleSystem`

`ParticleEmitter`와 `ParticleSystem`은 ECS에 올리는 경량 CPU 파티클 시스템입니다. 방출 설정은 컴포넌트인 `ParticleEmitter`가 들고, 실제 생성/업데이트/수명 관리와 이벤트 발행은 정적 시스템인 `ParticleSystem`이 담당합니다.

### 존재 이유

- 단순한 연기, 불꽃, 히트 이펙트처럼 짧은 2D 연출을 별도 전용 렌더러 없이 빠르게 구성하기 위해
- emitter별 상태, random seed, 남은 emission 소수점, 파티클 재사용 슬롯을 한곳에서 관리하기 위해
- 파티클 발생/만료를 `EventBus`와 연결해 다른 시스템이 반응할 수 있게 하기 위해

### `ParticleEmissionShape`

- `Point`
- `Circle`
- `Box`

### `ParticleEmitter` public API

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `Rate` | `float` | 초당 자동 방출 개수입니다. `ParticleSystem.Update(...)`가 `deltaTime`과 곱해 누적 방출량을 계산합니다. |
| `ParticleLifetime` | `float` | 각 파티클의 생존 시간입니다. |
| `ParticleSize` | `float` | 생성 시 기본 크기입니다. 시간이 지나면 수명 비율에 따라 감소합니다. |
| `ParticleColor` | `Color` | 생성 시 기본 색입니다. 알파도 수명 비율에 따라 감소합니다. |
| `InitialVelocity` | `Vector2` | 생성 직후 속도입니다. |
| `Gravity` | `Vector2` | 매 업데이트마다 속도에 더해지는 가속도입니다. |
| `MaxParticles` | `int` | emitter별 동시 활성 파티클 상한입니다. |
| `EmissionShape` | `ParticleEmissionShape` | 생성 위치 분포 방식입니다. |
| `EmissionRadius` | `float` | `Circle` 모드 반경입니다. |
| `EmissionBoxSize` | `Vector2` | `Box` 모드 크기입니다. |
| `RandomSeed` | `int` | emitter별 난수 초기값입니다. |

### `ParticleSystem` public API

| 시그니처 | 설명 |
| :--- | :--- |
| `void Update(ParticleEmitter emitter, float deltaTime)` | 자동 방출, 중력 적용, 수명 감소, 만료 정리까지 한 번에 수행합니다. |
| `void Emit(ParticleEmitter emitter, int count)` | 자동 방출과 별개로 즉시 지정 개수만큼 방출합니다. |
| `IReadOnlyList<Particle> GetParticles(ParticleEmitter emitter)` | 현재 활성 파티클 스냅샷을 반환합니다. |
| `int GetActiveCount(ParticleEmitter emitter)` | 활성 파티클 수를 반환합니다. |
| `int GetPoolCount(ParticleEmitter emitter)` | 내부 `ObjectPool<ParticleSlot>`에 반환된 슬롯 수를 반환합니다. |
| `int GetCreatedSlotCount(ParticleEmitter emitter)` | 지금까지 실제 생성된 슬롯 수를 반환합니다. |
| `void Clear()` | 모든 emitter 상태를 초기화합니다. 테스트나 런타임 리셋 경로에서 사용합니다. |

### 동작 규칙

- 위치 샘플링 기준점은 `emitter.Owner.Transform.WorldPosition`입니다.
- `Rate * deltaTime`의 소수점 잔량은 emitter 상태에 누적되어 프레임 사이에 보존됩니다.
- 실제 파티클 저장은 값 타입 `Particle` 자체가 아니라 내부 참조 타입 `ParticleSlot`을 재사용하는 방식입니다.
- 파티클이 생성되면 `EventBus.Publish(new ParticleEmittedEvent(...))`가 호출되고, 만료되면 `ParticleExpiredEvent`가 발행됩니다.

### 사용 예시

```csharp
var emitter = entity.AddComponent<ParticleEmitter>();
emitter.Rate = 20f;
emitter.ParticleLifetime = 0.8f;
emitter.InitialVelocity = new Vector2(0f, 3f);
emitter.Gravity = new Vector2(0f, -4f);
emitter.EmissionShape = ParticleEmissionShape.Circle;
emitter.EmissionRadius = 0.25f;

ParticleSystem.Update(emitter, Time.DeltaTime);

foreach (var particle in ParticleSystem.GetParticles(emitter))
{
    // 커스텀 디버그/렌더 경로에서 particle.Position, particle.Color, particle.Size 사용
}
```

### 다른 시스템과의 통합

- 내부 재사용 슬롯은 `ObjectPool<T>`를 사용하므로 짧은 수명의 파티클을 반복 생성할 때 할당을 줄일 수 있습니다.
- 생성/만료 이벤트는 `EventBus`로 흘러가므로 사운드, 통계, 디버그 로깅과 느슨하게 연결할 수 있습니다.
- 현재 구현은 CPU 시뮬레이션 중심이며, 실제 화면 렌더링은 별도 렌더러나 디버그 경로가 소비해야 합니다.

---

## 15. `ProfilerOverlay`

`ProfilerOverlay`는 실행 중 화면 좌상단에 FPS, 로직/물리/렌더 시간, 메모리 사용량, 엔티티 수를 덧그려 주는 디버그 전용 오버레이입니다.

### 존재 이유

- 에디터 외부 런타임에서도 즉시 성능 상태를 확인할 수 있게 하기 위해
- `RuntimeProfiler` 수치를 텍스트 패널 형태로 빠르게 시각화하기 위해
- 릴리스 빌드 비용을 최소화하기 위해 `#if DEBUG`에서만 실구현을 두기 위해

### public API

| 시그니처 | 설명 |
| :--- | :--- |
| `static bool ShowProfiler { get; set; }` | 오버레이 표시 여부를 전역적으로 토글합니다. |
| `void TickFrame()` | 프레임 수를 누적해 주기적으로 FPS를 갱신합니다. |
| `void SetRenderTime(double milliseconds)` | 마지막 렌더 구간 시간을 밀리초 단위로 기록합니다. |
| `void Render(RenderPipeline pipeline, World? world, int viewportWidth, int viewportHeight)` | 반투명 패널과 텍스트를 그립니다. viewport가 유효하고 `ShowProfiler`가 켜져 있을 때만 동작합니다. |

### 표시 항목

- `FPS`
- `Logic`
- `Physics`
- `Render`
- `Memory`
- `Entities`

### 사용 예시

```csharp
ProfilerOverlay.ShowProfiler = true;

var overlay = new ProfilerOverlay();
overlay.TickFrame();
overlay.SetRenderTime(renderMilliseconds);
overlay.Render(renderPipeline, WorldManager.ActiveWorld, viewportWidth, viewportHeight);
```

### 다른 시스템과의 통합

- `RuntimeProfiler.CaptureSnapshot()`의 로직/물리 측정치를 그대로 사용합니다.
- `World.StateVersion`을 캐시 키로 사용해 엔티티 수를 매 프레임 다시 세지 않습니다.
- 릴리스 빌드에서는 같은 API 표면을 유지하는 no-op stub로 대체되므로 호출 코드를 조건부 컴파일로 둘 필요가 없습니다.
