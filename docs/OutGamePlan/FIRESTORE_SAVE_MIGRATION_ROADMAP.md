# 로컬 세이브 → Firestore 이관 로드맵

> **[2026-08-26] 이 문서가 서술하는 Phase 1·2 구현은 되돌려졌다.**
> `박형석작업용` 머지에서 부트·세이브 축을 상대 설계(`GameInitialization` · `FirebaseManager` ·
> 동기 `DataSaveManager` · `envId`)로 채택하기로 결정해, `SaveTransaction` · `BootGate` ·
> `SaveSourceMode` · `FirestoreSaveRepository` 등은 트리에서 제거됐다.
> 아래 내용은 **재작업 시 참조할 설계 기록**이며 현재 코드와 일치하지 않는다.
> 구현 원본은 커밋 `525fda88b`(Phase 1) · `468f1bfa1`(Phase 2)에 남아 있다.


갱신 2026-08-26 · 브랜치 `feature_Firestore` · 현황 문서: [FIRESTORE_SAVE_MIGRATION.md](FIRESTORE_SAVE_MIGRATION.md)

## 방향

**Firestore를 저장 매체로 교체한다.** 데이터 모델은 건드리지 않는다.

`UserSaveData` JSON 통짜(1.5KB)가 세이브 단위로 남고, `IRepository` 구현체만 `JsonFileRepository` → `FirestoreSaveRepository`로 바뀐다. 문서 분해(`state/wallet`, `state/collection` …)는 **후속**이다.

```
매니저 (CurrencyManager · OwnershipManager · …)
  → DataSaveManager                    ← 여기까지 구조 무변경
    → IRepository (UniTask)            ← 시그니처만 비동기화
      → FirestoreSaveRepository        ← 신규. 진실원
      → JsonFileRepository             ← 읽기 전용 미러로 강등
```

### 이 방향을 택한 근거 — 쓰기 클리크 실측

문서 분해를 먼저 하면 **한 유저 행동이 여러 문서로 갈라진다.** 실측 결과 `currency`가 10블록 중 7개와 붙어 있는 허브다(`deck`·`tutorial`·`profile`만 비연결). **재화가 단독으로 바뀌는 경로는 디버그뿐이다.**

| 행동 | 진입점 | 함께 바뀌는 블록 |
|---|---|---|
| 팩 구매·개봉 | `CardPackOpener.cs:12` (Spend:36 / Grant:78 / Save:45) | currency + ownership + cardGrowth |
| 카드 강화 | `CardGrowthManager.cs:150` (Save:183 → CurrencyManager.Save:190) | currency + cardGrowth |
| 키워드 강화 | `KeywordGrowthManager.cs:113` | currency + keywordGrowth |
| 앨범 보상 수령 | `AlbumRewardManager.cs:75` | currency + albumReward |
| 랭크 보상 수령 | `RankRewardManager.cs:76` | currency + rank |
| 토너 정점·챕터 보상 | `TournamentProgress.cs:207` · `:261` | currency + tournament |
| 전투 결과 정산 | `TurnRunner.cs:395` → `RewardService.cs:40`, `:403` → `RankManager.cs:172` | currency + rank |
| 튜토 덱 지급 | `TutorialStepExecutor.cs:176` | ownership + deck (+별건 tutorial) |
| 스타터 덱 지급 | `StarterDeck.cs:10` | ownership + deck |
| 덱 편집 저장 | `DeckSaveManager.cs:252` · `:272` | deck 단독 |
| 튜토 좌표 커밋 | `OutgameTutorialProgress.cs:44` | tutorial 단독 |
| 프로필 편집 저장 | `ProfileManager.cs:112` | profile 단독 |
| 되감기 와이프 | `OutgameTutorialRewind.cs:67` | 10블록 전부 |

3-집합은 둘: `{currency, ownership, cardGrowth}`(팩), `{ownership, deck, tutorial}`(튜토 지급).

통짜 문서 1개면 **쓰기 1회 = 항상 원자적**이라 이 문제가 발생하지 않는다.

### 분해를 포기해서 잃는 것

