# 서버 권위 실태 점검 — 아웃게임

> 실측 2026-08-28 · 브랜치 `feature_Firestore` · HEAD `9eaa8b816`
> 질문: "아웃게임 도메인 관리와 검증이 클라이언트가 아닌 서버에서 이루어지고 있는가"
> 이 문서는 **실태 기록**이다. 계획·설계 근거는 `SERVER_VALIDATION_ROADMAP.md`(R축)와
> `CURRENCY_SERVICE_HANDOFF.md`(C축)가 갖는다. 여기서 방향을 새로 정하지 않는다.

조사 범위: `functions/src`(13 callable) · `functions-currency/src`(2 callable) · `firestore.rules` ·
`Assets/Scripts/OutGame/` 15도메인 · 클라 callable 호출부 전수.

---

## 결론

**명령 층은 대부분 서버로 넘어갔으나, 규칙 층이 아직 클라 쓰기를 막지 않는다.** 이 어긋남이 실태의 핵심이다.

| 층 | 재화(지갑 문서) | 세이브 문서(슬롯 9종) |
|---|---|---|
| 명령 | 서버 callable 전담 | 도메인별로 갈린다(아래 표) |
| 규칙 | `allow write: if false` (`firestore.rules:149`) | **`allow update` 가 소유자에게 열려 있다** (`firestore.rules:132`) |
| 슬롯 동결 | 해당 없음 | **`affectedKeys` 0건 — 미적용** |

클라는 `PlayerSaveCloud` 에서 `SetOptions.Overwrite` 로 세이브 문서를 통째로 덮는다.
따라서 **재화는 서버가 소유하지만, 그 재화를 써서 얻는 결과물(소유·성장·진행도)은 클라가 직접 쓸 수 있다.**

한 가지는 확실히 닫혀 있다. 클라 코드 전체에서 Firestore 직접 쓰기(`SetAsync`/`UpdateAsync`/`RunTransactionAsync`)를
부르는 자리가 `OutGame/Save/4.Cloud/` 바깥에 **0건**이고, 도메인 상태를 바꾸는 호출은 전부
`ServerSaveCommands.InvokeAsync` 를 거친다. 문제는 우회 경로가 아니라 **세이브 업로드 그 자체가 아직 자유롭다**는 점이다.

---

## 1. 도메인별 판정

| 도메인 | 주체 | 근거 심볼 |
|---|---|---|
| 재화 | **서버 전용** | `CurrencyManager.Adopt` 가 유일한 writer · 호출부는 `WalletCloud` 하나 |
| 카드팩 | **서버** | `openPack`(`packs/packDraw.ts:drawPack`, `node:crypto.randomInt`). 클라 `CardPackData.ResolvePool` 은 확률 표시와 `CardPackOpener.Precheck` 에만 남았다 |
| 카드 강화 | **서버** | `enhanceCard`(`growth/enhanceRules.ts:rollSucceeded`). 클라는 레벨을 대입하지 않고 `ServerSlotRehydrator` 재수화에 맡긴다 |
| 키워드 강화 | **서버** | `enhanceKeyword` |
| 전투 보상 | **서버** | 싱글 `claimBattleReward` · 멀티 `claimPayout.ack`. `RewardService.CalculateReward` 는 팝업 표시용 예상액이라 잔액을 만지지 않는다 |
| 도감 보상 | **서버** | `claimReward(ownerType="Album")` 이 `completionTable.ts` 로 완성도를 재계산한다 |
| **한계돌파(간식)** | **클라** | `CardGrowthManager.Snack.cs:TryLimitBreak`. **대응 callable 이 없다.** 적립은 서버(`OpenPackResult.snack`)인데 소비만 클라에 남았다 |
| 소유 | **혼재** | 팩·신규계정은 서버(`openPack` · `freshAccount.ts`), 튜토리얼 지급·`StarterDeck.GrantIfNoDeck`·디버그는 `OwnershipManager.Grant` 계열이 직접 쓴다 |
| 랭크 | **혼재** | 싱글 `RankManager.ApplyBattleResult`(클라) · 멀티 `submitMatchResult` → `ApplyServerPayout`(서버). 승급전 판정 `ApplyPromoResult` 도 클라다 |
| 토너먼트 | **혼재** | 클리어 확정·지급은 `claimReward`(서버), 해금 판정 `TournamentProgress.StateOf` 와 `pendingRewardNodeId` 낙인은 클라다 |
| 덱 | **클라** | `DeckSaveManager.SaveSlot` 등이 직접 저장한다. `lockDeck` 은 멀티 매치 진입 검증이고 덱 저장 경로에서 호출되지 않는다 |
| 튜토리얼 | **클라** | `OutgameTutorialProgress.CommitStep` · `TutorialStepExecutor.AcquireCard`. 강화 무료 한 방 소진만 서버 원장(`grants/current`)이 갖는다 |
| 프로필 | **클라** | `ProfileManager.Apply` → `Persist`. `IsAvatarOwned`/`IsFrameOwned` 는 현재 무조건 true 를 돌려준다 |
| 매치메이킹 | **클라** | `FakeMatchmaker` 가 상대를 고른다. `createMatch` 는 pairingKey 기반 만남일 뿐이다 |

