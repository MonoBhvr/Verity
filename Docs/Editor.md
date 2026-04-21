# Verity 에디터 기능 및 내부 구조

Verity 에디터는 프로젝트 런처, 멀티 윈도우 편집기, 런타임 미리보기, 자산 브라우저, Undo/Redo, 스크립트 컴파일, 현지화 시스템을 하나로 묶은 통합 제작 환경입니다.

이 문서는 단순한 메뉴 목록이 아니라, 각 창이 왜 존재하는지와 실제 코드 기준으로 어떤 역할을 맡는지를 함께 설명합니다.

---

## 1. 에디터 개요

Verity 에디터는 크게 두 단계로 동작합니다.

1. **런처 단계**: 프로젝트 목록을 보여 주고, 새 프로젝트를 만들거나 기존 프로젝트를 별도 인스턴스로 엽니다.
2. **메인 에디터 단계**: 월드, 블루프린트, UI, 타일, 애니메이션, 빌드 설정을 편집합니다.

| 계층 | 대표 구현 | 존재 이유 |
| :--- | :--- | :--- |
| 런처 | `EditorApp`의 launcher UI | 프로젝트 진입점을 분리해 여러 프로젝트를 안전하게 관리하기 위해 |
| 메인 편집기 셸 | `EditorApp`, `EditorWindow` | 도킹/분리 가능한 다중 창 구조를 제공하기 위해 |
| 장면 편집 | `WorldViewWindow`, `HierarchyWindow`, `InspectorWindow` | 엔티티 기반 월드 편집 흐름을 구성하기 위해 |
| 자산 편집 | `ProjectWindow`, `TilePaletteWindow`, `UIEditorWindow`, `AnimationWindow` | 파일 중심 제작 작업을 한곳에서 수행하기 위해 |
| 지원 시스템 | `UndoSystem`, `ScriptCompiler`, `L10n` | 편집 안정성, 즉시 반영, 다국어 UI를 보장하기 위해 |

메인 프로그램(`Editor/Verity.Editor.App/Program.cs`)은 다음 창을 등록합니다.

- `WorldViewWindow`
- `ScreenWindow`
- `HierarchyWindow`
- `InspectorWindow`
- `ConsoleWindow`
- `ProjectWindow`
- `BuildSettingsWindow`
- `ProfilerWindow`
- `FilterEditorWindow`
- `AnimationWindow`
- `TilePaletteWindow`
- `UIEditorWindow`

즉, Verity의 에디터는 “하나의 큰 창”이라기보다, 편집 대상별 전용 도구를 묶은 작업 공간에 가깝습니다.

---

## 2. 런처 시스템

런처는 프로젝트를 고르는 첫 화면입니다. `EditorApp`은 프로젝트를 직접 현재 프로세스에서 여는 대신, 런처에서 프로젝트를 선택하면 **별도 프로세스를 다시 실행**하면서 `--project "프로젝트명"` 인자를 넘깁니다.

### 2.1 핵심 동작

| 항목 | 실제 동작 |
| :--- | :--- |
| 프로젝트 목록 | `ProjectsRoot` 아래 디렉터리를 스캔하고, `Assets` 내부 파일 수정 시각까지 반영해 최근 수정 순으로 정렬 |
| 새 프로젝트 생성 | 이름만 입력하면 해당 이름으로 새 인스턴스를 실행 |
| 프로젝트 루트 변경 | 런처에서 프로젝트 루트 폴더를 바꿀 수 있음 |
| 탐색기 열기 | Windows 탐색기로 프로젝트 루트를 바로 열 수 있음 |
| 브랜딩 | `EditorResources/EditorLogo.png`가 있으면 런처 상단 로고로 사용 |

### 2.2 멀티 인스턴스 관리

Verity는 프로젝트마다 `.lock` 파일을 열어 **배타적 파일 잠금**을 잡습니다.

