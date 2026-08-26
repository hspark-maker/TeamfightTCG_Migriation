# 로컬 세이브 → Firestore 이관 로드맵 (세이브/로드 한정)

작성 2026-08-26 · 기준 브랜치 `feature_Firestore` · 기존 현황 문서: [FIRESTORE_SAVE_MIGRATION.md](FIRESTORE_SAVE_MIGRATION.md)

## Context

지금 이 게임의 모든 데이터는 **플레이어 기기 안의 JSON 파일 하나**(`outgame_save.json`, 1.5KB)가 진실원이다. Firestore는 이미 붙어 있지만 그 파일을 **통째로 문자열에 담아 업로드하는 백업**일 뿐이다 (`users/{uid}/save/{current|test}` 문서의 `payload` 필드).

목표는 **통짜 payload를 문서 구조로 분해하고, Firestore를 유일 진실원으로 만드는 것**이다.

### 이번 범위에 넣는 것 / 빼는 것

| | 항목 |
|---|---|
| **넣음** | 스키마 분해 · 리스너 기반 읽기 · 서버 권위 쓰기 · 매니저 캐시 제거 · 환경 2벌 분리 |
| **뺌 (후속)** | **마스터 테이블(스펙시트) 업로드** · 가격/확률 대조 규칙 · 추첨·강화 서버 판정 · Cloud Functions |

마스터가 서버에 없으면 규칙이 "이 팩 가격이 1000이 맞는가"를 대조할 수 없다. 따라서 **구매 판정의 서버 이관은 마스터 업로드 이후**로 미룬다. 이 계획은 그때 그 작업이 얹히기 좋은 토대를 만드는 것까지다.

---

## 신입 개발자를 위한 Firestore 30초 정리

| 용어 | 뜻 |
|---|---|
| **문서(Document)** | JSON 오브젝트 하나. 최대 1MB. **읽기/쓰기 과금의 단위** |
| **컬렉션(Collection)** | 문서를 담는 폴더. `users/{uid}/state/wallet` = users → uid문서 → state → wallet문서 |
| **map 필드** | 문서 안의 `{"Gold": 1200, "Diamond": 30}` 같은 사전. C#의 `Dictionary` |
| **리스너(Snapshot Listener)** | 문서를 "구독"하면 서버 값이 바뀔 때마다 콜백이 온다. 폴링 불필요 |
| **배치 쓰기(WriteBatch)** | 여러 문서를 **전부 성공 or 전부 실패**로 묶는다 |
| **보안 규칙(Security Rules)** | **서버에서 돌아가는 검증 코드.** 클라가 보낸 쓰기를 통과/거절한다. 클라가 우회 불가 |

---

## 확정된 전제 (사용자 결정)

| 항목 | 결정 |
|---|---|
| 환경 분리 | **Firebase 프로젝트 2개** — `cardbattle-dev`(Test) / `cardbattle-d94f7`(Live) |
| 결제 플랜 | **Spark(무료)** — Cloud Functions 사용 불가 |
| 읽기 방식 | **실시간 리스너 구독** — 클라는 서버 문서의 거울, 값을 계산하지 않음 |
| 기존 세이브 | **출시 전 전원 리셋** — 마이그레이션 코드 안 만듦 |
| 캐싱 | 뷰 즉시성 목적 외 메모리 캐싱 금지 |
| 마스터 테이블 | **후속 작업** — 이번 범위 제외 |

---

## 핵심 설계 판단 3가지

### 판단 1 — List+인덱스 평탄화를 전부 map으로 바꾼다

현재 세이브는 `List<long> balances`(인덱스=`ECurrencyType`), `List<CardGrowthEntry> entries` 처럼 **배열에 순서로 의미를 부여**한다. `JsonUtility`가 `Dictionary`를 못 다뤄서 생긴 우회다.

Firestore SDK는 `JsonUtility`를 쓰지 않는다. `[FirestoreData]` / `[FirestoreProperty]` 어트리뷰트로 `Dictionary<string, T>`를 **map 필드에 그대로** 매핑한다. (SDK 13.15.0에 심볼 존재 확인함)

