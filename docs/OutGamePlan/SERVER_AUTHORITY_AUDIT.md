# 서버 권위 실태 점검 — 아웃게임

> 실측 2026-08-31 · 브랜치 `feature_Firestore` · HEAD `2df116ca9` (직전 실측 2026-08-28 · `9eaa8b816`)
> 질문: "아웃게임 도메인 관리와 검증이 클라이언트가 아닌 서버에서 이루어지고 있는가"
> 이 문서는 **실태 기록**이다. 계획·설계 근거는 `SERVER_AUTHORITY_ROADMAP.md`(P축)와
> `CURRENCY_SERVICE_HANDOFF.md`(C축)가 갖는다. 여기서 방향을 새로 정하지 않는다.

조사 범위: `functions/src`(16 callable) · `functions-currency/src`(2 callable) · `firestore.rules` ·
`Assets/Scripts/OutGame/` 15도메인 · 클라 callable 호출부 전수.

---

## 결론

**명령 층은 대부분 서버로 넘어갔으나, 규칙 층이 아직 클라 쓰기를 막지 않는다.** 이 어긋남이 실태의 핵심이다.

| 층 | 재화(지갑 문서) | 세이브 문서(슬롯 9종) |
|---|---|---|
| 명령 | 서버 callable 전담 | 도메인별로 갈린다(아래 표) |
| 규칙 | `allow write: if false` (`firestore.rules:123`) | **`allow update` 가 소유자에게 열려 있다** (`firestore.rules:106`) |
| 슬롯 동결 | 해당 없음 | **`affectedKeys` 0건 — 미적용** |

클라는 `PlayerSaveCloud` 에서 `SetOptions.Overwrite` 로 세이브 문서를 통째로 덮는다.
따라서 **재화는 서버가 소유하고 성장·소유·진행도의 판정도 서버로 넘어갔지만, 그 결과가 적히는 슬롯은
아직 클라가 정규 업로드 경로로 덮어쓸 수 있다.** 명령 층에서 닫힌 것을 규칙 층이 다시 열어 두는 셈이다.

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
| 한계돌파(간식) | **서버** | `limitBreakCard`(`commands/limitBreakCard.ts` · 판정 `growth/limitBreakTable.ts` · `growth/cardGrowth.ts`). 소유·간식 잔량·단계 곡선을 서버가 재계산하고 `cardGrowth` 슬롯 갱신까지 한 트랜잭션이다. 클라 `CardGrowthManager.TryLimitBreakAsync` 는 낙관 선검사 뒤 `LimitBreakCommand` 로 넘기고 세이브에 대입하지 않는다 |
| 소유 | **서버**(정상 경로) | 지급 callable 은 셋뿐이다 — `openPack` · `grantTutorialCards`(팩 진실원은 `CardPack`/`CardPackDrop` 시트) · `ensureAccount`. `StarterDeck.GrantIfNoDeck` 은 `IsOwned` 로 읽기만 한다. **잔여 로컬 쓰기는 리테일 치트뿐이다**: 디버그 3건(`OutgameDebugActions:234`·`:240` · `UnlockAllCardsButton:25`) · 되감기 3건(`OutgameTutorialRewind:141`·`:165`·`:173`) |
| 랭크 | **혼재** | 싱글 `RankManager.ApplyBattleResult`(클라) · 멀티 `submitMatchResult` → `ApplyServerPayout`(서버). 승급전 판정 `ApplyPromoResult` 도 클라다 |
| 모험 | **서버** | 격파 신고 `reportAdventureWin` 이 선행 사슬(`prevNodeId`)과 랭크 잠금(`requiredPoints`)을 재판정해 `pendingRewardNodeId` 낙인을 세우고, 클리어 확정·지급은 `claimReward` 가 한다. 클라 `MarkRewardPending` 은 삭제됐고 `StateOf`·`CanEnter` 는 표시용 낙관으로만 남았다. 슬롯 쓰기는 `ResetForDebug`(호출부 0건)와 되감기 초기화(`OutgameTutorialRewind:85`)뿐이다 |
| 덱 | **클라** | `DeckSaveManager.SaveSlot` 등이 직접 저장한다. `lockDeck` 은 멀티 매치 진입 검증이고 덱 저장 경로에서 호출되지 않는다 |
| 튜토리얼 | **혼재** | 진행도는 클라다(`OutgameTutorialProgress.CommitStep`). 다만 카드 지급은 서버로 갔고(`TutorialStepExecutor` 5경로 → `TutorialGrantCommand.GrantAsync(packId)`), 강화 무료 한 방 소진은 서버 원장(`grants/current`)이 갖는다 |
| 프로필 | **클라** | `ProfileManager.Apply` → `Persist`. `IsAvatarOwned`/`IsFrameOwned` 는 현재 무조건 true 를 돌려준다 |
| 매치메이킹 | **클라** | `FakeMatchmaker` 가 상대를 고른다. `createMatch` 는 pairingKey 기반 만남일 뿐이다 |

