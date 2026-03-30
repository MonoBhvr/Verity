# Verity Editor Features & Internals

Verity 엔진의 에디터 환경과 내부 시스템에 대한 설명입니다.

## 1. Editor Features & Controls

### 1.1. Filter Editor Window
**Window > Filter Editor** 메뉴를 통해 프로젝트에서 사용할 모든 필터를 관리합니다. 생성한 필터는 즉시 인스펙터와 `Input` API에서 사용할 수 있습니다.

### 1.2. Shortcuts (단축키)
| Input | Action |
| :--- | :--- |
| **F Key / Double Click** | 선택된 엔티티로 화면 이동 및 줌 포커스. |
| **F2** | 선택된 엔티티/에셋 이름 변경. |
| **Ctrl + N** | 빈 엔티티 생성 또는 새 폴더 생성. |
| **W / E / R** | 이동 / 스케일 / 회전 도구 전환. |
| **Mouse Wheel** | 마우스 위치 중심 줌 조절. |
| **Right Click Drag** | 월드 자유 이동 (Panning). |

---

## 2. Branding & Customization
- **에디터 로고**: `EditorResources/EditorLogo.png` 파일이 런처와 창 아이콘에 적용됩니다.
- **빌드본 로고**: `BuildSettings.json`의 `LogoPath`에 지정된 파일이 사용됩니다.

---

## 3. Editor Internals (Advanced)

### 3.1. Undo/Redo System (`Verity.Editor.UndoSystem`)
- **Snapshot-based**: 모든 변경 시 월드와 프로젝트 설정의 상태를 JSON 스냅샷으로 저장합니다.
- **Continuous Action**: 기즈모 드래그와 같이 연속적인 변화는 작업이 끝난 시점에 하나의 이력으로 통합합니다.

### 3.2. Localization (L10n) (`Verity.Editor.L10n`)
- **JSON-based**: `Locales/en.json`, `Locales/ko.json` 파일에 정의된 키-값 쌍을 로드하여 다국어를 지원합니다.

### 3.3. Script Compilation (`Verity.Editor.ScriptCompiler`)
- **Roslyn API**: `Microsoft.CodeAnalysis`를 사용하여 에디터 실행 중에 사용자의 코드를 빌드하고 DLL로 생성하여 런타임에 반영합니다.