- 이미 다른 인스턴스가 열어 둔 프로젝트는 다시 열 수 없습니다.
- 프로젝트별 잠금을 사용하므로 서로 다른 프로젝트는 동시에 열 수 있습니다.
- 이 구조 덕분에 “프로젝트 허브 + 독립 편집 세션” 모델이 성립합니다.

### 2.3 프로젝트 열기 시 초기화 순서

`OpenProject` 기준으로 보면 다음 순서로 초기화됩니다.

1. 프로젝트 폴더와 `Assets` 폴더 생성
2. `.lock` 파일로 잠금 획득
3. 프로젝트 파일/창 상태 준비
4. `Filters.json`, `ProjectSettings.json`, 도킹 레이아웃, `BuildSettings.json` 로드
5. 에셋 감시기 초기화
6. `ScriptCompiler` 생성 후 사용자 스크립트 컴파일
7. 마지막으로 열었던 월드 또는 가장 최근 월드 복원

런처는 단순 목록 UI가 아니라, 에디터 진입에 필요한 상태 복원과 안전장치의 시작점입니다.

---

## 3. 메인 에디터 인터페이스

메인 에디터는 `EditorApp`이 관리하는 도킹 기반 레이아웃입니다.

### 3.1 레이아웃 구조

| 영역 | 기본 역할 |
| :--- | :--- |
| 메뉴 바 | 파일, 윈도우, 언어, 빌드, 재생 제어 |
| World View | 편집용 월드 뷰포트와 기즈모 |
| Screen | 게임 카메라 기준 최종 화면 미리보기 |
| Hierarchy | 엔티티 트리 편집 |
| Inspector | 엔티티/컴포넌트/에셋 속성 편집 |
| Project | `Assets` 파일 브라우저 |
| Console | 로그 확인 |

### 3.2 도킹과 분리

- 창은 `EditorWindow` 기반으로 관리됩니다.
- 도킹 모드와 분리 모드를 모두 지원합니다.
- 레이아웃은 프로젝트 설정에 저장되어 다음 실행 시 복원됩니다.
- 창 메뉴에서 레이아웃 초기화와 언어 전환이 가능합니다.

### 3.3 두 개의 뷰포트

Verity는 편집용과 결과 확인용을 나눕니다.

| 뷰 | 목적 |
| :--- | :--- |
| `WorldViewWindow` | 오브젝트 선택, 이동, 스케일, 회전, 타일 편집, 기즈모 표시 |
| `ScreenWindow` | 실제 카메라 기준 렌더 결과와 UI 오버레이 확인 |

이 분리는 “장면을 조작하는 작업”과 “플레이어가 보는 화면 확인”을 분리하기 위해 존재합니다.

---

## 4. 계층 구조(Hierarchy) 창

`HierarchyWindow`는 활성 월드의 루트 엔티티와 자식 엔티티를 트리로 보여 줍니다.

### 4.1 제공 기능

- 엔티티 계층 표시
- 선택 및 다중 선택
- 드래그 앤 드롭 재부모화
- 삽입 슬롯을 통한 순서 변경
- 컨텍스트 메뉴 기반 생성
- 복사 / 붙여넣기 / 복제 / 삭제
- 블루프린트 저장 진입점 제공

### 4.2 생성 가능한 대표 프리셋

| 생성 항목 | 실제 추가 컴포넌트 |
| :--- | :--- |
| 빈 엔티티 | 없음 |
| Sprite | `SpriteRenderer` |
| Tilemap + Shape | `TilemapRenderer`, `TilemapShape` |
| Tilemap | `TilemapRenderer` |
| Spot / Directional / Global Light | `Light2D` 설정값이 다른 프리셋 |
| Audio Listener | `AudioListener` |
| Audio Source | `AudioSource` |
| Camera | `Camera` |

### 4.3 블루프린트 편집 모드

블루프린트 자산을 열면 Hierarchy 상단에 블루프린트 편집 모드 헤더가 표시됩니다.

