# Firestore 세이브 이관 로드맵

> 최종 갱신: 2026-08-27 · 브랜치 `feature_Firestore`
> 이 문서는 **진행 상태 추적**용이다. 설계 근거와 구현 세부는 각 Phase 담당이 갖는다.
>
> ⚠️ **P4 이후는 [SERVER_VALIDATION_ROADMAP.md](SERVER_VALIDATION_ROADMAP.md) 로 이관됐다.**
> 요금제가 Blaze로 바뀌며 Cloud Functions가 열려, 판정을 서버로 옮기고 로컬 캐시를 걷어내는 쪽으로
> 방향이 바뀌었다. 이 문서는 P0~P3 기록으로 남는다.

## 목표

아웃게임 세이브의 진실원을 로컬 JSON 파일에서 **Firestore 문서**로 옮긴다.
끝나면 Firebase 콘솔에서 유저의 재화·덱·성장 상태를 눈으로 읽고 손으로 고칠 수 있다.

## 확정된 전제

| 항목 | 결정 |
|---|---|
| 문서 구조 | 도메인별 필드 맵. `payload` 통짜 문자열 폐기 |
| 진실원 | 클라우드. 로컬 파일은 캐시로 강등 |
| 오프라인 | **불가**. 초기화 시 Firestore 읽기 실패 = 게임 진입 불가 |
| 기존 v6 세이브 | 리셋. 마이그레이션 코드 없음 |
| 스키마 버전 | `UserSaveData.VERSION = 7` |

---

## Phase 현황

| Phase | 상태 | 내용 |
|---|---|---|
| **P0** 확증 | ✅ 완료 | 매퍼 계약 확인, 콘솔 준비 |
| **P1** 도메인 정리 | 🟡 코드 완료 · 검증 대기 | 레거시 제거, 프로퍼티 전환, 직렬화기 교체 |
| **P2** 필드맵 전환 | 🟡 코드 완료 · 잔가지 6건 | 클라우드 계층 재작성 |
| **P3** 실패 UX | 🟡 코드 완료 · 검증 대기 | 재시도 화면, 업로드 배너 |
| **P4** 규칙·운영 | ➡️ 이관 | [SERVER_VALIDATION_ROADMAP.md](SERVER_VALIDATION_ROADMAP.md) R1/R3/R9 로 흡수 |

---

### P0 — 확증 ✅

- [x] `[FirestoreProperty]` 의 `AttributeTargets` 확인 → **`128` = Property 전용**. 필드에는 못 붙는다
- [x] `[FirestoreData(UnknownPropertyHandling)]` · `[FirestoreProperty("name")]` 생성자 존재 확인
  → C#은 PascalCase 프로퍼티 규약을 지키면서 Firestore 필드명은 camelCase로 고정 가능
- [x] `FieldValue.Increment` / `ServerTimestamp` / `ArrayUnion` / `Delete` 존재 확인
- [x] `UnknownPropertyHandling` = `Ignore` / `Warn` / `Throw`
- [x] Newtonsoft 3.2.2 설치 확인 · `Assets/CookApps/link.xml` 존재 확인 · Android = IL2CPP 확인
- [x] 콘솔: 익명 로그인 활성화
- [x] 콘솔: Firestore 데이터베이스 생성
- [ ] iOS `GoogleService-Info.plist` — **미처리**. iOS 빌드 계획이 생기면 그때 (없으면 iOS에서 Firebase 초기화 실패)

**P0의 최대 발견**: 매퍼가 프로퍼티 전용이라 도메인 클래스를 auto-property로 전환해야 하고,
`JsonUtility` 는 프로퍼티를 직렬화하지 못하므로(조용히 `{}` 반환) **직렬화기 교체가 같은 커밋에 묶여야 한다**.

---

### P1 — 도메인 정리 🟡