map으로 가야 하는 이유는 두 가지다.

1. **enum 순서 사고 제거** — `CurrencySaveData.balances`는 칸 순서가 `ECurrencyType` 선언 순서에 묶여 있어, enum 중간에 값을 하나 끼우면 `version`이 못 잡는 **무성 데이터 오염**이 난다(코드 주석에도 명시돼 있다). `{"Gold": ...}` 같은 이름 키면 이 위험 자체가 사라진다.
2. **나중에 규칙 검증이 가능해진다** — 보안 규칙에는 **반복문이 없다.** 배열은 순회할 수 없어 "정확히 한 칸만 바뀌었는가"를 영원히 검증 못 한다. map은 `.diff(resource.data).affectedKeys()`로 바뀐 키를 정확히 뽑는다. 지금은 마스터가 없어 가격 대조를 못 하지만, **배열로 두면 마스터가 올라와도 검증할 수 없다.**

### 판단 2 — 캐시 제거가 서버 배선의 **선행 조건**이다

캐시가 진실원인 매니저가 5개다:

| 매니저 | 캐시 필드 | 파괴 패턴 |
|---|---|---|
| `CurrencyManager` | `static long[] s_currencies` | `t_data.balances[i] = s_currencies[i]` — 델타가 아니라 **전량 덮어쓰기** |
| `OwnershipManager` | `static HashSet<int> s_owned` | `t_data.ownedCardIds = new List<int>(s_owned)` — 통째로 교체 |
| `DeckSaveManager` | `s_slots[6]` (CardData 참조) | 세이브(int)와 **타입이 다름** |
| `CardGrowthManager` | `Dictionary<int, CardGrowthEntry>` | 값이 **세이브 엔트리 객체 참조** — 캐시 변형 = 세이브 변형 |
| `KeywordGrowthManager` | `Dictionary<CardKeyword, KeywordGrowthEntry>` | 동일 |

여기에 Firestore를 먼저 붙이면 **서버가 방금 준 값을 캐시가 조용히 덮어쓴다.** 게다가 `CardGrowthManager`/`KeywordGrowthManager`는 `DataSaveManager.Load()`가 다시 불리면 **버려진 옛 `Data` 객체를 계속 참조**한다(재-Init 경로 없음). 지금은 부트 게이트 덕에 안 터지지만, 리스너로 런타임 중 서버 값을 받는 순간 터진다.

### 판단 3 — 쓰기가 비동기가 되는 것은 피할 수 없다

"서버가 진실원"이면 `CurrencyManager.Spend()`가 값을 바꾸는 자리는 **네트워크 왕복**이다. 낙관적 로컬 반영(먼저 화면을 바꾸고 나중에 서버와 맞추기)은 "메모리 캐싱 금지" 요구와 정면으로 어긋나므로 쓰지 않는다.

따라서 매니저의 변경 메서드는 `UniTask`를 반환하게 되고, 호출부(UI)도 `await` 해야 한다. **이건 판정 서버화와 무관하게 세이브 이관 자체가 요구하는 변경**이다. 대신 이 경계를 인터페이스로 뽑아두면, 나중에 마스터가 올라와 규칙 검증·Functions 판정을 넣을 때 **호출부를 다시 안 고친다.**

```
LocalPlayerStateStore      (Phase 2: 현행 JSON 파일 구현, 동작 동일)
  → FirestorePlayerStateStore (Phase 3~7: 리스너 + 배치 쓰기)
     → (후속) 마스터 규칙 검증 → Cloud Functions 판정
```

---

## Firestore 스키마

분해 기준 3축: **변경 빈도**(쓰기 과금은 문서 단위) × **원자성 필요 범위** × **검증 강도**.

