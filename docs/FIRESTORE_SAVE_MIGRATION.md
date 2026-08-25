# Firestore 세이브 이관 — 남은 작업

작성 2026-08-25 · 기준 커밋 `883172aeb feat(save): Firestore 세이브 이관 배선`

로컬(`JsonFileRepository`)이 여전히 진실원이고, Firestore는 원격 사본이다.
읽기(pull)·판정·적용·쓰기(transaction) 배선은 끝났으나 **판정 분기 대부분이 실데이터로 검증되지 않았다.**

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

### 안 된 것

- **판정 5분기 실데이터 검증** (아래 3절)
- 진실원 전환 (로컬 → Firestore)
- 재화·소유·랭크의 서버 권위 이전
- 소셜 로그인 (익명 UID는 재설치 시 소실)

### 타임아웃 값

| 구간 | 값 | 위치 |
|---|---|---|
| 인증 | 5초 | `PlayerSaveSync.cs:187` |
| 원격 read | 5초 | `PlayerSaveSync.cs:254` |
| transaction | 10초 | `PlayerSaveSync.cs:687` |
| 업로드 디바운스 | 3초 | `PlayerSaveSync.cs` `UPLOAD_DEBOUNCE_MS` |

---

## 2. 결함 — 우선순위 순

### A1 (HIGH) 부팅 영구 대기

`GameManager.cs:84`에서 `PlayerSaveSync.Initialize(...)`를 try/catch 없이 호출한다.
예외가 나면 다음 줄(`:85 BootState = Syncing`)이 실행되지 않아 상태가 `Booting`에 머문다.

`BootInstaller.cs:137-142`의 대기 루프는 종료 조건이 `IsGateComplete` / `UpdateRequired` / `RecoveryRequired` 셋뿐이고,
**셋 다 `Syncing` 이후에만 생긴다.** 타임아웃도 로그도 없어 조용히 무한 루프한다.

**조치**: `Initialize` 호출을 try/catch로 감싸고 실패 시 `GameManager.MarkRecoveryRequired()`.

### A2 (HIGH) LobbyScene 직접 실행 시 재화 0 고착

정상 부팅(StartScene 경유)은 영향 없다. **에디터에서 LobbyScene을 바로 여는 개발 워크플로**에서만 발생한다.

- LoadingCover 프리팹이 StartScene에만 있어 게이트 대기(최대 ~10초) 동안 화면을 가리는 것이 없다
- `CurrencyHud.OnEnable`(`CurrencyHud.cs:200`)이 `GetBalance`를 읽어 0을 그린다
- 뒤늦게 도는 `CurrencyManager.Init`이 **`OnCurrencyChanged`를 발화하지 않아** 0이 고착된다
- 같은 창에서 `OwnershipManager` / `DeckSaveManager` 미초기화 상태의 덱·도감 UI도 빈 값으로 캐싱된다

**조치**: `CurrencyManager.Init()` 끝에 `OnCurrencyChanged` 발화 추가. (다른 매니저도 같은 패턴 점검)

### A3 (MED) `ApplyWipeIfScheduled()`가 스냅샷보다 뒤

"매니저 Init들의 슬롯 캐싱 전" 순서 계약은 지켜졌다(`BootInstaller.cs:164`, `InstallSaveDependent` 최상단).
그러나 `PlayerSaveSync.Initialize`(`GameManager.cs:84`)보다 **뒤**로 밀렸다.

`Initialize`는 진입 직후 `CreateSnapshot()`으로 업로드 대기 페이로드를 뜨고, 게이트도 그 스냅샷으로 분류한다.
→ 디버그 되감기 예약이 있으면 **분류·업로드 대상이 되감기 이전 세이브**가 된다.

디버그 기능이라 실사용 영향은 제한적이고 데이터 손실도 아니다(다음 저장에 덮인다).
다만 되감기 전 상태가 한 revision 올라가거나 다음 부팅에 `Diverged`로 잡힐 수 있다.

**조치**: 되감기 예약 확인·소비를 `PlayerSaveSync.Initialize`보다 앞으로 옮기거나, 게이트 스냅샷 시점을 Phase 2 직전으로 미룬다.

### A4 (MED) Enter Play Mode에서 Phase 2 영구 스킵

`PlayerSaveSync.cs:96 ResetRuntimeState`는 `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`로 게이트를 리셋하는데,
`BootInstaller`의 `s_booted` / `s_saveDependentInstalled`에는 같은 리셋이 없다.

