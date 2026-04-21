# Verity 입문자 퀵스타트

이 문서는 **처음 실행 → 첫 씬 → 첫 스크립트 → 물리 → UI → 빌드**까지 한 번에 따라가는 빠른 시작 가이드입니다.

더 자세한 설명은 다음 문서를 참고하세요.

- [에디터 문서](./Editor.md)
- [코어 문서](./Core.md)
- [C# 스크립팅 문서](./Scripting_CSharp.md)
- [Lua 스크립팅 문서](./Scripting_Lua.md)
- [물리 문서](./Physics.md)
- [UI 문서](./UI.md)

---

## 1. 엔진 설치 및 첫 실행

가장 빠른 방법은 릴리스 패키지를 사용하는 것입니다.

1. [GitHub Releases](https://github.com/MonoBhvr/Verity/releases)에서 최신 압축 파일을 받습니다.
2. 원하는 폴더에 압축을 풉니다.
3. `Editor/VerityEditor.exe`를 실행합니다.
4. 런처에서 새 프로젝트를 만들고 엽니다.

소스코드에서 직접 빌드하려면:

```powershell
git clone https://github.com/MonoBhvr/Verity.git
cd Verity
dotnet build Verity.sln
dotnet run --project Editor/Verity.Editor.App/Verity.Editor.App.csproj
```

프로젝트를 열면 기본적으로 다음 창을 자주 쓰게 됩니다.

- **Hierarchy**: 엔티티 추가/정리
- **Inspector**: 컴포넌트 값 수정
- **Project**: `Assets` 파일 관리
- **World View / Screen**: 장면 편집과 결과 확인

---

## 2. 첫 씬 만들기

이번 예제에서는 **플레이어 1개 + 바닥 1개**만 만듭니다.

### 2.1 플레이어 만들기

1. **Hierarchy**에서 `Sprite` 프리셋 엔티티를 만듭니다.
2. 이름을 `Player`로 바꿉니다.
3. **Inspector**에서 `SpriteRenderer.Sprite`에 스프라이트를 지정합니다.
4. `Transform.Position`을 `(0, 0)` 근처로 둡니다.
5. `SpriteRenderer.Size`를 필요하면 조정합니다.

### 2.2 바닥 만들기

1. 빈 엔티티를 하나 만들고 이름을 `Ground`로 바꿉니다.
2. `SpriteRenderer`를 추가해 바닥용 스프라이트를 지정합니다.
3. `Transform.Position`을 플레이어 아래로 내립니다.
4. `SpriteRenderer.Size`를 넓게 잡아 바닥처럼 보이게 합니다.

이 단계까지 끝나면 화면에는 최소한 **움직일 대상**과 **부딪힐 대상**이 생깁니다.

---

## 3. 첫 스크립트 작성

`Project` 창의 `Assets` 아래에 `PlayerMover.cs`를 만들고 다음 코드를 넣습니다.

> Verity 에디터는 `Verity.Core`, `Verity.Core.ECS`, `Verity.Graphics`, `Verity.Input`, `Vector2` 등을 전역 using으로 자동 주입합니다.

```csharp
public sealed class PlayerMover : Script
{
    [SerializeField] private float speed = 5f;
    [SerializeField] private float jumpSpeed = 10f;

    private Physical? body;
    private SpriteRenderer? spriteRenderer;

    public void Start()
    {
        body = GetComponent<Physical>();
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    public void Update()
    {
        if (body == null)
            return;

        float moveX = 0f;
        if (Input.GetKey("Left")) moveX -= 1f;
        if (Input.GetKey("Right")) moveX += 1f;

        body.Velocity = new Vector2(moveX * speed, body.Velocity.Y);

        if (spriteRenderer != null && moveX != 0f)
            spriteRenderer.FlipX = moveX < 0f;

        if (Input.GetKeyDown("Jump") && body.IsGrounded("Default"))
            body.Velocity = new Vector2(body.Velocity.X, jumpSpeed);
    }
}
```

이제 `Player` 엔티티에 다음 컴포넌트를 붙입니다.

- `Physical`
- `BoxShape`
- `PlayerMover`

권장 초기값:

- `BoxShape.Size` = 스프라이트 크기와 비슷하게
- `Physical.Mass` = `1`
- `Physical.GroupName` = `Default`

---

## 4. 물리 추가하기

충돌이 되려면 **움직이는 쪽**과 **막아 주는 쪽** 모두 shape가 필요합니다.

### 4.1 바닥 설정

`Ground` 엔티티에 다음을 추가합니다.

- `Physical`
- `BoxShape`

그리고 다음처럼 설정합니다.

- `Physical.IsStatic` = `true`
- `Physical.GroupName` = `Default`
- `BoxShape.Size` = 바닥 너비에 맞게 크게

이제 `Player`가 떨어져서 `Ground`와 충돌합니다.

### 4.2 충돌 콜백 예제

충돌이 들어왔을 때 색을 바꾸는 최소 예제입니다.

```csharp
public sealed class TouchTint : Script
{
    private SpriteRenderer? spriteRenderer;

    public void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    public void OnTouched(Physical other)
    {
        if (spriteRenderer != null)
            spriteRenderer.Color = Color.Yellow;
    }

    public void OnTouchEnd(Entity other)
    {
        if (spriteRenderer != null)
            spriteRenderer.Color = Color.White;
    }
}
```

이 스크립트를 `Player`에 붙이면, 무엇인가와 닿는 동안 노란색으로 바뀝니다.

---

## 5. 첫 UI 만들기

가장 쉬운 시작은 **텍스트 하나를 가진 HUD 화면**입니다.

### 5.1 UI 자산 만들기

1. `Project` 창에서 새 **UI Screen**을 만듭니다.
2. 이름을 `Hud.ui`로 저장합니다.
3. UI Editor에서 루트 아래에 텍스트 노드를 하나 만듭니다.
4. 텍스트 노드의 `Text`를 `=Message`에 바인딩합니다.
5. 필요하면 위치를 화면 좌상단이나 중앙으로 옮깁니다.

### 5.2 역할로 열기

`ProjectSettings.json`의 **UI Role Defaults**에서 `Hud` 역할이 방금 만든 화면을 가리키게 설정합니다.

그다음 `HudBootstrap.cs`를 만들고 아무 엔티티에 붙입니다.

```csharp
public sealed class HudBootstrap : Script
{
    public void Start()
    {
        OpenUiRole("Hud");
        SetUiRole("Hud", "Message", "안녕하세요, Verity!");
    }
}
```

핵심은 다음 두 줄입니다.

- `OpenUiRole("Hud")`: 화면 열기
- `SetUiRole("Hud", "Message", ...)`: 화면 변수 값 넣기

UI는 노드를 직접 조작하기보다 **화면 변수와 command**로 다루는 편이 기본 흐름입니다.

---

## 6. 빌드하고 실행하기

### 6.1 에디터 안에서 먼저 확인

1. 월드를 저장합니다.
2. `Build Settings` 창에서 현재 `.verity` 월드를 빌드 목록에 추가합니다.
3. `StartWorldIndex`를 방금 만든 월드로 맞춥니다.

### 6.2 퍼블리시

`Project` 창에서 **Debug Publish** 또는 **Release Publish**를 실행합니다.

퍼블리시 과정에서는 보통 다음이 함께 처리됩니다.

1. `Assets` 복사
2. `BuildSettings.json` 포함
3. 사용자 스크립트 `UserScripts.dll` 컴파일
4. 실행 파일 생성

### 6.3 실행

출력 폴더에서 생성된 실행 파일을 실행합니다.

처음 빌드가 끝났다면 다음이 보이면 성공입니다.

- 시작 월드가 열린다.
- `Player`가 좌우 이동/점프한다.
- `Ground`와 충돌한다.
- `Hud` UI가 보인다.

---

## 다음 단계

퀵스타트를 끝냈다면 이어서 읽으면 좋습니다.

- ECS 구조 이해: [Core.md](./Core.md)
- C# 스크립트 lifecycle/콜백: [Scripting_CSharp.md](./Scripting_CSharp.md)
- Lua 스크립팅 가이드: [Scripting_Lua.md](./Scripting_Lua.md)
- 에디터 전체 기능: [Editor.md](./Editor.md)
- 물리 세부 구조: [Physics.md](./Physics.md)
- UI 바인딩/UiScript: [UI.md](./UI.md)
