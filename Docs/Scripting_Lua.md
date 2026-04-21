# Verity Lua API 문서

이 문서는 Verity 엔진의 **Lua 스크립팅 전용 사용자 매뉴얼**입니다.  
기존 C# 스크립팅 문서와는 별개이며, `LuaScriptComponent`를 기준으로 Lua 스크립트를 작성하고 에디터에 연결하는 방법을 설명합니다.

---

## 1. 개요

Verity의 Lua 스크립팅은 엔티티에 `LuaScriptComponent`를 붙이고, 여기에 `.lua` 파일을 연결하는 방식으로 동작합니다.

Lua 스크립트는 다음 기능을 지원합니다.

- 엔진 lifecycle 함수 연결 (`Awake`, `Start`, `Update`, `FixedUpdate`, `LateUpdate`)
- 엔티티/트랜스폼 접근 (`self`, `Owner`, `Transform`)
- 엔진 API 접근 (`Vector2`, `Vector3`, `Time`, `Input`, `Keys`, `Entity`)
- Lua coroutine 실행
- Lua에서 사용자 C# 컴포넌트 호출 (`GetComponent`)
- `.lua` 파일 수정 시 핫 리로드
- `Export` 테이블을 통한 Inspector 노출

---

## 2. 에디터에서 Lua 스크립트 만들기와 연결하기

### 2.1 Lua 파일 생성

Project 창에서 원하는 폴더를 연 뒤, 생성 메뉴에서 **Lua Script**를 선택하면 기본 템플릿이 생성됩니다.

기본 템플릿은 다음과 비슷합니다.

```lua
function Awake()
end

function Start()
end

function Update(deltaTime)
end
```

### 2.2 엔티티에 할당

1. 에디터에서 엔티티를 선택합니다.
2. `LuaScriptComponent`를 추가합니다.
3. Inspector의 `ScriptPath` 필드에 `.lua` 파일을 지정합니다.
4. 플레이 모드에서 해당 Lua 스크립트가 엔진 lifecycle에 맞춰 실행됩니다.

`ScriptPath`는 `.lua` 자산 참조 필드로 연결되며, Inspector에서 직접 선택할 수 있습니다.

---

## 3. Lua 실행 컨텍스트

Lua 스크립트가 로드되면 컴포넌트별 Lua 상태가 생성되고, 다음 전역 값이 주입됩니다.

- `self`: 현재 `LuaScriptComponent`의 스크립트 컨텍스트
- `Owner`: 현재 컴포넌트가 붙어 있는 엔티티
- `Vector2(x, y)`
- `Vector3(x, y, z)`
- `Time`
- `Input`
- `Keys`
- `Entity`
- `Wait`, `WaitForSeconds`, `WaitForTicks`, `WaitForPhysicalTicks`, `WaitUntil`, `WaitWhile`
- `print(...)`

### 3.1 `self`

`self`는 현재 Lua 스크립트의 런타임 컨텍스트입니다.

주요 기능:

- `self.Owner`
- `self:StartCoroutine(fn)`
- `self:StartCoroutineByName("함수명")`
- `self:StopAllCoroutines()`

### 3.2 `Owner`

`Owner`는 현재 엔티티입니다. `self.Owner`와 같은 대상을 가리킵니다.

주요 멤버:

- `Owner.Name`
- `Owner.Tag`
- `Owner.Active`
- `Owner.Transform`
- `Owner:HasComponent("타입명")`
- `Owner:GetComponent("타입명")`
- `Owner:GetField("컴포넌트명", "멤버명")`
- `Owner:SetField("컴포넌트명", "멤버명", 값)`
- `Owner:Invoke("컴포넌트명", "메서드명")`

---

## 4. Lifecycle 매핑

Lua 스크립트는 함수 이름을 기준으로 lifecycle에 연결됩니다.