도메인 리로드를 끈 Enter Play Mode 환경에서 2회차 플레이부터 **게이트만 재실행되고 Phase 2는 건너뛴다.**

**조치**: `BootInstaller`에 `SubsystemRegistration` 리셋 추가.

### A5 (LOW) `RemoteAhead` 적용 실패 시 게이트 플래그 잔존

`PlayerSaveSync.cs:561-566` — `TryApplyRemote` 실패 시 `MarkRecoveryRequired()`만 호출하고 `s_gateComplete`를 세우지 않는다.
부팅은 `RecoveryRequired`로 빠져나오므로 hang은 아니다. 다만 플래그가 영구 false라
이후 `QueueUpload` / `RetryPending`이 재검사를 계속 건다.

### A6 (기록만) `LocalAhead` 오판 가능

`PlayerSaveSync.cs` `Classify()` — 원격 schemaVersion이 클라보다 낮으면 무조건 `LocalAhead`.
원격이 구버전이지만 진행도가 훨씬 많을 수 있다(다른 기기가 업데이트를 안 한 경우).
로컬이 방금 리셋된 빈 세이브여도 원격을 덮는다.

마이그레이션 코드가 없어 현재로선 대안이 없다. **이 경로로 원격을 덮기 전 반드시 백업하고 경고 로그를 남길 것.**
`v6 → v7` 마이그레이션이 생기면 "원격을 마이그레이션해서 채택"으로 바꿔야 한다.

---

## 3. 미검증 판정 분기 — 최우선

실측된 것은 `InSync` 하나뿐(revision 4, 로컬·원격·base 해시 일치).
**`RemoteAhead`와 `Diverged`는 실제로 데이터를 교체·백업하는 경로다. 여기가 틀리면 세이브를 잃는다.**

### 전제

- **`Save_Test` 폴더와 `test` 원격 문서를 먼저 백업**하고 시작한다
- `test` 프로필로만 실험한다. `current`는 건드리지 않는다
- 각 케이스 후 byte-for-byte 복구한다

### 케이스

| 판정 | 만드는 법 | 기대 |
|---|---|---|
| `RemoteMissing` | 콘솔에서 `users/{uid}/save/test` 삭제 | 로컬 업로드 |
| `InvalidRemote` | 콘솔에서 `payload` 한 글자 수정 | 해시 불일치 감지, 로컬 진행 + 업로드 금지 |
| `FutureSchema` | 콘솔에서 `schemaVersion` = 99 | `UpdateRequired`, 진행 차단 |
| `RemoteAhead` | `Save_Test/outgame_save.json` 이동 | 로컬 백업 후 원격 적용 |
| `Diverged` | 로컬 재화 변경 + 콘솔에서 payload도 다르게 | 로컬 유지 + 원격을 conflict sidecar로 보존 + 업로드 중단 |

`Diverged`가 안 나오고 `NoBaseConflict`가 뜨면 **메타(`outgame_sync_state_{uid}_{profileId}`) 배선이 안 된 것이다.**

### 추가 검증

- StartScene 전체 부팅 (실기)
- 두 기기 동시 변경 → transaction 충돌 감지
- pull / push 중 강제 종료
- 재설치 후 복원 (익명 UID가 바뀌므로 **새 계정으로 시작되는 것이 정상**)

---

## 4. 남은 단계

### T5 — 진실원 전환

위 검증이 전부 통과한 뒤에만 진행한다.
전환 후에도 **로컬 파일은 삭제하지 않고** 오프라인 캐시·복구본으로 남긴다.
처음부터 로컬 저장을 없애면 인증·Firestore 장애 시 게임 전체가 기동하지 못한다.

### 문서 분해

통짜 `payload` blob은 보안 규칙이 내용을 검증할 수 없다. 치트 방어에 필요한 것부터 분리한다.

```
users/{uid}/profile/main      본인 제한적 쓰기
users/{uid}/progress/main     본인 제한적 쓰기
users/{uid}/ownership/main    본인 읽기만, 쓰기는 Cloud Functions
users/{uid}/currency/main     본인 읽기만, 쓰기는 Cloud Functions
users/{uid}/rank/main         본인 읽기만, 쓰기는 Cloud Functions
```

### 재화 서버 권위

별도 설계 완료, 착수 전. 요약:

- 소비 3곳 — `CardPackOpener.cs:36` · `CardGrowthManager.cs:175` · `KeywordGrowthManager.cs:119`
- 획득 5원천 — `RewardService.cs:44` · `TournamentProgress.cs:303,322` · `RankRewardManager.cs:84` · `AlbumRewardManager.cs:80` · `OutgameDebugActions.cs:76`
- **획득이 클라 권위인 채로 소비만 서버로 옮기면 무의미하다**
- 팩 추첨·강화 RNG 판정도 서버로 가야 한다 (차감만 서버면 "돈은 빠졌는데 결과는 클라가 정함")
- `requestId` 기반 멱등성 필수
- 전투 보상 이관은 멀티플레이 로그 대조가 선행 조건

---

## 5. 미결 결정

| 항목 | 권장 | 상태 |
|---|---|---|
| 오프라인에서 팩 구매·강화 허용? | **금지** — 전투는 재화를 안 쓰므로 잃는 게 거의 없고, 허용하면 큐잉+충돌해결로 작업량 2배 | 확답 대기 |
| 기존 로컬 세이브 마이그레이션 | 출시 전이면 **전원 초기값 리셋** — 세이브에 서명이 없어 기존 잔액을 서버가 신뢰할 방법이 없다 | 미결 |
| 커밋 분할 | 위험 구간(부팅 게이트)은 별도 커밋으로 | `883172aeb`가 1,480줄 단일 커밋이라 롤백 지점 없음 |

---

## 6. 외부 블로커

- **Unity MCP 승인 해제** — 5분기 실측이 여기서 막혔다. 복구 후 재개
- **Blaze 플랜** — Cloud Functions 사용 시 필수 (재화 서버화 단계)
- **CookApps `platform.auth` 토큰 미노출** — 소셜 로그인 연동 불가.
  `PlatformUserInfo`가 `UserId`/`SocialPlatform`만 노출하고 Google idToken · Apple IdentityToken+rawNonce · Facebook accessToken을 전부 내부에서 폐기한다.
  사내(`tech@cookapps.com` / `cookapps-devops/tech-platform-auth`)에 **`PlatformUserInfo`에 토큰 필드 추가 요청** 필요. 미발송

---

## 7. 알려진 한계

- **익명 UID는 앱 재설치 시 바뀐다.** 원격 문서가 고아가 되고 진행도를 잃는다.
  소셜 로그인이 붙기 전까지 **유저에게 "백업"이라고 표시하지 말 것.** 내부 검증용이다
- 에디터와 실기는 UID 캐시가 따로라 서로 다른 UID를 쓴다. 정상 동작이다
- iOS 미검증 — `GoogleService-Info.plist` 없음
- `Assets/CookApps/Resources/CookAppsAuth.asset` 고아 파일 잔존 (`com.cookapps.auth` 패키지는 제거됨). 삭제 필요
- `Assets/google-services.json`은 클라이언트 설정이라 비밀키가 아니다(앱에 박혀 나간다). 커밋 완료

---

## 8. 파일 지도

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
  1.Repository/
    IRepository.cs · IAtomicRepository.cs · JsonFileRepository.cs · PlayerPrefsRepository.cs
  2.Domain/UserSaveData.cs        VERSION = 6, 하위 블록 9개
  3.Manager/DataSaveManager.cs    Load / Save / OnSaved / CreateSnapshot / 동기화 메타 I/O
  4.Sync/
    PlayerSaveSync.cs             pull · 판정 · 적용 · transaction push (895줄)
    PlayerSaveSyncMetadata.cs     outgame_sync_state_{uid}_{profileId}
    PlayerSaveConflictSnapshot.cs
    ESavePullState.cs · ESaveUploadState.cs

Assets/Scripts/UI/Common/LoadingCoverView.cs   부트 게이트 대기 + 상태 문구
Assets/Scenes/TEST/FirebaseAuthTest.unity      인증 단독 테스트 씬

firestore.rules · firebase.json                프로젝트 루트
docs/OutGamePlan/STRUCTURE.md                  4.Sync 계층 반영됨
```

---

## 9. 착수 순서

1. **A1** — `Initialize` try/catch. 한 줄로 최악의 실패 모드를 닫는다
2. **5분기 실측** — MCP 복구 즉시. `RemoteAhead` / `Diverged` 우선
3. **A2** — `CurrencyManager.Init`에 변경 통지 추가
4. A4 → A3 → A5
5. T5 진실원 전환 판단
6. 문서 분해 → 재화 서버 권위
