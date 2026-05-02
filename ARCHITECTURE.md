# Verity 엔진 아키텍처 문서

이 문서는 Verity 엔진 전체 구조를 설명하는 상위 문서입니다.

이 문서는 엔진이 실제로 어떤 실행 모델과 데이터 구조 위에서 동작하는지 설명하는 아키텍처 문서입니다.

상세 클래스, 함수, 메서드, 프로퍼티 레퍼런스는 시스템별 문서로 분리되어 있습니다. 이렇게 분리한 이유는 다음과 같습니다.

- 한 파일에 모든 API를 몰아넣으면 검색은 되지만 유지보수가 빠르게 어려워집니다.
- 아키텍처 설명과 API 레퍼런스는 읽는 목적이 다릅니다.
- Core, Physics, Graphics, UI처럼 변경 주기가 다른 시스템을 독립적으로 갱신할 수 있어야 합니다.

---

## 1. 엔진 실행 모델

### 1.1 `VerityCore` 진입점과 초기화 단계

현재 `VerityCore` 자체는 거대한 bootstrap class라기보다, 엔진 전역 상태에 접근하는 매우 얇은 진입 표면입니다.

- `VerityCore.Version`: 런타임/에디터가 공통으로 표시하는 엔진 버전 문자열입니다.
- `VerityCore.ResetRuntime()`: `WorldManager.Reset()`, `Time.Reset()`과 함께 이벤트, UI, 파티클, 물리, debug draw 같은 전역 서브시스템 상태를 함께 비워 런타임 재진입 시 잔여 상태가 남지 않게 합니다.

즉, 실제 엔진 기동 순서는 `VerityCore` 한 곳에 몰려 있지 않고, 호스트가 `VerityCore`와 각 서브시스템을 조합하는 방식입니다. 현재 런타임 기준 초기화 흐름은 `Verity.Game.Program.Main(...)`에서 다음 순서로 진행됩니다.

1. 실행 경로와 content root를 준비하고 필요하면 runtime logging을 연결합니다.
2. `GraphicsDevice`, `Shader2D`, `TextureManager`, `RenderPipeline`을 만들고 `RenderPipeline.BaseAssetsPath`, `SceneSerializer.AssetRootPath`, `UiSystem.AssetsRoot` 같은 전역 경로를 맞춥니다.
3. `DefaultSprites.Initialize(...)`로 기본 렌더 자산을 준비합니다.
4. `UserScripts.dll`을 로드해 사용자 스크립트 assembly를 확보합니다.
5. `BuildSettings`, `Filters.json`, `ProjectSettings.json`을 읽고 `FilterManager`, `SortingLayer.SyncWithSettings(...)`, `UiSystem.ProjectSettings` 같은 설정성 서브시스템을 맞춥니다.
6. 시작 월드를 `WorldLoader.LoadWorld(...)` 또는 `WorldLoader.LoadWorldFromJson(...)`으로 로드하고, 실패 시 fallback world 또는 `Empty World`를 활성화합니다.
7. 월드가 준비되면 에셋 바인딩을 수행한 뒤 `Time.Reset()`과 `new GameLoop { ProjectSettings = projectSettings }`로 런타임 tick 상태를 시작합니다.
8. `device.Window.OnSdlEvent += Verity.Input.Input.ProcessEvent`와 `AudioSystem.Initialize()`를 연결한 뒤 메인 루프에 들어갑니다.
9. 데스크톱 오디오 백엔드는 `miniaudio`를 사용하며, .NET 쪽에서는 `Miniaudio-CS`를 통해 연결됩니다.

에디터 플레이 모드도 큰 구조는 같습니다. `EditorApp.EnterPlayMode()`는 현재 월드 snapshot을 저장하고 `Time.Reset()` 후 `GameLoop`를 새로 만든 다음 `IsPlaying = true`로 전환합니다. 즉, Verity의 “엔진 진입”은 단일 `Main` 함수 하나보다, 호스트가 월드/시간/루프를 재초기화해 실행 상태로 바꾸는 절차로 이해하는 편이 맞습니다.