- [x] 도메인 11개 클래스: 필드 → PascalCase auto-property + `[FirestoreData]` / `[FirestoreProperty]`
- [x] 레거시 필드 제거 + **그 필드를 쓰던 이관 코드까지** 제거
- [x] `currency` / `cardGrowth` / `keywordGrowth` 를 인덱스 배열에서 **이름 키 맵**으로 전환
- [x] `UserSaveData.VERSION = 7`, 도메인별 `version` 필드·상수 폐기
- [x] `JsonUtility` → `Newtonsoft.Json` (`DataSaveManager` 5곳 + `PlayerSaveSync` 1곳)
- [x] 호출처 27개 파일 갱신
- [x] 컴파일 에러 0 확인 (Assembly-CSharp.dll 갱신 확인 · Editor.log CS 에러 없음)
- [x] 기능 지도 동기화 (`sync-agents-map.js`)
- [x] `tcg-reviewer` 검수 — critical 1건(camelCase 인코딩 불일치) 해결, 나머지는 P2 이월
- [ ] 런타임 검증 (아래 완료 판정)

**이번 단계에서 건드리지 않는 것**: 클라우드 전송 경로는 기존 payload 방식 유지(직렬화기만 교체),
`IRepository` 시그니처, `DataSaveManager.Save()` 시그니처.

제거 대상 레거시 (필드 + 딸린 코드):

| 필드 | 딸린 코드 |
|---|---|
| `CurrencySaveData.gold/diamond/energy`, `version` | `Normalize()` 흡수 분기 |
| `OwnershipSaveData.ownedCardKeys`, `defaultsGranted` | `OwnershipManager` 흡수 블록 |
| `DeckSlotSaveData.cardKeys` | `DeckSaveManager.MigrateLegacyCardKeys` 외 |
| 덱 레거시 외부 파일 흡수 | `TryMigrateLegacyFile` / `LegacyFile` / `LegacySlot` 전체 |
| `RankSaveData.claimedCount` | `RankRewardManager.MigrateClaimedCount` |
| `TutorialSaveData.outgameStepIndex`, `migrationChecked` | `MigrateLegacyCompletion` |
| `CardGrowthEntry.cardKey` | 관련 참조 |

**완료 판정**: 세이브 폴더를 비우고 부팅 → 튜토리얼 → 재화 획득 → 덱 편집 → 강화 → 종료 → 재부팅.
잔액·덱·성장 단계가 전부 살아 있어야 한다.

---

### P2 — 필드맵 전환 🟡

- [x] `PlayerSaveCloud` 신설 — 비동기 부트 로드, `AdoptRemote`, 디바운스 업로드, 캐시 봉투
- [x] `DataSaveManager` 재설계 — `Load()` 제거, 원격 채택 경로로 교체
- [x] `GameManager.Boot` 수정 — 동기 `Load()` 와 `IsSaveBlocked` 분기 삭제
- [x] 구 `PlayerSaveSync`(896줄) 삭제, 살릴 관용구만 이식
- [x] `link.xml` 에 세이브 타입 보존 항목 추가 (`Assets/Scripts/OutGame/Save/link.xml`)
- [x] `DataSaveManager.s_saveBlocked` / `IsSaveBlocked` 죽은 경로 정리 — 코드에 없다
- [x] **초기 골드 재지급 경로** — "빈 맵 = 신규" 센티널을 `PlayerSaveCloud.IsFreshAccount` 플래그로 교체.
      `InitializationInstaller.InstallSaveDependent` 가 이 플래그만 보고 `CurrencyManager.Init(_freshAccount)` 를 부른다
- [x] 캐시 봉투에서 미사용 `PendingUpload` 제거 — 미업로드 판정은
      `캐시revision == 원격revision && 스냅샷 불일치` 로 한다(`PlayerSaveCloud.LoadCoreAsync`)

**남은 잔가지 6건** (실측 확인 2026-08-26, P3 범위 밖이라 미처리):

- [ ] 데드코드 `CardCatalog.LegacyIdOfName` / `s_legacyNameToId` — 호출자 0건
- [ ] 데드코드 `OwnershipManager.HasAnyOwnedSaved()` — 호출자 0건
- [ ] **KeywordGrowth 키 규약 통일** — Currency는 enum 이름(`"Gold"`), KeywordGrowth는 여전히 정수 문자열
      (`KeywordGrowthManager.SyncSaveData` 가 `((int)key).ToString()`). 콘솔 가독성이 목적이었으므로 이름 쪽으로 맞출 것
- [ ] `TryApplyRemote` 자기검증 재검토 — Dictionary 직렬화 순서 의존. 불안정하면 역직렬화 후 필드 비교로 교체
- [ ] **중첩 컬렉션 null 정규화** — 콘솔에서 사람이 `null` 을 넣으면 `TournamentProgress.ClearedNodeIds` ·
      `AlbumRewardManager.ClaimedKeys` · `RankRewardManager.ClaimedTiers` 에서 NRE