| 경로 | 주요 필드 | 변경 빈도 | 현 세이브 대응 |
|---|---|---|---|
| `users/{uid}/state/wallet` | `balances` map<string,long>, `rev` long, `updatedAt` ts | **최다** | `CurrencySaveData` |
| `users/{uid}/state/collection` | `owned` map<cardId,bool>, `count`, `rev` | 중 | `OwnershipSaveData` |
| `users/{uid}/state/growth` | `cards` map<id,{lv,snack,lb}>, `keywords` map<name,long>, `rev` | 중 | `CardGrowthSaveData` + `KeywordGrowthSaveData` |
| `users/{uid}/state/progress` | `rankPoints`, `rankTier`, `tutorial` map, `rev` | 중 | `RankSaveData.points` + `TutorialSaveData` |
| `users/{uid}/state/claims` | `claimed` map<claimKey, ts> (≈55키, ~4KB) | 저 | `albumReward.claimedKeys` + `rank.claimedTiers` + 토너 낙인 |
| `users/{uid}/state/deck` | `slots` array[6] of `{name, imageKey, cardIds[6]}` | 저 | `DeckSaveData` |
| `users/{uid}/state/tournament` | `clearedNodes` map, `claimedChapters` map, `pendingRewardNodeId` | 저 | `TournamentSaveData` |
| `users/{uid}/meta/profile` | `schemaVersion`, `deviceId`, `appVersion`, `createdAt` | 희소 | 신규 |

### 리스너는 문서 7개에 대해 **1개**만 건다

`users/{uid}/state` **컬렉션 전체**에 리스너 1개를 걸면 7문서를 모두 커버한다. Firestore 과금은 리스너 개수가 아니라 **전달된 문서 스냅샷 수**로 매겨진다 — 실행당 초기 7 read, 이후 바뀐 문서만 1 read씩.

### 왜 이렇게 쪼갰는가

- **낙인(`claims`)을 한 문서에 몰아넣은 이유**: 컬렉션(문서 1개=낙인 1개)이면 UI가 "이미 받았나"를 알기 위해 55문서를 읽어야 한다(55 read). 단일 문서 map이면 1 read.
- **`deck`을 `progress`에 안 합친 이유**: 덱은 6슬롯×6장 배열이라 규칙 검증이 원리상 불가능하다. **검증 강도가 다른 데이터는 문서를 분리한다** — 나중에 규칙을 강화할 때 검증 가능한 문서가 오염되지 않는다.
- **`wallet`을 따로 뺀 이유**: 압도적으로 자주 바뀐다. 쓰기 과금이 문서 단위라, 소유·성장과 한 문서에 있으면 골드 1 변할 때마다 전부 재기록된다.

### 스키마 예시

```jsonc
// users/{uid}/state/wallet
{ "balances": { "Gold": 12400, "Diamond": 35, "Energy": 8, "Shard": 210 },
  "rev": 187, "updatedAt": <serverTimestamp> }

// users/{uid}/state/growth
{ "cards": { "1017": { "lv": 4, "snack": 12, "lb": 1 },
             "1023": { "lv": 2, "snack": 0,  "lb": 0 } },
  "keywords": { "Ranged": 3, "Taunt": 1 },
  "rev": 42 }

// users/{uid}/state/claims
{ "claimed": { "album:p:숲/2": <ts>, "rank:7": <ts>, "tour:node_3_2": <ts> },
  "rev": 11 }
```

---

## 마스터 없이도 규칙으로 강제할 수 있는 것

가격 대조는 못 해도, 이번 범위에서 **진짜 서버 강제**가 되는 항목이 있다.

| 항목 | 마스터 필요? | 규칙 |
|---|---|---|
| 남의 데이터 접근 차단 | ❌ | `request.auth.uid == uid` |
| 문서 구조·타입·크기 검사 | ❌ | `keys().hasOnly([...])`, `is int`, `size() <=` |
| **잔액 음수 금지** | ❌ | `request.resource.data.balances[c] >= 0` |
| **보상 낙인 중복 수령 차단** | ❌ | 낙인 키는 **추가만** 허용, 삭제·덮어쓰기 금지 |
| `rev` 단조 증가 (되감기·동시쓰기 차단) | ❌ | `request.resource.data.rev == resource.data.rev + 1` |
| 서버 시각 강제 | ❌ | `updatedAt == request.time` |
| 팩 가격이 정의와 일치 | ✅ **필요** | 후속 |
| 획득 카드가 팩 풀 소속 | ✅ **필요** | 후속 |
| 강화 비용·성공률 | ✅ **필요** | 후속 |