| Lua 함수 | 호출 시점 |
| --- | --- |
| `Awake()` | 컴포넌트가 활성화되고 스크립트가 준비된 뒤 최초 1회 |
| `Start()` | 첫 실행 시점에 1회 |
| `Update(deltaTime)` | 매 로직 틱마다 호출 |
| `FixedUpdate(deltaTime)` | 물리/고정 틱 시점에 호출 |
| `LateUpdate(deltaTime)` | 일반 Update 이후 호출 |

핵심 규칙:

- 함수를 정의하지 않으면 그냥 생략됩니다.
- `Update`, `FixedUpdate`, `LateUpdate`는 `deltaTime`을 인자로 받습니다.
- `Awake`와 `Start`는 인자 없이 사용합니다.
- `Start()`는 일반 함수로 실행할 수도 있고, Lua coroutine으로 시작할 수도 있습니다.

예시:

```lua
function Awake()
    print("Awake 호출")
end

function Start()
    print("Start 호출")
end

function Update(deltaTime)
    print("매 틱 호출", deltaTime)
end
```

---

## 5. 엔진 API 접근

### 5.1 Transform 접근

가장 자주 쓰는 접근 경로는 `Owner.Transform`입니다.

지원 멤버:

- `Owner.Transform.Position`
- `Owner.Transform.Rotation`
- `Owner.Transform.Scale`

예시:

```lua
function Update(deltaTime)
    local pos = Owner.Transform.Position
    pos.X = pos.X + 100 * deltaTime
    Owner.Transform.Position = pos
end
```

> 참고: 현재 Lua 바인딩에서 `Transform.Position`과 `Transform.Scale`은 `Vector2` 형태로 노출됩니다.  
> `Vector3`는 별도 수학 값이나 다른 C# API와의 상호운용에 사용할 수 있습니다.

### 5.2 Vector2 / Vector3 생성

```lua
local move = Vector2(1, 0)
local dir3 = Vector3(0, 0, 1)
```

`Vector2`, `Vector3`는 각각 `X`, `Y`, `Z` 필드를 가집니다.

```lua
local v = Vector3(1, 2, 3)
print(v.X, v.Y, v.Z)
```

### 5.3 Time API

- `Time.DeltaTime`
- `Time.TotalTime`
- `Time.LogicTickCount`
- `Time.PhysicsTickCount`

예시:

```lua
function Update(deltaTime)
    if Time.TotalTime > 3.0 then
        print("3초 경과")
    end
end
```

### 5.4 Input / Keys API

입력은 `Input`과 `Keys`를 통해 접근합니다.

- `Input.IsKeyDown(key)`
- `Input.IsKeyPressed(key)`
- `Input.IsKeyReleased(key)`

예시:

```lua
function Update(deltaTime)
    if Input.IsKeyDown(Keys.Space) then
        print("스페이스 입력 중")
    end
end
```

### 5.5 Entity 검색 API

- `Entity.Find("이름")`
- `Entity.FindWithTag("태그")`

예시:

```lua
function Start()
    local player = Entity.Find("Player")
    if player ~= nil then
        print("플레이어 찾음:", player.Name)
    end
end
```

---

## 6. 사용자 C# 컴포넌트 호출

Lua는 엔진 기본 컴포넌트뿐 아니라, 사용자 어셈블리의 C# 컴포넌트도 문자열 타입명으로 가져올 수 있습니다.

### 6.1 `GetComponent` 사용

```lua
local stats = Owner:GetComponent("PlayerStats")
if stats ~= nil then
    print(stats.Hp)
    stats.Hp = stats.Hp - 10
end
```

동작 방식:

- `Owner:GetComponent("PlayerStats")`는 해당 이름의 C# 컴포넌트를 찾습니다.
- 반환값은 Lua에서 접근 가능한 프록시 객체입니다.
- public 프로퍼티/필드 읽기 및 쓰기가 가능합니다.
- public 인스턴스 메서드도 호출할 수 있습니다.

예시:

```lua
local combat = Owner:GetComponent("CombatController")
if combat ~= nil then
    combat:Fire()
end
```

### 6.2 보조 접근 함수

