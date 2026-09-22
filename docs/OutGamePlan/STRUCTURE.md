# 아웃게임 구조: 온보딩 실행·복구

2026-09-16. 기존 경로에 구조 문서가 없어 이번에 추가했다. 이 문서는 이번에 변경한 온보딩 경계만 다룬다.

## 칭호 선택·장착

`OutGame/Title/TitleCatalog`는 칭호 ID·표시 이름·설명·아이콘·색을 제공한다. `ProfileConfig.titleCatalog`를 `OutgameConfigStep`이 `TitleManager`에 주입한다. 서버 지급 검증은 `docs/SpecData/Title_sheet.csv`의 `id`·`titleId`로 한다. 기존 테스트 8종만 등록하며 기본 지급은 없다. 칭호는 `CosmeticType`·`CosmeticItem`에 포함하지 않는다.

서버 profile 슬롯의 `ownedTitleIds`가 소유의 진실원이다. `functions/src/titles/titleOwnership`이 영구 소유를 지급하고 `automaticTitles`가 Title 전용 표의 eventKey·synergyId·targetCount·description을 읽어 기존 서버 통계로 판정한다. Reward·Achievement 표와 공통 `grantRewardItems`에는 칭호를 넣지 않는다. 기존 카드·팩·꾸미기 보상은 유지한다. 중복은 소유 유지, 장착은 변경하지 않는다. 결과는 꾸미기와 별도 `titles` 배열로 전달하며 기존 표시 UI를 재사용한다. 공개 인덱스에 Title 표가 없을 때만 자동 칭호 처리를 생략하고, 표 손상·조회 실패는 오류로 처리한다.

`TitleManager`는 소유 조회와 장착·해제를 담당한다. 클라이언트 직접 지급은 없고 장착 `equippedTitleId`만 업로드한다. Firestore 규칙이 소유 변경을 막고 장착을 기존 서버 소유로 검증한다. 서버 응답 채택은 소유를 갱신하며 로컬 장착 후보를 보존한다. 명시적 서버 세이브 초기화에서는 칭호 소유·장착도 초기화한다. `ServerSlotRehydrator`가 저장 없이 칭호 변경을 통지한다. 필드 부재는 빈 소유·미장착이며 표시 데이터가 없는 소유 ID도 삭제하지 않는다.

`UI/Profile/ProfileTitleTab`은 전체 칭호를 표시하고 미보유도 잠금·미리보기를 제공한다. 편집창 내부 `TitleItemCell` 템플릿을 사용한다. 선택 후보는 저장·닫기에서 확정하며 지급 통지는 후보·스크롤을 유지한 채 잠금만 갱신한다. `ProfileSummaryView`도 `TitleManager.OnChanged`를 구독한다. 기존 공통 보상 팝업은 칭호 이름·아이콘을 표시한다. 에디터 직접 지급 메뉴는 제거했고, `TitleOwnershipValidation`은 외부 저장 없이 실제 프리팹을 검사한다.

`PlayerSaveCloud`는 초기 채택 전에 `ensureTitles`로 이미 달성한 칭호를 보완하고 변경된 문서를 다시 읽는다. `OutGame/Title/TitleUnlocks`는 서버가 준 조건 문구를 세션 동안 보관하며 `ProfileTitleTab`이 표시한다. 전투 칭호는 `claimBattleExperience`에서 본인의 저장 직렬화 경로로 지급한다. `submitMatchResult`에서 상대 세이브 revision을 변경하지 않는다.

## 실행 구조

- `OutGame/Tutorial/OnboardingSession.cs`: 스텝 실행 수명, 세대별 취소, 중복 완료 방지, 효과 확인과 진행 위치 저장의 경계.
- `OutGame/Tutorial/Steps/TutorialActionMeta.cs`: 행동별 진입·완료 확인 정책. 설명은 디바운스 저장, 경제 행동·덱 확정·전투 진입·챕터 종료는 저장 확인.
- `OutGame/Tutorial/Steps/TutorialStepExecutor.cs`: 지급·구매를 기다린 뒤 연출 및 다음 진행. `OutgameTutorialRunner`는 강제/자율 커서와 미완료 대본 전투 위치를 관리한다.
- `UI/Tutorial/OutgameTutorialBridge.cs`: 실행 수명에 UI 이벤트 연결, 공통 완료 창구, 실패 재시도, 앵커 복구. UI 콜백은 현재 스텝과 실행 세대가 유효해야 한다.
- `UI/Tutorial/GuidanceCoordinator.Missions.cs`, `.Recovery.cs`: 안내의 오류·앱 이탈 복구와 화면 복원. 필수 온보딩과 별도의 수명이다.

## 저장·서버 경계