낙인 규칙 예시 — 이것만으로 **세이브 편집을 통한 보상 무한 재수령이 서버에서 막힌다**:

```javascript
match /users/{uid}/state/claims {
  allow update: if request.auth.uid == uid
    && request.resource.data.rev == resource.data.rev + 1
    // 기존 키는 하나도 사라지거나 바뀌지 않아야 한다 (추가만 허용)
    && request.resource.data.claimed.diff(resource.data.claimed)
         .affectedKeys().hasOnly(
           request.resource.data.claimed.keys().toSet()
             .difference(resource.data.claimed.keys().toSet()));
}
```

---

## 코드 경계

프로젝트 규약(`CLAUDE.local.md`)의 `Manager → interface → Service` 의존 방향을 따른다.

```csharp
/// <summary>플레이어 상태의 서버 창구. 읽기는 리스너가 채운 거울, 쓰기는 서버 커밋.</summary>
public interface IPlayerStateStore
{
    bool IsReady { get; }
    event Action<EStateSection> SectionChanged;   // Wallet/Collection/Growth/Deck/Progress/Claims/Tournament

    WalletView     Wallet     { get; }
    CollectionView Collection { get; }
    GrowthView     Growth     { get; }
    DeckView       Deck       { get; }
    ProgressView   Progress   { get; }
    ClaimsView     Claims     { get; }

    UniTask WaitReadyAsync(CancellationToken _ct);
    UniTask<EWriteResult> CommitAsync(StateMutation _mutation, CancellationToken _ct);
}
```

```csharp
/// <summary>여러 문서에 걸친 변경을 하나로 묶는다. Firestore WriteBatch에 대응.</summary>
public readonly struct StateMutation
{
    public readonly EStateSection Sections;   // [Flags] — 이 변경이 건드리는 문서들
    public readonly IReadOnlyList<FieldEdit> Edits;
}

public enum EWriteResult
{
    Committed,
    Rejected,     // 규칙 거절 (PERMISSION_DENIED)
    Conflict,     // rev 충돌 — 리스너 최신값으로 1회 재시도
    Offline,
    Failed,
}
```

### 신입이 반드시 지킬 규약 3가지

1. **`CommitAsync` 결과를 로컬에 직접 반영하지 않는다.** 반영은 리스너가 `SectionChanged`를 쏠 때만 일어난다. `Committed`는 "서버가 받았다"는 뜻이지 "화면을 바꿔라"가 아니다.
2. **매니저의 static 캐시 필드는 전부 제거**하고 `IPlayerStateStore`의 뷰를 읽는다. 뷰는 리스너가 통째로 교체하므로 전량 덮어쓰기 파괴 패턴이 원천 소멸한다.
3. **`CurrencyManager.Save()`를 따로 부르는 규약은 폐기.** 저장은 `CommitAsync`가 서버에 쓰는 그 순간이 전부다. 현재 `Earn/Spend`가 메모리만 바꾸고 호출부가 나중에 `Save()`를 부르는 규약은 **서버 권위에서 성립하지 않는다.**

---

## Phase 로드맵

각 Phase는 **끝났을 때 게임이 완전히 정상 동작하는 상태**여야 한다. Phase 3부터는 섹션별 소스 플래그(`local`/`server`)로 **재빌드 없이 롤백**한다.

