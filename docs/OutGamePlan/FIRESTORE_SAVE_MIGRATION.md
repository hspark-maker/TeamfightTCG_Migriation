# Firestore 세이브 이관 — 남은 작업

갱신 2026-08-26 · 기준 커밋 `8763786b1`

로컬(`JsonFileRepository`)이 진실원이고, Firestore는 원격 사본이다.
읽기(pull)·판정·적용·쓰기(transaction) 배선은 끝났고, 판정 분기 대부분은 실데이터로 검증되지 않았다.

계획 방향은 [FIRESTORE_SAVE_MIGRATION_ROADMAP.md](FIRESTORE_SAVE_MIGRATION_ROADMAP.md)에서 **저장 매체 교체**(`IRepository` 구현체 교체)로 확정됐다.
그 결과 아래 3절의 3-way 판정 기계는 **검증 대상이 아니라 삭제 대상**이다.

---

## 1. 현재 위치

### 된 것

- Firebase Auth 익명 로그인, UID를 프로세스 동안 고정
- 문서 경로 `users/{firebaseUid}/save/{current|test}` — 프로필은 `ContentProfileConfig.CloudSaveProfileId`
- 부팅 게이트: `BootInstaller.Awake()`(세이브 무관) / `InstallSaveDependent()`(세이브 의존) 2단 분리
- 원격 pull + 3-way 판정(로컬 / 원격 / `outgame_sync_state` 메타)
- 원격 적용 시 로컬 백업 → 원자 교체 → 재로드 검증 → 메타 갱신
- 충돌 시 로컬 유지 + 원격 스냅샷 별도 보존
- revision·hash 기반 Firestore transaction push
- 실패·오프라인·타임아웃 시 로컬 플레이 유지 + 클라우드 쓰기 차단
- `firestore.rules` 배포 (소유권 · revision 되감기 방지 · schemaVersion 되감기 방지 · payload 300000 상한)
- **프로필(닉네임·아바타·프레임) 세이브 영속화** — `870fe1723`(cherry-pick) + `8763786b1`
- **로드맵 Phase 1 — 인터페이스 비동기화 (2026-08-26)**
  - `IRepository`/`IAtomicRepository` UniTask화, `JsonFileRepository`·`PlayerPrefsRepository` 적응
  - `DataSaveManager.LoadAsync`/`SaveAsync` + 쓰기 직렬화 사슬 + `ApplyRemoteAsync`(`Try*`+`out` → `SaveApplyReport`)
  - **`SaveTransaction` 신설** — 커밋의 단일 진입점, 전 도메인 함께 flush (함정 2 해소)
  - 팩 개봉 `GrantAll` 통합 — 5장 팩 최대 6회 → 2회 쓰기 (함정 3 해소)
  - `BootInstaller` 코루틴 → UniTask, 부트 중 쓰기 3회 → 1회
  - 앱 종료 전용 동기 경로 `CommitBlocking()` — 종료 콜백은 await를 기다려주지 않는다
  - **검수에서 잡은 회귀 2건 수정**: 전 도메인 flush가 기존 보존 장치를 우회하던 문제.
    덱은 못 읽은 카드가 있으면(`s_slotsDegraded`), 프로필은 Config가 모르는 id로 폴백했으면(`s_fellBackToDefault`) flush를 건너뛴다.
    부트 로드 실패가 게이트를 영원히 열지 않던 경로도 `RecoveryRequired`로 닫았다.

### 안 된 것

- ~~진실원 전환 (로컬 → Firestore)~~ — **코드 완료(Phase 2). 실기 Play 검증 미완**
- 소셜 로그인 (익명 UID는 재설치 시 소실)
- 서버 권위 판정 — Spark에서는 불가. 요금제 전환(Blaze) 예정, 시점 미정

### 프로필 세이브 (`870fe1723` cherry-pick + `8763786b1`)

