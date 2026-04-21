# Verity C# 스크립팅 API 문서

이 문서는 Verity에서 사용하는 **C# 게임플레이 스크립팅 API**를 설명합니다.

기존 `Docs/Scripting.md`의 내용을 기반으로 하되, C# 사용자 관점에서 내용을 더 자세히 풀어 설명하고 예제를 보강했습니다. 이 문서는 **C# 전용 문서**이며, Lua 관련 내용은 포함하지 않습니다.

문서 범위는 다음과 같습니다.

- Script 모델
- Lifecycle
- Coroutine 동작
- Physics Callback
- 정적 Shortcut API
- UI 접근 방식
- EventBus
- 간단한 C# 스크립팅 튜토리얼

---

## 1. Script 모델

게임플레이 스크립트는 `Script`를 상속하며, `Script`는 다시 `Component`를 상속합니다.

즉, C# 스크립트는 단순한 유틸리티 클래스가 아니라 **엔티티에 부착되는 컴포넌트**입니다. 따라서 일반 컴포넌트처럼 엔티티와 함께 생성되고, 활성화 상태와 수명 주기를 공유하며, 다른 컴포넌트와 협력하면서 동작합니다.

`Script`가 존재하는 이유는 다음과 같습니다.

- lifecycle callback 제공
- physics callback 제공
- coroutine 지원
- 자주 쓰는 엔진 접근 경로에 대한 정적 편의 API 제공

가장 기본적인 형태는 다음과 같습니다.

```csharp
using Verity.Engine;

public sealed class PlayerController : Script
{
    private void Awake()
    {
        Debug.Log("PlayerController 초기화");
    }

    private void Update()
    {
        // 매 logic tick마다 실행할 로직
    }
}
```

핵심적으로 기억할 점은 다음과 같습니다.

- 스크립트는 엔티티에 붙는 컴포넌트입니다.
- 일반적인 게임 로직은 `Update`, `FixedUpdate`, coroutine, physics callback에 배치합니다.
- 전역 검색이나 UI 접근 같은 보조 기능은 `Script`가 제공하는 shortcut API를 통해 빠르게 사용할 수 있습니다.

---

## 2. Lifecycle 바인딩

Verity는 스크립트가 생성될 때 lifecycle 메서드를 이름으로 해석하고, delegate를 미리 바인딩합니다.

즉 현재 구조는 다음과 같습니다.

1. 초기화 시 reflection을 한 번 수행
2. 스크립트 인스턴스에 delegate를 캐시
3. 실제 tick에서는 delegate를 직접 호출

이 구조의 장점은, 스크립트 작성자는 Unity 스타일처럼 익숙한 이름의 메서드만 선언하면 되고, 엔진은 런타임 매 tick마다 reflection을 반복하지 않아도 된다는 점입니다.

지원되는 lifecycle 메서드는 다음과 같습니다.

- `Awake()`
- `Start()`
- `FixedUpdate()`
- `Update()`
- `LateUpdate()`
- `OnDrawGizmos()`
- `OnDrawGizmosSelected()`

규칙은 다음과 같습니다.

- 메서드는 파라미터가 없어야 합니다.
- 반환형은 `void`일 수 있습니다.
- `Start()`는 `IEnumerator`를 반환할 수도 있으며, 이 경우 자동으로 coroutine으로 시작됩니다.

### 각 Lifecycle의 역할

#### `Awake()`

가장 이른 초기화 지점입니다. 다른 필드 초기화, 컴포넌트 참조 확보, 이벤트 구독 준비 같은 작업에 적합합니다.

```csharp
private void Awake()
{
    Debug.Log("Awake 호출");
}
```

주로 사용하는 경우:

- 필수 컴포넌트 캐싱
- 기본 상태값 세팅
- EventBus 구독 등록

#### `Start()`

실제 게임 시작 흐름에 맞춘 초기 로직을 넣기 좋습니다. 특히 `IEnumerator Start()` 형태를 사용하면 시작 직후 자연스럽게 coroutine을 진행할 수 있습니다.

```csharp
private void Start()
{
    Debug.Log("Start 호출");
}
```

또는:

```csharp
private IEnumerator Start()
{
    Debug.Log("1초 대기 후 시작");
    yield return new WaitForSeconds(1f);
    Debug.Log("게임 시작 연출 종료");
}
```

#### `FixedUpdate()`

고정된 물리/로직 간격에 맞춘 업데이트가 필요할 때 사용합니다. 물리 계산과 보폭을 맞추고 싶은 이동, 추적, 충돌 기반 보정 로직에 적합합니다.

#### `Update()`

일반적인 게임플레이 로직을 배치하는 가장 기본적인 콜백입니다. 상태 갱신, 입력 해석, AI 상태 전환, 타이머 감소 등 대부분의 스크립트 로직이 여기에 들어갑니다.

#### `LateUpdate()`

다른 업데이트가 끝난 뒤 정리하거나 후처리할 일이 있을 때 사용합니다. 예를 들어 다른 스크립트가 위치를 갱신한 뒤 카메라를 따라가게 만드는 식의 순서 제어에 유용합니다.

#### `OnDrawGizmos()` / `OnDrawGizmosSelected()`

디버그용 시각화가 필요할 때 사용합니다. 탐지 범위, 이동 경로, 충돌 반경처럼 숫자로만 보기 어려운 정보를 그릴 때 유용합니다.

### 권장 사용 패턴

- 참조 캐싱, 구독 등록: `Awake()`
- 시작 직후 한 번 실행하는 연출/초기 시퀀스: `Start()` 또는 `IEnumerator Start()`
- 물리 보폭 기준 로직: `FixedUpdate()`
- 일반 게임 로직: `Update()`
- 후처리/순서 의존 보정: `LateUpdate()`

---

## 3. Physics Callback

현재 지원되는 physics callback은 다음과 같습니다.

- `OnTouched(Physical other)`
- `OnTouching(Physical other)`
- `OnTouchEnd(Entity other)`
- `OnDetected(Entity other)`
- `OnDetecting(Entity other)`
- `OnDetectEnd(Entity other)`

이 메서드들은 다음 반환형을 가질 수 있습니다.

- `void`
- `IEnumerator`

physics callback이 `IEnumerator`를 반환하면 자동으로 coroutine으로 시작됩니다.

### 콜백 의미 정리

#### `OnTouched(Physical other)`

접촉이 처음 발생했을 때 한 번 호출되는 진입 이벤트로 이해하면 됩니다. 피해 판정 시작, 충돌 사운드 재생, 최초 트리거 반응 등에 적합합니다.

#### `OnTouching(Physical other)`

접촉이 유지되는 동안 반복 호출됩니다. 지속 피해, 밀어내기, 접촉 중 상태 유지 같은 로직에 적합합니다.

#### `OnTouchEnd(Entity other)`

접촉이 끝났을 때 호출됩니다. 접촉 상태 해제, 이펙트 종료, 대상 참조 제거에 사용합니다.

#### `OnDetected(Entity other)` / `OnDetecting(Entity other)` / `OnDetectEnd(Entity other)`

이 계열은 실제 물리 충돌보다는 감지/탐지 영역 반응에 가깝게 사용할 수 있습니다. 예를 들어 적 AI의 시야, 센서, 트리거 범위 같은 기능에 적합합니다.

### 기본 예시

```csharp
private void OnTouched(Physical other)
{
    Debug.Log($"충돌 시작: {other.Entity?.Name}");
}

private void OnTouching(Physical other)
{
    Debug.Log($"충돌 유지: {other.Entity?.Name}");
}

private void OnTouchEnd(Entity other)
{
    Debug.Log($"충돌 종료: {other.Name}");
}
```

### Coroutine 형태 예시

물리 콜백에서 바로 coroutine을 시작하고 싶다면 `IEnumerator`를 반환하면 됩니다.

```csharp
private IEnumerator OnDetected(Entity other)
{
    Debug.Log($"{other.Name} 감지");
    yield return new WaitForSeconds(0.5f);
    Debug.Log("감지 반응 지연 처리 완료");
}
```

이 방식은 다음과 같은 상황에 유용합니다.

- 충돌 직후 짧은 무적 시간 부여
- 감지 후 일정 시간 뒤 추적 시작
- 접촉 시 이펙트/사운드를 약간 지연해서 재생

