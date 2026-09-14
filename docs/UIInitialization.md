# UI 초기화와 Contents 구조

## 기본 계약

```text
MissionOverlay             활성 유지: ContentsPooledUI 파생 제어 스크립트
└─ Contents                최초 비활성: 실제 표시·입력·연출
   ├─ 배경과 안전 영역
   ├─ 버튼
   └─ 목록과 행
```

제어 루트는 인스턴스가 존재하는 동안 활성 상태를 유지한다. UI를 닫을 때는 `Hide`/`Close`를 호출하고 `Contents`만 끈다. 모든 UI를 앱 시작 때 생성한다는 뜻은 아니다. 기존 지연 적재와 풀 재사용을 유지한다.

## 초기화 소유권

- 생성 담당자 `UIPoolManager`가 `InitializeUI()` → 풀 등록 → 제어 루트 활성화 → `Initialization(UIData)` → `Show()` 순서로 처리한다.
- `ContentsPooledUI.InitializeUI()`는 비활성 상태에서도 호출 가능하며, 완료된 인스턴스에서는 다시 실행하지 않는다. `Awake`는 직접 생성·배치한 제어 루트의 준비만 보장하고 풀에는 등록하지 않는다.
- 루트는 자신과 Contents 아래의 `IUIInitializable` 구현을 초기화한다. 비활성 GameObject와 비활성 컴포넌트도 포함한다. 중첩된 Contents UI는 해당 제어 루트가 하위 초기화를 맡는다.
- 각 `IUIInitializable.InitializeUI()`도 중복 호출에 안전해야 한다. 동적으로 생성한 행은 소유자가 `UIInitialization.InitializeHierarchy(row.gameObject)`로 준비할 수 있다. 현재 미션·패스 행의 `Bind`는 직접 사용 경로를 위해 자기 초기화도 보장한다.
- `OnInitializeUI()`에는 참조 캐싱·고정 버튼 연결만 넣는다. 서버 조회, 아트 적재, 목록 구성, 화면 이벤트 구독은 넣지 않는다.
- `Initialization(UIData)`는 매 개봉의 데이터 전달이다. 인스턴스 일회 초기화와 구분한다.

## 표시 수명

- `SetContentsVisible(true)`에서 `isShow`를 올리고 `OnViewShown()`을 한 번 호출한다. 표시 구독은 이 훅에서 건다.
- `SetContentsVisible(false)`에서 즉시 `isShow`를 내리고 `OnViewHidden()`에서 구독을 해제한다. Contents 비활성화는 기존 퇴장 연출 완료 후 수행한다.
- 같은 표시 상태를 반복 요청해도 구독과 연출을 중복 시작하지 않는다.
- 외부에서 제어 루트나 상위가 꺼지거나 파괴되면 구독·딤·전환을 정리한다. 다시 켜져도 화면은 자동으로 열리지 않는다.
- 제어 루트의 `Update`는 숨김 중에도 실행되므로 `isShow`로 표시 작업을 제한한다.
- Contents 아래의 튜토리얼 앵커·레이아웃·애니메이션은 표시 활성화 시 동작을 유지한다. 비활성 초기화가 이들을 표시 중으로 등록해서는 안 된다.
- 비동기 팝업 요청은 제어 루트 활성 여부 외에 `isShow`와 `VisibilityVersion`도 확인한다. 닫았다 다시 연 뒤 이전 요청이 화면을 열지 않게 한다.
- 보상 수령처럼 숨긴 후에도 결과를 처리해야 하는 기존 서버 요청은 표시 구독과 별도로 유지한다.

## 1차 적용 범위

- 일일·주간 미션: `MissionPanel` / `MissionOverlay.prefab`
- 가이드 미션: `GuideMissionPanel` / `GuideMissionOverlay.prefab`
- 배틀패스: `PassPanel` / `PassOverlay.prefab`
- 비활성 준비 지원: `MissionRowView`, `PassLevelRowView`, `SafeAreaFitter`

1차 시점에는 기존 `PooledUIBase` 화면의 Awake 등록을 유지했다. 씬에 배치된 `SimpleYNPopup`, `hostEmbedded` 덱 편집과 로비의 나머지 화면은 아래 6차에서 이관했다. 전투 전용 HUD 전체를 이관한 것은 아니다.

## 2차 적용 범위