프로젝트를 닫거나 다른 프로젝트로 전환할 때도 같은 철학을 유지합니다. 현재 에디터는 `EditorApp.ResetProjectScopedState()`에서 preview server, pending action 큐, asset watcher, script compiler, Lua hot-reload, undo/selection/inspector cache, 입력, filter registry를 한 번에 정리한 뒤 `VerityCore.ResetRuntime()`을 호출합니다. 이 경로 덕분에 프로젝트 전환 뒤에도 이전 월드/스크립트/에디터 UI 상태가 다음 세션으로 새지 않도록 관리합니다.

### 1.2 `GameLoop`의 3개 흐름 진입 방식

Verity는 기본적으로 세 개의 흐름을 분리해서 운용합니다.

| 흐름 | 기준 값 | 역할 |
| :--- | :--- | :--- |
| Logic Tick | `Time.TargetTPS` | 스크립트 lifecycle, coroutine, 애니메이션, 일반 게임 로직 처리 |
| Physics Tick | `Time.TargetPTPS` | 강체 적분, 충돌 판정, 접촉 해석, 물리 이벤트 처리 |
| Render Frame | 별도 프레임 루프 | 카메라 기준 렌더러 수집, 정렬, 드로우, 후처리 수행 |

세 흐름이 실제로 들어가는 입구는 다음과 같습니다.

- Logic/Physics는 호스트가 프레임마다 `GameLoop.TickLogic(deltaTime)`를 호출하면서 함께 진입합니다.
- Render는 `GameLoop.TickRender()`가 `OnRender`만 호출하는 최소 훅으로 존재하지만, 현재 런타임 기본 경로는 `RenderPipeline.RenderWorld(...)`와 `UiRenderer.Render(...)`를 호스트 메인 루프에서 직접 호출합니다.
- 그래서 현재 구조는 “로직/물리는 `GameLoop` 중심, 렌더는 호스트 프레임 루프 중심”이라고 보는 것이 정확합니다.

`TickLogic(deltaTime)`의 내부 진입 규칙도 중요합니다.

1. `WorldLoader.PendingWorldName != null`이면 즉시 반환해 월드 전환 중에는 tick을 멈춥니다.
2. `WorldManager.ActiveWorld`가 없으면 아무 것도 실행하지 않습니다.
3. 활성 월드의 custom setting 또는 `ProjectSettings`에서 `TargetTPS`, `TargetPTPS`를 결정합니다.
4. `deltaTime * Time.TimeScale`을 logic/physics accumulator에 누적하고, 각각의 고정 간격을 넘을 때만 tick을 실행합니다.
5. 마지막에 `world.ProcessPendingDestroys()`를 호출해 로직/물리 중 예약된 파괴를 한 곳에서 정리합니다.

Logic Tick 내부의 기본 실행 순서는 다음과 같습니다.

1. `Awake`
2. `Start`
3. `FixedUpdate`
4. `Update`
5. Coroutine 전진
6. `LateUpdate`

이 구조의 존재 이유는 다음과 같습니다.

- 스크립트 갱신과 렌더링을 분리해야 프레임레이트 변화가 로직 의미를 직접 깨뜨리지 않습니다.
- 물리는 별도 tick으로 분리해야 충돌과 적분의 일관성을 유지할 수 있습니다.
- coroutine이 logic tick 기준으로 전진해야 스크립트 대기 규칙이 예측 가능해집니다.

### 1.3 Logic / Physics / Render 세부 흐름

#### Logic Flow: `PerformLogicTick(...)`

Logic Flow는 한 번 진입할 때마다 다음 순서로 진행됩니다.

