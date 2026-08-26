# Firestore 세이브 이관 로드맵

> 최종 갱신: 2026-08-26 · 브랜치 `feature_Firestore`
> 이 문서는 **진행 상태 추적**용이다. 설계 근거와 구현 세부는 각 Phase 담당이 갖는다.

## 목표

아웃게임 세이브의 진실원을 로컬 JSON 파일에서 **Firestore 문서**로 옮긴다.
끝나면 Firebase 콘솔에서 유저의 재화·덱·성장 상태를 눈으로 읽고 손으로 고칠 수 있다.

## 확정된 전제

| 항목 | 결정 |
|---|---|
| 문서 구조 | 도메인별 필드 맵. `payload` 통짜 문자열 폐기 |
| 진실원 | 클라우드. 로컬 파일은 캐시로 강등 |
| 오프라인 | **불가**. 부트 시 Firestore 읽기 실패 = 게임 진입 불가 |
| 기존 v6 세이브 | 리셋. 마이그레이션 코드 없음 |
| 스키마 버전 | `UserSaveData.VERSION = 7` |

---

## Phase 현황

| Phase | 상태 | 내용 |
|---|---|---|
| **P0** 확증 | ✅ 완료 | 매퍼 계약 확인, 콘솔 준비 |
| **P1** 도메인 정리 | 🟡 코드 완료 · 검증 대기 | 레거시 제거, 프로퍼티 전환, 직렬화기 교체 |
| **P2** 필드맵 전환 | ⬜ 대기 | 클라우드 계층 재작성 |
| **P3** 실패 UX | ⬜ 대기 | 재시도 화면, 업로드 배너 |
| **P4** 규칙·운영 | ⬜ 대기 | 보안 규칙, 실기 검증, 문서 |

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

### P2 — 필드맵 전환 ⬜

- [ ] `PlayerSaveCloud` 신설 — 비동기 부트 로드, `AdoptRemote`, 디바운스 업로드, 캐시 봉투
- [ ] `DataSaveManager` 재설계 — `Load()` 제거, 원격 채택 경로로 교체
- [ ] `GameManager.Boot` 수정 — 동기 `Load()` 와 `IsSaveBlocked` 분기 삭제
- [ ] 구 `PlayerSaveSync`(896줄) 삭제, 살릴 관용구만 이식
- [ ] `link.xml` 에 세이브 타입 보존 항목 추가 (IL2CPP 리플렉션)
- [ ] P1에서 남긴 데드코드 정리 — `CardCatalog.LegacyIdOfName` / `s_legacyNameToId` (마지막 호출자가 사라졌으나 카탈로그 빌드 경로라 P1 범위 밖으로 미룸)
- [ ] `DataSaveManager.s_saveBlocked` / `IsSaveBlocked` 죽은 경로 정리 — 버전 분기 제거로 true가 되는 경로가 없어졌다 (`GameManager.cs:76` 업데이트 차단 분기도 함께)
- [ ] `TryApplyRemote` 자기검증 재검토 — 현재 Dictionary 직렬화 순서가 round-trip에서 보존되는 것에 의존한다. 불안정하면 역직렬화 후 필드 비교로 교체
- [ ] **초기 골드 재지급 경로** — `CreateSnapshot()` 이 `CurrencyManager.Init()` 보다 먼저(SyncingSave 단계) 찍혀 신규 계정의 최초 payload가 `balances:{}` 로 올라간다. 그 문서를 pull해 적용하면 `Init` 이 또 신규로 판정해 100골드를 재지급한다(기기 교체·재설치). "빈 맵 = 신규" 센티널을 명시적 플래그로 교체
- [ ] **중첩 컬렉션 null 정규화** — `Parse` 가 최상위 슬롯만 채우고 중첩 컬렉션은 안 본다. 콘솔에서 사람이 `null` 을 넣으면 `TournamentProgress.ClearedNodeIds` · `AlbumRewardManager.ClaimedKeys` · `RankRewardManager.ClaimedTiers` 에서 NRE. `Parse` 에서 한 번에 정규화
- [ ] **KeywordGrowth 키 규약 통일** — Currency는 enum 이름(`"Gold"`), KeywordGrowth는 enum 정수값(`"1"`,`"2"`). 콘솔 가독성이 목적이었으므로 이름 쪽으로 맞출 것
- [ ] 데드코드 `OwnershipManager.HasAnyOwnedSaved()` — 유일한 소비자였던 `MigrateLegacyCompletion` 제거로 호출자 0
- [ ] 구 세이브 파싱 실패 시 `SAVE_KEY` 원본이 남아 첫 `Save()` 전까지 매 부트 LogError 반복 (실측 확인됨)

**완료 판정 (이번 작업의 핵심 수용 기준)**:
콘솔에서 `currency.Gold` 를 손으로 고친다(타입 integer 유지) → 앱 재시작 → 로비 상단바에 그 값이 뜬다.

---

### P3 — 실패 UX ⬜