| # | 목표 | 주요 파일 | 완료 판정 | 롤백 |
|---|---|---|---|---|
| **0** | **인프라만. 게임 코드 0줄 변경** — dev 프로젝트 생성, 규칙 v1(마스터 무관 항목만), `google-services.json` 2벌 + 빌드 훅, `.firebaserc` | `firestore.rules`, `Assets/Scripts/Editor/ContentProfileValidator.cs` | dev에서 익명 로그인 성공, 규칙 시뮬레이터에서 위조 쓰기 거절 | 없음 |
| **1** | **DTO 정의.** `[FirestoreData]` map DTO 7종 + `UserSaveData` ↔ DTO 변환기. **배선 안 함** (순수 타입) | `OutGame/Save/2.Domain/Dto/` 신규 | 현 세이브를 DTO로 변환 → 되돌리기 했을 때 **값 손실 0** | git revert |
| **2** | **캐시 제거 + 쓰기 비동기화.** `IPlayerStateStore` + `LocalPlayerStateStore`(현행 JSON 파일 구현). 매니저 5종 캐시 제거, 호출부 `await`화 | `CurrencyManager`, `OwnershipManager`, `DeckSaveManager`, `CardGrowthManager`, `KeywordGrowthManager`, `BootInstaller`, 호출 UI | 캐시 필드 grep 0건. **플레이 동작 100% 동일, 세이브 파일 바이트 동일** | git revert |
| **3** | **리스너 개통 + 지갑 서버화.** `FirestorePlayerStateStore`, `state` 컬렉션 리스너 1개. wallet만 서버 권위(나머지는 기존 payload 병행) | `PlayerSaveSync` 분해 시작, `FirestorePlayerStateStore` | 콘솔에서 `balances.Gold` 수정 → **앱 화면 즉시 갱신**. 잔액 음수 쓰기 → 규칙 거절 | `wallet = local` |
| **4** | **소유 서버화.** collection 문서 | `OwnershipManager`, `CardPackOpener`(쓰기 경로만) | 콘솔에서 카드 추가 → 컬렉션에 즉시 등장 | `collection = local` |
| **5** | **진행도 + 보상 낙인.** progress + claims 문서. **낙인 중복 수령 차단 규칙 적용** | `OutgameTutorialProgress`, `RankManager`, `RankRewardManager`, `AlbumRewardManager`, `TournamentProgress` | 낙인 키 삭제 쓰기 시도 → `PERMISSION_DENIED`. 같은 보상 2회 수령 불가 | `progress/claims = local` |
| **6** | **성장 서버화.** growth 문서 (키워드 → 카드 순) | `KeywordGrowthManager`, `CardGrowthManager` | 강화 후 리스너로 레벨 반영. 두 기기 동시 강화 → 늦은 쪽 `Conflict` | `growth = local` |
| **7** | **덱 + 토너먼트. 통짜 payload 폐기** | `DeckSaveManager`, `TournamentProgress`, `PlayerSaveSync` 대폭 축소 | **신규 계정 → 전 구간 플레이 → 재설치 → 상태 100% 복원** | payload 병행 2주 후 제거 |

### 순서 근거

`BootInstaller.cs:167-187`의 초기화 의존 사슬을 그대로 따랐다:

```
재화(2) → 소유(4) → 튜토(5)·랭크(6) → 키워드성장(7) → 카드성장(8) → 덱(9)
```

상류가 서버화되기 전에 하류를 서버화하면 부팅 중 "서버 값 대기"와 "로컬 값 사용"이 뒤섞여 초기화 순서가 깨진다. 성장은 재화(강화 결제)와 소유(한계돌파의 `IsOwned` 검사) 양쪽에 의존하므로 그 뒤여야 하고, 덱은 소유가 확정된 최후미다.

### 이 계획 이후 (별도 착수)

| 순서 | 작업 | 선행 조건 |
|---|---|---|
| 8 | **마스터 테이블 업로드** — SO/`SpecData.bytes` → `master/*` 문서 (에디터 메뉴 + Admin SDK 스크립트) | Phase 7 완료 |
| 9 | **규칙에 가격·풀 검증 추가** — 팩 가격 대조, 풀 소속 검증, 강화 비용 대조 | 8 |
| 10 | **Blaze 승인 → Cloud Functions 판정 이관** — 추첨·강화 RNG 서버화, 전투 정산 | 결제 플랜 |