- [ ] 구 세이브 파싱 실패 시 `SAVE_KEY` 원본이 남아 첫 `Save()` 전까지 매 부트 LogError 반복

**완료 판정 (미검증)**:
콘솔에서 `currency.Gold` 를 손으로 고친다(타입 integer 유지) → 앱 재시작 → 로비 상단바에 그 값이 뜬다.

---

### P3 — 실패 UX 🟡

- [x] `4.Sync` 잔재 삭제 — 코드에 없다. P2 커밋에서 `4.Cloud` 로 재편되며 함께 사라졌다
- [x] `RecoveryRequired` 재시도 버튼 — **인플레이스 재시도**(씬 재로드 없음).
      2026-08-27 한 번 폐기됐다가 같은 날 모바일 표준 패턴으로 되살렸다(아래)
- [x] 업로드 실패 배너 (3회 연속 실패부터)
- [x] 차단 세션(`Blocked`) 재시작 요구 모달 — 오프라인 배너와 표면 분리
- [x] pause/quit flush 재검증 — **실기능 버그 1건 발견·수정**(아래)
- [x] 부트 타임아웃 대소관계 정리 — `LoadingCoverView.initializeWaitTimeout = 20f` 가
      최악 10초(auth 5s + 읽기 5s)를 덮는다
- [ ] 런타임 검증 (아래 완료 판정)

**부트 실패 — 대기 없이 재시도/종료** (2026-08-27, 같은 날의 "안내 + 종료" 계약을 대체)

모바일 표준 패턴이다 — **짧은 타임아웃으로 실패를 빨리 확정하고, 곧장 2버튼 패널을 띄우고,
재시도는 실패한 단계부터 재개한다.** 로컬 캐시 폴백은 여전히 없다(진실원은 Firestore 문서 하나).

*실패 확정까지의 예산*

| | 이전 | 지금 |
|---|---|---|
| 망 끊김이 확실할 때 | 21초 | **0초** — `LoadCoreAsync` 가 `Application.internetReachability` 로 선체크해 auth를 시도하지 않는다 |
| 그 외 | 최악 21초 | 최악 **10초** — auth 5초 + 읽기 5초 |

읽기 자동 재시도(`READ_ATTEMPT_COUNT` 3회 + 500ms 백오프)를 걷었다. **재시도의 주체는 사람**이다 —
`ReadWithRetryAsync` 는 단일 시도 `ReadAsync` 로, `IsRetryableRead` 는 함께 삭제됐다.
`InitializationInstaller.CoRunGate` 의 애셋 대기 루프에는 `CardArtCache.HasFailed` 탈출이 없어
Addressables가 완료도 실패도 못 하면 무한 대기였다 — 탈출 조건을 채웠다.

*재시도 경로 (씬 재로드도 Firebase 재초기화도 없다)*

`Fail()` 이 `Shutdown()` 을 부르지 않아 `s_context` · `s_initialized` 가 살아 있고,
`GameInitialization.WhenReady` 구독자가 코드 전체에 0건이며, 부트 게이트 소비자가 `CoRunGate` ·
`CoRunInitialize` 둘뿐이라 **재시도 범위를 실패한 단계로만 좁힐 수 있었다**. 8월 27일 오전에 걷어낸
`GameManager.RetryInitialize` · `FirebaseManager.Reinitialize` 는 되살리지 않았다.

순서 계약의 주인은 **부트 소유자인 `InitializationInstaller.RestartBoot()`** 하나다.
`LoadingCoverView.Retry()` 는 화면만 로딩 상태로 되돌리고 이걸 부른 뒤 `CoRunInitialize` 를 다시 건다.

1. 실패한 캐시만 되돌리기 — `CardArtCache.ResetIfFailed` / `UiPrefabCache.ResetIfFailed`
   (성공한 쪽까지 Release하면 재적재 비용만 확정으로 늘어난다. 되돌리기만 하고 다시 걸지는 않는다)
2. `GameInitialization.ResetForRetry()` — `RecoveryRequired` → `SyncingSave`
3. `PlayerSaveCloud.ResetForRetry()` — `s_generation++` · `s_gateComplete=false` 후 `LoadAsync` 재기동.
   `Initialize` 를 다시 부르지 않는다 — 훅·구독이 살아 있어 재배선하면 이중으로 걸린다