필요하면 엔티티 핸들에서 직접 멤버를 읽거나 호출할 수도 있습니다.

```lua
local hp = Owner:GetField("PlayerStats", "Hp")
Owner:SetField("PlayerStats", "Hp", hp - 5)
Owner:Invoke("CombatController", "Reload")
```

### 6.3 타입명 규칙

문자열 타입명은 보통 아래 둘 중 하나를 사용하면 됩니다.

- 짧은 타입명: `"PlayerStats"`
- 전체 타입명: `"Game.Scripts.PlayerStats"`

핫 리로드나 사용자 어셈블리 갱신 이후에도 Lua 쪽 컴포넌트 타입 등록은 다시 구성됩니다.

---

## 7. Lua Coroutine 실행

Verity의 Lua coroutine은 엔진의 C# coroutine 스케줄러와 연결됩니다.

사용 가능한 대기 객체:

- `Wait(seconds)`
- `WaitForSeconds(seconds)`
- `WaitForTicks(ticks)`
- `WaitForPhysicalTicks(ticks)`
- `WaitUntil(function() ... end)`
- `WaitWhile(function() ... end)`

### 7.1 `Start()`를 coroutine처럼 쓰기

`Start()` 안에서 `coroutine.yield(...)`를 사용하면 엔진이 이를 Lua coroutine으로 시작합니다.

```lua
function Start()
    print("시작")
    coroutine.yield(WaitForSeconds(1.0))
    print("1초 후 실행")
end
```

### 7.2 별도 함수 coroutine 시작

```lua
function BlinkRoutine()
    while true do
        print("blink")
        coroutine.yield(WaitForTicks(30))
    end
end

function Awake()
    self:StartCoroutineByName("BlinkRoutine")
end
```

또는 함수 자체를 넘길 수도 있습니다.

```lua
function MyRoutine()
    coroutine.yield(Wait(0.5))
    print("재개됨")
end

function Start()
    self:StartCoroutine(MyRoutine)
end
```

### 7.3 조건 대기

```lua
function Start()
    coroutine.yield(WaitUntil(function()
        return Input.IsKeyPressed(Keys.Space)
    end))

    print("스페이스가 눌린 뒤 실행")
end
```

---

## 8. 핫 리로드

Lua 스크립트 파일은 파일 변경 감시로 핫 리로드됩니다.

동작 방식:

1. 엔진이 `Assets` 아래의 `.lua` 파일 변경을 감시합니다.
2. 연결된 `LuaScriptComponent`가 해당 파일 경로와 일치하면 스크립트를 다시 로드합니다.
3. 성공하면 로그에 `[Lua] Reloaded script: ...`가 출력됩니다.

리로드 시 주의점:

- Lua 상태는 다시 생성됩니다.
- lifecycle 상태(`Awake`, `Start` 실행 여부)도 다시 초기화됩니다.
- 실행 중 coroutine은 중지되고 다시 시작해야 합니다.
- 파일 저장 직후 즉시 반영되므로, 문법 오류가 있으면 로그에 로드 실패가 표시됩니다.

즉, 핫 리로드는 **코드 변경 반영**에는 강하지만, 기존 Lua 런타임 상태를 자동 보존하는 방식은 아닙니다.

---

## 9. `Export` 테이블과 Inspector 노출

Lua 스크립트에서 `Export` 테이블을 정의하면 Inspector에서 값을 노출하고 수정할 수 있습니다.

현재 Inspector에서 지원되는 기본 타입:

- number
- string
- boolean

예시:

```lua
Export = {
    Speed = 120,
    DisplayName = "Player",
    UseInput = true
}
```

이렇게 작성하면 Inspector에 각 값이 표시되며, 수정한 값은 Lua의 `Export` 테이블에 바로 반영됩니다.

스크립트에서는 다음처럼 사용합니다.

```lua
function Update(deltaTime)
    if Export.UseInput and Input.IsKeyDown(Keys.RightArrow) then
        local pos = Owner.Transform.Position
        pos.X = pos.X + Export.Speed * deltaTime
        Owner.Transform.Position = pos
    end
end
```