---

## Firebase 콘솔 — 사람이 직접 할 작업

### 1. `cardbattle-dev` 프로젝트 생성
1. console.firebase.google.com → **프로젝트 추가**
2. 이름 `cardbattle-dev` → 계속 → **Google 애널리틱스 사용 안 함** → 프로젝트 만들기

### 2. Firestore 생성 — ⚠️ 리전은 영구 확정
1. **빌드 → Firestore Database → 데이터베이스 만들기**
2. **프로덕션 모드에서 시작** (테스트 모드는 30일 뒤 전면 차단되고, 그 사이 규칙이 무력하다)
3. 위치: **`asia-northeast3`(서울)**
   > **한 번 정하면 절대 못 바꾼다.** 바꾸려면 프로젝트를 새로 만들어야 한다. **먼저 `cardbattle-d94f7`의 기존 리전을 확인해 동일하게 맞춰라.**

### 3. 익명 인증
**빌드 → Authentication → 시작하기 → Sign-in method → 익명 → 사용 설정 → 저장**

### 4. Android 앱 등록
1. ⚙️ **프로젝트 설정 → 내 앱 → Android**
2. 패키지 이름 **`com.BurgerMonster.CardBattle`** (`ProjectSettings.asset:172`와 반드시 일치)
3. 닉네임 `dev`, SHA-1 **비워둠**(익명 로그인엔 불필요) → 등록 → **google-services.json 다운로드**
4. 파일 배치:
   - `Assets/Firebase/Config/dev/google-services.json`
   - `Assets/Firebase/Config/prod/google-services.json` (기존 파일 이동)
   - 빌드 직전 훅이 선택된 벌을 `Assets/google-services.json`으로 복사
   - `Assets/google-services.json`은 **`.gitignore`에 추가** — 잘못된 벌이 커밋되는 사고 방지

> 빌드 훅은 새로 만들 필요 없다. `Assets/Scripts/Editor/ContentProfileValidator.cs`가 이미 `IPreprocessBuildWithReport`를 구현하고 `BuildOptions.Development`로 Test/Live를 판정하고 있다. 여기에 복사 한 줄을 얹으면 된다.

### 5. 규칙 배포
- **CLI (권장)**: `npm i -g firebase-tools` → `firebase login` → `firebase deploy --only firestore:rules --project cardbattle-dev`
  - `firebase.json`이 이미 `firestore.rules`를 가리키므로 추가 설정 불필요
  - `.firebaserc`가 없어 프로젝트 별칭이 안 잡혀 있다 → `firebase use --add`로 dev/prod 별칭 등록
- **규칙 시뮬레이터**(규칙 탭 우상단)로 위조 쓰기가 거절되는지 **반드시** 확인

### 6. prod(`cardbattle-d94f7`)에 반복
2~5를 반복(닉네임 `prod`). **규칙은 항상 dev에서 검증 후 prod에 배포한다.**

---

## 검증 방법

| 단계 | 방법 |
|---|---|
| Phase 1 | DTO 왕복 변환 테스트 — 현 세이브 → DTO → 세이브가 **원본과 동일**한지 |
| Phase 2 | 세이브 파일 **바이트 비교** — 같은 조작을 하면 이전과 동일한 JSON이 나와야 한다 |
| Phase 3 | 콘솔에서 `wallet.balances.Gold` 수정 → **앱 화면이 즉시 갱신**되는지 (리스너 개통 증거) |
| 규칙 | **규칙 시뮬레이터**에 위조 쓰기 4종(잔액 음수, 낙인 삭제, `rev` 미증가, 남의 uid) 입력 → 전부 거절 |
| 규칙(실기) | dev 빌드로 조작 쓰기 시도 → `PERMISSION_DENIED` 로그 |
| Phase 3~6 | 각 Phase 완료 시 플래그를 `local` ↔ `server`로 토글하며 **양쪽 다 정상 동작**하는지 |
| Phase 7 | **신규 계정 → 튜토리얼 완주 → 팩 구매 → 강화 → 전투 → 앱 삭제 후 재설치 → 상태 복원** 전 구간 1회 |
| 컴파일 | 각 Phase 후 `Unity_ReadConsole`로 CS 에러 0건 확인 |