1. `RuntimeProfiler.BeginLogicTick()`으로 profiling 구간을 시작합니다.
2. `Verity.Input.Input.NewLogicTick()`으로 입력 상태를 logic tick 경계에 맞춰 갱신합니다.
3. `Time.DeltaTime`, `Time.TotalTime`, `Time.LogicTickCount`를 갱신합니다.
4. `AnimationSystem.Update(fixedDelta)`를 먼저 실행합니다.
5. 활성 스크립트 목록을 가져와 아직 실행되지 않은 스크립트에 `Awake`, `Start`를 1회만 호출합니다.
6. 모든 활성 스크립트에 `FixedUpdate`, `Update`, coroutine 전진, `LateUpdate`를 순서대로 호출합니다.
7. 각 단계 뒤에 `OnFixedUpdate`, `OnUpdate`, `OnLateUpdate` 같은 엔진 측 콜백 훅도 호출합니다.

중요한 점은 Verity에서 `FixedUpdate`가 physics tick 안이 아니라 logic flow 안에 있다는 점입니다. 즉, 사용자 스크립트의 `FixedUpdate`는 “logic 고정 tick 단계”이고, 실제 물리 적분은 별도의 Physics Flow에서 수행됩니다.

#### Physics Flow: `PerformPhysicsTick(...)`

Physics Flow는 physics accumulator가 `physicsFixedDelta` 이상일 때만 진입합니다.

1. `Time.PhysicsTickCount`를 증가시킵니다.
2. `PhysicsManager.Step(fixedDelta, world, ProjectSettings)`로 실제 물리 시뮬레이션을 수행합니다.
3. 완료 후 `OnPhysicsTick` 콜백을 호출합니다.

이 흐름은 로직과 동일한 프레임 안에서 여러 번 돌 수도 있고, 프레임 상황에 따라 한 번도 돌지 않을 수도 있습니다. 핵심은 render frame과 1:1로 묶이지 않는다는 점입니다.

#### Render Flow

Render Flow는 현재 `GameLoop`에 완전히 흡수되어 있지 않습니다.

- `GameLoop.TickRender()`는 `OnRender?.Invoke()`만 수행하는 얇은 확장 지점입니다.
- 실제 런타임 기본 구현은 호스트 루프에서 카메라를 찾고 `RenderPipeline.RenderWorld(world, mainCam)`를 호출한 뒤, `UiRenderer.Render(...)`로 UI canvas를 그립니다.
- 따라서 Render Flow는 현재 구조상 “엔진 루프의 세 번째 축”이지만, 구현 위치는 `GameLoop`보다 런타임/에디터 호스트에 더 가깝습니다.

### 1.4 엔진 종료 흐름

런타임 종료는 메인 루프 `while (!device.ShouldClose)`가 끝난 뒤 정리 단계로 이어집니다.

1. `AudioSystem.Shutdown()`으로 오디오 시스템을 먼저 종료합니다.
2. 멀티 윈도우가 켜져 있었다면 보조 윈도우 렌더러와 관련 SDL 리소스를 먼저 정리합니다.
3. `renderPipeline.Dispose()`, `shader.Dispose()`, `textureManager.Dispose()`로 그래픽 리소스를 해제합니다.
4. 마지막으로 `device.Dispose()`와 log writer 정리를 수행합니다.

에디터 플레이 모드 종료는 별도 경로입니다. `EditorApp.ExitPlayMode()`는 저장해 둔 snapshot을 `Restore(...)`해 월드 상태를 되돌리고, 에셋을 다시 바인딩한 뒤 `_gameLoop = null`, `IsPlaying = false`, `Verity.Input.Input.Enabled = true` 순서로 플레이 상태를 해제합니다.

즉, Verity의 shutdown도 단일 `VerityCore.Shutdown()` API보다, “호스트가 각 서브시스템의 수명주기를 역순으로 정리하는 구조”로 이해해야 합니다.

---

## 2. ECS와 월드 구조

Verity의 런타임 코어는 `World`, `Entity`, `Component`, `Transform`, `Script` 다섯 축으로 이해하면 됩니다.