권장 사항:

- Inspector에서 조정할 값만 `Export`에 둡니다.
- 복잡한 중첩 테이블보다는 평평한 구조를 권장합니다.
- 런타임 전용 임시 상태는 `Export`가 아닌 일반 Lua 지역/전역 변수로 관리합니다.

---

## 10. 간단한 Lua 스크립팅 튜토리얼

이 절에서는 엔티티를 움직이는 아주 기본적인 Lua 스크립트를 만드는 과정을 설명합니다.

### 10.1 새 Lua 스크립트 만들기

Project 창에서 **Create -> Lua Script**를 선택해 새 파일을 만듭니다. 예를 들어 `PlayerMover.lua`라고 하겠습니다.

### 10.2 스크립트 작성

다음 내용을 입력합니다.

```lua
Export = {
    Speed = 180,
    AutoMove = true,
    Message = "Lua Player Ready"
}

function Awake()
    print(Export.Message)
end

function Start()
    print("PlayerMover Start")
end

function Update(deltaTime)
    local pos = Owner.Transform.Position

    if Export.AutoMove then
        pos.X = pos.X + Export.Speed * deltaTime
    end

    if Input.IsKeyDown(Keys.LeftArrow) then
        pos.X = pos.X - Export.Speed * deltaTime
    end

    if Input.IsKeyDown(Keys.RightArrow) then
        pos.X = pos.X + Export.Speed * deltaTime
    end

    Owner.Transform.Position = pos
end
```

### 10.3 에디터에 연결

1. 엔티티를 선택합니다.
2. `LuaScriptComponent`를 추가합니다.
3. `ScriptPath`에 `PlayerMover.lua`를 지정합니다.
4. 플레이 모드를 실행합니다.

### 10.4 Inspector에서 변수 조절

`Export`에 정의한 값은 Inspector에 표시됩니다.

- `Speed`: 이동 속도
- `AutoMove`: 자동 이동 여부
- `Message`: 시작 로그 메시지

즉, Lua 파일을 직접 수정하지 않아도 기본 설정값을 빠르게 바꿀 수 있습니다.

### 10.5 C# 컴포넌트와 함께 사용하기

엔티티에 `PlayerStats` 같은 C# 컴포넌트가 붙어 있다면 다음처럼 함께 사용할 수 있습니다.

```lua
function Start()
    local stats = Owner:GetComponent("PlayerStats")
    if stats ~= nil then
        print("현재 HP:", stats.Hp)
    end
end
```

---

## 11. 실전 팁

- 이동처럼 매 프레임 반복되는 작업은 `Update(deltaTime)`에 둡니다.
- 초기화 작업은 `Awake()` 또는 `Start()`에 둡니다.
- 시간 대기는 `WaitForSeconds`보다 `Wait`를 짧게 쓰는 것도 가능합니다.
- 엔티티 컴포넌트 접근은 반복 호출보다 한 번 가져와 재사용하는 편이 좋습니다.
- 핫 리로드를 자주 사용할 경우, 상태가 재초기화된다는 점을 전제로 코드를 작성하는 편이 안전합니다.

---

## 12. 요약

Verity의 Lua 스크립팅은 `LuaScriptComponent` 하나로 다음 흐름을 제공합니다.

1. `.lua` 파일 생성
2. 엔티티에 `LuaScriptComponent` 연결
3. `Awake`, `Start`, `Update` 등 lifecycle 함수 작성
4. `Owner`, `Transform`, `Time`, `Input`, `Entity`로 엔진 제어
5. `GetComponent`로 사용자 C# 스크립트와 상호작용
6. `Export`로 Inspector 변수 노출
7. 저장 시 핫 리로드로 즉시 반영

즉, Lua는 Verity에서 **입문자 친화적이면서도 엔진 API와 C# 컴포넌트까지 직접 연결되는 병렬 스크립팅 경로**로 사용할 수 있습니다.