- 마지막으로 열었던 월드로 돌아가는 버튼이 생깁니다.
- 저장 후 원래 월드로 복귀할 수 있습니다.
- 블루프린트 편집 시 새 엔티티는 기본 부모 아래에 붙도록 보정됩니다.

---

## 5. 인스펙터(Inspector) 창

`InspectorWindow`는 선택된 엔티티, 여러 엔티티, 또는 에셋의 속성을 편집하는 중심 창입니다.

### 5.1 존재 이유

- 엔티티/컴포넌트 데이터를 한곳에서 수정하기 위해
- 타입별 전용 UI와 일반 리플렉션 UI를 함께 제공하기 위해
- 코드 attribute를 에디터 UI 규칙으로 해석하기 위해

### 5.2 엔티티 편집

- 활성 상태
- 이름
- 태그
- 컴포넌트 목록
- 컴포넌트 추가 팝업
- 컴포넌트 제거

멀티 셀렉션 시에는 공통으로 존재하는 컴포넌트만 골라 공통 필드를 동시에 편집합니다.

### 5.3 attribute 기반 UI

Inspector는 멤버의 attribute를 읽어 자동으로 편집 UI를 바꿉니다.

| attribute / 규칙 | 인스펙터 동작 |
| :--- | :--- |
| `HideInInspector` | 숨김 |
| `AssetReferenceAttribute` | 에셋 선택 드롭다운 |
| `TagSelectorAttribute` | 태그 선택 UI |
| `PhysicsGroupSelectorAttribute` | 물리 그룹 선택 UI |
| `SortingLayerSelectorAttribute` | 정렬 레이어 선택 UI |
| 마스크 selector 계열 | bitmask 드롭다운 |
| `ButtonAttribute` | 메서드를 버튼으로 노출 |

`ButtonAttribute(undoable: true)`가 붙은 메서드는 실행 전후를 Undo 범위로 감쌉니다.

### 5.4 자산 편집 범위

Inspector는 엔티티뿐 아니라 다음 자산도 편집합니다.

- 스프라이트 및 슬라이스 선택
- 스타일/셰이더 자산 참조
- 블루프린트 미리보기
- UI 관련 프로젝트 설정(`Default UI Font`, `UI Catalog`, `UI Role Defaults`)

### 5.5 블루프린트 인스턴스 오버라이드 표시

블루프린트 인스턴스를 선택하면 원본 경로와 오버라이드된 필드를 표시합니다.

- 이름
- 활성 여부
- Transform 위치/회전/스케일
- 컴포넌트별 필드 변경

즉, 인스펙터는 단순 프로퍼티 그리드가 아니라, Verity의 메타데이터 기반 편집 규칙을 해석하는 UI 계층입니다.

---

## 6. 프로젝트(Project) 창

`ProjectWindow`는 `Assets` 폴더를 관리하는 파일 브라우저입니다.

### 6.1 화면 구성

| 패널 | 역할 |
| :--- | :--- |
| 좌측 폴더 트리 | 디렉터리 탐색 |
| 상단 경로 바 | 현재 경로 입력/이동 |
| 중앙 브라우저 | 파일, 폴더, 스프라이트 슬라이스 표시 |
| 하단 줌 푸터 | 아이콘/타일 크기 조절 |

### 6.2 생성 가능한 자산

- Script
- World (`.verity`)
- Folder
- Shader
- Style
- UI Screen
- UI Style
- Tile
- Animated Tile
- Rule Tile

### 6.3 지원 기능

- 상세/타일/아이콘 보기
- 썸네일 미리보기
- 폴더 이동과 경로 직접 입력
- 드래그 앤 드롭 이동
- 이름 변경, 삭제, 복제
- Windows 탐색기 열기
- SDF 폰트 생성
- 월드 저장/로드 진입점 제공

### 6.4 자산 의미

Project 창은 단순 파일 탐색기가 아닙니다.