### 사용 시 주의점

- `OnTouched`와 `OnTouching`은 의미가 다르므로 구분해서 사용해야 합니다.
- 지속적으로 호출되는 콜백 안에서 무거운 전역 검색을 반복하면 비용이 커질 수 있습니다.
- 감지 계열과 실제 접촉 계열은 의도한 물리 구조에 맞게 구분해 사용하는 것이 좋습니다.

---

## 4. Coroutine

`Script`는 coroutine 관리를 포함합니다.

사용 가능한 API는 다음과 같습니다.

- `Coroutine StartCoroutine(IEnumerator routine)`
- `void StopCoroutine(Coroutine coroutine)`
- `void StopAllCoroutines()`

지원되는 대기 명령은 다음과 같습니다.

- `WaitForSeconds`
- `WaitForTicks`
- `WaitForPhysicalTicks`
- `WaitUntil`
- `WaitWhile`

Coroutine은 render frame이 아니라 logic tick 기준으로 전진합니다.

이는 매우 중요합니다. 즉, Verity의 coroutine은 화면이 몇 번 그려졌는지보다 **엔진 로직이 몇 번 진행되었는지**를 기준으로 흐릅니다. 따라서 게임플레이 로직의 예측 가능성이 높고, 연출보다 시스템 제어에 특히 적합합니다.

### 기본 사용 예시

```csharp
private Coroutine? _blinkRoutine;

private void Start()
{
    _blinkRoutine = StartCoroutine(Blink());
}

private IEnumerator Blink()
{
    while (true)
    {
        Debug.Log("반짝임 ON");
        yield return new WaitForSeconds(0.2f);

        Debug.Log("반짝임 OFF");
        yield return new WaitForSeconds(0.2f);
    }
}
```

### 중지 예시

```csharp
private void StopBlink()
{
    if (_blinkRoutine is not null)
    {
        StopCoroutine(_blinkRoutine);
        _blinkRoutine = null;
    }
}
```

모든 coroutine을 한 번에 정리해야 한다면:

```csharp
private void ResetState()
{
    StopAllCoroutines();
}
```

### 대기 명령 설명

#### `WaitForSeconds`

초 단위로 대기합니다. 일정 시간 뒤 로직을 이어가고 싶을 때 가장 직관적입니다.

```csharp
yield return new WaitForSeconds(1.5f);
```

#### `WaitForTicks`

로직 tick 수만큼 대기합니다. 시간이 아닌 정확한 tick 수를 기준으로 제어하고 싶을 때 적합합니다.

```csharp
yield return new WaitForTicks(10);
```

#### `WaitForPhysicalTicks`

물리 tick 기준으로 대기합니다. 물리 시뮬레이션과 보조를 맞춘 지연에 적합합니다.

```csharp
yield return new WaitForPhysicalTicks(5);
```

#### `WaitUntil`

특정 조건이 참이 될 때까지 대기합니다.

```csharp
yield return new WaitUntil(() => _isReady);
```

#### `WaitWhile`

특정 조건이 참인 동안 계속 대기합니다.

```csharp
yield return new WaitWhile(() => _isCasting);
```

### 언제 coroutine을 쓰면 좋은가

- 일정 시간 간격으로 반복되는 패턴
- 여러 단계를 가진 짧은 연출 시퀀스
- 상태 전환 사이의 지연 처리
- 이벤트 이후 조건이 만족될 때까지 대기

### 언제 일반 `Update()`가 더 나은가

- 매 tick 지속적으로 상태를 계산해야 할 때
- 로직 흐름이 단순하고, 별도 시퀀스 표현이 필요 없을 때
- 반복적으로 아주 많은 객체에서 동작하는 초경량 계산일 때

---

## 5. 정적 Shortcut API

`Script`는 자주 쓰는 게임플레이 작업을 위한 정적 helper를 제공합니다.

### 엔티티 검색과 생성/파괴