---

## 2. callable 실태 (15개)

`functions/src/index.ts` 13개 + `functions-currency/src/index.ts` 2개.

| 함수 | 서버가 **판정** | 서버가 **검증** | 클라 입력을 **그대로 신뢰** |
|---|---|---|---|
| `openPack` | 추첨 결과 · 가격 차감 · 중복→간식 | 랭크 잠금 · 잔액 · 팩 행 존재 · 카탈로그 | 판정 입력인 `rank.points` 가 클라가 쓴 세이브 값이다 |
| `enhanceCard` | 성공률 롤 · 비용 · 레벨 | 최대레벨 · 잔액 · 룰 표 · 무료 한 방 소진 | cardId(카탈로그 대조 없음) |
| `enhanceKeyword` | 비용 · 레벨 | 키워드 화이트리스트 · 최대레벨 · 잔액 | keyword 플래그값 |
| `claimReward` | 보상 액수 · 낙인 append | 기수령 · 자격(랭크 점수·챕터 완주·도감 완성) · 표 결손 시 fail-closed | 자격 근거 `rank.points`·`clearedNodeIds`·`ownership` 이 전부 클라 세이브 값이다 |
| `claimBattleReward` | 금액(`payout.ts:computeCurrencyPayout`) · 지갑 가산 | 재화 키 · 금액 > 0 · remaining 클램프 | **`won`·`remaining` 을 그대로 믿는다. 전투를 하지 않아도 호출된다** |
| `claimPayout` | 지급 대상 필터 · 지갑 가산 | uid·matchId·status · 20개 상한 · 금액은 서버 생성 문서만 | matchIds 목록(권한 밖은 무시) |
| `submitMatchResult` | 양측 제출 대조 · 보상액 · 랭크 점수 · payout 문서 | 커맨드로그 해시·길이 · nonce↔matchId · 제출 변경 금지 | `won`·`myRemaining`(양측이 모순일 때만 flagged) |
| `lockDeck` | 덱 해시 재계산 대조 | **덱 규칙 전량**: 6장·중복·정렬·소유·레벨/hpBonus/진화/키워드 기대값 재계산 | 대조 기준인 `ownership`·`cardGrowth`·`keywordGrowth` 가 클라 쓰기 산물이다 |
| `createMatch` | matchId·seed(`randomBytes`) · 슬롯 판정 | contentFingerprint 64hex · 정원 2명 | pairingKey |
| `ensureAccount` | 스타터 카드 · 골드 100 · 초기 슬롯 | deviceId 32hex · appVersion · 문서 기존재(멱등) | deviceId·appVersion 값 |
| `ensureWallet` | v7→v8 이관 잔액 · 승급 | `assertMigratableSchema` · 세이브 선행 존재 | 없음 |
| `devGrantCurrency` (codebase `currency`) | 지갑 가산 | env=="test" · 재화 키 · 양의 정수 | amount(test 한정) |
| `ping` · `currencyPing` · `devBumpRevision` | 진단·디버그 | env 화이트리스트 · test 강제 | — |

**마스터 데이터는 서버가 자체 보유한다.** 클라가 보낸 수치를 마스터로 쓰지 않고 전부
`envs/{env}/specs/{table}/rows` 에서 읽는다(`packs/packSpecReader.ts:readSpecRows`, 5분 캐시):
`Card`/`Card_Test` · `CardPack` · `CardPackDrop` · `RankGrade` · `Reward` ·
`CardEnhanceRule`/`CardEnhance`/`KeywordEnhance` · `AlbumEntry`/`TournamentChapter`.
폴백은 둘뿐이고 둘 다 `logger.error` 를 남긴다(`packs/rankGrade.ts:FALLBACK_ENTRY_POINTS` ·
`save/starterPool.ts:FALLBACK_STARTER_CARD_IDS`). 나머지는 표를 못 읽으면 fail-closed 로 거절한다.