- [ ] `4.Sync` 잔재 삭제 (`ESaveReconcileDecision`, `PlayerSaveSyncMetadata`, `PlayerSaveConflictSnapshot`)
- [ ] `RecoveryRequired` 재시도 버튼 — **현재 `LoadingCoverView` 에 재시도 버튼이 없다. 신규 작업**
- [ ] 업로드 실패 배너 (3회 연속 실패부터)
- [ ] pause/quit flush 재검증
- [ ] 부트 타임아웃 대소관계 정리 — auth+read 재시도 총합(~13초)이 `LoadingCoverView` 대기 타임아웃보다 길면 재시도가 끝나기 전에 실패 화면으로 튄다

**완료 판정**: 비행기 모드 부팅 → 재시도 화면 → 네트워크 복구 후 재시도 → 정상 진입.
플레이 중 비행기 모드 → 배너 → 복귀 시 자동 업로드 → 콘솔 `revision` 증가.

---

### P4 — 규칙·운영 ⬜

- [ ] `firestore.rules.prod` 새 스키마로 재작성 (최상위 전수 검증 + 도메인 맵은 깊이 1까지)
- [ ] `revision == resource.data.revision + 1` 이 `FieldValue.Increment` 적용 후 값으로 평가되는지 rules 에뮬레이터로 실증
- [ ] 개방 규칙(`allow read, write: if true`) 제거하고 배포
- [ ] 스펙 업로더 권한 대안 결정 — **미결** (아래 참조)
- [ ] Android IL2CPP 실기 1회 검증
- [ ] `docs/OutGamePlan/FIRESTORE_SAVE.md` 신입 개발자용 문서 작성
- [ ] `docs/OutGamePlan/STRUCTURE.md` 세이브 절 갱신

**완료 판정**: 다른 uid 문서 읽기가 `PERMISSION_DENIED`, 정상 왕복은 통과. 실기 빌드 세이브 왕복 성공.

---

## 미결 결정

| # | 항목 | 언제까지 | 선택지 |
|---|---|---|---|
| 1 | **저장 API 비동기 축** | P2 착수 전 | (a) `Save()` void 유지 + `FlushAsync()` 추가 (b) 전면 `SaveAsync` 전환 (c) (a) + `IRepository` 까지 비동기화. `Save()` 와 `IRepository` 시그니처는 한 덩어리 결정이다 |
| 2 | **스펙 업로더 권한** | P4 착수 전 | `Editor/SpecFirestoreUploader` 가 API key로 `specs/**` 에 쓴다. 운영 규칙은 이 경로를 차단하므로 배포 즉시 죽는다. (a) 툴 전용 계정 uid에만 쓰기 허용 (b) 서비스 계정 + Admin SDK CLI로 이전 |
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

프로젝트 `cardbattle-d94f7`

- [x] `빌드 > Authentication > Sign-in method > 익명` 사용 설정
- [x] `빌드 > Firestore Database > 데이터베이스 만들기` (asia-northeast3 Seoul)
- [ ] 규칙 배포 — 콘솔에서 직접 편집 금지. 로컬에서 `firebase deploy --only firestore:rules`
- [ ] (선택) `색인 > 단일 필드 > 예외 추가` 로 `cardGrowth`/`ownership`/`deck` 자동 색인 해제
- [ ] (iOS 진행 시) `프로젝트 설정 > 일반 > 앱 추가 > iOS` → `GoogleService-Info.plist` → `Assets/` 직하

**내 세이브 문서 찾는 법**
`Authentication > Users` 에서 UID 복사 → `Firestore Database > 데이터` → `envs` → `test` → `users` → UID → `save` → `current`

> ⚠️ 현재 `firestore.rules` 는 전면 개방 상태다(`allow read, write: if true`). 누구나 남의 세이브를 읽고 쓸 수 있다.
> 운영 배포 전 반드시 P4를 끝낼 것.

---

## 위험 요소

1. **콘솔 수동 편집이 타입을 깨뜨린다** — 숫자 편집 시 타입 드롭다운을 `number`(double)로 두면 int64가 아니라 double로 저장되어 역직렬화가 실패한다. 도메인 단위 try/catch로 깨진 도메인만 기본값 복구하도록 방어한다
2. **익명 uid 분실 = 세이브 분실** — 앱 삭제/키체인 초기화로 uid가 바뀌면 기존 문서가 고아가 된다
3. **부트 지연이 곧 진입 지연** — 오프라인 폴백이 없어 느린 네트워크가 그대로 노출된다
4. **매 저장 = 전체 문서 재작성** — Firestore 과금은 쓰기 횟수 기준이라 요금엔 영향 없으나 모바일 트래픽에는 영향. 3초 디바운스가 최악을 초당 1/3회로 묶는다
5. **`JsonUtility` 잔존 위험** — 프로퍼티 전환 후 어딘가에 `JsonUtility` 가 남아 세이브 타입을 다루면 조용히 `{}` 를 뱉는다. 컴파일러가 못 잡는다