- `Entity? Find(string name)`
- `Entity? FindWithTag(string tag)`
- `Entity[] FindEntitiesWithTag(string tag)`
- `T? FindObjectOfType<T>(bool includeInactive = false) where T : Component`
- `T[] FindObjectsOfType<T>(bool includeInactive = false) where T : Component`
- `void Destroy(Entity entity)`
- `void Destroy(Component component)`
- `Entity Instantiate(string name = "New Entity")`
- `Entity? Instantiate(Entity original)`
- `T? Instantiate<T>(T original) where T : Component`

이 API들은 편의성은 높지만, 전역 검색 계열은 hot path에서 남용하지 않는 편이 맞습니다.

### 검색 API 설명

#### `Find(string name)`

이름으로 엔티티를 하나 찾습니다.

```csharp
var player = Find("Player");
```

#### `FindWithTag(string tag)`

특정 태그를 가진 엔티티 하나를 찾습니다.

```csharp
var boss = FindWithTag("Boss");
```

#### `FindEntitiesWithTag(string tag)`

특정 태그를 가진 엔티티들을 모두 가져옵니다.

```csharp
var enemies = FindEntitiesWithTag("Enemy");
```

#### `FindObjectOfType<T>()` / `FindObjectsOfType<T>()`

특정 컴포넌트 타입을 기준으로 검색합니다.

```csharp
var cameraFollow = FindObjectOfType<CameraFollow>();
var allSpawners = FindObjectsOfType<EnemySpawner>();
```

### 생성과 파괴 예시

```csharp
var bullet = Instantiate("Bullet");
Destroy(bullet);
```

프리팹성 원본 엔티티나 컴포넌트 복제 패턴이 필요하다면:

```csharp
var clone = Instantiate(originalEntity);
var clonedComponent = Instantiate(originalComponent);
```

### 사용 권장 사항

- `Awake()`나 `Start()`에서 한 번 찾아 캐싱하는 방식이 가장 일반적입니다.
- `Update()`나 `OnTouching()`에서 매번 전역 검색하는 패턴은 가능하면 피하는 편이 좋습니다.
- 빠른 프로토타이핑 단계에서는 shortcut API가 매우 유용하지만, 반복 비용이 큰 경로에서는 명시적인 참조 관리가 더 좋습니다.

---

## 6. 스크립트에서의 UI 접근

스크립트의 UI 접근은 현재 UI 아키텍처와 직접 연결되어 있습니다.

핵심 원칙은 다음과 같습니다.

- 게임플레이 스크립트는 가능하면 화면 단위로 접근합니다.
- UI 노드 직접 접근은 존재하지만 기본 경로가 되어서는 안 됩니다.

### 노드와 Canvas 검색

- `Canvas? FindCanvas(string screenNameOrId)`
- `UiNode? FindUi(string nameOrId)`
- `T? FindUi<T>(string nameOrId) where T : UiNode`

### 역할 기반 화면 접근

- `Canvas? OpenUiRole(string role)`
- `Canvas? FindUiRole(string role)`
- `void CloseUiRole(string role)`
- `void SetUiRole(string role, string variable, object? value)`
- `void SendUiRole(string role, string command, object? payload = null)`

### 직접 화면 핸들 접근

- `void SetUi(string screenNameOrId, string variable, object? value)`
- `void SendUi(string screenNameOrId, string command, object? payload = null)`

현재 권장 방식은 구체적인 화면 이름보다 역할 기반 접근입니다.

예:

- `OpenUiRole("Hud")`
- `SetUiRole("Hud", "Health", hp)`
- `SendUiRole("Inventory", "OpenTab", "Equipment")`

즉 스크립트는 UI 내부 노드를 직접 만지기보다, 화면 변수와 command를 통해 상호작용하는 편이 맞습니다.

### 왜 역할 기반 접근이 좋은가

- 화면 이름이 바뀌어도 역할 계약만 유지되면 스크립트가 덜 깨집니다.
- 게임 로직이 UI 구조 세부사항에 과도하게 결합되지 않습니다.
- HUD, Inventory, QuestLog 같은 개념 단위로 책임을 나누기 쉽습니다.

### 예시: 체력 표시 갱신

```csharp
private int _hp = 100;

private void Awake()
{
    OpenUiRole("Hud");
    SetUiRole("Hud", "Health", _hp);
}

private void ApplyDamage(int amount)
{
    _hp -= amount;
    SetUiRole("Hud", "Health", _hp);
}
```