- `.verity` 월드를 열면 메인 편집 대상으로 전환됩니다.
- `.blueprint`는 프리뷰 및 편집 진입점이 됩니다.
- 이미지 자산은 스프라이트/슬라이스 단위로도 선택됩니다.
- `.ui`는 UI Editor와 연결됩니다.
- 타일 자산은 Tile Palette와 연결됩니다.

---

## 7. 월드 뷰(World View)

`WorldViewWindow`는 에디터의 핵심 장면 편집 뷰입니다.

### 7.1 툴바 기능

| 기능 | 설명 |
| :--- | :--- |
| Move / Scale / Rotate / Rect | 기즈모 툴 전환 |
| Grid | 그리드 표시 토글 |
| Gizmos | 기즈모 렌더 토글 |
| Snap | 격자 스냅 토글 |
| Snap Size | 스냅 간격 조절 |
| Render Detail | Outline / Basic / Lighting / PostProcess 전환 |

### 7.2 실제 편집 기능

- 엔티티 클릭 선택
- 다중 선택
- 박스 선택
- 드래그 이동
- 스케일 핸들 조작
- 회전 핸들 조작
- 빈 엔티티도 AABB 기준 선택 가능
- 타일맵 셀 히트 테스트
- 선택 대상 포커스

### 7.3 기즈모와 보조 렌더링

- 공간 그리드와 원점 축 렌더링
- 스크립트의 `OnDrawGizmos`, `OnDrawGizmosSelected` 호출
- 물리 도형, 특히 `TilemapShape` 보조선 표시
- 선택 대상 외곽/핸들 렌더링

### 7.4 드래그 프리뷰

Project 창에서 자산을 끌어오면 World View에서 반투명 프리뷰를 보여 줍니다.

- 블루프린트 드롭 시 인스턴스 미리보기
- 이미지 드롭 시 스프라이트 엔티티 미리보기
- 스냅이 켜져 있으면 그리드에 맞춰 배치

---

## 8. 화면 뷰(Screen)

`ScreenWindow`는 플레이어가 보게 될 최종 화면을 보여 주는 창입니다.

| 항목 | 동작 |
| :--- | :--- |
| 카메라 기준 렌더 | 월드에서 첫 활성 카메라를 찾아 렌더 |
| UI 오버레이 | UI Editor가 열려 있고 overlay가 켜져 있으면 미리보기 UI를 합성 |
| 유휴 최적화 | 포커스/호버가 없을 때는 일정 간격으로만 다시 그림 |

World View가 “편집용 시야”라면 Screen은 “실제 결과 시야”입니다.

---

## 9. 타일 팔레트(Tile Palette)

`TilePaletteWindow`는 타일맵 편집에 쓰는 타일 자산과 브러시 상태를 관리합니다.

### 9.1 기능

- 타일 자산 목록 자동 스캔
- 타일, 애니메이션 타일, 룰 타일 생성
- 타일 썸네일 그리드 표시
- 현재 선택 타일 미리보기
- 타일 속성 즉시 저장

### 9.2 브러시 설정

| 항목 | 설명 |
| :--- | :--- |
| Tool | `TilemapEditor.Tool` 기반 도구 선택 |
| Brush Size | 브러시 크기 |
| Brush Shape | Rectangle 등 브러시 형상 |

브러시 상태는 에디터 선택 상태에 저장되며 Undo 대상에도 포함됩니다.

### 9.3 편집 가능한 타일 속성

| 타일 종류 | 편집 항목 |
| :--- | :--- |
| `Tile` | 이름, 충돌 여부, 색상, 스프라이트 |
| `AnimatedTile` | 이름, 충돌 여부, 색상, 재생 속도, 프레임 목록 |
| `RuleTile` | 기본 스프라이트, 규칙별 출력 스프라이트, 3x3 이웃 조건 |

Rule Tile은 각 이웃 칸을 `Any / Required / NotRequired`로 순환시키는 UI를 제공합니다.

---