1. **보안 규칙이 내용을 검증할 수 없다.** blob이라 잔액 음수 금지·낙인 append-only를 못 건다. 단 Spark 구간에는 클라가 문서를 직접 쓰므로 분해하더라도 `balances.Gold` 직접 증액을 막을 방법이 없다 — 낙인만 막는 실익이 낮다.
2. **쓰기 과금이 문서 단위 전체 재기록이다.** 1.5KB / Spark 2만 쓰기·일 기준 무의미.
3. **두 기기 동시 플레이가 blob 통째 last-write-wins다.** `rev`로 늦은 쪽을 거절하되, 거절된 변경은 병합 없이 버려진다.
4. **마스터가 올라오면 결국 분해해야 한다.** 미루는 것이지 없애는 것이 아니다.

## 확정된 전제

| 항목 | 결정 |
|---|---|
| 저장 단위 | **통짜 payload 유지.** 문서 분해는 후속 |
| 진실원 | **Firestore.** 로컬 JSON 파일은 읽기 전용 미러 |
| 환경 분리 | Firebase 프로젝트 2개 — `cardbattle-dev`(Test) / `cardbattle-d94f7`(Live) |
| 결제 플랜 | 현재 Spark(무료) — Cloud Functions 사용 불가. **Blaze 전환 예정, 시점 미정.** Phase 0~3은 Spark 전제 그대로 진행 |
| 읽기 방식 | **부팅 시 1회 read.** 실시간 리스너 없음 |
| 쓰기 방식 | `IRepository`를 **UniTask 시그니처로 변경**, 저장 시점에 `await` |
| 기존 세이브 | 출시 전 전원 리셋 — 마이그레이션 코드 안 만듦 |
| 마스터 테이블 | 후속 |

리스너를 안 쓰므로 매니저 static 캐시 제거·재-Init 경로·`HasPendingWrites` 처리가 전부 불필요하다. 캐시는 부팅 시 한 번 채워지고 그 뒤 서버가 값을 밀어넣지 않는다.

## 인터페이스

Phase 1에서 실제로 확정된 형태다.

```csharp
/// <summary>세이브 저장 매체 추상화(키-값). 매체가 네트워크일 수 있어 전 메서드가 UniTask다.</summary>
public interface IRepository
{
    UniTask<bool> HasAsync(string _key);
    UniTask<string> LoadAsync(string _key);
    UniTask<ESaveWriteResult> SaveAsync(string _key, string _value);
    UniTask DeleteAsync(string _key);
}

/// <summary>기록을 원자 교체한다(교체 전 값은 백업 키로 남는다).</summary>
public interface IAtomicRepository : IRepository
{
    UniTask<ESaveWriteResult> ReplaceWithBackupAsync(string _key, string _value, string _backupKey);
}

public enum ESaveWriteResult
{
    Success,
    Blocked,      // 로드된 세이브가 상위 버전이라 쓰기 봉쇄
    IoFailed,
    Conflict,     // 서버 문서가 내가 아는 revision보다 앞서 있다 (Phase 2)
    Offline,      // 서버에 도달하지 못했다 — 재시도로 풀릴 수 있다 (Phase 2)
}
```

Phase 2에서 **능력 인터페이스 2종**이 추가됐다. 세 구현체가 같은 계약을 온전히 채우게 하고,
`is not JsonFileRepository` 같은 **구체 타입 탐지를 코드베이스에서 0건으로 만들기 위해서**다.

```csharp
/// <summary>동기 쓰기가 가능한 매체(앱 종료 콜백은 await를 기다려주지 않는다).</summary>
public interface IBlockingWriteRepository { ESaveWriteResult SaveBlocking(string _key, string _value); }

/// <summary>종료 시점 세이브를 로컬에 동기 기록하고 다음 부팅에 소비하는 매체.
/// 매체가 네트워크라 종료 콜백 안에서 쓰기를 끝낼 수 없을 때만 필요하다.</summary>
public interface ISaveJournalRepository
{
    ESaveWriteResult WriteJournalBlocking(string _payload);
    UniTask<ESaveWriteResult> ConsumeJournalAsync();
}
```