| 변경 | 위치 |
|---|---|
| `ProfileSaveData` 신규 — `nickname` · `avatarId` · `frameId` (기본값 없음) | `2.Domain/ProfileSaveData.cs:5-10` |
| `UserSaveData.profile` 슬롯 추가 → 블록 10개 | `UserSaveData.cs:35` |
| `Init` 폴백(빈 값·미지 id → `Default*Id`, 슬롯 되쓰기 없음) · `Persist` | `ProfileManager.cs:56-63` · `:105-113` |
| 되감기 와이프 목록에 `profile` 추가 | `OutgameTutorialRewind.cs:86` |
| `IsComplete()`에 `profile != null` 추가 | `PlayerSaveSync.cs:441` |
| `ProfileManager.Init()`이 `InstallSaveDependent()` 안 | `BootInstaller.cs:173` |

`VERSION`은 6 유지(슬롯 추가). `firestore.rules`는 schemaVersion을 `> 0` + 단조 증가로만 검사하고 특정 버전을 하드코딩하지 않아 규칙 영향 없음.

### 타임아웃 값

| 구간 | 값 | 위치 |
|---|---|---|
| 인증 | 5초 | `PlayerSaveSync.cs:192` (`PULL_TIMEOUT_MS`) |
| 원격 read | 5초 | `PlayerSaveSync.cs:259` |
| transaction | 10초 | `PlayerSaveSync.cs:693` (`TRANSACTION_TIMEOUT_MS`) |
| 업로드 디바운스 | 3초 | `PlayerSaveSync.cs:14` `UPLOAD_DEBOUNCE_MS` |

---

## 2. 결함

A1(부팅 영구 대기) · A4(Enter Play Mode Phase 2 스킵) · A5(`RemoteAhead` 실패 시 게이트 플래그 잔존)은 수정됨.
현재 코드: `GameManager.cs:84-95` try/catch + `MarkRecoveryRequired()` · `BootInstaller.cs:39-44` `SubsystemRegistration` 리셋 · `PlayerSaveSync.cs:568-572` `MarkRecoveryRequired()` + `MarkGateComplete()`.

### A2 (HIGH) LobbyScene 직접 실행 시 덱·도감 UI 빈 값 고착

정상 부팅(StartScene 경유)은 영향 없다. **에디터에서 LobbyScene을 바로 여는 개발 워크플로**에서만 발생한다.

- LoadingCover 프리팹이 StartScene에만 있어 게이트 대기(최대 ~10초) 동안 화면을 가리는 것이 없다
- UI가 미초기화 매니저를 읽어 빈 값을 그린 뒤, 뒤늦게 도는 `Init`이 변경 통지를 안 쏴서 빈 값이 고착된다

재화는 해소됨 — `CurrencyManager.cs:28-29`가 `Init()` 끝에 전 재화 `OnCurrencyChanged`를 발화한다.

남은 경로:
- `OwnershipManager.Init()` — `OnOwnershipChanged` 미발화
- `DeckSaveManager.LoadFromSave` — 통지 없음

**조치**: 위 둘에 `Init` 완료 통지 추가.

### A3 (MED) `ApplyWipeIfScheduled()`가 스냅샷보다 뒤

`ApplyWipeIfScheduled()`는 `BootInstaller.cs:171`(`InstallSaveDependent` 최상단)에 있어 "매니저 Init들의 슬롯 캐싱 전" 계약은 지킨다.
그러나 `PlayerSaveSync.Initialize`(`GameManager.cs:86`)보다 뒤다.
`Initialize`는 진입부(`PlayerSaveSync.cs:62`)에서 `s_pendingPayload = DataSaveManager.CreateSnapshot()`을 뜨고, 게이트도 그 스냅샷으로 분류한다.

→ 디버그 되감기 예약이 있으면 **분류·업로드 대상이 되감기 이전 세이브**가 된다.
데이터 손실은 아니다(다음 저장에 덮인다). 되감기 전 상태가 한 revision 올라가거나 다음 부팅에 `Diverged`로 잡힐 수 있다.

**조치**: 되감기 예약 확인·소비를 `PlayerSaveSync.Initialize`보다 앞으로 옮기거나, 게이트 스냅샷 시점을 Phase 2 직전으로 미룬다.