## 10. 애니메이션(Animation) 윈도우

`AnimationWindow`는 선택된 엔티티의 `Animator`와 `AnimationClip`을 편집합니다.

### 10.1 전제 조건

- 엔티티가 선택되어 있어야 함
- `Animator`가 없으면 생성 버튼 제공
- `Animator.Controller`가 없으면 컨트롤러 생성 버튼 제공

### 10.2 툴바 기능

- 녹화 토글
- 재생/정지
- 현재 시간 표시
- 상태 선택 콤보
- 상태 추가
- 기본 상태 지정
- 클립 FPS 변경
- 루프 여부 변경
- 컨트롤러 저장

### 10.3 타임라인 구성

| 영역 | 역할 |
| :--- | :--- |
| 좌측 트랙 목록 | 애니메이션할 프로퍼티 목록 |
| 우측 도프시트 | 초 단위 눈금, 스크러버, 키프레임 표시 |
| 하단 검사 영역 | 선택 키프레임/상태 세부 편집 |

### 10.4 트랙 추가 방식

선택 엔티티의 각 컴포넌트를 반사해 다음 조건을 만족하는 멤버를 보여 줍니다.

- public field 또는 property
- 읽기/쓰기 가능
- `AnimationTypeUtility.IsAnimatable(...)`를 통과

즉, Animation 창은 미리 정해진 소수 타입만 다루는 것이 아니라, 애니메이션 가능 타입이라면 일반 컴포넌트 필드도 트랙으로 추가할 수 있습니다.

---

## 11. UI 편집기(UI Editor)

`UIEditorWindow`는 `.ui` 파일을 직접 편집하는 전용 도구입니다.

### 11.1 3패널 구조

| 패널 | 역할 |
| :--- | :--- |
| 좌측 Hierarchy | UI 노드 트리 |
| 중앙 Canvas | 해상도 기반 미리보기와 직접 조작 |
| 우측 Inspector | 선택 노드 속성, 바인딩, 레이아웃 편집 |

### 11.2 지원 노드 팔레트

- Container
- Panel
- Label
- Image
- Button
- Toggle
- InputField
- TextArea
- Slider
- ProgressBar
- ScrollView
- DynamicArea
- Spacer

### 11.3 캔버스 편집 기능

- Move / Scale / Rotate 도구
- 줌 인/아웃
- 팬 이동
- 해상도 프리셋 전환
- 프레임 뷰 리셋
- 선택 노드 직접 조작
- 오버레이 표시 토글

### 11.4 `.ui` 편집 범위

기존 문서에 있던 핵심 항목은 그대로 유지되며, 실제 구현상 다음을 다룹니다.

- 화면 설정(`ReferenceResolution` 등)
- 화면 변수
- 레이아웃
- `DynamicArea`
- 바인딩
- 이벤트
- 프리팹 저장

### 11.5 내부 Undo

UI Editor는 전역 `UndoSystem`과 별도로 자체 Undo 스택을 가집니다.

- 화면 JSON 스냅샷 저장
- 선택 노드 ID 저장
- 해상도 프리셋 상태 저장
- `Ctrl+Z`, `Ctrl+Y` 지원

즉, UI 편집은 월드 편집과 분리된 로컬 히스토리를 가집니다.

---

## 12. 필터 에디터(Filter Editor)

**Window > Filter Editor** 메뉴를 통해 프로젝트에서 사용할 모든 필터를 관리합니다. 생성한 필터는 즉시 인스펙터와 `Input` API에서 사용할 수 있습니다.

`FilterEditorWindow`는 `FilterManager`와 연결된 전용 관리 창입니다.

### 12.1 편집 가능한 요소

| 항목 | 설명 |
| :--- | :--- |
| 필터 이름 | 식별 이름 |
| 모드 | Whitelist / Blacklist |
| 타입 | 단일 enum 기반 또는 mixed 타입 |
| 값 목록 | enum 값 또는 mixed 규칙 목록 |