4. 게이트 재기동 — 게이트 첫 줄의 `StartAssetLoads()` 가 재적재까지 함께 건다

2가 4보다 **먼저**여야 한다 — 순서가 뒤집히면 다시 건 게이트가 `IsTerminated` 를 보고 그 자리에서 끝난다.

*재시도 전용 코드 경로를 두지 않는다* (2026-08-27 리팩터)

애셋 선로드를 `Awake` 에서 게이트 첫 줄(`InitializationInstaller.StartAssetLoads`)로 옮겼다.
두 캐시 모두 "이미 시작한 적재는 스스로 건너뛴다"라 게이트가 매번 무조건 걸어도 안전하고,
**게이트를 다시 걸면 재적재가 저절로 따라온다.** 재시도만을 위한 재적재 메서드(`RestartFailedAssetLoads`)와
`CardCatalog.AllSpecs` 의 이중 진실원이 함께 사라졌다. 세이브 채택 대기보다 앞줄이라 병렬성도 그대로다.

*재진입이 부수는 것을 막는 장치 2개*

- **게이트 사본 1개** — `RunGate()` 가 `m_gate` 를 들고 있다가 다시 걸기 전에 `StopCoroutine` 한다.
  둘이 되면 `InstallSaveDependent` 가 두 번 돌아 스타터 지급이 겹친다
- ~~**설치 재시도 금지**~~ — 2026-08-27 삭제. `InstallSaveDependent` 안 11개 호출이 전부 재실행 안전이라
  막을 이유가 없었다(`CurrencyManager.Init` 은 초기 골드를 **대입**, `StarterDeck.GrantIfNoDeck` 은 덱이 있으면 return,
  `RankManager.TryEnterFirstTier` 는 `IsRanked` 면 return, 되감기 예약 둘은 1회 소비). 오히려 설치 도중 예외 한 번이
  그 세션의 재시도를 영구히 막는 막다른 길이었다. 계약("여기 들어가는 모든 호출은 재실행 안전")은
  `InstallSaveDependent` 주석으로 남는다
- **`UiPrefabCache` generation 토큰** — `CardArtCache` 와 같은 모양. 이게 없으면 살아 있던 옛 `Preload`
  continuation이 **새 핸들**의 상태를 읽어 `s_failed` 를 세우고, 옛 `Register` 콜백이 비운 색인에
  Release된 프리팹을 다시 꽂는다(검수에서 잡힌 유일한 높음 등급)

*버튼 노출* — **업데이트 필요만 재시도가 없다.** 나머지는 전부 재시도를 준다.

| 실패 | 버튼 |
|---|---|
| 업데이트 필요 | 종료만 (다시 태워도 같은 답) |
| 애셋 로드 실패 | 재시도(Addressables 재적재) + 종료 |
| 서버 연결 실패 | 재시도 + 종료 |
| 초기화 대기 타임아웃(느린 적재) | 재시도 + 종료 |

마지막 줄이 재시도를 조건부로 두지 않은 이유다 — 이 경로는 `PlayerSaveCloud` 가 아직 `Loading` 이라
"재시도 가능한 실패"로 판정되지 않는데, 여기서 재시도를 감추면 느린 부트가 막다른 길이 된다.
이때 `PlayerSaveCloud.ResetForRetry()` 는 스스로 no-op이 되고 게이트 재기동만 일어난다.
판정의 주인은 `GameInitialization.CanRetry`(= `State == RecoveryRequired`) 하나이고 뷰는 묻기만 한다.

프리팹 `LoadingCover.prefab` 의 `RecoveryPanel` 에 `Button_Quit` 을 추가하고 좌우로 벌렸다
(재시도 x=-240 / 종료 x=+240). 기존 `retryButton` 직렬화 키가 그대로 재시도 버튼에 붙는다.

**표면 3분할**

| 상태 | 진입 | 표면 |
|---|---|---|
| `Failed` | `PlayerSaveCloud.Fail()` — 부트 게이트 **전** | 복구 화면 + 재시도·종료 |
| `Blocked` | `PlayerSaveCloud.BlockSession()` — 게이트 **후** | 재시작 요구 모달(`SimpleYNPopup`) 1회 |
| `Offline` | 업로드 3회 연속 실패 | 상시 배너(`CloudSyncBannerView`, 층 940) |