| 타입 | 존재 이유 | 현재 구현상 핵심 포인트 |
| :--- | :--- | :--- |
| `World` | 전체 엔티티 트리와 전역 설정을 관리하기 위해 | 루트 엔티티 목록, 플랫 엔티티 캐시, 활성 스크립트 캐시, `StateVersion` 보유 |
| `Entity` | 컴포넌트를 묶는 최소 런타임 단위가 필요해서 | 컴포넌트 리스트 기반, 타입별 조회 캐시 보유 |
| `Component` | 공통 수명주기와 소유 관계를 묶기 위해 | `Owner`, `Transform`, `Enabled` 제공 |
| `Transform` | 계층과 좌표계를 모든 엔티티에 일관되게 부여하기 위해 | local/world matrix, world rotation, world scale dirty-cache 사용 |
| `Script` / `LuaScriptComponent` | 게임 로직을 ECS 컴포넌트로 붙일 수 있게 하기 위해 | C#은 `Script`, Lua는 `LuaScriptComponent`로 연결되고 둘 다 월드/엔티티 수명주기를 따름 |

### 2.1 최근 구조 변경의 의미

이번 문서 정리 대상이 된 최근 구조 변경은 성능 관점에서 중요합니다.

| 변경점 | 이전 문제 | 현재 구조 |
| :--- | :--- | :--- |
| `World.GetAllEntities()` 플랫 캐시 | 재귀 `yield return`로 enumerator 할당과 느린 순회 발생 | 한 번 평탄화한 `IReadOnlyList<Entity>` 캐시 재사용 |
| `World.StateVersion` | 물리/기타 시스템이 월드 상태 변화를 싸게 감지하기 어려움 | 상태가 바뀔 때 버전 증가, 캐시 재구축 트리거로 사용 |
| `Entity.GetComponent<T>()` 캐시 | 선형 탐색 반복 | 타입별 단건/다건 캐시 사용 |
| `Transform` dirty-cache | world transform 계산이 부모 체인을 계속 다시 탐색 | local/world matrix 및 회전/스케일 캐시 |

---

## 3. 스크립팅 통합 구조

이 문서는 스크립팅 사용법 자체보다, 스크립트 시스템이 엔진 구조 안에 어떻게 연결되는지를 설명합니다.

### 3.1 ECS에 스크립트가 붙는 방식

Verity는 스크립트를 ECS 컴포넌트로 다룹니다.

- C# 스크립트는 `Script`를 통해 엔티티에 부착됩니다.
- Lua 스크립트는 `LuaScriptComponent`를 통해 엔티티에 부착됩니다.
- 두 방식 모두 `Entity`와 `Component`의 소유 관계, 활성화 상태, 월드 전환 규칙을 그대로 따릅니다.

즉, 스크립팅은 ECS 바깥의 별도 런타임이 아니라, 월드 안에 배치된 컴포넌트 계층 위에서 동작하는 로직 계층입니다.

### 3.2 GameLoop와 스크립트의 연결

스크립트 실행은 `GameLoop`의 logic flow에 연결됩니다.

- `Awake`, `Start`, `FixedUpdate`, `Update`, coroutine 전진, `LateUpdate`는 logic tick 순서 안에서 처리됩니다.
- Physics 시뮬레이션은 별도 physics flow에서 수행되므로, 스크립트는 물리와 같은 프레임에 실행되더라도 동일한 단계로 취급되지 않습니다.
- coroutine 역시 render frame이 아니라 logic tick 기준으로 전진하므로, 스크립트 대기 규칙은 렌더 속도와 분리됩니다.

세부 lifecycle API, coroutine 사용법, shortcut API는 [Scripting 문서](./Docs/Scripting.md)로 분리합니다.

---

## 4. 물리 엔진 구조

현재 물리 엔진은 다음 계층으로 이해할 수 있습니다.

1. 월드에서 물리 객체와 shape를 수집
2. spatial hash grid로 broad phase 후보 추출
3. SAT 기반 narrow phase 충돌 판정
4. pair 단위 contact 그룹화
5. penetration correction과 impulse 해석
6. touch/detect 이벤트 dispatch

### 4.1 현재 물리 구조의 핵심 특징