### 예시: 인벤토리 탭 열기

```csharp
private void OpenEquipmentTab()
{
    OpenUiRole("Inventory");
    SendUiRole("Inventory", "OpenTab", "Equipment");
}
```

---

## 7. `EventBus`

`EventBus`는 타입 기반 publish/subscribe 패턴을 제공하는 전역 이벤트 허브입니다. 스크립트, 코어 유틸리티, 런타임 시스템이 직접 서로를 참조하지 않고도 신호를 주고받을 수 있게 해 줍니다.

### 존재 이유

- 단순한 전역 이벤트 전달 경로가 필요하지만, 복잡한 메시지 브로커나 scene object 연결까지는 원하지 않을 때 적합합니다.
- 이벤트 타입 자체를 `record struct` 등으로 정의하면 payload와 의미를 함께 표현할 수 있습니다.
- `ParticleSystem`, `SceneTransition`처럼 Core/Graphics 성격의 시스템도 스크립팅 계층에서 쉽게 구독할 수 있습니다.

### public API

| 시그니처 | 설명 |
| :--- | :--- |
| `void Subscribe<T>(Action<T> handler)` | 타입 `T` 이벤트를 구독합니다. |
| `void Unsubscribe<T>(Action<T> handler)` | 등록한 핸들러를 제거합니다. |
| `void Publish<T>(T eventData)` | 해당 타입 구독자들에게 이벤트를 즉시 전달합니다. |
| `void Clear()` | 모든 타입의 구독을 제거합니다. 테스트 초기화나 런타임 리셋에 사용합니다. |

### 사용 예시

```csharp
public readonly record struct DamageTakenEvent(int Amount);

private void Awake()
{
    EventBus.Subscribe<DamageTakenEvent>(OnDamageTaken);
}

private void OnDestroy()
{
    EventBus.Unsubscribe<DamageTakenEvent>(OnDamageTaken);
}

private void OnDamageTaken(DamageTakenEvent e)
{
    Debug.Log($"Damage: {e.Amount}");
}

private void ApplyDamage(int amount)
{
    EventBus.Publish(new DamageTakenEvent(amount));
}
```

### 사용 규칙

- 구독은 보통 `Awake()` 또는 `Start()`에서 등록하고, 해제는 `OnDestroy()`에서 수행하는 편이 안전합니다.
- `Publish<T>`는 현재 구현상 즉시 동기 호출입니다. 큐잉되거나 다음 프레임으로 미뤄지지 않습니다.
- 내부 저장소는 `Dictionary<Type, List<Delegate>>`이므로 멀티스레드 안전성을 전제로 하지 않습니다. 현재 엔진의 단일 스레드 루프 전제와 맞춰 사용해야 합니다.

### 다른 시스템과의 통합

- `ParticleSystem`은 `ParticleEmittedEvent`, `ParticleExpiredEvent`를 발행합니다.
- `SceneTransition`은 전환 완료 시 `SceneTransitionCompletedEvent`를 발행합니다.
- 사용자 스크립트는 이벤트 타입만 공유하면 Core/Graphics 구현 세부사항을 몰라도 시스템 변화에 반응할 수 있습니다.

### 설계 팁

- 이벤트 이름은 가능한 한 의미가 명확해야 합니다.
- 이벤트 payload는 최소한으로 유지하되, 구독자가 별도 전역 검색 없이 처리 가능한 정보는 담는 편이 좋습니다.
- 수명 주기가 짧은 객체는 구독 해제를 빼먹지 않도록 주의해야 합니다.

예를 들어 플레이어 체력이 바뀌었을 때 HUD와 사운드 시스템이 동시에 반응하도록 만들고 싶다면, 서로 직접 참조시키기보다 이벤트를 하나 발행하는 방식이 더 단순하고 느슨한 결합을 유지하기 쉽습니다.

```csharp
public readonly record struct PlayerHealthChangedEvent(int CurrentHp);

private void ChangeHp(int hp)
{
    EventBus.Publish(new PlayerHealthChangedEvent(hp));
}
```