---

## 2. callable 실태 (18개)

`functions/src/index.ts` 16개 + `functions-currency/src/index.ts` 2개.
직전 실측 이후 세 개가 늘었다 — `grantTutorialCards`(P2) · `reportAdventureWin`(P4) · `limitBreakCard`(P3).

| 함수 | 서버가 **판정** | 서버가 **검증** | 클라 입력을 **그대로 신뢰** |
|---|---|---|---|
| `openPack` | 추첨 결과 · 가격 차감 · 중복→간식 | 랭크 잠금 · 잔액 · 팩 행 존재 · 카탈로그 | 판정 입력인 `rank.points` 가 클라가 쓴 세이브 값이다 |
| `enhanceCard` | 성공률 롤 · 비용 · 레벨 | 최대레벨 · 잔액 · 룰 표 · 무료 한 방 소진 | cardId(카탈로그 대조 없음) |
| `enhanceKeyword` | 비용 · 레벨 | 키워드 화이트리스트 · 최대레벨 · 잔액 | keyword 플래그값 |
| `limitBreakCard` | 다음 단계 · 간식 차감 · HP 보너스 | 소유(`readOwnedIds`) · 최대 단계 · 간식 잔량 · `CardLimitBreak` 표(결손 시 `RuleUnavailable`) · 영수증 멱등 | cardId |
| `grantTutorialCards` | 줄 카드 집합(드롭 풀 전량) | `CardPack.price == 0`(결손도 유료로 간주) · 팩 행 존재 · 카탈로그 대조 | packId — **튜토리얼을 하지 않아도 무료 팩을 바로 호출할 수 있다**(순서·시점은 못 막는다) |
| `reportAdventureWin` | 낙인(`pendingRewardNodeId`) | 선행 사슬 `prevNodeId`(뿌리 1개가 아니면 `ChainUnreadable` fail-closed) · 랭크 잠금 `requiredPoints` · 기클리어 · 미수령 잔존 | nodeId — **`won` 을 받지 않는다**(패배는 호출하지 않는 것이 계약). 잠금 판정 입력인 `rank.points` 는 여전히 클라 세이브 값이다 |
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
`CardEnhanceRule`/`CardEnhance`/`KeywordEnhance` · `CardLimitBreak` · `AlbumEntry` ·
`AdventureChapter`(파서가 `completionTable.ts` 에서 `adventureTable.ts` 로 이사했고, 완주 모수뿐 아니라
해금 사슬 `prevNodeId` 와 랭크 잠금 `requiredPoints` 도 이 표에서 읽는다).
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
`keywordGrowth` · `rank` · `albumReward` · `adventure` · `deck` · `tutorial` · `profile`.

매치 4종(`createMatch`·`lockDeck`·`submitMatchResult`·`claimPayout`)은 `enforceAppCheck: false` 로 명시돼 있다.

---

## 4. 지금 열려 있는 것

