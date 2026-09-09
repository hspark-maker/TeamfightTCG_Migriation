# Editor/SROptions

SRDebugger의 `SROptions`를 UXML/USS로 다시 짠 별도 창. 기존 `SROptionsWindow`(IMGUI,
`Assets/ThirdParty/StompyRobot/`)는 그대로 두고 나란히 쓴다.

메뉴: **Window/SRDebugger/SROptions Window (UXML)**

| 파일 | 역할 |
|---|---|
| `SROptionsUIWindow.cs` | 창 진입점(`EditorWindow`). 좌측 카테고리 사이드바 + 우측 옵션 표 2-Pane. 검색·즐겨찾기·최근 사용·플레이 상태 반영 |
| `SROptionsTreeModel.cs` | 카테고리 → 서브카테고리 → 메서드 줄(`SROptionGroup`) 트리 구성 + 검색 필터. UI 의존 없는 순수 C#(`internal`) |
| `SROptionsFieldFactory.cs` | `OptionDefinition` 타입별 UI Toolkit 컨트롤 생성 (bool/int/float/double/string/enum/정수계열/`[Serializable]` 중첩) |
| `SROptionsPrefs.cs` | 즐겨찾기·최근 사용·접힘 상태·선택 카테고리·라벨 폭을 `EditorPrefs`에 저장 |
| `SROptionsWindow.uxml` / `.uss` | 창 레이아웃·스타일 |

## 동작

1. `CreateGUI`에서 UXML을 로드해 붙이고, `SROptionsTreeModel.Build`로 `SROptions.Current`
   (플레이 중) 또는 임시 인스턴스(에디트 모드)를 스캔해 트리를 만든다.
2. 창 폭이 340px 미만이면 사이드바를 접고, 대신 crumb의 카테고리 버튼(`GenericMenu`)으로
   카테고리를 옮겨 다닌다.
3. 검색어가 있으면 본문이 카테고리를 가로지르는 평면 결과 목록(`BuildSearchResults`)으로
   전환된다.
4. `OnInspectorUpdate`에서 플레이 중이면 화면에 뜬 필드(`visibleFieldList`)를 `Refresh`로
   맞추고, `SROptions.Current`가 바뀌면(플레이 진입) 트리를 다시 만든다.

## 파라미터-메서드 묶기 — `[SRParamOf]`

서브카테고리 하나가 표 하나이고, **한 줄 = 실행 메서드 하나**다: `[이름 = 실행 버튼] [파라미터 필드들] [★]`.
파라미터 프로퍼티는 `[SRParamOf(nameof(메서드))]`(`SRDebugger` 네임스페이스, 여러 개 나열 가능)로
**같은 카테고리·서브카테고리의** 메서드에 붙는다. 어트리뷰트가 없거나 가리키는 메서드가 그 서브카테고리에
없으면 프로퍼티 혼자 한 줄이다(이름 자리에 프로퍼티 이름 라벨, 버튼 아님). 파라미터 없는 메서드는 가운데가 빈다.

파라미터 라벨은 메서드 이름과 겹치는 토큰을 뺀 잔여(`SROptionsTreeModel.MakeParamLabel`) —
「장비 ID」+「장비 획득」→「ID」. 전부 겹치면 마지막 토큰, 하나도 안 겹치면 원래 이름.
메서드 이름은 `OptionDefinition`이 주지 않으므로 `BuildMethodIdLookup`이 컨테이너를 리플렉션으로 훑어
C# 이름 → 항목 Id를 만든다.

## 메모

- `SROptionsFieldFactory`는 `internal`이라 이 폴더 밖에서 직접 쓰지 않는다.
  기존 창이 옵션마다 붙이던 `"{이름}_검색"` 텍스트필드는 만들지 않는다.
- `NumberRangeAttribute`가 붙은 int/float는 슬라이더, enum 값이 9개 이상이면 검색창이 달린
  `AdvancedDropdown`(`EnumSearchDropdown`)을 띄운다.
- `[Serializable]` 클래스·구조체 프로퍼티는 `BuildNested`가 리플렉션으로 필드를 펼쳐
  접이식으로 편집한다. 구조체는 값 복사본이라 편집 후 프로퍼티에 다시 써야 반영된다.
- `SROptions.Cheat.cs` 등 옵션 정의 자체는 이 폴더가 건드리지 않는다. 여기는 표시 전용이다.