- 시즌 랭킹: `RankingBoardPanel` / `RankingOverlay.prefab`
- 랭크 보상: `RankRewardPanel` / `RankRewardOverlay.prefab`
- 키워드 강화: `KeywordGrowthPanel` / `KeywordGrowthOverlay.prefab`
- 비활성 준비 지원: `RankingRowView`, `RankRewardRowView`, `KeywordGrowthCellView`

랭킹 조회는 숨김 시 요청 버전을 무효화한다. 랭크 보상은 정상 닫기에서 퇴장 연출 후 상단바를 내리고 외부 비활성화·파괴에서는 즉시 회수한다. 카드 보상 수령 후 공용 보상 팝업이 계속 재생하는 흐름은 유지한다.

키워드 강화는 숨김 시 구독과 튜토리얼 앵커·강화 연출을 정리한다. 진행 중인 서버 요청의 중복 강화 잠금은 응답까지 유지하며, 이전 표시 수명의 응답이 재개방한 화면에서 성공 연출을 재생하지 않도록 `VisibilityVersion`을 검사한다. 앵커 등록은 Contents를 켠 뒤 수행한다.

기존 프리팹의 패널·버튼·행 참조와 연출·딤 저작값은 유지한다. `SafeAreaInstaller`의 두 프리팹 경로도 `Contents`로 변경한다.

## 3차 적용 범위

- 프로필 편집: `ProfileEditPanel` / `ProfileEditPanel.prefab`
- 룰렛: `RoulettePanel` / `RouletteOverlay.prefab`
- 비활성 준비 지원: `ProfileItemCell`, `EmoteItemCell`, `RouletteBulbRing`, `TabButtonView`

프로필은 기존 표시 루트를 Contents로 이름을 바꾸고 초기 비활성화한다. 고정 버튼·닉네임 입력·드래그 참조는 한 번 준비한다. 정상 닫기는 세션 저장 후 호출 화면으로 복귀하며, 외부 숨김은 저장·키보드·드래그만 정리한다. SafeArea 설치 도구도 변경한 경로를 사용한다.

룰렛은 기존 두 자식을 감싸는 스트레치 Contents를 추가하고 기존 CanvasGroup을 옮긴다. 패널·버튼·보상칸의 참조, 좌표, 딤과 전환 저작값은 보존한다. 재화 구독은 표시 중에만 유지하며 숨김에서 회전 취소와 전구 연출을 정리한다. 정상 닫기 후 상단바 지연 복원과 카드팩 결과의 별도 보상 연출은 유지한다.

## 4차 적용 범위

- 카드팩 확률: `PackOddsPopup` / `PackOddsPopup.prefab`
- 키워드·시너지 설명: `ExplainPopupUI` / `ExplainPopup.prefab`
- 모험 도전 확인: `AdventureNodePopup` / `AdventureNodePopup.prefab`

세 프리팹은 기존 Contents 참조와 배치를 유지하고 최초 표시만 비활성으로 변경한다. `UsePopupTransition`과 `UseScreenDim`은 기본값 true이며, 기존 이관 화면의 동작을 유지한다. 카드팩 확률·설명은 두 옵션을 모두 끄고 기존 즉시 개폐를 사용한다. 모험 확인은 공용 딤만 끄고 기존 프리팹의 딤과 전환을 사용한다.

카드팩 확률은 초기화에서 표시를 중복 호출하지 않고 풀의 Show 단계에서 연다. 모험 확인은 고정 버튼을 한 번 연결하되 기존 Show/Hide의 UIData 콜백 및 Hide 이후 전투 콜백 순서를 유지한다. 생성 도구도 루트를 비활성 상태에서 배선한 뒤 켠다.

카드 상세에서 행으로 쓰는 `UI/KeywordExplain.prefab`은 `KeywordExplainItem`이 표시를 담당한다. BG에 붙어 있던 미배선 `ExplainPopupUI`만 제거해 잘못된 풀 등록·초기화를 방지한다. 행의 텍스트·아이콘 참조는 유지한다.

## 5차 적용 범위

- 로비 계정·설정: `LobbySettingPanel` / `LobbySetting.prefab`
- 서버 응답 대기: `ServerWaitOverlay` / `ServerWaitOverlay.prefab`
- 비활성 준비 지원: `LobbyProfileButton`, `AccountServiceCellView`, `SettingsOptionsView`, `SelectionStateView`

