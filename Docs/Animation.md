# Verity 애니메이션 문서

이 문서는 애니메이션 시스템의 구조와 공개 API를 설명합니다.

현재 기준으로 **`Animator`가 주력(primary) 애니메이션 시스템**이며, controller/parameter/condition transition과 에디터/런타임 통합도 이 경로를 기준으로 구성되어 있습니다. `ClipAnimator`는 단순 clip 재생용 하위 호환 컴포넌트로 유지되지만, 신규 구현에는 `Animator` 사용을 권장합니다.

범위는 다음과 같습니다.

- `Animator`
- `ClipAnimator` (deprecated / simple playback)
- `AnimationClip`, `AnimationTrack`, `Keyframe`
- 상태 머신 기반 controller 그래프
- 런타임 업데이트 시스템

---

## 1. 애니메이션 시스템 개요

현재 애니메이션 시스템은 “컴포넌트 멤버 경로에 값을 샘플링해서 주입하는 구조”입니다.

즉, 애니메이션은 전용 transform만 조작하는 것이 아니라, 경로 문자열로 지정된 컴포넌트의 프로퍼티/필드에 값을 써넣습니다.

예:

- `Transform.Position`
- `SpriteRenderer.Color`
- 사용자 정의 컴포넌트의 공개 프로퍼티

### 존재 이유

- 시스템별 전용 애니메이션 모듈을 따로 만들지 않고, 공용 경로 기반으로 확장 가능하게 하기 위해
- 데이터만으로 clip을 저장하고, 런타임에는 공통 샘플러를 사용하기 위해

---

## 2. `Animator`

`Animator`는 엔티티에 붙어서 controller와 clip을 실제로 재생하는 **기본 런타임 애니메이션 컴포넌트**입니다.

### 프로퍼티

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `ControllerPath` | `string` | controller asset 경로 |
| `ControllerGuid` | `string` | controller GUID |
| `Controller` | `AnimatorController?` | 현재 controller |
| `Speed` | `float` | 재생 속도 배율 |
| `IsPlaying` | `bool` | 현재 재생 중인지 |
| `IsPaused` | `bool` | 일시 정지 상태인지 |
| `CurrentTime` | `float` | 현재 상태 시간 |
| `CurrentStateName` | `string` | 현재 상태 이름 |

### 메서드

| 시그니처 | 설명 |
| :--- | :--- |
| `void Play(string stateName, bool restart = true)` | 특정 상태 재생 |
| `void Stop()` | 재생 정지 |
| `void Pause()` | 현재 시간은 유지한 채 일시 정지 |
| `void Resume()` | 일시 정지된 상태를 이어서 재생 |
| `void SetFloat(string name, float value)` | float 파라미터 설정 |
| `void SetInt(string name, int value)` | int 파라미터 설정 |
| `void SetBool(string name, bool value)` | bool 파라미터 설정 |
| `void SetTrigger(string name)` | trigger 파라미터 설정 |
| `void ResetTrigger(string name)` | trigger 초기화 |
| `void UpdateAnimation(float deltaTime)` | 애니메이션 스텝 전진 |
| `void SampleCurrentState(float time)` | 현재 상태를 특정 시간에 샘플링 |
| `void SampleClip(AnimationClip? clip, float time)` | clip을 특정 시간에 샘플링 |

### 존재 이유

- 엔티티 단위로 애니메이션 상태와 controller 파라미터를 보관해야 해서
- clip 데이터와 런타임 재생 상태를 분리하기 위해

### 구현상 중요한 규칙

- binding path 해석 결과는 캐시됩니다.
- controller가 교체되면 현재 상태/시간/캐시가 초기화됩니다.
- `Enabled` 상태일 때 `AnimationSystem`에 등록됩니다.
- condition transition, parameter, trigger 소비 흐름은 `Animator`를 기준으로 지원됩니다.

### 언제 `Animator`를 써야 하나?

- controller 기반 상태 머신이 필요할 때
- float/int/bool/trigger 파라미터를 쓸 때
- 조건 기반 상태 전이가 필요할 때
- 에디터/런타임 통합 경로와 동일한 시스템을 쓰고 싶을 때

---

## 3. Clip 데이터 타입

## 3.1 `AnimationClip`

`AnimationClip`은 여러 트랙과 키프레임을 묶은 애니메이션 데이터입니다.

### 프로퍼티

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `Name` | `string` | 클립 이름 |
| `FrameRate` | `float` | 편집 기준 프레임레이트 |
| `Loop` | `bool` | 루프 여부 |
| `Duration` | `float` | 전체 길이 |
| `Tracks` | `List<AnimationTrack>` | 트랙 목록 |

### 메서드

| 시그니처 | 설명 |
| :--- | :--- |
| `void AddTrack(AnimationTrack track)` | 트랙 추가 |
| `void RecalculateDuration()` | 마지막 키프레임 기준 길이 재계산 |
| `void PostLoad()` | 역직렬화 후 타입 보정과 정렬 수행 |

