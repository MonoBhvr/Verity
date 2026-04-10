# Verity Engine

## **Even Slow, Use Easier**
Verity는 개발 입문자와 1인 개발자를 위해 설계된, 완전히 C#으로 작성된 쉽고 강력한 2D 게임 엔진입니다.

Verity는 복잡한 네이티브 코드 대신 최신 .NET 환경을 기반으로 하며, 직관적인 에디터와 강력한 ECS 아키텍처를 결합하여 아이디어를 빠르게 프로토타입으로 구현할 수 있게 돕습니다.

---

## 주요 특징

-   **간결한 C# 스크립팅**: 복잡한 설정 없이 C# 클래스를 상속받는 것만으로 게임 로직을 작성할 수 있습니다.
-   **통합 에디터**: 직관적인 계층 구조(Hierarchy), 인스펙터(Inspector), 프로젝트 관리 시스템을 포함합니다.
-   **현대적인 ECS 아키텍처**: 엔티티-컴포넌트-시스템 구조를 통해 유연하고 확장성 있는 개발이 가능합니다.
-   **멀티 인스턴스 런처**: 유니티 허브 스타일의 런처를 통해 여러 프로젝트를 동시에 관리하고 안전하게 열 수 있습니다.
-   **2D 물리 엔진**: SAT 알고리즘 기반의 정교한 충돌 판정과 물리 시뮬레이션을 지원합니다.
-   **필터 시스템**: Tag, Sorting Layer, Physics Group, 여러 enum 타입을 화이트리스트와 블랙리스트로 관리하여 빠른 검색과 최적화된 시스템 업데이트를 제공합니다.

---

## 기술 스택

-   **Language**: C# 12 / .NET 9.0
-   **Graphics**: Irodori (OpenGL, via Silk.Net)
-   **UI Framework**: Dear ImGui (Hexa.NET.ImGui)
-   **Windowing**: SDL2

---

## 시작하기

### 1. 릴리스 설치 (권장)
가장 빠르고 쉬운 설치 방법입니다.
1.  **[GitHub Releases](https://github.com/MonoBhvr/Verity/releases)** 페이지로 이동합니다.
2.  최신 버전의 `Verity_Engine_vX.X.X.zip` 파일을 다운로드합니다.
3.  원하는 폴더에 압축을 해제합니다.
4.  `Editor/VerityEditor.exe`를 실행하여 런처를 시작합니다.

### 2. 사전 준비
Verity 엔진을 직접 빌드하고 실행하려면 다음이 설치되어 있어야 합니다.
- **[.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)** (필수)
- **Windows OS** (현재 에디터는 Windows 환경에 최적화되어 있습니다.)

### 3. 다운로드 및 빌드
전체 소스코드를 다운로드하고 빌드하는 방법입니다.
```powershell
# 저장소 복제
git clone https://github.com/MonoBhvr/Verity.git
cd Verity

# 전체 솔루션 빌드
dotnet build Verity.sln
```

### 4. 에디터 실행
런처를 실행하여 프로젝트를 관리하거나 새로 생성할 수 있습니다.
```powershell
# 에디터 실행
dotnet run --project Editor/Verity.Editor.App/Verity.Editor.App.csproj
```

### 5. 배포 패키징
엔진을 독립된 실행 파일 패키지로 만들고 싶다면, 포함된 배포 스크립트를 사용하세요.
```powershell
# 배포용 패키지 생성 (Dist 폴더에 생성됨)
.\publish_engine.ps1
```
실행이 완료되면 `Dist/Editor/VerityEditor.exe`를 통해 엔진을 즉시 실행할 수 있습니다.

---

## 문서

엔진의 상세 아키텍처와 스크립팅 API에 대한 정보는 아래 문서에서 확인하실 수 있습니다.

-   [아키텍처 문서](D:\Verity\ARCHITECTURE.md)
-   [UI 문서](D:\Verity\Docs\UI.md)
-   [Graphics 문서](D:\Verity\Docs\Graphics.md)
-   [Scripting 문서](D:\Verity\Docs\Scripting.md)
-   [Editor 문서](D:\Verity\Docs\Editor.md)

---

## 지원 언어
- **Korean** (ko)
- **English** (en)

현지화 형식으로 json 파일로 새 언어를 추가할 수 있습니다. 현재는 소스코드 수정을 통해 추가할 수 있으며, 차후에는 엔진 내부에서 언어 패치를 추가할 수 있도록 확장할 예정입니다.

## 상태

Verity Engine은 현재 활발히 개발 중인 Alpha 단계 프로젝트입니다. 핵심 기능들이 지속적으로 추가 및 개선되고 있습니다.
### 진행 중
- **UI** : 현재 개발 중이며, 잔존 기능이 있으나 아직 완전히 정리되지는 않았습니다.
- **Sprite Setting** : 슬라이싱을 제외한 구현이 완료되었습니다.
- **Animation** : 테스트가 완료되지 않았습니다.
- **Networking** : 구현되지 않았습니다.