### (1) 서버 검증이 자기신고 위에 서 있다

서버는 자격을 성실히 재계산하지만, 그 **입력이 클라가 쓴 세이브 값**이다.

- `openPack` 의 랭크 잠금 · `claimReward(Rank)` 의 자격 · `reportAdventureWin` 의 챕터 잠금은 `rank.points` 를 읽는다
- `claimReward(Album)` 의 완성 판정은 `ownership.cardIds` 를 읽는다
- `lockDeck` 의 소유·성장 검증도 같은 세이브 문서를 본다. 위조 세이브와 일관된 덱은 통과한다
- `claimReward(Adventure)` 의 정점 자격은 `pendingRewardNodeId` 낙인의 존재다. 낙인을 *만드는* 판정은
  서버로 갔지만 슬롯이 열려 있어, 변조 클라가 정규 업로드로 세운 낙인이 표에 실재하는 정점을 가리키면
  `hasNode` 방어선을 통과하고 사슬 검증은 상속되지 않는다

즉 세이브를 먼저 위조하면 서버 검증을 정상적으로 통과한다. **닫는 자리는 P6(슬롯 동결)이다.**
P2·P3·P4 가 판정을 서버로 옮겼어도, 그 판정이 적히는 슬롯을 규칙이 잠그기 전까지는
"클라 코드가 판정하지 않는다"와 "클라가 그 값을 쓸 수 없다"가 같은 말이 아니다.

### (2) 멱등 키가 없어 재시도가 곧 재과금이다

`mutateSave`(`save/saveDocument.ts`)는 클라의 기대 revision 을 받지 않고 현재값을 읽어 +1 할 뿐이다.

C8 이 영수증(`wallet/current/receipts/{txId}`)을 세워 이 절의 절반이 닫혔다. `walletStore.ts` 의
구 원장 코덱 `ledgerEntry` 는 사라지고 `receiptRef`·`writeReceipt` 가 그 자리를 대신하며, 호출부가 실재한다.

- `openPack` · `enhanceCard` · `enhanceKeyword` · `limitBreakCard` — 클라가 스탬프한 `txId` 를
  `mutateSave` 의 영수증이 받아 재시도가 중복 과금으로 이어지지 않는다
- `claimBattleReward` — **여전히 낙인이 없다.** matchId 도 받지 않아 같은 페이로드를 반복 호출하면 무한 지급된다
- `claimReward` 는 낙인 덕에 우연히 안전하다(설계가 아니라 부작용이다)

**남은 것을 닫는 자리는 P1(`claimBattleReward` 멱등)이다.**

### (3) 디버그 치트가 릴리스 빌드에 들어간다

`OutgameDebugActions.cs` 의 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 는 **74~174행 한 구간뿐**이고,
그 안에 든 것은 서버 진단 셋(`PingServer` · `BumpServerRevision` · `ProbeRuleDenials`)이다.
`OutgameDebugOverlay.cs` 는 파일 전체가 가드 안에 있다.

가드 **밖**에 있는 것(리테일 빌드에도 컴파일된다): `GrantCurrency` 와 `GrantGold`/`GrantDiamond`/
`GrantEnergy`/`GrantShard`(직전 실측이 가드 안이라고 적은 것은 오기다) · `UnlockAllCards`/`RevokeAllCards` ·
`MaxCardGrowth`/`ResetCardGrowth` · `RaiseTier`/`LowerTier`/`ResetTier`/`JumpToPromoStandby` ·
`SkipTutorial`/`ResetTutorial` 계열 · `StartCurrentAdventureNode` · `ForceAlbumInsertSession` ·
`UI/Debug/UnlockAllCardsButton`(전처리 지시자가 아예 없고 프리팹 버튼에 직결된다).

여기에 `OutgameTutorialRewind` 가 더해진다. 되감기 재생은 `LocalPrefs` 키 하나로 서고 호출부
(`SaveDependentManagersStep`)도 가드 밖이라, 소유·모험 슬롯을 클라가 쓰는 마지막 정규 경로다.