두 화면은 공용 딤·전환을 사용하지 않고 기존 즉시 개폐를 유지한다. 로비 설정은 기존 Contents를 최초 비활성화하고, 대기 프리팹은 이미 같은 구조라 저작값을 바꾸지 않는다. 계정 상태 구독은 로비 설정이 표시된 동안만 유지한다. 프로필 편집 후 복귀와 로그아웃 실패 시 복귀 경로는 유지한다. 설정 초기화는 입력을 연결하며 설정값을 저장하거나 인증을 시작하지 않는다.

대기 화면은 초기화 후 owner를 더하고 Show에서 목록을 유지한다. 입력은 즉시 차단하고 딤·스피너는 기존 임계 뒤에 표시한다. 마지막 owner가 Release될 때만 닫힌다. Hide·외부 비활성화·파괴에서 owner·지연 세대·페이드·스피너를 명시적으로 정리하며, 기존 Show/Hide 콜백은 유지한다.

## 6차 적용 범위

- 로비 탭 공통 및 전투·덱·도감·팩·상점 탭: `LobbyTabPanel`, `LobbyTabController`와 파생 탭. 슬라이드 위치는 제어 루트가 유지하고 Contents만 개폐한다. `PackShowcaseController`는 탭의 표시 이벤트로 구독을 관리한다.
- 덱 편집: `DeckEditController`를 ContentsPooledUI로 이관한다. 로비 내장 `hostEmbedded`는 풀에 등록하지 않으며, 숨김에서 편집 사본·드래그·구독을 정리한다.
- 카드 상세·강화 결과·도감 페이지·모험 맵: `CardDetailOverlayView`, `EnhanceResultPanelView`, `AlbumPageOverlayView`, `AdventureMapOverlayView`. 강화·진화 연출과 섹션 해금 효과도 비활성 상태에서 참조를 준비한다.
- 보상·승급·해금 소개: `CardRewardOverlay`, `CardSetRewardOverlay`, `PackRewardOverlay`, `RewardClaimPopup`, `RankPromoteOverlay`, `UnlockIntroOverlay`, `ContentUnlockIntroView`.
- 남은 풀 화면: `SimpleYNPopup`, `PooledCardElement`, `MissionCutInView`, `SettingsPanel`. StartScene의 직접 배치 확인 팝업은 `ScenePooledUIRegistrar`가 명시적으로 초기화·등록한다. 카드 설명의 잔여 딤 페이드와 설정의 닫는 중 재열기 처리를 유지한다.
- 카드팩 개봉·매칭·출전 덱 선택: `PackOpenOverlay`, `MatchmakingShell`, `MatchDeckShell`. 개봉 브레인·뷰와 덱 선택 표시도 명시적으로 초기화한다. 매칭 정상 핸드오프는 다음 덱의 등장까지 공유 시퀀스를 유지하고, 일반 숨김은 토큰·트윈·스캔을 정리한다.

비풀 화면은 `ContentsUIBehaviour`를 사용한다. 직렬화된 `viewContents`는 직계 Contents를 가리키며 초기 비활성이다. 초기화는 멱등이고 `IUIInitializationRoot` 경계에서 각 프리팹 소유자가 하위 초기화를 맡는다. 기존 풀 화면의 즉시 숨김 훅과 달리 비풀 화면의 `OnViewShown`/`OnViewHidden`은 `ContentsVisibilityRelay`를 통해 실제 활성화·퇴장 완료 시점에 호출된다. 기존 닫힘 콜백과 튜토리얼 진행 순서를 보존하기 위한 차이다. 즉시 취소해야 하는 요청은 각 화면의 Close/Hide에서 처리한다.

`IsViewVisible`은 Contents의 실제 계층 활성 상태이므로 퇴장 연출 중에도 true다. `VisibilityVersion`은 개폐 요청이 바뀔 때 증가하며 지연 팝업 요청이 이전 표시 세션을 재사용하지 못하게 한다. 피벗·앵커·크기 등 기존 자식 배치는 보존하며, 상세 화면의 배경 Graphic과 스와이프 입력, 페이드용 CanvasGroup은 Contents로 옮긴다. 새 래퍼는 RectTransform 전체 stretch 또는 원래 3D Transform의 단위 변환을 사용한다.