---

## 3. 규칙 실태 (`firestore.rules`)

| 경로 | read | write | 판정 |
|---|---|---|---|
| `users/{uid}/save/current` | 소유자 | create·delete 거부, **update 는 소유자에게 열림** | 키 집합·타입·크기 상한과 `revision+1` 단조 증가만 본다 |
| `users/{uid}/wallet/current` | 소유자 | `if false` | 서버 권위가 성립한다 |
| `.../wallet/ledger/{txId}` | 거부 | 거부 | Admin SDK 전용 |
| `users/{uid}/grants/current` | 소유자 | `if false` | 무료 한 방 원장 |
| `envs/{env}/specs/**` | 로그인 사용자 | `isAdmin()` | 마스터 데이터 위조 불가 |
| `envs/{env}/matches/**` | 거부 | 거부 | Admin SDK 전용 |
| `matchPairings` · `matchLocks` · `payouts` · `payoutState` | — | — | 규칙에 항목이 없어 말미 catch-all 전면 거부에 걸린다 |

세이브 문서에서 클라가 임의로 쓸 수 있는 슬롯은 아홉 개 전부다: `ownership` · `cardGrowth` ·
`keywordGrowth` · `rank` · `albumReward` · `tournament` · `deck` · `tutorial` · `profile`.

매치 4종(`createMatch`·`lockDeck`·`submitMatchResult`·`claimPayout`)은 `enforceAppCheck: false` 로 명시돼 있다.

---

## 4. 지금 열려 있는 것

### (1) 서버 검증이 자기신고 위에 서 있다

서버는 자격을 성실히 재계산하지만, 그 **입력이 클라가 쓴 세이브 값**이다.

- `openPack` 의 랭크 잠금과 `claimReward(Rank)` 의 자격은 `rank.points` 를 읽는다
- `claimReward(Album)` 의 완성 판정은 `ownership.cardIds` 를 읽는다
- `lockDeck` 의 소유·성장 검증도 같은 세이브 문서를 본다. 위조 세이브와 일관된 덱은 통과한다

즉 세이브를 먼저 위조하면 서버 검증을 정상적으로 통과한다. **닫는 자리는 C7(룰 조이기)이다.**

### (2) 멱등 키가 없어 재시도가 곧 재과금이다

`mutateSave`(`save/saveDocument.ts`)는 클라의 기대 revision 을 받지 않고 현재값을 읽어 +1 할 뿐이다.

- `openPack` · `enhanceCard` · `enhanceKeyword` — 응답만 유실된 클라가 재시도하면 다시 뽑고 다시 차감한다.
  차감과 지급이 한 트랜잭션이라 "돈만 나가는" 상태는 생기지 않는다
- `claimBattleReward` — **낙인이 전혀 없다.** matchId 도 받지 않아 같은 페이로드를 반복 호출하면 무한 지급된다
- `claimReward` 는 낙인 덕에 우연히 안전하다(설계가 아니라 부작용이다)
- 지갑 원장 `walletStore.ts:ledgerEntry` 는 코덱만 있고 **호출부가 0건**이라 감사 기록이 남지 않는다

**닫는 자리는 C8(재화 원장)이다.**

### (3) 디버그 치트가 릴리스 빌드에 들어간다

`#if UNITY_EDITOR || DEVELOPMENT_BUILD` 가드 안에 있는 것은 재화·서버 진단 계열뿐이다
(`GrantCurrency` · `PingServer` · `BumpServerRevision` · `ProbeRuleDenials`).

가드 **밖**에 있는 것: `OutgameDebugActions.UnlockAllCards`/`RevokeAllCards` ·
`MaxCardGrowth`/`ResetCardGrowth` · `RaiseTier`/`LowerTier`/`ResetTier`/`JumpToPromoStandby` ·
`SkipTutorial`/`ResetTutorial` 계열 · `UI/Debug/UnlockAllCardsButton`.

### (4) 이중 진실원 4건

- **`cardGrowth` 슬롯의 필자가 둘이다.** 서버 `enhanceCard`/`openPack` 과 클라 `TryLimitBreak` 가 같은 슬롯을 쓴다.
  서버가 `readGrowthEntries` 로 클라 업로드분을 보존해 지금은 깨지지 않지만,
  한계돌파는 서버가 검증하지 않는 유일한 성장 축이다