---

## 8. 간단한 C# 스크립팅 튜토리얼

이 섹션에서는 **기본적으로 계속 움직이는 엔티티 컴포넌트**를 하나 만드는 과정을 간단히 설명합니다. 목표는 아주 작은 예제로 `Script`, lifecycle, 값 노출, tick 기반 업데이트 감각을 빠르게 익히는 것입니다.

### 목표

- 엔티티에 붙일 C# 스크립트를 만든다.
- 매 tick마다 오른쪽으로 이동하는 로직을 작성한다.
- 속도를 필드로 분리해 나중에 쉽게 수정할 수 있게 한다.

### 예제 스크립트

```csharp
using System.Numerics;
using Verity.Engine;

public sealed class SimpleMover : Script
{
    private Vector2 _direction = Vector2.UnitX;
    private float _speed = 2.0f;

    private void Awake()
    {
        Debug.Log("SimpleMover 준비 완료");
    }

    private void Update()
    {
        Entity.Transform.Position += _direction * _speed;
    }
}
```

### 코드 해설

#### `public sealed class SimpleMover : Script`

이 클래스는 `Script`를 상속하므로 엔티티에 부착할 수 있는 C# 스크립트 컴포넌트가 됩니다.

#### `_direction`

이동 방향입니다. `Vector2.UnitX`는 오른쪽 방향을 의미합니다.

#### `_speed`

한 tick마다 얼마나 이동할지를 나타내는 속도 값입니다. 아주 단순한 예제이므로 보간이나 시간 계수 없이 직접 사용합니다.

#### `Awake()`

스크립트가 준비될 때 로그를 남깁니다. 실제 프로젝트에서는 여기서 필요한 컴포넌트를 캐싱하거나 초기 상태를 설정할 수 있습니다.

#### `Update()`

매 logic tick마다 위치를 이동시킵니다. 이 예제의 핵심 로직입니다.

### 조금 더 실용적인 버전

일정 tick 뒤 멈추게 만들고 싶다면 coroutine을 조합할 수 있습니다.

```csharp
using System.Collections;
using System.Numerics;
using Verity.Engine;

public sealed class TimedMover : Script
{
    private Vector2 _direction = Vector2.UnitX;
    private float _speed = 2.0f;
    private bool _canMove = true;

    private IEnumerator Start()
    {
        yield return new WaitForSeconds(3f);
        _canMove = false;
        Debug.Log("이동 종료");
    }

    private void Update()
    {
        if (!_canMove)
            return;

        Entity.Transform.Position += _direction * _speed;
    }
}
```

이 예제에서는:

- 시작 후 3초 동안 이동하고
- 그 뒤 `_canMove`를 `false`로 바꾸어
- `Update()`가 더 이상 위치를 갱신하지 않게 합니다.

### 태그나 검색 API와 연결하기

움직이는 대상이 플레이어를 향하도록 바꾸고 싶다면, `Find("Player")` 같은 API를 활용해 대상을 찾고 방향을 계산하는 방식으로 확장할 수 있습니다.

예를 들어 개념적으로는 다음 순서로 확장할 수 있습니다.

1. `Awake()` 또는 `Start()`에서 플레이어 엔티티를 찾는다.
2. `Update()`에서 현재 위치와 목표 위치 차이를 구한다.
3. 방향을 정규화해 이동량에 곱한다.

즉, 지금의 단순 이동 예제는 이후 다음과 같은 기능으로 자연스럽게 확장할 수 있습니다.

- 플레이어 추적
- 지정 구간 왕복 이동
- 충돌 시 방향 전환
- 감지 범위 진입 시에만 이동

### 초보자용 정리

- C# 스크립트는 `Script`를 상속해서 만든다.
- 초기화는 `Awake()`에서, 지속 동작은 `Update()`에서 작성한다.
- 시간 지연이나 순차 동작은 coroutine으로 표현한다.
- 다른 엔티티, UI, 이벤트 시스템과의 연결도 같은 스크립트 안에서 확장할 수 있다.

이 정도만 익혀도 Verity에서 기본적인 게임플레이 컴포넌트를 직접 만들기 시작할 수 있습니다.