**세 도메인의 판정이 서버로 갔어도 이 절이 열려 있는 한 이득은 그대로다** — 치트는 판정을 우회하는 것이
아니라 결과를 직접 쓰기 때문이다. **닫는 자리는 P0 이고, P6 착수의 선행조건이다.**

### (4) 이중 진실원 3건

직전 실측의 네 건 가운데 셋이 닫혔다. `cardGrowth` 슬롯의 필자는 서버 하나가 됐고(P3),
`ownership.cardIds` 의 지급 진실원은 클라 시나리오 SO 에서 `CardPack`/`CardPackDrop` 시트로 옮겼으며(P2),
모험 해금 판정은 서버 `judgeNodeUnlock` 이 소유한다(P4). 남은 것은 셋이다.

- **`rank.points` 의 필자가 둘이고 규칙도 두 벌이다.** 싱글(클라)·멀티(서버)로 갈리고,
  `RankManager.PreviewBattleResult` 가 `ApplyBattleResult` 의 클램프·승급 규칙을 줄 단위로 다시 구현한다
  (`RankManager.cs:166` 대 `:209`). 승급 곡선도 클라 `RankConfig` 와 서버 `RankGrade` 표 양쪽에 있다.
  **이 값이 남은 자기신고의 뿌리다** — `openPack` 의 랭크 잠금 · `claimReward(Rank)` 의 자격 ·
  `reportAdventureWin` 의 챕터 잠금이 모두 이 하나를 읽는다
- **한계돌파 곡선의 저작이 두 벌이다.** 서버는 `CardLimitBreak` 표를 `growth/limitBreakTable.ts` 로 읽고,
  클라 `GrowthRules.TryGetLimitBreakStep` 은 같은 곡선을 상수로 들고 있다(HP 가산 고정값 · 간식 비용 = 단계 수).
  클라 값은 버튼 표시와 낙관 선검사에만 쓰이므로 결과를 바꾸지는 않지만, 두 곡선이 갈리면 화면과 결과가 어긋난다.
  어긋남을 알리는 창구는 `LimitBreakCommand.LogSettled` 의 비교 로그 하나뿐이다
- **모험 랭크 잠금의 축이 둘이다.** 클라 `AdventureProgress.IsChapterRankLocked` 는 등급(`ERankGrade`)으로,
  서버 `judgeNodeUnlock` 은 점수(`requiredPoints`)로 잰다. 두 축의 등가성은 업로더 검증
  (`entryPoints` 와 등급이 함께 오름차순)에 기대고 있어, 표가 어긋나면 화면은 열려 있는데 신고만 `RankLocked` 로 막힌다

### (5) 재수화 구멍

`ServerSlotRehydrator.Rehydrate` 는 네 슬롯을 다룬다 — `Ownership`·`KeywordGrowth`·`CardGrowth` 는
`Init()` 로 캐시를 다시 세우고, P4 에서 더해진 `Adventure` 는 **통지뿐**이다
(`AdventureProgress.NotifyRehydrated`). `AdventureProgress` 가 세이브를 직독하고 캐시를 두지 않아
값은 채택 시점에 이미 새것이고 모르는 것은 화면뿐이기 때문이다.

`rank`·`albumReward` 매니저도 `DataSaveManager.Data` 를 매번 읽어 지금은 맞지만,
`deck`·`tutorial`·`profile` 은 static 캐시라 서버가 그 슬롯을 쓰기 시작하면 옛 값이 남는다.
파일 안 TODO 두 줄이 그대로 남아 있다(`deck` 은 `LoadFromSave` 가 채택 도중 저장을 튀게 해 미구독,
`Rank`·`AlbumReward`·`Tutorial`·`Profile` 은 미착수).

### (6) 죽은 코드 — 해소, 참조만 남았다