---

## 위험 · 미결

| # | 항목 | 대응 |
|---|---|---|
| **F-1** | **판정이 여전히 클라에 있다** — 마스터 미업로드 구간에는 팩 가격·추첨·강화 성공률·전투 승패 전부 클라 권위. 세이브 위조는 막히지만 **로직 위조는 안 막힌다** | 이번 범위의 명시적 한계. 후속 8~10단계에서 해소 |
| **F-2** | **`OutgameDebugActions.cs:76`이 릴리즈 빌드에 포함** — 재화 지급 디버그에 `#if UNITY_EDITOR` 없음 | **Phase 0에서 즉시** 가드 추가 (코드 1줄, 인프라 단계에 끼워 넣는다) |
| **F-3** | **호출부 비동기화 범위가 크다** — `CardDetailOverlayView.cs:1447` 등 UI가 동기 반환을 전제로 연출을 짜 놓았다 | Phase 2에서 한 번에 처리. UI에 **대기 상태 표현**(버튼 잠금 등)이 필요하며 이건 신규 작업이다 |
| **F-4** | **Spark 일일 한도** — 읽기 50k / 쓰기 20k / 저장 1GiB | 실행당 초기 read ≈ 7. 내부 테스트엔 충분하나 **오픈 베타 전 Blaze 승인 필수** |
| **F-5** | **오프라인 정책** | `PersistenceEnabled = false` 유지(로컬 캐시가 "가짜 진실원"이 되는 걸 막기 위해). 쓰기는 오프라인 시 즉시 `Offline` 반환 → UI가 "네트워크 연결 필요" 표시. 부팅은 첫 스냅샷까지 기존 `IsGateComplete` 게이트로 차단 |
| **F-6** | **동시 편집 충돌** | `rev` 단조 증가 + 규칙 강제 → 늦은 쪽 자동 거절. 클라는 `Conflict` 수신 시 리스너 최신값으로 1회 재시도 |
| **F-7** | **배열 필드는 검증 불가 (미결)** | `deck.slots`는 규칙으로 못 지킨다. 덱 조작은 경제적 이득이 없어 우선순위 낮음. **Spark 구간 방치 승인 필요** |
| **F-8** | **`profile` 유령 필드** | 실제 세이브 파일에 `"profile":{nickname,avatarId,frameId}`가 들어 있는데 `UserSaveData`에는 필드가 없다(`ProfileManager.cs:43` TODO). 로드는 되지만 다음 `Save()`에서 소실된다. Phase 1 DTO 정의 때 **넣을지 버릴지 결정 필요** |

---

## 핵심 파일

- `Assets/Scripts/OutGame/Save/4.Sync/PlayerSaveSync.cs` (895줄) — 유일한 Firestore 접점. Phase 3에서 `FirestorePlayerStateStore`로 분해되고 Phase 7에서 대부분 소멸
- `Assets/Scripts/OutGame/Save/2.Domain/UserSaveData.cs` — List+인덱스 평탄화의 원점. `[FirestoreData]` map DTO로 재설계할 대상 전체 목록
- `Assets/Scripts/OutGame/Save/3.Manager/DataSaveManager.cs` — 전체 저장(`Save()` 호출 26곳)의 중심. Phase 2에서 `IPlayerStateStore` 뒤로 물러난다
- `Assets/Scripts/Core/BootInstaller.cs:167-187` — 초기화 의존 사슬. Phase 순서의 근거이자 서비스 주입 지점
- `Assets/Scripts/Editor/ContentProfileValidator.cs` — 이미 있는 빌드 전처리 훅. google-services.json 교체를 여기 얹는다
- `firestore.rules` — 이번 범위에서는 마스터 무관 항목(소유권·rev·음수·낙인)만 강화