### 존재 이유

- 애니메이션을 재사용 가능한 데이터 단위로 저장하기 위해

## 3.2 `Keyframe`

### 프로퍼티

- `float Time`
- `object Value`
- `float InTangent`
- `float OutTangent`

### 생성자

- `Keyframe()`
- `Keyframe(float time, object value)`

### 존재 이유

- 시간별 값 변화를 표현하는 최소 단위가 필요하기 때문입니다.

## 3.3 `AnimationTrack`

`AnimationTrack`은 하나의 경로에 대한 시간축 값을 나타냅니다.

### 프로퍼티

- `string Path`
- `string TypeName`
- `List<Keyframe> Keyframes`

### 메서드

- `void SortKeyframes()`
- `object Evaluate(float time)`

### 존재 이유

- 한 클립 안에서 서로 다른 멤버를 독립적으로 샘플링하기 위해

---

## 4. Controller 상태 머신

현재 controller는 상태, 전이, 파라미터로 구성됩니다.

## 4.1 `AnimatorConditionMode`

- `If`
- `IfNot`
- `Greater`
- `Less`
- `Equals`
- `NotEqual`

## 4.2 `AnimatorCondition`

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `Parameter` | `string` | 파라미터 이름 |
| `Mode` | `AnimatorConditionMode` | 비교 방식 |
| `Threshold` | `float` | 비교 값 |

## 4.3 `AnimatorTransition`

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `ToState` | `string` | 대상 상태 이름 |
| `HasExitTime` | `bool` | exit time 사용 여부 |
| `ExitTime` | `float` | exit time 값 |
| `Conditions` | `List<AnimatorCondition>` | 전이 조건 |

## 4.4 `AnimatorState`

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `Name` | `string` | 상태 이름 |
| `Clip` | `AnimationClip?` | 상태가 재생할 clip |
| `Transitions` | `List<AnimatorTransition>` | 가능한 전이 목록 |

## 4.5 `AnimatorController`

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `States` | `List<AnimatorState>` | 상태 목록 |
| `DefaultStateName` | `string` | 기본 상태 이름 |
| `FloatParameters` | `Dictionary<string, float>` | float 파라미터 |
| `IntParameters` | `Dictionary<string, int>` | int 파라미터 |
| `BoolParameters` | `Dictionary<string, bool>` | bool 파라미터 |
| `TriggerParameters` | `Dictionary<string, bool>` | trigger 파라미터 |
| `DefaultState` | `AnimatorState?` | 기본 상태 |

메서드:

- `AnimatorState? FindState(string? stateName)`
- `void AddState(AnimatorState state)`
- `void PostLoad()`

### 존재 이유

- clip 단순 재생이 아니라 조건 기반 상태 전이를 지원하기 위해

---

## 5. `AnimationSystem`

`AnimationSystem`은 활성 animation component 목록을 관리하는 정적 시스템입니다.

### 메서드

- `void Register(Animator animator)`
- `void Unregister(Animator animator)`
- `void Register(ClipAnimator animator)`
- `void Unregister(ClipAnimator animator)`
- `void Update(float deltaTime)`

### 존재 이유

- 월드 내 여러 animator를 매 tick 공통 규칙으로 갱신하기 위해

---

## 6. `ClipAnimator` (deprecated)

`ClipAnimator`는 제거되지 않았고 계속 동작하지만, 현재는 **단순 clip/state 재생용 하위 호환 표면**으로 보는 것이 맞습니다.

지원 범위:

- `Play`, `PlayIfChanged`, `Stop`, `Pause`, `Resume`
- 기본 상태 clip 재생
- 수동 상태 전환
- `ClipPlayback` 기반 샘플링/페이드

제한 사항:

- controller asset 기반 상태 머신을 직접 제공하지 않습니다.
- `SetFloat` / `SetInt` / `SetBool` / `SetTrigger` 같은 parameter API가 없습니다.
- 조건 기반 자동 transition 모델이 없습니다.
- 에디터/런타임의 주 경로는 `Animator` 기준으로 정리되어 있습니다.

### 마이그레이션 가이드

`ClipAnimator`를 새 코드에서 사용할 계획이라면, 가능하면 아래 기준으로 `Animator`로 옮기는 편이 안전합니다.

1. 기본 clip을 `AnimatorController`의 default state로 이동합니다.
2. 수동 `Play("StateName")` 호출은 동일한 이름의 animator state 재생으로 치환합니다.
3. 조건 분기 로직은 controller transition + parameter(`SetFloat`, `SetInt`, `SetBool`, `SetTrigger`)로 옮깁니다.
4. 프로젝트의 주 애니메이션 워크플로우는 `Animator` 하나를 기준으로 통일합니다.
