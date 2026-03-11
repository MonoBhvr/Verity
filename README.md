# Verity Engine

**Even Slow, Use Easier** - Verity는 개발 입문자와 1인 개발자를 위해 설계된, 완전히 C#으로 작성된 쉽고 강력한 2D 게임 엔진입니다.

Verity는 복잡한 네이티브 코드 대신 최신 .NET 환경을 기반으로 하며, 직관적인 에디터와 강력한 ECS 아키텍처를 결합하여 아이디어를 빠르게 프로토타입으로 구현할 수 있게 돕습니다.

---

## 🚀 Key Features

-   **Seamless C# Scripting**: 복잡한 설정 없이 C# 클래스를 상속받는 것만으로 게임 로직을 작성할 수 있습니다.
-   **Integrated Editor**: 직관적인 계층 구조(Hierarchy), 인스펙터(Inspector), 프로젝트 관리 시스템을 포함합니다.
-   **Modern ECS Architecture**: 엔티티-컴포넌트-시스템 구조를 통해 유연하고 확장성 있는 개발이 가능합니다.
-   **Spatial LOD Grid**: 유니티 스타일의 지능형 동적 그리드 시스템으로 줌 레벨에 따라 자연스러운 작업 환경을 제공합니다.
-   **Multi-Instance Launcher**: 유니티 허브 스타일의 런처를 통해 여러 프로젝트를 동시에 관리하고 안전하게 열 수 있습니다.
-   **VS Code Support**: 프로젝트 로드 시 인텔리센스를 위한 `.csproj` 파일을 자동으로 생성하여 쾌적한 코딩 환경을 제공합니다.
-   **2D Physics Engine**: SAT 알고리즘 기반의 정교한 충돌 판정과 물리 시뮬레이션을 지원합니다.

---

## 🛠 Tech Stack

-   **Language**: C# 12 / .NET 9.0
-   **Graphics**: Irodori (OpenGL)
-   **UI Framework**: Dear ImGui (Hexa.NET.ImGui)
-   **Windowing**: SDL2
-   **Mathematics**: System.Numerics

---

## 📖 Documentation

엔진의 상세 아키텍처와 스크립팅 API에 대한 정보는 아래 문서에서 확인하실 수 있습니다.

-   [Architecture & Scripting API Reference](ARCHITECTURE.md)
-   [Physics Engine Design Specification](PHYSICS_DESIGN.md)

---

## 🚧 Status

Verity Engine은 현재 활발히 개발 중인 Alpha 단계 프로젝트입니다. 핵심 기능들이 지속적으로 추가 및 개선되고 있습니다.