직전 실측이 지목한 `CardGrowthManager.AddSnack` 은 **삭제됐다**. 클라에 간식을 늘리는 경로 자체가 없고
적립은 서버 `functions/src/growth/cardGrowth.ts:addSnack` 이 `openPack` 안에서 수행한다.
다만 그 이름을 언급하는 낡은 주석이 두 곳(`functions/src/growth/cardGrowth.ts:16` ·
`functions/scripts/test-growth.js`)에 남아, 클라에 아직 대응물이 있는 것처럼 읽힌다.

---

## 5. 문서 상태 정정

- **`SERVER_VALIDATION_ROADMAP.md` 는 더 이상 없다.** 그 자리를 `SERVER_AUTHORITY_ROADMAP.md`(P축)가
  대신하고, 이 문서가 참조할 계획 정본은 그것과 `CURRENCY_SERVICE_HANDOFF.md`(C축) 둘이다.
  C7 이 세이브에서 `currency` 슬롯을 걷어내 슬롯은 10 에서 9 로 줄었고, 잔액은 별도 지갑 문서에 있다.
- 직전 실측이 "계획에 없는 미결"로 지목한 한계돌파 callable 은 P3 으로 들어와 **구현·배포까지 끝났다**.
  같은 흐름에서 P2(소유)·P4(모험)도 닫혔다.
- **지금 남은 미결은 넷이고 모두 P축에 서 있다.** P0(릴리스 치트 차단) · P1(`claimBattleReward` 멱등) ·
  P5(재수화 구멍) · P6(룰 슬롯 동결). 순서는 P0 이 P6 의 선행조건이고, P5 도 P6 의 선행조건이다 —
  슬롯을 잠그기 전에 클라 쓰기 경로를 지우고 채택 후 화면 갱신을 메워야 한다.
- 랭크(`rank.points`)는 P축의 **범위 밖**으로 남았다. 위 (1)의 자기신고가 이 하나로 수렴하므로,
  P6 이후에도 닫히지 않는 유일한 판정 입력이다.

---

## 6. 이 판정을 다시 내리는 방법

브랜치가 움직이면 아래 넷만 다시 재면 된다.

1. **클라 직접 쓰기** — `Assets/Scripts/` 에서 `SetAsync|UpdateAsync|DeleteAsync|RunTransactionAsync` 를 grep 해
   `OutGame/Save/4.Cloud/` 바깥에 결과가 있는지 본다. 0이어야 한다
2. **클라 명령 호출부** — 같은 범위에서 `InvokeAsync|InvokeReadOnlyAsync|CallAsync` 를 grep 하면
   어느 도메인이 서버로 갔는지가 그대로 나온다.
   **여기에 하나를 덧붙여야 한다** — 판정이 서버로 갔어도 클라에 슬롯 쓰기 함수가 남아 있을 수 있다.
   `OwnershipManager.Grant|GrantAll|GrantEntireCatalog|RevokeAll` 과 `ResetForDebug` 를 함께 grep 해
   남은 호출부가 디버그·되감기뿐인지 본다. 그것이 P0 의 진척도다
3. **규칙 동결** — `firestore.rules` 에서 `affectedKeys` 를 grep 한다. 0건이면 세이브 슬롯이 전부 열려 있다는 뜻이다
4. **재화 이동 지점** — 서버에서 `nextWallet` 호출부를 grep 한다. C6.1 이 그것을 지갑 상태의 유일한 출구로 만들어서
   한 줄 grep 으로 전수가 잡힌다

**`devGrantCurrency` 를 `functions/src` 에서만 찾으면 "서버에 없다"는 오판이 나온다.** C6.6 이
`functions-currency/` 로 옮겼으므로 두 codebase 를 함께 훑어야 한다.

판정을 틀리게 만드는 나머지 함정은 `CURRENCY_SERVICE_HANDOFF.md` 의 같은 이름 절에 정리돼 있다
(배포 로그는 호출 가능을 증명하지 않는다 · `functions:log` 는 3~4분 늦는다 · 룰 하네스는 종료코드가 거짓말한다 등).
