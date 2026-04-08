# Verity 엔진 아키텍처 문서

이 문서는 Verity 엔진 전체 구조를 설명하는 상위 문서입니다.

이 문서의 목적은 두 가지입니다.

1. 엔진이 실제로 어떤 실행 모델과 데이터 구조 위에서 동작하는지 설명하는 아키텍처 문서
2. 세부 스크립팅 API 문서로 진입하기 위한 문서 맵 제공

상세 클래스, 함수, 메서드, 프로퍼티 레퍼런스는 시스템별 문서로 분리되어 있습니다. 이렇게 분리한 이유는 다음과 같습니다.

- 한 파일에 모든 API를 몰아넣으면 검색은 되지만 유지보수가 빠르게 어려워집니다.
- 아키텍처 설명과 API 레퍼런스는 읽는 목적이 다릅니다.
- Core, Physics, Graphics, UI처럼 변경 주기가 다른 시스템을 독립적으로 갱신할 수 있어야 합니다.

---

## 1. 엔진 실행 모델

Verity는 기본적으로 세 개의 흐름을 분리해서 운용합니다.

| 흐름 | 기준 값 | 역할 |
| :--- | :--- | :--- |
| Logic Tick | `Time.TargetTPS` | 스크립트 lifecycle, coroutine, 애니메이션, 일반 게임 로직 처리 |
| Physics Tick | `Time.TargetPTPS` | 강체 적분, 충돌 판정, 접촉 해석, 물리 이벤트 처리 |
| Render Frame | 별도 프레임 루프 | 카메라 기준 렌더러 수집, 정렬, 드로우, 후처리 수행 |

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

---

## 2. ECS와 월드 구조

Verity의 런타임 코어는 `World`, `Entity`, `Component`, `Transform`, `Script` 다섯 축으로 이해하면 됩니다.

| 타입 | 존재 이유 | 현재 구현상 핵심 포인트 |
| :--- | :--- | :--- |
| `World` | 전체 엔티티 트리와 전역 설정을 관리하기 위해 | 루트 엔티티 목록, 플랫 엔티티 캐시, 활성 스크립트 캐시, `StateVersion` 보유 |
| `Entity` | 컴포넌트를 묶는 최소 런타임 단위가 필요해서 | 컴포넌트 리스트 기반, 타입별 조회 캐시 보유 |
| `Component` | 공통 수명주기와 소유 관계를 묶기 위해 | `Owner`, `Transform`, `Enabled` 제공 |
| `Transform` | 계층과 좌표계를 모든 엔티티에 일관되게 부여하기 위해 | local/world matrix, world rotation, world scale dirty-cache 사용 |
| `Script` | 게임 로직을 컴포넌트로 붙일 수 있게 하기 위해 | reflection 초기 바인딩 후 delegate 호출, coroutine 관리 |

### 2.1 최근 구조 변경의 의미

이번 문서 정리 대상이 된 최근 구조 변경은 성능 관점에서 중요합니다.

| 변경점 | 이전 문제 | 현재 구조 |
| :--- | :--- | :--- |
| `World.GetAllEntities()` 플랫 캐시 | 재귀 `yield return`로 enumerator 할당과 느린 순회 발생 | 한 번 평탄화한 `IReadOnlyList<Entity>` 캐시 재사용 |
| `World.StateVersion` | 물리/기타 시스템이 월드 상태 변화를 싸게 감지하기 어려움 | 상태가 바뀔 때 버전 증가, 캐시 재구축 트리거로 사용 |
| `Entity.GetComponent<T>()` 캐시 | 선형 탐색 반복 | 타입별 단건/다건 캐시 사용 |
| `Transform` dirty-cache | world transform 계산이 부모 체인을 계속 다시 탐색 | local/world matrix 및 회전/스케일 캐시 |

---

## 3. 스크립팅 런타임 구조

Verity 스크립팅은 Unity 스타일 사용감에 가깝지만, 내부적으로는 더 단순한 구조를 취합니다.

### 3.1 왜 reflection을 초기 바인딩에만 쓰는가

스크립트는 `Awake`, `Start`, `Update` 같은 이름 기반 메서드를 지원해야 합니다. 이를 위해 생성 시점에는 reflection이 필요합니다. 하지만 매 tick마다 reflection으로 메서드를 찾으면 비용이 큽니다. 그래서 현재 구조는 다음과 같습니다.

1. 스크립트 생성 시 lifecycle/physics 메서드를 한 번 찾음
2. 찾은 결과를 delegate로 캐시
3. 실제 루프에서는 delegate만 직접 호출

즉, “유연성은 reflection으로 얻고, 반복 실행 비용은 delegate 캐시로 줄이는” 구조입니다.

### 3.2 coroutine이 logic tick 기준인 이유

coroutine은 render frame 기준이 아니라 logic tick 기준으로 전진합니다. 이 설계를 택한 이유는 다음과 같습니다.

- `WaitForTicks`, `WaitForPhysicalTicks`의 의미를 분명히 하기 위해
- 프레임 드랍이 있어도 스크립트 시뮬레이션 규칙이 크게 흔들리지 않게 하기 위해
- 스크립트 로직과 렌더 타이밍을 분리하기 위해

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

---

## 6. 현재 남아 있는 공통 병목 후보

이번 코드와 문서를 기준으로, 여전히 성능에 민감한 지점은 다음과 같습니다.

- `FindObjectOfType`, `FindObjectsOfType` 같은 전역 검색 API
- 부모/자식 방향의 재귀 컴포넌트 검색
- UI binding/action의 reflection 경로
- 텍스트 렌더링의 무거운 glyph/raster 경로
- `AssetPathUtility` 캐시 미스 시 파일 시스템 접근
- batching 부재로 인한 많은 sprite/tile draw submit 비용

---

## 7. 문서 구성

아래 문서들이 실제 상세 레퍼런스입니다.

| 문서 | 범위 |
| :--- | :--- |
| [Core 문서](D:/Verity/Docs/Core.md) | ECS, 월드, 공용 수학 타입, 디버그, 타일맵, 에셋 경로 유틸리티 |
| [Scripting 문서](D:/Verity/Docs/Scripting.md) | `Script`, lifecycle, coroutine, 스크립트 shortcut API |
| [Physics 문서](D:/Verity/Docs/Physics.md) | `Physical`, `PhysicalShape`, 쿼리, contact, solver 구조 |
| [Graphics 문서](D:/Verity/Docs/Graphics.md) | 카메라, 렌더러, 조명, sorting layer, 후처리 |
| [Animation 문서](D:/Verity/Docs/Animation.md) | `Animator`, clip, track, controller graph |
| [Audio 문서](D:/Verity/Docs/Audio.md) | `AudioClip`, `AudioSource`, `AudioManager`, audio system |
| [Input 문서](D:/Verity/Docs/Input.md) | 입력 폴링, `KeyCode`, `MouseButton` |
| [Filter 문서](D:/Verity/Docs/Filter.md) | `Filter`, `MixedFilter`, `FilterRegistry`, bitmask 체계 |
| [UI 문서](D:/Verity/Docs/UI.md) | UI 노드, 캔버스, 바인딩, 레이아웃, UI 시스템 |

---