`BlockSession` 은 `MarkRecoveryRequired()` 를 더 이상 부르지 않는다 — 게이트 뒤라 화면을 못 바꾸면서
`IsReady` 만 false로 떨어뜨렸다. 소비자는 부트 경로 둘뿐이라 안전(전수 확인).

**이번 단계에서 함께 고친 실기능 버그 3건**

1. `BattleContentFirebaseModule.FlushPendingAsync()` 가 `throw new NotImplementedException()` 이었다.
   이 모듈이 먼저 등록돼 `FirebaseManager.FlushPendingAsync` 의 루프가 첫 원소에서 동기 예외로 죽어
   **pause/quit 클라우드 flush가 한 번도 실행된 적 없었다**. 모듈별 try/catch로 팬아웃을 격리하고 본체도 수정
2. 복구 문구(`Text_Value`)가 `Slider_LoadingBar_Green/FillArea` 자식이라, 문구를 넣은 직후 진행바를 끄면
   **함께 사라졌다**. 슬라이더 밖 `RecoveryPanel/Text_Recovery` 로 분리
3. `InstallSaveDependent()` 의 조기 return이 `MarkReady()` 까지 건너뛰어 재시도가 `Ready` 에 도달 못 했다

**flush 커버리지 감사 결과 (코드 변경 없음)**: 진짜 지연 커밋 매니저는 `CurrencyManager` 하나뿐이다
(`Earn`/`Spend` 가 `s_currencies` 미러만 갱신). 나머지 10개 매니저는 변경 시점에 `DataSaveManager.Data.*`
까지 쓴다. 잠재 취약점 2곳 — `DeckSaveManager.SetName`/`SetImageKey` 와 `CardGrowthManager.AddSnack` 는
지금은 호출부가 같은 동기 흐름에서 커밋하지만, 호출부가 늘면 pause/quit 유실 후보가 된다.

**완료 판정**: 비행기 모드 부팅 → 재시도 화면 → 네트워크 복구 후 재시도 → 정상 진입.
플레이 중 비행기 모드 → 배너 → 복귀 시 자동 업로드 → 콘솔 `revision` 증가.

---

### P4 — 규칙·운영 ➡️ 이관됨

**[SERVER_VALIDATION_ROADMAP.md](SERVER_VALIDATION_ROADMAP.md) 로 통째로 넘어갔다.** 대응표:

| 원래 항목 | 간 곳 |
|---|---|
| `firestore.rules.prod` 재작성 · 개방 규칙 제거 | R1 (뼈대) · R5~R9 (슬롯 동결) |
| `revision + 1` rules 에뮬레이터 실증 | R1 회귀 3종 |
| 스펙 업로더 권한 대안 (미결 #2) | R3(a) — 관리자 callable + custom claim 으로 확정 |
| Android IL2CPP 실기 1회 | R9 |
| `STRUCTURE.md` 세이브 절 갱신 | R9 |
| 신입 개발자용 `FIRESTORE_SAVE.md` | R9 (범위가 서버까지 넓어져 재작성) |

`.prod` 파일 자체는 P2 이전의 `payload` 통짜 스키마라 이미 실효를 잃었다 — R1에서 폐기하고 새로 쓴다.

---

## 미결 결정

| # | 항목 | 언제까지 | 선택지 |
|---|---|---|---|
| 1 | **저장 API 비동기 축** | P2 착수 전 | (a) `Save()` void 유지 + `FlushAsync()` 추가 (b) 전면 `SaveAsync` 전환 (c) (a) + `IRepository` 까지 비동기화. `Save()` 와 `IRepository` 시그니처는 한 덩어리 결정이다 |
| 2 | ~~**스펙 업로더 권한**~~ ➡️ R3(a)로 이관 · 관리자 callable(Admin SDK) + custom claim 으로 확정 | — | `Editor/SpecFirestoreUploader` 가 API key로 `specs/**` 에 쓴다. 운영 규칙은 이 경로를 차단하므로 배포 즉시 죽는다. (a) 툴 전용 계정 uid에만 쓰기 허용 (b) 서비스 계정 + Admin SDK CLI로 이전 |
| 3 | **계정 연동** | 별도 백로그 | 익명 uid 분실 = 세이브 분실. 클라우드가 진실원이 된 이상 치명도가 올라간다 |
| 4 | **부분 업데이트** | 실측 후 | 지금은 매 저장이 전체 문서 재작성. 도입하려면 도메인별 dirty 추적이 필요한데 타입 B 매니저가 `Data.*` 를 직접 변형해 신호 지점이 없다 |

---

## 스키마 요약

경로: `envs/{envId}/users/{uid}/save/current`

**최상위 메타**: `schemaVersion`(int64) · `revision`(int64, 1부터 단조 증가) · `updatedAt`(timestamp) · `deviceId`(string32) · `appVersion`(string)

**도메인 맵 10종**

| 맵 | 내용 |
|---|---|
| `currency` | `balances` : map — 키가 `ECurrencyType` 이름(`Gold`/`Diamond`/`Energy`/`Shard`) |
| `ownership` | `cardIds` : array\<int64\> |
| `deck` | `slots` : array\<map\> — 원소 `{ name, cardIds, imageKey }` |
| `cardGrowth` | `entries` : map — 키 = cardId 문자열, 값 `{ level, snack, limitBreak }` |
| `keywordGrowth` | `levels` : map — 키 = keyword id 문자열, 값 = level |
| `rank` | `points`, `claimedTiers` |
| `albumReward` | `claimedKeys` |
| `tournament` | `clearedNodeIds`, `claimedChapterIds`, `pendingRewardNodeId` |
| `tutorial` | `outgameCompleted`, `chapterIndex`, `chapterStepIndex`, `stepId`, `lastBoot*`, `sameCoordBootCount`, `completedTriggers` |
| `profile` | `nickname`, `avatarId`, `frameId` |

---

## 콘솔 작업 체크리스트

프로젝트 `bm-cardbattle` (커밋 `8e63e8c` 에서 이관. `.firebaserc` · `Assets/google-services.json` 과 같다)

- [x] `빌드 > Authentication > Sign-in method > 익명` 사용 설정
- [x] `빌드 > Firestore Database > 데이터베이스 만들기` (asia-northeast3 Seoul)
- [ ] 규칙 배포 — 콘솔에서 직접 편집 금지. 로컬에서 `firebase deploy --only firestore:rules`
- [ ] (선택) `색인 > 단일 필드 > 예외 추가` 로 `cardGrowth`/`ownership`/`deck` 자동 색인 해제
- [ ] (iOS 진행 시) `프로젝트 설정 > 일반 > 앱 추가 > iOS` → `GoogleService-Info.plist` → `Assets/` 직하

**내 세이브 문서 찾는 법**
`Authentication > Users` 에서 UID 복사 → `Firestore Database > 데이터` → `envs` → `test` → `users` → UID → `save` → `current`

> ⚠️ 현재 `firestore.rules` 는 전면 개방 상태다(`allow read, write: if true`). 누구나 남의 세이브를 읽고 쓸 수 있다.
> 운영 배포 전 반드시 [SERVER_VALIDATION_ROADMAP.md](SERVER_VALIDATION_ROADMAP.md) 의 R1(룰 1차 배포)을 끝낼 것.

---

## 위험 요소

1. **콘솔 수동 편집이 타입을 깨뜨린다** — 숫자 편집 시 타입 드롭다운을 `number`(double)로 두면 int64가 아니라 double로 저장되어 역직렬화가 실패한다. 도메인 단위 try/catch로 깨진 도메인만 기본값 복구하도록 방어한다
2. **익명 uid 분실 = 세이브 분실** — 앱 삭제/키체인 초기화로 uid가 바뀌면 기존 문서가 고아가 된다
3. **초기화 지연이 곧 진입 지연** — 오프라인 폴백이 없어 느린 네트워크가 그대로 노출된다
4. **매 저장 = 전체 문서 재작성** — Firestore 과금은 쓰기 횟수 기준이라 요금엔 영향 없으나 모바일 트래픽에는 영향. 3초 디바운스가 최악을 초당 1/3회로 묶는다
5. **`JsonUtility` 잔존 위험** — 프로퍼티 전환 후 어딘가에 `JsonUtility` 가 남아 세이브 타입을 다루면 조용히 `{}` 를 뱉는다. 컴파일러가 못 잡는다
