# Verity 오디오 문서

이 문서는 오디오 시스템의 구조와 공개 API를 설명합니다.

범위는 다음과 같습니다.

- `AudioClip`
- `AudioSource`
- `AudioGroup`
- `AudioListener`
- `AudioSystem`
- `AudioManager`

---

## 1. 오디오 시스템 개요

현재 오디오 시스템은 데스크톱에서 miniaudio 백엔드를 사용하며, 월드당 하나의 `AudioManager`가 그룹과 공간 음향 갱신을 담당합니다.

### 존재 이유

- 간단한 효과음과 음악 재생을 빠르게 지원하기 위해
- 그룹 단위 볼륨 조절과 공간 음향을 제공하기 위해

---

## 2. `AudioType`

| 값 | 의미 |
| :--- | :--- |
| `Effect` | 일반 효과음 |
| `Music` | 배경 음악 |

---

## 3. `AudioClip`

`AudioClip`은 오디오 파일 참조와 로드 핸들을 함께 보관합니다.

### 프로퍼티

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `Name` | `string` | 표시 이름 |
| `Path` | `string` | 에셋 경로 |
| `Guid` | `string` | 에셋 GUID |
| `Handle` | `IntPtr` | 런타임 호환용 핸들 값 |
| `Type` | `AudioType` | 음악/효과음 구분 |
| `DefaultVolume` | `float` | 기본 볼륨 |
| `DefaultPitch` | `float` | 기본 피치 |
| `IsLooping` | `bool` | 루프 기본값 |

### 생성자와 정적 메서드

- `AudioClip()`
- `AudioClip(string name, string path, AudioType type)`
- `static AudioClip FromPath(string path, AudioType? type = null)`
- `static AudioType GuessType(string? path)`

### 인스턴스 메서드

- `void PostLoad(string? resolvedPath = null)`
- `void Preview()`
- `void Dispose()`

### 존재 이유

- 에셋 참조와 런타임 핸들을 하나의 타입으로 묶어야 하기 때문입니다.

---

## 4. `AudioGroup`

`AudioGroup`은 여러 source를 논리적으로 묶는 그룹입니다.

### 프로퍼티

- `string Name`
- `float Volume`
- `float Pitch`
- `bool IsMuted`
- `int MaxVoices`
- `Queue<int> ActiveChannels`

### 메서드

- `float GetFinalVolume(float masterVolume)`

### 존재 이유

- SFX, BGM, UI 같은 그룹별로 볼륨과 voice 제한을 따로 두기 위해

---

## 5. `AudioSource`

`AudioSource`는 엔티티에 붙어 실제 재생 요청을 내리는 컴포넌트입니다.

### 프로퍼티

- `AudioClip? Clip`
- `string GroupName`
- `bool Loop`
- `bool PlayOnStart`
- `bool IsSpatial`
- `bool Mute`
- `float Volume`
- `float Pitch`
- `float MinPitch`
- `float MaxPitch`
- `float MinVolume`
- `float MaxVolume`
- `float MinDistance`
- `float MaxDistance`
- `int CurrentChannel`

### 메서드

- `void Play()`
- `void Stop()`
- `void PlayOneShot(AudioClip clip, float volumeScale = 1.0f)`

### 존재 이유

- 엔티티 위치와 연결된 오디오 재생 단위를 제공하기 위해

---

## 6. `AudioListener`

`AudioListener`는 공간 음향 기준점입니다.

### 존재 이유

- 2D 위치 기반 감쇠와 panning 계산의 기준점이 필요하기 때문입니다.

---

## 7. `AudioSystem`

`AudioSystem`은 오디오 백엔드 초기화와 종료를 담당합니다.

### 메서드

- `void Initialize()`
- `void Shutdown()`

### 존재 이유

- 오디오 백엔드 초기화/종료를 게임 로직과 분리하기 위해

---

## 8. `AudioManager`

`AudioManager`는 월드당 하나만 존재하는 오디오 관리 스크립트입니다.

### 정적 프로퍼티

- `AudioManager Instance`

### 프로퍼티

- `List<AudioGroup> Groups`
- `float MasterVolume`

### 메서드

- `override void Awake()`
- `void SyncGroupMap()`
- `void EnsureDefaultGroups()`
- `AudioGroup GetGroup(string name)`
- `void RemoveGroup(string name)`
- `void Play(AudioSource source)`
- `void Stop(AudioSource source)`
- `void StopGroup(string groupName)`
- `void PlayOneShot(AudioClip clip, string groupName = "SFX", float volumeScale = 1.0f, Vector2? position = null, float minDistance = 1.0f, float maxDistance = 10.0f)`
- `override void Update()`
- `override void OnDestroy()`

### 존재 이유

- 그룹별 볼륨, active listener, 공간 음향 보정을 중앙에서 관리해야 하기 때문입니다.