`FirestoreSaveRepository`는 **`IAtomicRepository`를 구현한다.** 기존 구현체 2개가 모두 이 인터페이스라
이게 사실상 유일한 계약이고, `IRepository`만 구현하면 "구현체만 교체한다"는 치환 가능성이 깨진다.

`CancellationToken`은 **넣지 않았다.** 취소를 소비할 주체가 없다 — 호출부 15곳이 전부 fire-and-forget이고
부트 커밋은 1회다. 타임아웃은 Firestore 호출 안쪽이 `Task.WhenAny`로 이미 소유한다.

**커밋 진입점은 `SaveTransaction` 하나다.** 로드맵 초안은 "`Save()` 호출 15곳이 `await` 지점"이라고 봤으나,
그대로 하면 `RewardClaimPopup`의 `Func<bool>` 계약(넘기는 곳 7건)과 `ITutorialProgressSink`가 깨지고
`OnApplicationQuit`에서는 원리상 `await`가 불가능하다. 그래서 아래 원칙을 구조로 만들었다.

**메모리 변경 메서드는 동기로 남는다.** `CurrencyManager.Earn/Spend`, `OwnershipManager.Grant` 같은 것은 캐시만 바꾸고,
비동기가 되는 것은 커밋뿐이다. UI 버튼 핸들러로 async가 번지는 경로는 **0건**이다.

## Firestore 문서

경로는 현행 그대로 `users/{firebaseUid}/save/{current|test}` (프로필은 `ContentProfileConfig.CloudSaveProfileId`).

| 필드 | 타입 | 용도 |
|---|---|---|
| `payload` | string | `UserSaveData` JSON 통짜 |
| `hash` | string | `payload` 무결성 |
| `rev` | long | 단조 증가. 되감기·동시쓰기 차단 |
| `schemaVersion` | long | `UserSaveData.VERSION` (현재 6) |
| `updatedAt` | timestamp | 서버 시각 |
| `deviceId` · `appVersion` | string | 진단용 |

실제 필드명은 `hash`/`rev`가 아니라 **`payloadHash`(풀해시 앞 16자)/`revision`** 이다 — 현행 코드와 배포된 규칙이 쓰는 이름을 그대로 뒀다. 단일 창구는 `4.Sync/PlayerSaveDocument.cs`의 `FIELD_*` 상수다.

**`requestId`는 넣지 않았고 `firestore.rules`는 무변경이다.** ack 유실 대응으로 계획했으나,
`PlayerSaveDocument.PushTransactionAsync`에 **이미 hash 멱등 경로가 있다** — 서버 문서 hash가 올릴 hash와 같으면 현재 rev를 그대로 반환한다. 타임아웃 후 재전송 중복은 이걸로 막히고, 저널 재업로드는 `baseRevision` 대조가 잡는다.
게다가 `hasOnly` 화이트리스트는 **단방향**이다 — 필드를 한 번 찍으면 되돌릴 때 그 문서의 모든 쓰기가 영구 거절된다. 안 넣는 편이 싸다.

### 부팅 흐름

```
1. 익명 인증
2. FirestoreSaveRepository.LoadAsync  — 문서 1회 read
3-a 성공        → 메모리 보유 + 미러 파일 기록 → DataSaveManager가 그 값으로 Load
3-b 문서 없음   → 신규 세이브 생성 후 업로드
3-c 실패·오프라인 → BlockedRetryable(차단 화면 + 재시도). 미러 열람 전용 모드는 만들지 않았다
4. 게이트 통과 → BootInstaller.InstallSaveDependent()
```

미러는 게임플레이가 절대 쓰지 않는다. 쓰기 주체는 "서버 스냅샷 수신"과 "종료 저널" 둘뿐이다. Firestore SDK 자체 캐시는 `PersistenceEnabled = false`로 끈다.

부트 게이트 신호는 Phase 2에서 `PlayerSaveSync`를 떠나 **`Core/BootGate.cs`** 로 옮겼다 — Cloud에서 `PlayerSaveSync`가 꺼지는데 부트 계약이 그 안에 남으면 **꺼진 부품이 부트를 붙잡는다.** `PlayerSaveSync.IsGateComplete`/`MarkGateComplete()`는 시그니처를 유지한 채 위임만 한다.