### 12.2 지원 타입

- 시스템 타입: `Tag`, `PhysicsGroup`, `SortingLayer`
- 사용자 enum: `ScriptCompiler`가 수집한 public enum 전체
- mixed 필터: 여러 타입 값을 한 필터에 혼합 가능

필터는 입력, 레이어, 그룹 선택 UI와 연결되는 공용 선택자 자산이라는 점에서 중요합니다.

---

## 13. 프로파일러(Profiler) 창

`ProfilerWindow`는 에디터 프레임과 런타임 틱 성능을 함께 보여 줍니다.

### 13.1 요약 수치

- FPS
- 실제 TPS
- 실제 PTPS
- 설정된 Target TPS / PTPS

### 13.2 그래프/섹션

| 섹션 | 내용 |
| :--- | :--- |
| Frame | 에디터 프레임 시간 히스토리 |
| Logic | 런타임 로직 틱 시간 |
| Physics | 물리 틱 시간 |
| Frame Stages | 프레임 세부 단계 분해 |
| Render Stages | World View Render, Screen Render, Overlay UI 등 |
| Window Latency | 각 편집 창 처리 시간 |
| Script Phases | 스크립트 단계별 시간 |
| Scripts | 스크립트별 총 시간, 평균, 호출 수 |

Profiler는 단순 fps 카운터가 아니라, “에디터 비용”과 “게임 루프 비용”을 동시에 추적하는 창입니다.

---

## 14. 콘솔(Console) 창

`ConsoleWindow`는 에디터 및 엔진 로그를 보는 창입니다.

### 14.1 특징

- Info / Warning / Error 색상 구분
- 최대 1000개 로그 보존
- 전체 지우기
- 전체 복사
- 선택 로그 복사
- 다중 선택, Shift 범위 선택, 드래그 선택
- 우클릭 컨텍스트 메뉴

### 14.2 의미

스크립트 컴파일 실패, 퍼블리시 실패, 로케일 누락 키, 자산 관련 오류는 대부분 콘솔을 통해 먼저 확인하게 됩니다.

---

## 15. 빌드 설정(Build Settings)

`BuildSettingsWindow`는 내부적으로 `BuildSettingsEditorUi`를 사용해 프로젝트 빌드 대상을 편집합니다.

### 15.1 편집 항목

| 항목 | 설명 |
| :--- | :--- |
| Worlds | 빌드에 포함할 월드 목록 |
| StartWorldIndex | 시작 월드 지정 |
| Branding / LogoPath | 빌드 결과물 로고 경로 |

### 15.2 지원 작업

- 활성 월드를 빌드 목록에 추가
- 프로젝트 내 모든 `.verity` 월드를 목록에서 선택해 추가
- 포함 순서 변경
- 시작 월드 지정
- 목록 저장

### 15.3 퍼블리시 흐름

Project 창에서 Debug/Release 퍼블리시를 시작하면 다음 순서로 진행됩니다.

1. 출력 폴더 준비
2. `Verity.Game` 프로젝트의 `Assets` 동기화
3. `BuildSettings.json` 복사
4. `ScriptCompiler.CompileToFile(...)`로 `UserScripts.dll` 생성
5. `dotnet publish` 실행

Release는 단일 파일, Debug는 디버깅 친화 설정으로 배포됩니다.

---

## 16. 단축키 전체 목록

실제 코드에 드러난 대표 단축키는 다음과 같습니다.