- `TutorialSaveData.execution`: 선택적 실행 기록. 기존 세이브는 필드 없이 읽는다. `battleEntryStepId`는 대본 전투 완료 전 재시작 위치다.
- `TutorialSaveData.onboardingCommand`: 스텝 ID, 명령, 거래 ID, 인자, 확정 결과, 소비 여부. 경제 명령 전 의도를 원격 저장한다.
- `OutGame/Save/4.Cloud/OnboardingCommands.cs`: 미확정 명령의 결과 조회·동일 거래 재전송·연출 재생. UI 취소가 서버 처리를 취소했다고 간주하지 않는다.
- `ServerSaveCommands`: 기존 직렬화 창구를 공유한다. 일반 응답의 `revision + 1` 계약은 유지한다.
- `PlayerSaveCloud`: 복구 전 계정·환경·세션 세대·원격 기기를 확인한다. 최신 서버 스냅샷에서 서버 소유 슬롯을 채택하고 로컬 진행·덱 편집은 보존한다. 차단된 세션은 재초기화가 필요하다.
- `functions/src/save/onboardingOperation.ts`, `saveDocument.ts`: `onboardingOperations/{txId}`에 경제 변경과 같은 트랜잭션으로 처리 기록을 남긴다. 일반 TTL 영수증과 분리한다.
- `functions/src/commands/getOnboardingOperation.ts`: 인증된 자기 계정의 불변 처리 결과와 현재 save/wallet/missions를 읽는다. 다른 인자로 거래 ID를 재사용하면 거절한다.

## UI 경계

안내 중에는 지정된 탭·뒤로가기만 허용한다. 자율 안내 시작 시 카드·재료 준비 부족은 재시도·나중에를 제공하고, 나중에는 진행을 보존한 채 매치 탭으로 돌아온다. 저장·동기화와 기타 오류는 재시도·종료를 유지한다. 경제 처리·확정 저장 중에는 입력을 차단한다. 덱 초안은 같은 프로세스·계정·슬롯에서 보존하며 앱 종료 후에는 서버에 저장한 덱을 복원한다.

가이드 프리뷰의 문구와 클릭은 `UI/Mission/GuideMissionPreviewState`를 공유하며 안내 시작·재개 여부는 `GuidanceCoordinator.GetMissionGuideAction`에서 조회한다. `GuideMissionTrackerView`는 수령 결과 채택과 별개로 보상 화면 종료까지 이전 목표를 표시하고, 로비가 다시 준비되면 다음 목표로 전환한다. 탭 이탈은 표시 대기만 취소하며 서버 수령을 취소하지 않는다. 화면에 보이는 현재 목표의 진행 알림은 프리뷰가 담당하고, 다른 목표·화면은 기존 컷인이 담당한다.

`OutGame/Tutorial/GuideMissionHintHistory`는 졸업 때만 최초 사용법 안내를 예약하며 계정별 기기 로컬 `LocalPrefs`에 보관한다. 기존 졸업 계정에는 소급 예약하지 않고, 튜토리얼 되감기에서 이력을 지운다. 서버 세이브·미션 조건은 바꾸지 않는다.

대본 전투 결과는 `TutorialConfig` 이벤트로 아웃게임에 통지한다. `TurnRunner`의 전투 판정 규칙은 바꾸지 않는다. 로비 전투 진입은 온보딩 저장 확인 이후 씬을 전환한다.

상세 원인·변경 파일·검증 범위: [온보딩 복구 개편](../ONBOARDING_RECOVERY_20260916.md).

## 배틀 복귀 준비

`LoadingCoverView`는 `BattleReturnLoadingCover`를 로비 씬 활성화 뒤에도 유지한다. `OutgameTutorialBridge.PrepareLobbyReturnAsync`로 첫 안내의 지급·진행 저장·첫 랭크 확인을 기다린 뒤 커버를 걷는다. 사용자 입력과 연출 종료는 대기 대상이 아니다. 복귀 준비 실패는 같은 커버의 재시도·종료로 처리하며 로비 씬을 다시 로드하지 않는다.

복귀 준비가 소유한 요청은 `ServerWaitOverlay`를 중복 생성하지 않는다. 자동 구매는 `PackPurchaseFlow`의 호출자 대기 소유 옵션을 사용한다. 로비가 열린 뒤 사용자 구매·강화의 대기 표시는 기존 경로를 유지한다. 미래 스텝의 지급을 앞당겨 실행하지 않는다.

## 계정 레벨 콘텐츠 해금·안내

`ContentUnlockConfig`는 콘텐츠별 계정 레벨·최고 도달 랭크·FTUE 졸업 중 조건 하나만 선택한다. `ContentUnlockDefDrawer`는 선택한 조건의 수치만 표시하고 검증·판정도 그 조건만 읽는다. 콘텐츠별 현재 조건과 수치는 `Assets/SO/ContentUnlockConfig.asset`이 소유한다. `ContentUnlockManager`가 선택한 조건과 서버 채택 상태를 기준으로 영구 해금과 소개 대기를 갱신한다. 카드 강화는 설정된 조건 또는 첫 패배로 해금한다. 첫 패배는 FTUE 졸업 여부와 무관하게 DefeatEnhancePending에 저장하며, FTUE 도중에는 기존 안전한 전투 진입점에서 강화 안내를 끼우고 졸업 후에는 로비에서 시작한다. 완료한 강화 안내는 반복하지 않는다. 신규 계정의 초기 해금은 소개를 예약하고 기존 계정의 영구 해금과 대기 이력은 보존한다.