### 종료 저널

`OnApplicationQuit`/`OnApplicationPause(true)`는 `await`를 기다려주지 않아 Cloud에서는 동기 쓰기가 불가능하다.
그래서 종료 시 `SaveJournalEntry`(payload · payloadHash · baseRevision · schemaVersion · profileId · uid · writtenAtUtcTicks)를 미러에 **동기 기록**하고, 다음 부팅의 `PrimeAsync` 직후 · `LoadAsync` 이전에 소비한다.

채택 조건은 전부 만족해야 한다: `uid` 일치 · `profileId` 일치 · `schemaVersion` 일치 · `baseRevision == 서버 revision` · `payloadHash != 서버 hash` · payload 무결성(재해시 일치). 하나라도 어긋나면 폐기한다.
`CommitBlocking`이 `s_writeChain` 밖인 것은 고치지 않았다 — 동기 경로에서 비동기 완료를 기다릴 수 없어 애초에 불가능하고, `baseRevision` 낙관 규칙이 순서 역전을 흡수한다.

### 저장 흐름

```
DataSaveManager.SaveAsync
  → 직렬화 + hash
  → requestId 생성
  → transaction: 서버 rev == 내 rev 이면 rev+1 로 커밋
  → Committed → 미러 갱신
  → Conflict  → 세션 종료 사유. 재시도로 풀리지 않는다(아래)
  → Offline   → 대기 오버레이 → 실패 팝업(재시도 / 나중에). 종료 시 저널이 받는다
```

**`Conflict`는 재시도 대상이 아니다.** 쓰기 선조건이 부팅 때 읽은 `revision`이라 다시 밀어도 같은 자리에서 막힌다 — 재시도를 주면 무한 반복이 된다. 부트 커밋에서는 종료 상태로, 런타임에서는 "다른 기기가 먼저 저장했다 · 재시작 필요" 팝업으로 갈랐다. F-5의 "늦은 쪽 변경이 통째로 버려진다"가 그대로 관측되는 자리다.

**ack 유실 대응**: `PushTransactionAsync`의 hash 멱등 경로가 맡는다 — 서버 문서 hash가 올릴 hash와 같으면 rev를 올리지 않고 현재 rev를 반환한다. 별도 `requestId` 필드가 필요 없다.

## 구현 시 걸리는 함정 (실측)

1. **`cardGrowth`의 디스크 쓰기 책임자가 currency 경로다.** `CardGrowthManager.Snack.cs:21 AddSnack`은 일부러 flush하지 않고, `CardPackOpener.cs:44`의 `FlushToData()` + `CurrencyManager.Save()`가 대신 쓴다. 통짜 유지에서는 문제없지만 **후속 분해 때 여기서 간식이 유실된다.**
2. ~~**`CurrencyManager.Earn/Spend`는 flush하지 않는다.**~~ **해소(Phase 1)** — `SaveTransaction.CommitAsync()`가 캐시 보유 6개 도메인을 함께 flush한다. 호출 순서 의존이 사라져 주석 3건(`TournamentProgress` · `RankRewardManager` · `AlbumRewardManager`)을 삭제했고, 강화 경로의 이중 저장 2건도 1회로 합쳤다.
   그 대가로 **새 함정이 생겼다**: 전 도메인 flush는 초기화 전에 걸리면 빈 캐시로 저장분을 덮는다. 부트 첫 줄 `ApplyWipeIfScheduled`가 각 `Init()`보다 앞서므로 실재하는 경로다. `CurrencyManager`·`OwnershipManager`·`ProfileManager`에 초기화 플래그를 새로 넣어 막았다(나머지 3개는 이미 보유).