| 입력 | 범위 | 동작 |
| :--- | :--- | :--- |
| **F** | Hierarchy / World View / UI Editor | 선택 대상 포커스 또는 캔버스 프레임 |
| **F2** | Hierarchy / Project | 이름 변경 |
| **Ctrl + N** | Hierarchy / Project | 빈 엔티티 생성 또는 새 폴더 생성 |
| **Ctrl + C** | Hierarchy / World View / Console | 엔티티 복사 또는 선택 로그 복사 |
| **Ctrl + V** | Hierarchy / World View | 엔티티 붙여넣기 |
| **Ctrl + D** | Hierarchy / World View / Project | 복제 |
| **Delete** | Hierarchy / World View / Project | 삭제 |
| **W / E / R** | World View / UI Editor | 이동 / 스케일 / 회전 도구 |
| **T** | World View | Rect 도구 |
| **Ctrl + Z** | 전역 에디터 / UI Editor | Undo |
| **Ctrl + Y** | 전역 에디터 / UI Editor | Redo |
| **Ctrl + Shift + Z** | 전역 에디터 | Redo |
| **Mouse Wheel** | World View / UI Editor | 줌 |
| **Middle / Right Drag** | UI Editor | 캔버스 팬 |
| **Right Click Drag** | World View | 월드 이동(Panning) |
| **Double Click** | 기존 문서 기준 | 선택된 엔티티/자산 포커스 흐름에서 사용 |

기존 문서에 있던 다음 항목도 계속 유효합니다.

| Input | Action |
| :--- | :--- |
| **F Key / Double Click** | 선택된 엔티티로 화면 이동 및 줌 포커스. |
| **F2** | 선택된 엔티티/에셋 이름 변경. |
| **Ctrl + N** | 빈 엔티티 생성 또는 새 폴더 생성. |
| **W / E / R** | 이동 / 스케일 / 회전 도구 전환. |
| **Mouse Wheel** | 마우스 위치 중심 줌 조절. |
| **Right Click Drag** | 월드 자유 이동 (Panning). |

---

## 17. Undo/Redo 시스템 (`Verity.Editor.UndoSystem`)

### 17.1 개요

- **Snapshot-based**: 모든 변경 시 월드와 프로젝트 설정의 상태를 JSON 스냅샷으로 저장합니다.
- **Continuous Action**: 기즈모 드래그와 같이 연속적인 변화는 작업이 끝난 시점에 하나의 이력으로 통합합니다.

### 17.2 실제 저장 범위

`UndoState`는 다음 네 가지를 저장합니다.

| 필드 | 내용 |
| :--- | :--- |
| `WorldJson` | 월드 전체 직렬화 결과 |
| `ProjectSettingsJson` | 프로젝트 설정 |
| `BuildSettingsJson` | 빌드 설정 |
| `EditorStateJson` | 선택 자산, 월드뷰 상태, 타일 브러시 상태 등 |

### 17.3 보존 정책

- scope key 기준으로 히스토리를 분리합니다.
- 최대 100개까지 유지합니다.
- 동일 스냅샷은 중복 기록하지 않습니다.

### 17.4 복원 시 동작

복원 시에는 현재 월드를 비우고 JSON으로 다시 역직렬화합니다.

- 이전 선택 엔티티 ID 복원 시도
- 월드 자산 재바인딩
- World View 상태 복원
- 타일 도구/브러시 상태 복원

즉, Verity의 Undo는 필드 단위 patch가 아니라 “편집 세션 상태 전체”를 되감는 방식입니다.

---

## 18. 스크립트 컴파일 (`Verity.Editor.ScriptCompiler`)

### 18.1 개요

- **Roslyn API**: `Microsoft.CodeAnalysis`를 사용하여 에디터 실행 중에 사용자의 코드를 빌드하고 DLL로 생성하여 런타임에 반영합니다.

### 18.2 실제 동작

| 항목 | 설명 |
| :--- | :--- |
| 감시 대상 | `Assets` 아래 모든 `*.cs` 파일 |
| 감시 방식 | `FileSystemWatcher` + 500ms debounce |
| 참조 수집 | 현재 AppDomain의 로드된 assembly를 메타데이터 참조로 사용 |
| 출력 | 메모리 내 assembly 로드 또는 파일 출력 |

### 18.3 자동 주입되는 전역 using

컴파일 시 다음 계열 네임스페이스를 전역 using으로 추가합니다.