`OutgameTutorialData.guide.guideFlows`의 `GuideMissionFlow.activation`은 미션 도달과 콘텐츠 해금을 구분한다. 콘텐츠형은 `content`, 미션형은 `missionId`를 사용한다. 미션·강화·모험·룰렛은 콘텐츠형이며 시너지 성장·전투는 기존 미션형이다. `GuidanceCoordinator`가 재개 안내를 우선한 뒤 졸업형 → 레벨형(레벨순) → 랭크형(티어순), 같은 조건값은 저작 순서로 콘텐츠 안내를 실행하고 마지막에 미션형을 처리한다. 일반 안내는 FTUE 졸업 뒤 매치 탭의 안전한 무대에서 시작하고, 첫 패배 강화만 FTUE를 잠시 중단한다.

소개 완료는 콘텐츠 `Pending`, 챕터 완료는 기존 트리거 기록, 재개 위치·대상 카드는 `GuideResume`에 보관한다. 콘텐츠형은 과거 재개 기록의 `MissionId`에 의존하지 않는다. 해금 소개가 끝나도 미완료 챕터는 계속 실행 대상이며, 첫 패배 강화 완료 후 Lv2에 도달해도 반복하지 않는다.

## 프로필 레벨업 연출

서버 응답 채택 후 ServerSaveCommands가 AccountLevelUpHandoff에 레벨업만 기록한다. 일반·온보딩 재생·복구 경로가 같은 revision 중복 제거를 사용하며, 서비스 교체 시 대기열과 재생을 초기화한다. 초기 로그인 데이터 로드는 연출을 만들지 않는다.

LobbyGainEffectDirector는 재화·카드 획득과 동시에 로비 전용 ProfileLevelUpEffect.prefab을 재생한다. 획득 시퀀스 조립 시 같은 프레임에 시작하고, 늦게 도착한 레벨업도 획득 종료를 기다리지 않는다. 팝업·튜토리얼·매치·삽입 진행 중에는 대기한다. 누적 레벨업은 최종 레벨 하나로 합친다. 기존 Playing에 포함하지만 OnAnyFinished는 발행하지 않는다. 공용 ProfileAvatarView.prefab과 사운드·보상 지급은 변경하지 않는다.

## 프로필 꾸미기 소유·지급

2026-09-22. CosmeticItem_sheet.csv는 서버 검증·기본 소유의 진실원이다. SpecFirestoreUploader가 CSV 전용 DTO로 발행하며 SpecPayloadCodec.ServerOnlyTableNames에만 포함한다. 클라이언트 필수 표·자동 생성 C#·SpecData.bytes에는 추가하지 않는다. ProfileConfig와 EmoteCatalog는 표시 자산을 제공한다.

ProfileSaveData.ownedAvatarIds / ownedFrameIds / ownedEmoteIds는 서버가 확정한다. 신규 계정은 buildFreshAccountSlots에서 기본 소유를 받고, 구 계정은 초기 세이브 채택 전에 ensureProfileCosmetics로 보완한 문서를 다시 읽는다. 명령은 기존 소유·장착·칭호·닉네임·경험치를 보존하는 합집합이며 표 미발행 시 명시 실패한다.

grantRewardItems는 Avatar·Frame·Emote를 수량 1로 지급하고 cosmetics에 종류·ID·신규 여부를 반환한다. 중복 소유는 재화로 보상하지 않는다. 소유·지갑·영수증은 기존 mutateSave 트랜잭션으로 확정한다. 계정 경험치는 지급 후 profile에 병합한다. 실제 외형 상품·구매 callable은 없으며 미래 구매는 영수증 재생 후 assertCosmeticPurchasable로 기소유 여부를 검사한다.

DataSaveManager.AdoptServerSlots는 서버 소유와 로컬 편집값을 병합한다. 일반 지급은 닉네임·장착·칭호 후보를 보존하고 명시적 devResetSave만 외형 장착을 서버값으로 초기화한다. PlayerSaveDocument는 프로필 편집 필드만 부분 업로드하며 소유·경험치를 보내지 않는다. Firestore 규칙은 소유 변조와 미소유 장착을 거절한다.

ServerSlotRehydrator → ProfileManager.NotifyOwnershipRehydrated → OnOwnershipChanged가 저장 없이 목록 갱신을 통지한다. ProfileEditPanel은 소유하고 표시 가능한 항목만 카탈로그 순서로 삽입하며 기존 셀·편집 후보·탭·스크롤과 고정 장착 6칸을 유지한다. 미지원 소유 ID는 보존하고 진단한다. RewardClaimOutcome.Cosmetics는 기존 보상 큐와 팝업으로 연결하며 신규는 골드, 중복은 무채색 표식을 쓴다.

검증: scripts/test-profile-cosmetics.ps1, Functions 외형·에뮬레이터·규칙·출석 회귀, Unity Tools/검증/프로필 꾸미기 회귀(실제 프리팹 격리 검사). 원격 표 발행·서버/규칙 배포·실기기 재접속 검증은 별도다.