3. ~~**`OwnershipManager.Grant`는 1장당 저장 1회다**~~ **해소(Phase 1)** — 팩 개봉이 지역 `HashSet`에 신규를 모아 `GrantAll` 1회로 지급한다. **5장 팩 최대 6회 → 최대 2회**(지급 커밋 + 간식·차감 확정 커밋). 같은 팩에서 두 번 나온 카드를 중복으로 잡으려면 그 집합이 반드시 필요하다.
   되감기 재생(`OutgameTutorialRewind.cs:144`)은 손대지 않았다 — `GrantCardSet`/`GrantPackPool`이 이미 `GrantAll`이고 남은 단건은 스텝당 1장이라 통합 대상이 없다.
4. **부트 중 쓰기가 있다.** `OwnershipManager.Init`(`:71` `if (t_dirty) Save()`), `RankRewardManager.MigrateClaimedCount`(`:112`), `DeckSaveManager` 레거시 이관(`:426`). 게이트 통과 전 쓰기가 발생하지 않는지 확인해야 한다.
5. **`OutgameTutorialRewind.ApplyWipeIfScheduled`(`:77~86`)가 숨은 두 번째 블록 목록이다.** `UserSaveData`에 블록을 추가하면 여기도 고쳐야 그 축이 이전 세션 값으로 남지 않는다. `profile`은 반영됨(`:86`).

## Phase 로드맵

각 Phase는 끝났을 때 게임이 정상 동작해야 한다.

| # | 목표 | 주요 파일 | 완료 판정 | 롤백 |
|---|---|---|---|---|
| **0** | **인프라만. 게임 코드 0줄 변경** — dev 프로젝트 생성, 규칙 배포, `google-services.json` 2벌 + 빌드 훅, `.firebaserc` | `firestore.rules`, `Editor/ContentProfileValidator.cs` | dev에서 익명 로그인 성공. 규칙 시뮬레이터에서 위조 쓰기 거절 | 없음 |
| **1** ✅ | **인터페이스 비동기화 — 완료(2026-08-26).** `IRepository`/`IAtomicRepository` UniTask화, 구현체 2종 적응, `DataSaveManager.LoadAsync/SaveAsync`, **커밋 진입점 `SaveTransaction` 신설**, 부트 코루틴 UniTask 승격. **진실원은 여전히 로컬 파일** | `1.Repository/*`, `3.Manager/DataSaveManager.cs` · `SaveTransaction.cs`, 도메인 매니저 11개, `Core/GameManager.cs` · `BootInstaller.cs` | 컴파일 0에러 · 미초기화 상태 커밋에도 세이브 무손실 확인. 함정 2·3 해소 | git revert |
| **2** ✅ | **`FirestoreSaveRepository` + 미러 — 코드 완료(2026-08-26).** 부팅 시 서버 1회 read, 미러 기록, 진실원 전환(`ContentProfileConfig.SaveSourceMode`). 종료 저널 · 차단 화면 + 재시도 · 저장 대기 오버레이 포함 | `1.Repository/FirestoreSaveRepository.cs` · `ISaveJournalRepository.cs` · `IBlockingWriteRepository.cs` 신규, `2.Domain/SaveJournalEntry.cs`, `4.Sync/PlayerSaveDocument.cs`, `Save/SaveSourceMode.cs` · `ESaveBootBlockReason.cs`, `Core/BootGate.cs` 신규, `Core/GameManager.cs` · `BootInstaller.cs` · `EGameBootState.cs` · `ContentProfileConfig.cs`, `UI/Common/SaveBusyOverlay.cs` · `LoadingCoverView.cs` · `UiSortingOrder.cs` | 컴파일 0에러 · 계약 전수 확인. **실기 Play 검증 미완** | `SaveSourceMode` = `Local` |
| **3** | **동기화 기계 철거.** `PlayerSaveSync` 3-way 판정·conflict sidecar·업로드 큐 제거 | `4.Sync/*` 대부분 삭제 | `RemoteAhead`/`Diverged`/`LocalAhead`/`NoBaseConflict` 코드 부재. **신규 계정 → 전 구간 플레이 → 앱 재시작 → 상태 100% 복원** | Phase 2 상태로 revert |

### 후속 (별도 착수)