- `Physical` 하나에 여러 `PhysicalShape`를 붙일 수 있습니다.
- `Physical`이 없는 shape는 가상 static body로 취급됩니다.
- 물리 객체 캐시는 `World.StateVersion`이 바뀔 때만 재구축됩니다.
- sub-step은 고정 8회가 아니라 adaptive 방식입니다.

### 4.2 남아 있는 제약

- grid는 여전히 sub-step마다 다시 구축됩니다.
- continuous collision detection은 없습니다.
- solver warm starting, island solving도 아직 없습니다.

---

## 5. 렌더링 구조

현재 렌더링은 CPU 정렬 기반의 immediate draw 모델에 가깝습니다.

### 5.1 현재 렌더링 파이프라인의 핵심

- 월드 전체 엔티티를 단일 순회하며 렌더러를 수집합니다.
- sorting layer와 order in layer를 기준으로 CPU에서 정렬합니다.
- sprite 경로 해석, slice 해석, 그림자 보조 데이터는 캐시합니다.
- 그림자 occluder 후보 정렬에는 scratch buffer를 재사용합니다.

### 5.2 아직 해결되지 않은 큰 제약

진짜 draw-call batching은 아직 없습니다.

이건 단순 최적화가 아니라 렌더 상태, 텍스처 묶음, 머티리얼 경계, uniform 업로드 방식까지 다시 잡아야 하는 아키텍처 레벨 작업입니다. 따라서 현재 문서에서는 “이미 해결된 문제”와 “남아 있는 구조적 한계”를 분리해서 기록합니다.

### 5.3 UI 렌더링의 위치

UI는 렌더링 파이프라인과 완전히 분리된 독립 앱이 아니라, 월드 렌더 뒤에 이어지는 별도 레이어로 동작합니다.

- 런타임 기본 경로에서는 `RenderPipeline.RenderWorld(...)` 뒤에 `UiRenderer.Render(...)`가 호출됩니다.
- `UiSystem`은 에셋 루트와 프로젝트 설정을 공유하며, 월드 렌더와 같은 호스트 프레임 루프에 매달려 있습니다.
- 따라서 UI는 엔진 구조상 별도 사용자 경험 계층이지만, 실행 시점은 render flow에 인접한 후단 패스로 이해하는 것이 맞습니다.

---

## 6. 에디터 구조

에디터는 엔진 바깥의 별도 제품이 아니라, 동일한 월드/ECS/렌더링 시스템을 호스팅하는 상위 애플리케이션입니다.

### 6.1 플레이 모드와 런타임 재사용

- `EditorApp.EnterPlayMode()`는 현재 월드 snapshot을 저장한 뒤 `Time.Reset()`과 새 `GameLoop`를 통해 런타임 상태를 다시 구성합니다.
- `EditorApp.ExitPlayMode()`는 snapshot 복원, 에셋 재바인딩, 입력/플레이 상태 해제를 통해 편집 상태로 되돌립니다.
- 즉, 에디터의 플레이 모드는 별도 엔진을 하나 더 실행하는 방식이 아니라, 같은 엔진 시스템을 재초기화해 재사용하는 구조입니다.

### 6.2 에디터의 역할

에디터는 다음 역할을 담당합니다.

- 월드/에셋/프로젝트 설정을 편집하는 호스트 UI 제공
- 플레이 모드 전환과 런타임 수명주기 제어
- 엔진 서브시스템이 사용할 경로, 설정, 에셋 바인딩 상태 관리

따라서 에디터는 Core, ECS, GameLoop, Rendering, UI 위에 올라가는 orchestration 계층으로 보는 편이 정확합니다.

---

## 7. 현재 남아 있는 공통 병목 후보

이번 코드와 문서를 기준으로, 여전히 성능에 민감한 지점은 다음과 같습니다.

- `FindObjectOfType`, `FindObjectsOfType` 같은 전역 검색 API
- 부모/자식 방향의 재귀 컴포넌트 검색
- UI binding/action의 reflection 경로
- 텍스트 렌더링의 무거운 glyph/raster 경로
- `AssetPathUtility` 캐시 미스 시 파일 시스템 접근
- batching 부재로 인한 많은 sprite/tile draw submit 비용