### A6 (기록만) `LocalAhead` 오판 가능

`PlayerSaveSync.cs` `Classify()` — 원격 schemaVersion이 클라보다 낮으면 무조건 `LocalAhead`.
원격이 구버전이지만 진행도가 훨씬 많을 수 있다(다른 기기가 업데이트를 안 한 경우).
로컬이 방금 리셋된 빈 세이브여도 원격을 덮는다.

마이그레이션 코드가 없어 현재로선 대안이 없다. **이 경로로 원격을 덮기 전 반드시 백업하고 경고 로그를 남길 것.**
`v6 → v7` 마이그레이션이 생기면 "원격을 마이그레이션해서 채택"으로 바꿔야 한다.

---

## 3. 삭제 예정 — 3-way 판정 기계

로컬이 진실원이던 시절의 장치다. 로드맵 Phase 3에서 제거한다. **검증하지 않는다.**

`PlayerSaveSync.Classify()`의 판정 분기 — 실측된 것은 `InSync` 하나뿐(revision 4, 로컬·원격·base 해시 일치).

| 판정 | Phase 3 이후 |
|---|---|
| `RemoteAhead` · `Diverged` · `LocalAhead` · `NoBaseConflict` | 삭제. 서버가 진실원이면 부팅은 read 1회로 끝난다 |
| `RemoteMissing` | 유지 — 신규 세이브 생성 경로가 된다 |
| `InvalidRemote` (hash 불일치) | 유지 |
| `FutureSchema` | 유지 — `UpdateRequired` |

함께 사라지는 것: 동기화 메타 `outgame_sync_state_{uid}_{profileId}`, conflict sidecar, 업로드 디바운스 큐.

---

## 4. 미결 결정

| 항목 | 권장 | 상태 |
|---|---|---|
| 오프라인 저장 허용? | **금지** — 미러는 읽기 전용, 쓰기는 서버 도달 시에만 | 로드맵에서 확정 |
| 기존 로컬 세이브 마이그레이션 | **전원 초기값 리셋** — 세이브에 서명이 없어 기존 잔액을 서버가 신뢰할 방법이 없다 | 로드맵에서 확정 |
| `profile` 블록 추가 여부 | 추가함. `VERSION` 유지 + 되감기 목록 동반 수정 | **해결** (`870fe1723`) |
| 커밋 분할 | 위험 구간(부팅 게이트)은 별도 커밋으로 | `883172aeb`가 1,480줄 단일 커밋이라 롤백 지점 없음 |

---

## 5. 외부 블로커

- **Blaze 플랜** — Cloud Functions 사용 시 필수 (서버 판정 단계). **전환 예정, 시점 미정.** 전환되면 후속 8이 착수 가능해진다
- **CookApps `platform.auth` 토큰 미노출** — 소셜 로그인 연동 불가.
  `PlatformUserInfo`가 `UserId`/`SocialPlatform`만 노출하고 Google idToken · Apple IdentityToken+rawNonce · Facebook accessToken을 전부 내부에서 폐기한다.
  사내(`tech@cookapps.com` / `cookapps-devops/tech-platform-auth`)에 **`PlatformUserInfo`에 토큰 필드 추가 요청** 필요. 미발송

---

## 6. 알려진 한계

- **익명 UID는 앱 재설치 시 바뀐다.** 원격 문서가 고아가 되고 진행도를 잃는다.
  소셜 로그인이 붙기 전까지 **유저에게 "백업"이라고 표시하지 말 것.** 내부 검증용이다
- 에디터와 실기는 UID 캐시가 따로라 서로 다른 UID를 쓴다. 정상 동작이다
- iOS 미검증 — `GoogleService-Info.plist` 없음
- `Assets/CookApps/Resources/CookAppsAuth.asset` 고아 파일 잔존 (`com.cookapps.auth` 패키지는 제거됨). 삭제 필요
- `Assets/google-services.json`은 클라이언트 설정이라 비밀키가 아니다(앱에 박혀 나간다). 커밋 완료