| 순서 | 작업 | 선행 조건 |
|---|---|---|
| 4 | **계정 연동** — 익명 UID 승격. 재설치 복원은 여기서부터 가능 | CookApps `platform.auth` 토큰 노출 |
| 5 | **문서 분해** — 통짜 payload → `state/*` 다중 문서. 위 함정 1·3 선결 | Phase 3 |
| 6 | **마스터 테이블 업로드** — SO/`SpecData.bytes` → `master/*` | 5 |
| 7 | **규칙에 가격·풀 검증 추가** | 6 |
| 8 | **Cloud Functions 판정 이관 — 서버 권위 검증** | Blaze 전환(예정, 시점 미정) |

## 검증

| 단계 | 방법 |
|---|---|
| Phase 1 | 같은 조작 시퀀스 후 세이브 파일 내용 비교 — 비동기화 전후가 같아야 한다 |
| Phase 2 | 콘솔에서 `payload` 교체 → 재시작 시 반영 / 기내모드 기동 → **차단 화면 + 재시도** / `rev` 되감기 쓰기 → `PERMISSION_DENIED` |
| Phase 2 | 두 기기 동시 저장 → 늦은 쪽 `Conflict` 반환, 데이터 파손 없음 |
| Phase 2 | 저장 중 강제 종료 → 재기동 시 저널 `baseRevision` 대조로 채택 또는 폐기, 중복 적용 없음 |
| Phase 3 | 신규 계정 → 튜토리얼 완주 → 팩 구매 → 강화 → 전투 → **앱 재시작** → 상태 복원 |
| 규칙 | 규칙 시뮬레이터에 위조 쓰기 3종(`rev` 미증가, `schemaVersion` 되감기, 남의 uid) → 전부 거절 |
| 컴파일 | 각 Phase 후 `Unity_ReadConsole`로 CS 에러 0건 |

**재설치 복원은 완료 판정에 넣지 않는다.** 익명 UID는 재설치 시 바뀌므로 새 계정으로 시작되는 것이 정상이다. 재설치 복원은 후속 4(계정 연동) 이후에만 성립한다.

## 위험 · 미결

| # | 항목 | 대응 |
|---|---|---|
| **F-1** | **판정이 전부 클라에 있다.** Spark라 클라가 문서를 직접 쓰므로, `payload`를 조작해 잔액·소유·진행도를 임의 설정할 수 있다. 규칙이 막는 것은 소유권·`rev` 되감기·크기뿐이다. 분해하더라도 마스터가 없는 한 결과는 같다 | 이번 범위의 명시적 한계. Blaze 전환 후 후속 6~8에서 해소 |
| ~~**F-2**~~ | ~~`await` 사이 프레임이 끼면서 순서 의존이 위험해진다~~ | **해소(Phase 1)** — 순서를 고정한 게 아니라 없앴다. 커밋이 전 도메인을 함께 flush한다. 겹치는 쓰기는 `DataSaveManager.s_writeChain`이 직렬화해 구 스냅샷이 새 것을 덮지 못한다 |
| ~~**F-3**~~ | ~~저장이 네트워크가 되어 실패가 실재한다~~ | **해소(Phase 2)** — `SaveTransaction.CommitAsync()` 한 곳에 물렸다(호출부 15곳 무변경). 250ms 지연 후 `SaveBusyOverlay`(`UiSortingOrder.SaveBusy = 920`)로 입력 차단, 실패 시 `SimpleYNPopup` 재사용. `Request()` 경로에서도 뜬다. **부트 구간(`BootState != Ready`)은 배선이 돌지 않는다** — 재시도 창구가 `BootInstaller`와 둘로 갈리면 안 된다. `SaveBusyOverlay` 프리팹·Addressables 등록은 미완이며, 없으면 경고 1회 후 저장은 정상 진행된다 |
| **F-4** | **Spark 일일 한도** — 읽기 50k / 쓰기 20k / 저장 1GiB. Blaze 전환 시 한도가 사용량 과금으로 바뀐다 | 실행당 read 1. 쓰기는 저장 1회당 1 — 팩 개봉이 최대 6회이므로 함정 3 해소가 곧 과금 절감 |
| **F-5** | **blob 충돌은 병합 불가** — 두 기기 동시 플레이 시 늦은 쪽 변경이 통째로 버려진다 | 프로토 구간 감수. 계정 연동(후속 4) 전에는 다기기 시나리오 자체가 드물다 |
| ~~**F-6**~~ | ~~`profile`이 저장되지 않는다~~ | **해소** — `870fe1723`(cherry-pick) + `8763786b1`. `UserSaveData.profile` 슬롯(`:35`) · `ProfileSaveData` · 되감기 목록(`:86`) · `PlayerSaveSync.IsComplete()`(`:441`). `VERSION`은 6 유지 |
| **F-7** | **`.firebaserc` 부재로 프로젝트 별칭이 없다** | Phase 0에서 `firebase use --add`로 dev/prod 등록 |