- **`rank.points` 의 필자가 둘이고 규칙도 두 벌이다.** 싱글(클라)·멀티(서버)로 갈리고,
  `RankManager.PreviewBattleResult` 가 `ApplyBattleResult` 의 클램프·승급 규칙을 줄 단위로 다시 구현한다
  (`RankManager.cs:166` 대 `:209`). 승급 곡선도 클라 `RankConfig` 와 서버 `RankGrade` 표 양쪽에 있다
- **`ownership.cardIds` 의 필자가 둘이다.** 튜토리얼 지급 카드 목록의 진실원이 클라 시나리오 SO 다
- **토너먼트 해금 판정이 양쪽에 있다.** 클라 `TournamentProgress.StateOf` 와 서버 `claimReward.ts` 가
  같은 판정을 각자 수행하고, 정점 재도전 가능 여부는 클라 판정만 본다

### (5) 재수화 구멍

`ServerSlotRehydrator.Rehydrate` 는 `Ownership`·`KeywordGrowth`·`CardGrowth` 세 슬롯만 재수화한다.
`rank`·`albumReward`·`tournament` 매니저는 `DataSaveManager.Data` 를 매번 읽어 지금은 맞지만,
`deck`·`tutorial`·`profile` 은 static 캐시라 서버가 그 슬롯을 쓰기 시작하면 옛 값이 남는다(파일 안에 TODO 로 표기돼 있다).

### (6) 죽은 코드 1건

`CardGrowthManager.AddSnack` 은 정의만 있고 호출자가 0건이다. 간식 적립은 서버
`functions/src/growth/cardGrowth.ts:addSnack` 이 `openPack` 안에서 수행한다.
주석은 여전히 "적립 지점은 `CardPackOpener` 하나"라고 말한다.

---

## 5. 문서 상태 정정

- **`SERVER_VALIDATION_ROADMAP.md` 는 낡았다.** 마지막 갱신이 `5748cfa8b` 이고, 그 뒤로 C3.5~C6.6
  (강화 2종 서버화 · 도감/챕터 수령 서버화 · 전투 보상 · 재화 지갑 분리)이 들어왔다. 그 문서의 슬롯 표는
  여전히 `currency` 를 세이브 슬롯으로 적지만 실제로는 별도 지갑 문서로 떠났다(세이브 슬롯 10 → 9).
  **현재 실태의 정본은 `CURRENCY_SERVICE_HANDOFF.md` 다.**
- 위 구멍 가운데 (1)은 C7, (2)는 C8 로 이미 계획에 서 있다.
  **계획에 없는 미결은 한계돌파(Snack) callable 하나**이고, 인계 문서도 이것을
  "재화 writer 0 집계에는 안 잡히지만 성장 판정 소유권 목표에는 걸린다"고 미결로 표시해 두었다.

---

## 6. 이 판정을 다시 내리는 방법

브랜치가 움직이면 아래 넷만 다시 재면 된다.

1. **클라 직접 쓰기** — `Assets/Scripts/` 에서 `SetAsync|UpdateAsync|DeleteAsync|RunTransactionAsync` 를 grep 해
   `OutGame/Save/4.Cloud/` 바깥에 결과가 있는지 본다. 0이어야 한다
2. **클라 명령 호출부** — 같은 범위에서 `InvokeAsync|InvokeReadOnlyAsync|CallAsync` 를 grep 하면
   어느 도메인이 서버로 갔는지가 그대로 나온다
3. **규칙 동결** — `firestore.rules` 에서 `affectedKeys` 를 grep 한다. 0건이면 세이브 슬롯이 전부 열려 있다는 뜻이다
4. **재화 이동 지점** — 서버에서 `nextWallet` 호출부를 grep 한다. C6.1 이 그것을 지갑 상태의 유일한 출구로 만들어서
   한 줄 grep 으로 전수가 잡힌다

**`devGrantCurrency` 를 `functions/src` 에서만 찾으면 "서버에 없다"는 오판이 나온다.** C6.6 이
`functions-currency/` 로 옮겼으므로 두 codebase 를 함께 훑어야 한다.

판정을 틀리게 만드는 나머지 함정은 `CURRENCY_SERVICE_HANDOFF.md` 의 같은 이름 절에 정리돼 있다
(배포 로그는 호출 가능을 증명하지 않는다 · `functions:log` 는 3~4분 늦는다 · 룰 하네스는 종료코드가 거짓말한다 등).