- `Verity.Core`
- `Verity.Core.ECS`
- `Verity.Core.UI`
- `Verity.Graphics`
- `Verity.Input`
- `Vector2`, `Vector3`, `Color` 별칭

### 18.4 에디터 연동 지점

- Add Component 팝업에서 사용자 컴포넌트 수집
- Filter Editor에서 사용자 enum 수집
- `GetUserScripts()`를 통한 스크립트 타입 반영
- 빌드 시 `CompileToFile()`로 `UserScripts.dll` 생성

컴파일 에러는 콘솔 로그 형식으로 파일명, 줄, 열, 메시지를 출력합니다.

---

## 19. 현지화(L10n) (`Verity.Editor.L10n`)

### 19.1 개요

- **JSON-based**: `Locales/en.json`, `Locales/ko.json` 파일에 정의된 키-값 쌍을 로드하여 다국어를 지원합니다.
- **Fallback Merge**: 현재는 영어 locale을 먼저 로드한 뒤, 선택 언어를 덮어쓰는 방식으로 동작합니다. 따라서 일부 키가 빠져 있어도 원문 키 대신 영어 문자열이 fallback으로 표시됩니다.

### 19.2 실제 특징

| 항목 | 설명 |
| :--- | :--- |
| 기본 언어 | 현재 기본값은 `ko` |
| 탐색 방식 | 실행 경로와 저장소 경로를 모두 후보로 검색 |
| 지원 언어 목록 | 초기값 `en`, `ko`, 이후 JSON 파일 스캔으로 확장 |
| 누락 키 처리 | 최초 1회 디버그 로그 후 fallback 또는 key 자체 반환 |

### 19.3 에디터 UI 연동

- 메뉴 바에서 언어 변경 가능
- 새 언어 추가 팝업 지원
- 표시 이름은 `lang_xx` 키를 통해 가져옴
- 글로벌 설정에 현재 언어 저장

즉, L10n은 단순 문자열 테이블이 아니라 런처와 편집기 전체 UI를 전환하는 공용 계층입니다.

---

## 20. 브랜딩 및 커스터마이징

기존 문서의 브랜딩 항목은 다음과 같으며, 실제 코드와도 일치합니다.

- **에디터 로고**: `EditorResources/EditorLogo.png` 파일이 런처와 창 아이콘/미리보기에 적용됩니다.
- **빌드본 로고**: `BuildSettings.json`의 `LogoPath`에 지정된 파일이 사용됩니다.

추가로 확인되는 커스터마이징 지점은 다음과 같습니다.

| 영역 | 커스터마이징 포인트 |
| :--- | :--- |
| 프로젝트 루트 | 글로벌 설정에서 변경 가능 |
| 언어 | 런처/에디터 메뉴에서 전환 가능 |
| 레이아웃 | 프로젝트별 도킹 레이아웃 저장 |
| UI 기본값 | `Default UI Font`, `UI Catalog`, `UI Role Defaults` 편집 가능 |
| 월드 배경 | `ProjectSettings.EditorWorldBackgroundColor` 사용 |

---

## 21. 정리

Verity 에디터는 다음 특징으로 요약할 수 있습니다.

1. 런처 단계에서 프로젝트를 분리하고, 프로젝트별 잠금으로 멀티 인스턴스를 안전하게 관리합니다.
2. 메인 에디터는 월드 편집, 화면 확인, 자산 관리, UI/타일/애니메이션 편집을 전용 창으로 분리합니다.
3. Inspector는 attribute 기반 UI를 통해 코드 메타데이터를 실제 편집 경험으로 변환합니다.
4. Undo, 스크립트 컴파일, 현지화는 편집 전반을 떠받치는 공통 시스템입니다.

Verity의 에디터는 “런타임 엔진 위에 얹은 부가 도구”라기보다, 프로젝트 관리부터 월드 제작, UI 제작, 빌드 준비까지 연결하는 제작 파이프라인 자체에 가깝습니다.