## Firebase 콘솔 — 사람이 직접 할 작업

### 1. `cardbattle-dev` 프로젝트 생성
console.firebase.google.com → 프로젝트 추가 → 이름 `cardbattle-dev` → Google 애널리틱스 사용 안 함

### 2. Firestore 생성 — 리전은 영구 확정
빌드 → Firestore Database → 데이터베이스 만들기 → **프로덕션 모드** → 위치 `asia-northeast3`(서울)

> 한 번 정하면 못 바꾼다. **먼저 `cardbattle-d94f7`의 기존 리전을 확인해 동일하게 맞춘다.**

### 3. 익명 인증
빌드 → Authentication → 시작하기 → Sign-in method → 익명 → 사용 설정

### 4. Android 앱 등록
패키지 이름 **`com.BurgerMonster.CardBattle`** (`ProjectSettings.asset:172`와 일치). SHA-1은 익명 로그인에 불필요.

`google-services.json` 배치:
- `Assets/Firebase/Config/dev/google-services.json`
- `Assets/Firebase/Config/prod/google-services.json` (기존 파일 이동)
- 빌드 직전 훅이 선택된 벌을 `Assets/google-services.json`으로 복사
- `Assets/google-services.json`을 `.gitignore`에 추가

빌드 훅은 신규 작성이 불필요하다. `Editor/ContentProfileValidator.cs:8`이 `IPreprocessBuildWithReport`를 구현하고 `:29`에서 `BuildOptions.Development`로 Test/Live를 판정한다.

### 5. 규칙 배포
`npm i -g firebase-tools` → `firebase login` → `firebase use --add` → `firebase deploy --only firestore:rules --project cardbattle-dev`

`firebase.json`이 이미 `firestore.rules`를 가리킨다. 규칙 시뮬레이터로 위조 쓰기 거절을 확인한다.

### 6. prod(`cardbattle-d94f7`)에 2~5 반복
규칙은 항상 dev에서 검증 후 prod에 배포한다.

## 핵심 파일

- `Assets/Scripts/OutGame/Save/1.Repository/IRepository.cs` · `IAtomicRepository.cs` — Phase 1의 시그니처 변경 지점
- `Assets/Scripts/OutGame/Save/1.Repository/JsonFileRepository.cs` — Phase 2에서 읽기 전용 미러로 강등
- `Assets/Scripts/OutGame/Save/3.Manager/DataSaveManager.cs` — `Save()` 호출 15곳(11개 파일)의 중심
- `Assets/Scripts/OutGame/Save/2.Domain/UserSaveData.cs` — `VERSION = 6`, 10블록(currency·ownership·deck·tutorial·rank·cardGrowth·keywordGrowth·albumReward·tournament·profile)
- `Assets/Scripts/OutGame/Save/4.Sync/PlayerSaveSync.cs` (902줄) — Phase 3에서 대부분 삭제
- `Assets/Scripts/Core/BootInstaller.cs:171-183` — 초기화 사슬. `:171` 와이프 → `:172` 재화 → `:173` 프로필 → `:174` 소유 → `:175` 튜토 → `:176` 랭크 → `:177` 키워드성장 → `:178` 카드성장 → `:179` 덱
- `Assets/Scripts/Editor/ContentProfileValidator.cs` — 빌드 전처리 훅
- `firestore.rules` — 통짜 문서용. `requestId` 조건만 추가