---

## 8. 문서 구성

아래 문서들이 실제 상세 레퍼런스입니다.

| 문서 | 범위 |
| :--- | :--- |
| [Core 문서](./Docs/Core.md) | ECS, 월드, 공용 수학 타입, 디버그, 타일맵, 에셋 경로 유틸리티, `ObjectPool<T>`, `SceneTransition`, `SaveManager` |
| [Scripting 문서](./Docs/Scripting.md) | `Script`, lifecycle, coroutine, 스크립트 shortcut API, `EventBus` |
| [Physics 문서](./Docs/Physics.md) | `Physical`, `PhysicalShape`, 쿼리, contact, solver 구조 |
| [Graphics 문서](./Docs/Graphics.md) | 카메라, 렌더러, 조명, sorting layer, 후처리, UI 텍스트 렌더링, `ParticleEmitter`, `ParticleSystem`, `ProfilerOverlay` |
| [Animation 문서](./Docs/Animation.md) | `Animator`, clip, track, controller graph |
| [Audio 문서](./Docs/Audio.md) | `AudioClip`, `AudioSource`, `AudioManager`, audio system |
| [Input 문서](./Docs/Input.md) | 입력 폴링, `KeyCode`, `MouseButton` |
| [Filter 문서](./Docs/Filter.md) | `Filter`, `MixedFilter`, `FilterRegistry`, bitmask 체계 |
| [UI 문서](./Docs/UI.md) | UI 노드, 캔버스, 바인딩, 레이아웃, 현재 스크린 UI 구조, `DynamicArea` 부분 갱신 |
| [Editor 문서](./Docs/Editor.md) | 에디터 앱 구조, 플레이 모드, 인스펙터/계층/프로젝트 도구 |

---

## 9. 브라우저 런타임과 렌더 백엔드

현재 아키텍처는 단일 렌더 디바이스 기반 구조가 아니라, 공통 렌더 계층 아래에 타깃별 백엔드가 분리된 구조입니다.

### 9.1 계층 구조

| 계층 | 공통/타깃 | 주요 타입 |
| :--- | :--- | :--- |
| 상위 렌더 로직 | 공통 | `RenderPipeline`, `Shader2D`, `TextureManager`, `UiRenderer` |
| 렌더 디바이스 추상화 | 공통 | `IRenderDevice`, `RenderProgram`, `RenderTexture`, `RenderTarget`, `RenderMesh` |
| 네이티브 백엔드 | 타깃별 | `GraphicsDevice`, `NativeRenderProgram`, `NativeRenderTexture` |
| 브라우저 백엔드 | 타깃별 | `BrowserRenderDevice`, `BrowserRenderProgram`, `BrowserRenderTexture` |

즉 현재 Verity는 "공통 파이프라인 + 타깃별 렌더 백엔드" 구조로 보는 것이 맞습니다.

### 9.2 브라우저 진입 경로

브라우저 타깃은 `Verity.Game.Browser` 프로젝트가 담당합니다.

주요 흐름:

1. `main.js`가 WebAssembly 런타임과 캔버스를 초기화
2. `BrowserEntry`가 브라우저 런타임 진입점 역할 수행
3. `BrowserRenderDevice`가 상위 렌더 요청을 브라우저 백엔드로 연결
4. `graphics.js`가 실제 WebGL 2.0 호출을 수행

### 9.3 셰이더 경로 해석

웹에서는 프로그램 생성 전에 `BrowserShaderSourceAdaptation`이 데스크톱 GLSL 소스를 WebGL 2.0 / GLSL ES 3.00 규격으로 변환합니다.

따라서 웹 셰이더 문제는 다음 세 층 중 어디서든 발생할 수 있습니다.

- 공통 렌더 파이프라인
- 브라우저 셰이더 소스 변환
- JS WebGL 바인딩/컴파일

### 9.4 관련 문서