---

## 7. 파일 지도

```
Assets/Scripts/Core/
  GameManager.cs                  Boot() · BootState · Flush()
  EGameBootState.cs               Booting / Syncing / Ready / UpdateRequired / RecoveryRequired
  BootInstaller.cs                Phase 1 = Awake, Phase 2 = InstallSaveDependent()
  ContentProfileConfig.cs         CloudSaveProfileId (current / test)
  Firebase/
    FirebaseAuthService.cs        익명 로그인 · StateChanged · IsCurrentUserActive
    FirebaseAuthProbe.cs          테스트 씬 UI 드라이버
    EFirebaseAuthState.cs

Assets/Scripts/OutGame/Save/
  1.Repository/                   ← 로드맵 Phase 1의 UniTask 시그니처 변경 지점
    IRepository.cs · IAtomicRepository.cs · JsonFileRepository.cs · PlayerPrefsRepository.cs
  2.Domain/UserSaveData.cs        VERSION = 6, 하위 블록 10개
  2.Domain/ProfileSaveData.cs     nickname · avatarId · frameId
  3.Manager/DataSaveManager.cs    Load / Save / OnSaved / CreateSnapshot / 동기화 메타 I/O
                                  Save() 호출처 15곳 / 11개 파일 (2026-08-26 실측)
  4.Sync/                         ← 로드맵 Phase 3에서 대부분 삭제
    PlayerSaveSync.cs             pull · 판정 · 적용 · transaction push (902줄)
    PlayerSaveSyncMetadata.cs     outgame_sync_state_{uid}_{profileId}
    PlayerSaveConflictSnapshot.cs
    ESavePullState.cs · ESaveUploadState.cs

Assets/Scripts/UI/Common/LoadingCoverView.cs   부트 게이트 대기 + 상태 문구
Assets/Scenes/TEST/FirebaseAuthTest.unity      인증 단독 테스트 씬

firestore.rules · firebase.json                프로젝트 루트
docs/OutGamePlan/STRUCTURE.md                  4.Sync 계층 반영됨
```

---

## 8. 착수 순서

1. ~~**A2 나머지**~~ — `OwnershipManager.Init` / `DeckSaveManager.LoadFromSave` 변경 통지 추가 (작업 트리에 구현됨)
2. **A3** — 되감기 예약 소비를 `PlayerSaveSync.Initialize`보다 앞으로 (미착수)
3. ~~Phase 0 (인프라)~~ — **불필요해짐.** dev/prod 프로젝트를 나누지 않기로 해서 `.firebaserc`·Config 2벌·빌드 훅이 전부 빠졌다. 남은 것은 `firestore.rules`의 `requestId` 조건뿐이고 Phase 2에서 구현체와 함께 넣는다
4. ~~**Phase 1** (인터페이스 비동기화)~~ — **완료(2026-08-26)**
5. ~~**Phase 2** (Firestore 구현체)~~ — **코드 완료(2026-08-26). 실기 Play 검증 미완**
6. **Phase 3** (동기화 기계 철거) ← **다음**

### Phase 2 결과 (2026-08-26)

`SaveSourceMode.Current`(← `ContentProfileConfig.saveSourceMode`)가 진실원을 가른다. **Test = `Cloud`, Live = `Local`.**
에디터 RunMode도 Test로 맞춰 뒀으므로 지금 Play하면 `users/{uid}/save/test`를 친다 — 운영 문서 `current`는 안 건드린다.
`google-services.json`이 한 벌뿐이라 **어느 프로필이든 실제 대상은 운영 프로젝트 `cardbattle-d94f7`** 이고, 갈리는 것은 문서 id뿐이다.