검증: 기존 RectTransform 459개의 기하값 보존(탭·덱 188개, 상세·도감·모험·보상 271개), 래퍼의 직계 관계·컴포넌트 참조·초기 활성 상태를 확인했다. Unity에서 36개 프리팹의 실제 비활성 초기화 및 재호출과 공통 개폐 수명 검증 1개 묶음이 통과했다. 수명 검사는 편집 모드에서 relay 콜백을 명시적으로 호출한다. 실제 플레이의 서버 왕복·전투 진입·매칭은 이 격리 검증에 포함하지 않았다. 검증 중 작업 씬의 활성·dirty 상태를 보존했다.

## SafeArea 편집 미리보기

`SafeAreaFitter`는 ExecuteAlways 미리보기를 유지한다. 자동 계산하는 래퍼의 Anchors, AnchoredPosition, SizeDelta만 `DrivenRectTransformTracker`로 관리해 씬 저장에서 제외한다. offsetMin/Max 변경은 AnchoredPosition과 SizeDelta에 반영되므로 함께 등록한다. 피벗·회전·스케일과 하위 UI의 배치는 계속 편집·저장할 수 있다.

비활성화 때 tracker와 예약된 에디터 갱신을 해제하고, 다시 켜면 등록한다. 조상 SafeArea 때문에 전체 stretch로 유지하는 중첩 래퍼도 같은 계약을 따른다. 이미 저장된 오버라이드나 설치 도구의 계층 변경은 이 수정으로 자동 삭제하지 않는다.

검증: Unity 컴파일 및 비활성 초기화·안전영역 계산·반복 활성화·중첩 래퍼·자식 편집·비활성 상태의 지연 갱신 10개 검사를 통과했다. 임시 씬과 중첩 프리팹에서 tracker가 소유한 미리보기 영역 값을 변경해 저장 전후 YAML이 같음을 확인했다(프리팹 사본 이름만 정규화). 자식 위치 수정의 저장과 프리팹 재생성 후 tracker 등록도 확인했다. 기존 작업 씬은 저장하지 않았고 임시 검증 에셋은 제거했다.

## 이관 검증

비활성 초기화와 재호출, 자식 강제 활성화 없음, 첫 표시, 중복 Show/Hide, 퇴장 중 재개방, 외부 비활성화·파괴, 닫힌 뒤 지연 요청, 초기화 실패 후 인스턴스 회수, 기존 프리팹 참조·레이아웃 보존을 확인한다.

2차 검증: Unity 컴파일과 실제 세 프리팹의 초기화·개폐·정리 37개 검사, 비활성 행 및 버튼 초기화 4개 검사를 통과했다. 프리팹 비교에서 Contents 연결·이름·초기 비활성 외 직렬화 변경이 없음을 확인했다. 서버 왕복과 실제 플레이 연출은 이번 격리 검증에 포함하지 않았다.

3차 검증: Unity 컴파일, 실제 두 화면의 비활성 초기화·반복 개폐·퇴장 중 재개방·외부 정리 및 전구 원본 보존 31개 검사와 연결된 프로필 셀 4개 검사를 통과했다. 프리팹의 기존 좌표·이벤트 참조·연출값 보존을 비교 확인했다. 실제 프로필 저장과 룰렛 서버 왕복은 격리 검증에 포함하지 않았다.

4차 검증: Unity 컴파일 및 기존 이관 화면을 포함한 11개 프리팹의 초기화·반복 개폐·즉시 닫기/퇴장 연출·외부 정리 89개 검사, 팝업 콜백·닫기 버튼·설명 행 참조·아이콘 옆 배치 8개 검사를 통과했다. 서버 조회와 실제 모험 전투 진입은 실행하지 않았다.

5차 검증: 코드·호출부·프리팹 참조 검토와 diff 검사를 통과했다. Unity가 생성한 Assembly-CSharp에서 두 패널의 ContentsPooledUI 상속과 하위 세 컴포넌트의 IUIInitializable 구현을 확인했다. 이후 보완한 SelectionStateView는 같은 Unity 참조를 사용하는 별도 Roslyn 컴파일을 통과했다. Unity 제어 도구가 300초 시간 초과되어 실제 화면 개폐·대기 owner 중첩·지연 표시 실행 검증은 미완료다. 실제 로그아웃·서버 요청은 실행하지 않았다.