이 브라우저 백엔드 설명은 이제 다음 문서와 함께 읽는 것이 맞습니다.

- [Graphics 문서](./Docs/Graphics.md): 렌더링 기능과 브라우저 렌더 백엔드 보강
- [Build 문서](./Docs/Build.md): 웹 빌드, 퍼블리시, 브라우저 런타임, 셰이더 변환, 트리밍 보강

---

## 10. 멀티 카메라와 멀티 윈도우

현재 아키텍처는 단일 카메라 렌더링뿐 아니라, 다중 카메라 출력과 별도 네이티브 윈도우 표시까지 지원합니다.

### 10.1 카메라 출력 계층

멀티 카메라 출력은 다음 계층으로 구성됩니다.

| 계층 | 타입 | 역할 |
| :--- | :--- | :--- |
| 출력 설정 | `CameraOutput` | 카메라의 렌더 대상(MainWindow / RenderTexture / Window)을 제어 |
| 카메라 선택 | `CameraSelection` | 월드에서 기본 카메라와 활성 출력을 탐색 |
| 렌더 실행 | `RenderPipeline.RenderCameraOutputs` | 활성 출력별 카메라 렌더를 수행하고 텍스처에 저장 |
| 텍스처 에셋 | `CameraTextureAsset` | 렌더 텍스처의 크기/필터 설정을 에셋 파일로 관리 |
| 윈도우 렌더 | `NativeMultiWindowRenderer` | 별도 SDL2 윈도우에 렌더 텍스처를 블릿 |

### 10.2 카메라 선택 우선순위

`CameraSelection.GetDefaultCamera`는 다음 우선순위로 기본 카메라를 결정합니다.

1. `CameraOutputTarget.MainWindow`이고 `Primary = true`인 카메라
2. `CameraOutputTarget.MainWindow`인 첫 카메라
3. `MainCamera` 태그를 가진 활성 카메라
4. `CameraOutput`이 없거나 `MainWindow`인 첫 활성 카메라
5. 비 MainWindow 출력 카메라 (최후 fallback)

이 구조 덕분에 대부분의 프로젝트는 아무 설정 없이 첫 카메라가 기본이 되지만, 명시적으로 제어할 수도 있습니다.

### 10.3 멀티 윈도우 렌더링 흐름

데스크톱 런타임에서 `CameraOutputTarget.Window`를 사용하면:

1. `NativeMultiWindowRenderer.Render`가 매 프레임 호출됩니다.
2. `CameraSelection`에서 Window 대상 출력을 수집합니다.
3. `RenderPipeline`에서 이미 렌더된 텍스처를 조회합니다.
4. 각 출력에 대해 SDL2 보조 윈도우를 생성하거나 풀에서 획득합니다.
5. OpenGL 컨텍스트를 보조 윈도우로 전환하고 텍스처를 블릿합니다.
6. 메인 윈도우 컨텍스트를 복원합니다.
7. 더 이상 사용하지 않는 윈도우는 풀로 반환됩니다.

### 10.4 윈도우 풀링과 예열

윈도우 생성 비용을 줄이기 위해 `MultiWindowPrewarmMode` 설정을 제공합니다.

| 모드 | 동작 |
| :--- | :--- |
| `None` | 필요할 때마다 생성 |
| `Startup` | 엔진 시작 시 `MultiWindowPrewarmCount`만큼 미리 생성 |
| `LazyBackground` | 매 프레임마다 하나씩 점진적으로 풀을 채움 |

풀에서 반환된 윈도우는 숨김 상태로 유지되며, 새 출력이 필요할 때 즉시 재사용됩니다.

### 10.5 플랫폼 제약

- `CameraOutputTarget.RenderTexture`는 모든 플랫폼에서 사용할 수 있습니다.
- `CameraOutputTarget.Window`는 데스크톱 네이티브 런타임에서만 동작합니다. 브라우저 백엔드는 별도 윈도우 생성이 불가능합니다.
- `CameraOutputTarget.MainWindow`는 기존 단일 카메라 동작과 동일합니다.