| 축 | 결과 |
|---|---|
| 진실원 | `FirestoreSaveRepository`(`IRepository`+`IAtomicRepository`+`ISaveJournalRepository`). 세이브 본문 키만 서버, 나머지 키는 전부 미러 위임 |
| 부트 | `Core/BootGate.cs`가 게이트 신호의 단일 창구. `GameManager.BootCloudSaveAsync`가 인증 → `PrimeAsync` → `ConsumeJournalAsync` → `LoadAsync` 재시도 루프를 소유 |
| 종료 | `SaveJournalEntry`를 미러에 동기 기록 → 다음 부팅에 `baseRevision` 대조로 채택/폐기 |
| 차단 | `EGameBootState.BlockedRetryable` + `ESaveBootBlockReason` + 로딩 커버 재시도 버튼(미배선 시 5초 자동 재시도) |
| 쓰기 UI | `SaveBusyOverlay`(920) 250ms 지연 표시 + `SimpleYNPopup` 실패 팝업. `BootState == Ready`일 때만 배선이 돈다 |
| 규칙 | **무변경.** `requestId`는 hash 멱등 경로가 대신해 불필요 |

**미완**: `SaveBusyOverlay` 프리팹 + Addressables 등록(주소 `SaveBusyOverlay`, 라벨 `UIPrefab`). 없으면 경고 1회 후 저장은 정상 진행된다.

**작업 중 잡은 기존 버그**: `LoadingCoverView.CoFillBar`가 15초에 강제 탈출한 뒤 이어지는 대기 조건이 `BootState == Syncing`이라, Cloud 부트(게이트 전 상태 `Booting`)에서는 한 프레임도 안 돌고 **세이브 미설치 상태로 로비를 열었다.** 대기 축을 설치/종료 상태로 바꿔 막았다.

A3는 Phase 3에서 `PlayerSaveSync.Initialize` 자체가 사라지면 함께 해소된다. Phase 3 전까지 되감기를 쓸 일이 있으면 먼저 고친다.

### Phase 2 착수 지점

- **신설**: `Save/1.Repository/FirestoreSaveRepository.cs` — `IAtomicRepository` 구현. 재료는 이미 `PlayerSaveSync`에 있다(`Document()`, `PushTransactionAsync`, 원격 read).
- **배선**: `Core/GameManager.cs`의 `SetRepository(new JsonFileRepository(...))` 한 줄이 교체 지점이다. 미러 기록은 그 뒤에 얹는다.
- **`ESaveWriteResult`에 `Conflict`/`Offline` 추가** · `IRepository`에 `CancellationToken` 추가 — 둘 다 Phase 1에서 의도적으로 미룬 것이다.
- **`SaveTransaction.IsBusy` 가드 배치 + 대기 UI** — Phase 1에서 판정만 만들어 뒀다. `SaveBusyOverlay : SingletonOverlay<T>`를 이 하나에 물리면 된다.
- **종료 경로 재설계(필수)**: `DataSaveManager.SaveBlocking()`은 매체가 `JsonFileRepository`가 아니면 **경고만 남기고 아무것도 쓰지 않는다.** Firestore 구현체를 꽂는 순간 `OnApplicationQuit`·`OnApplicationPause(true)`의 저장이 전부 사라진다. "로컬 저널에 동기 기록 → 다음 부팅에 업로드"가 필요하다. 진입점(`CommitBlocking`)은 그대로 쓴다.
  - 함께: `CommitBlocking`은 `s_writeChain` 밖에서 돌아 대기 중인 비동기 쓰기와 순서가 역전될 수 있다. 지금은 파일 매체가 동기라 무해하지만 네트워크에서는 아니다.

**Phase 1에서 남긴 낮은 우선순위 항목**

- 팩 개봉이 아직 쓰기 2회다(`GrantAll`의 커밋 + `TryPurchase`의 확정 커밋). 커밋 없는 지급 API를 만들면 1회가 된다.
- `PlayerPrefsRepository`는 인스턴스화 0건이다. UniTask로 유지보수 중인 데드 구현이라 삭제 후보.
- 덱 저장 1회에 `OnDeckChanged`가 3번 발화해 목록이 3번 재빌드된다(`SetName`·`SetImageKey`·`SaveSlot`). 이 이벤트는 이관과 별개 축(A2 조치)이다.
