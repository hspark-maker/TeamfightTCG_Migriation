# 서버 권위 이관 로드맵 — 아웃게임

> 실측 2026-08-31 · 브랜치 `feature_Firestore` · HEAD `2df116ca9` (당초 작성 시점 `87b493891`)
> 실태 기록은 `SERVER_AUTHORITY_AUDIT.md`, 재화 축 정본은 `CURRENCY_SERVICE_HANDOFF.md`.
> 이 문서는 **무엇을 어떤 순서로 할지**만 정한다. `c9f7d71d2` 가 지운
> `SERVER_VALIDATION_ROADMAP.md` 자리를 대신한다.

## Context

`docs/OutGamePlan/SERVER_AUTHORITY_AUDIT.md`(2026-08-28 실측)는 "명령 층은 대부분 서버로 넘어갔으나 규칙 층이 아직 클라 쓰기를 막지 않는다"고 판정했다. 그 뒤로 브랜치가 움직였고, 이번에 재실측해 **감사 문서 두 곳을 정정**했다.

- **C7(룰 `hasOnly` 15키→14키 · currency 블록 제거)은 이미 끝나 있다.** `firestore.rules:48-53` 이 이미 14키이고 하네스 `13c` 도 `assertFails` 로 뒤집혀 있다(C6.5 시점, `8edb278f2`). 남은 C7 은 감사 문서가 별도로 짚은 **슬롯 동결(`affectedKeys`)** 뿐이고 그건 여전히 레포 전체 0건.
- **서버 전투 재시뮬레이터가 새로 들어왔다**(`functions/src/battleSimulation.ts` 475줄 + 골든 15케이스, `0702181d4`). 다만 `submitMatchResult.ts:34` 의 `SERVER_SIMULATION_AUTHORITATIVE = false` 로 **섀도 모드**라 승패 진실원은 여전히 두 클라 제출 합의다.

**이 로드맵의 범위 결정(사용자 확정)**

- **전투 관련은 제외** — 싱글 전투 결과 서버화, 멀티 시뮬 권위 전환, 랭크 점수 판정 이관은 다루지 않는다. 단 **토너먼트 관련은 허용**.
- 예외 하나: `claimBattleReward` 의 **멱등 낙인만** 넣는다. 전투 판정이 아니라 지급 callable 쪽 결함이고, C8 재화 원장 작업에 곱어 가는 것이라 추가 비용이 거의 없다.
- 룰 슬롯 동결에서 **`deck` · `profile` · `tutorial` 은 제외**(클라가 계속 쓴다). `rank` 는 싱글 전투 판정이 범위 밖이라 자동 제외.
- 순서는 **구멍 크기 순**.

**의도한 결말** — 세이브 9슬롯 중 **5슬롯(`ownership` · `cardGrowth` · `keywordGrowth` · `albumReward` · `tournament`)이 서버 전용**이 되고, 재화가 움직인 모든 기록이 영수증에 남으며 재시도가 재과금이 되지 않는다.

---

## 현재 권위 배분 (실측)

**서버가 이미 소유** — 재화 지갑(클라 writer 0) · 팩 추첨 · 강화 2종 · 보상 수령(랭크/도감/챕터/정점) · 멀티 정산 · 신규 계정 · 멀티 덱 검증(`lockDeck` 이 소유·레벨·hpBonus·진화·키워드를 전량 재계산)

**클라가 아직 쥔 것** (부정행위 이득 순)

| # | 항목 | 위치 | 이득 | 이번 범위 |
|---|---|---|---|---|
| 1 | ~~`claimBattleReward` 멱등 없음~~ | ~~`functions/src/commands/claimBattleReward.ts`~~ | 같은 페이로드 반복 → **무한 골드** | **P1 완료** · 잔여는 전투 범위 |
| 2 | 릴리스 빌드 디버그 치트 | `OutgameDebugActions.cs` · `UI/Debug/*` | 버튼 하나로 3~6번 전부 | **P0** |
| 3 | 소유 `ownership` | ~~튜토리얼 5경로~~ → 디버그 3 · 되감기 4 | 전 카드 무료 → 팩 우회 + 도감 보상 연쇄 | **P2 완료** · 잔여는 P0 |
| 4 | ~~한계돌파~~ | ~~`CardGrowthManager.Snack.cs:TryLimitBreak`~~ | 간식 0으로 HP 보너스 최대 · `lockDeck` 도 통과 | **P3 완료** · 잔여는 P0 |
| 5 | ~~토너먼트 해금·낙인~~ | ~~`TournamentProgress.MarkRewardPending`~~ | 챕터 건너뛰고 정점 보상 수령 | **P4 완료** · 잔여는 P0 |
| 6 | 싱글 랭크 `rank.points` | `RankManager.ApplyBattleResult` | 팩 잠금·티어 보상·챕터 잠금 자격 | **범위 밖** |
| 7 | 세이브 슬롯 무동결 | `firestore.rules` `affectedKeys` 0건 | 위 3·4·5를 룰 층에서 못 막는다 | **P6** |

---

## 단계

### P0 — 릴리스 치트 차단 (서버 무관 · 가장 싸다)

`#if UNITY_EDITOR || DEVELOPMENT_BUILD` 안에 있는 것은 `OutgameDebugActions.cs:73-172` 의 서버 진단 3개와 `OutGame/Debug/OutgameDebugOverlay.cs` 파일 전체뿐이다. 나머지는 리테일 빌드에 그대로 실린다.

- `Assets/Scripts/OutGame/Debug/OutgameDebugActions.cs` — 파일 전체를 가드로 감싼다. 현재 노출: `MaxCardGrowth`/`ResetCardGrowth` · `UnlockAllCards`/`RevokeAllCards` · `RaiseTier`/`LowerTier`/`StepTier`/`JumpToPromoStandby`/`ResetTier` · `SkipTutorial`/`ResetTutorial`/`RestartTutorialFromChapter` · `ToggleFeatureLock` · `StartCurrentTournamentNode` · `ForceAlbumInsertSession`
- `Assets/Scripts/UI/Debug/UnlockAllCardsButton.cs` · `Assets/Scripts/UI/Debug/DebugCurrencyButton.cs` — 파일 전체 가드. **`MonoBehaviour` 라 프리팹에 붙은 채 빌드에 실린다** — 가드 후 프리팹 참조가 끊어지지 않는지 확인
- 매니저 쪽 `*ForDebug` public static API 도 같이 가드: `RankManager.SetTierForDebug`/`StepTierForDebug`/`SetPromoStandbyForDebug`/`ResetForDebug` · `TournamentProgress.ResetForDebug` · `RankRewardManager.ResetForDebug` · `OutgameTutorialProgress.JumpForDebug`/`ClearTriggersForDebug`/`ResetForDebug` · `CardGrowthManager.DebugMaxAll`/`DebugResetAll` · `OwnershipManager.GrantEntireCatalog`/`RevokeAll`

`GrantCurrency` 계열은 이미 서버가 막는다 — `functions-currency/src/commands/devGrantCurrency.ts:30` 이 `env !== "test"` 를 거부하고 리테일은 `live` 다. 나머지는 순수 클라 조작이라 서버 방어선이 0이다.

호출부와 정의부를 **같은 가드로** 묶어야 리테일 컴파일이 깨지지 않는다.

---

### P1 — 재화 원장 + 멱등 (C8) — **구현 완료** (`98042b94b` `52138f910` `86abe584c` `a6f0aaa7d` `0ed2c830a` · **배포됨** 2026-08-31)

착수하려고 실측하니 이 단계는 이미 끝나 있었다. 문안이 지목한 `walletStore.ts` 의 `ledgerEntry` 는 정의째 사라졌고, 같은 장치가 **`receipt`(영수증)** 라는 이름으로 서 있다. 원장과 멱등이라는 두 축을 한 장치가 함께 닫았다. 아래에는 문안과 실태가 갈린 자리만 적는다 — 설계 정본은 `CURRENCY_SERVICE_HANDOFF.md` 의 C8 절이 갖는다.

1. **이름이 `ledger` → `receipt` 로 바뀌었다.** 회계 용어보다 게임 도메인 용어를 쓰는 프로젝트 컨벤션이고, txId 가 곧 영수증 번호라 재시도가 "같은 영수증을 다시 내미는 것"으로 읽혀 멱등 역할이 이름만으로 드러난다. 경로는 `envs/{env}/users/{uid}/wallet/current/receipts/{txId}` 이고, 룰은 `firestore.rules:126` 이 `read, write: if false` 로 닫아 뒀다(감사 기록이라 클라에 아예 열지 않는다).
2. **타입 수준 강제는 문안이 그린 것보다 강하게 섰다.** 쌍(tuple)이 아니라 **손으로 조립할 수 없는 브랜드 타입 `WalletUpdate`**(`walletStore.ts:59-73`)를 `nextWallet` 이 내고 `writeWallet`(`:243`)이 그것만 받는다. 증감 `changes` 는 호출부가 넘기지 않고 `diffBalances`(`:130`)가 before/after 를 차분해 만든다 — 상한 클램프 때문에 "의도한 증감"과 "실제 증감"이 갈릴 수 있어서, 손으로 넘기게 두면 그 자리가 영수증이 거짓말할 수 있는 유일한 축이 된다.
3. **배선 7곳 전수 완료** — `openPack.ts:142` · `enhanceCard.ts:145` · `enhanceKeyword.ts:137` · `claimReward.ts:418` · `claimBattleReward.ts:123` · `claimPayout.ts:150` · `functions-currency/src/commands/devGrantCurrency.ts:58`. 지갑을 쓰지 않고 낙인만 쓰는 자리(`claimPayout` 의 지급 0건 ack)를 위해 `writeReceiptOnly`(`walletStore.ts:335`)가 따로 있다.
4. **트랜잭션 순서는 문안대로다.** 영수증 조회가 콜백 진입 **전**에 서서, 히트면 쓰기 0회로 첫 응답을 그대로 돌려준다(`saveDocument.ts:221` · `walletTransaction.ts:73`). `source` 가 어긋나면 `TxIdReused`(`permission-denied`)로 거절한다. `mutateSave` 시그니처는 문안이 예측한 그대로 바꾸지 않았다.
5. **클라도 한 곳만 고쳤다.** `ServerSaveCommands.cs:35` 가 `{명령이름}:{Guid:N}` 으로 발급하고, `SendAsync`(`:118-134`)가 **같은 payload 참조를 재사용**해 다시 태우므로 재시도가 txId 를 유지한다. 재시도 판정은 `CloudFailureClassifier.IsLostResponse` 다. txId 가 없으면 서버가 발급하는 폴백이 있어 서버·클라 배포 순서에 제약이 없다.
6. **`createWallet` 3곳도 영수증을 남긴다**(문안이 "착수 시 정한다"로 열어 뒀던 자리). 남기지 않으면 영수증 합계와 잔액이 어긋나 감사가 성립하지 않는다. 초기화 둘은 `create` 가 아니라 `set` 이다 — `create` 로 두면 지갑만 지워지고 영수증이 남은 계정이 재생성에서 영구 실패한다.
7. **회귀** — `functions/scripts/test-wallet-store.js`(원장 줄 생성 · `readReceipt` 히트/미스 · 캐시본 왕복)와 에뮬레이터 전용 `functions/scripts/test-receipt-replay.js` 9케이스가 지킨다. 후자는 같은 txId 재호출에서 **`mutate` 콜백이 아예 불리지 않는다**는 것을 직접 증거로 잡는다(재추첨·재차감 부재). 미러 목록(`functions-currency/scripts/shared-files.js`)에는 `currency/walletStore.ts` 와 `save/receiptId.ts` 가 둘 다 등재돼 있다.

**결과: 재화가 움직인 기록이 전부 남고, 응답만 잃은 재시도가 재과금이 되지 않는다.**

**남는 구멍(문안 그대로 남는다)** — `claimBattleReward` 는 txId 로 **중복**은 막히지만 **전투를 하지 않고 1회 호출**하는 것은 여전히 통과한다. `won`·`remaining` 을 증명할 매치 문서 대조는 전투 범위라 다음 로드맵 몫이다. 영수증에 TTL·아카이빙 정책이 없어 유저당 무한히 쌓이는 것도 그대로다 — 결제 착수 시점에 재검토한다.

**하지 않은 것** — hold/capture, 이벤트 소싱, 복식부기, 웨어하우스 스트리밍.

---

### P2 — 소유(`ownership`) 서버 이관 — **구현 완료**

서버가 소유를 주는 경로는 `openPack` 과 `ensureAccount`(`functions/src/save/starterCards.ts`) 뿐이었고, 클라 12경로가 세이브를 직접 썼다. 그중 **실질적인 권위 생성은 튜토리얼 지급 5건**이고 나머지는 되감기 3 · 스타터덱 1 · 디버그 3 이다.

배관은 이미 서 있었다 — 팩 개봉이 `slots.ownership` → `AdoptServerSlots` → `ServerSlotRehydrator.RehydrateOwnership()` → `OwnershipManager.Init()` 로 채택형이라, 이번 작업은 **배관이 아니라 지급 판정만** 옮겼다.

1. **지급 팩의 진실원은 `CardPack`/`CardPackDrop` 시트다**(당초 문안에서 변경). 처음에는 전용 `TutorialGrant` 표(열 `id | stepId | cardId | order`, 4스텝 20행)를 신설해 `stepId` 를 좌표로 썼는데, **"모든 카드팩은 CardPack 표가 관리한다(튜토리얼 지급 포함)"** 로 방침이 서면서 그 표를 통째로 걷어냈다 — 서버 파서 `functions/src/tutorialGrantTable.ts` 와 업로더 `SpecFirestoreUploader` 의 `UploadTutorialGrants`·`TryBuildTutorialGrantRows`·`TryReadGrantCardIds` · 표 상수가 모두 삭제됐다. 이제 좌표는 packId 이고, 줄 카드는 그 팩의 `CardPackDrop` 행 전량이다. 표 하나가 줄어든 만큼 저작 진실원도 하나로 붙었다 — 팩을 시트에서 고치면 튜토리얼 지급이 따라 움직인다.
2. **callable `grantTutorialCards(packId)` 신설**(`functions/src/commands/grantTutorialCards.ts`, 판정은 순수 모듈 `functions/src/packs/tutorialGrantPack.ts` 의 `packGrantCardIds`·`judgeTutorialGrant`). 그 packId 의 드롭 행을 전량 모아 기존 소유와 합집합한 `ownership` 슬롯 전체 값을 쓴다 — **추첨이 아니라 확정 지급**이라 `drawCount`·`uniqueDraw`·`weight` 를 보지 않는다. 줄 카드가 0장이거나 `CardPack` 에 그 행이 없으면 `rejectDomain("GrantNotFound")`, **무료가 아니면 `GrantNotAllowed`** 로 막는다(유료 팩 packId 를 넣어 공짜로 받는 길 차단). price 셀을 못 읽은 팩도 유료로 본다 — 이 판정이 지급 경로의 유일한 권한 게이트라 표 결손 앞에서 열리면 상점 팩이 통째로 공짜가 된다. cardId 는 `openPack` 과 같은 카탈로그 집합(`packs/cardCatalog.loadCatalogIds`)으로 거른다. `CardPackDrop` 표 전량이 비었을 때만 `logger.error` 를 먼저 남겨 배포 사고와 미저작 packId 를 로그에서 가른다. 지갑은 채우지 않는다.
3. **낙인을 두지 않는다**(당초 문안에서 변경). 소유는 집합이고 `buildOwnershipSlotFromIds` 가 이미 가진 카드를 skip 하므로 같은 packId 재호출의 델타가 0이다. 서버가 줄 수 있는 총량 상한은 그 팩의 드롭 풀로 이미 닫혀 있고, `grants/current` 는 `readGrants` 가 **fail-open**(못 읽으면 미사용)이라 지급 낙인으로 쓰면 방어 가치가 0이면서 `GRANT_SCHEMA_VERSION` 승급 비용만 진다. **packId 가 좌표이고 그 팩 드롭 풀의 총합이 곧 상한이다.**
4. **클라 호출부 전환** — `Tutorial/Steps/TutorialStepExecutor.cs` 의 5곳이 `TutorialGrantCommand.GrantAsync(packId)` 로 간다. packId 는 스텝 저작의 기존 `pack` 필드(`CardPackData`)에서 꺼내므로 새 저작 축을 만들지 않았다 — 대신 `Steps/TutorialActionMeta` 에 `DeckGrant`·`CardGrant`·`CardSetGrant` 의 `EStepField.Pack` 요구를 더하고, `Editor/Tutorial/TutorialValidator` 가 미배선과 유료 팩 배선을 Error 로 잡는다(실행 시점의 결손은 `GrantPackIdOf` 가 소리내어 남기고 요청을 접는다). **5경로 전부 왕복을 기다리지 않는다**(선커밋 후 `Forget()`, `EnterAutoPurchase` 관용구). `EnterDeckGrant` 도 예외가 아니다 — 오히려 `TryInsertFront` 가 지급 요청보다 **앞에** 선다: `ServerSaveCommands.InvokeAsync` 안에서 시작되는 업로드 봉인 밖에서 덱 저장을 끝내야 `AdoptServerResult` 가 세운 업로드 기준선과 경합하지 않고, 바로 다음 스텝(전투 진입)의 덱 게이트가 빈 슬롯을 보지 않는다. 그 사이 덱 카드가 잠시 미소유일 수 있으나 덱 저장은 클라 권한이고 `lockDeck` 재검증은 멀티 진입에만 걸린다(튜토 전투는 싱글).
5. **`StarterDeck.GrantIfNoDeck`** — 에디터 격리(당초 문안) 대신 **지급만 벗겼다**. 실측 결과 이 경로는 리테일에서도 선다: `freshAccount.ts:50` 이 `deck.slots[0]` 을 채워 신규 계정에선 서지 않지만, `DeckListController:182 → TryDeleteAt` 에 최소 덱 수 가드가 없어 유저가 덱을 전량 삭제할 수 있고 그때 이 안전망이 실제로 선다. 이제 6장이 전부 `IsOwned` 일 때만 삽입한다.
6. **`OwnershipManager` 는 축소하지 않았다** — 되감기·디버그가 P0 몫으로 남아 `Grant`/`GrantAll`/`GrantEntireCatalog`/`RevokeAll` 을 여전히 호출한다. 대신 `Init()` 끝에 `OnOwnershipChanged` 발화를 넣어 **기존 잠복 결함**을 메웠다(서버 채택 경로에 UI 갱신 통지가 아예 없어 도감·덱편집을 연 채 소유가 늘면 화면이 안 바뀌었다). 호출부 0건이던 `HasAnyOwnedSaved()` 는 삭제.

**결과: 튜토리얼 지급의 진실원이 서버 표가 됐고, 클라가 임의 cardId 를 소유에 밀어 넣는 리테일 경로가 사라졌다.** 도감 완성 판정(`claimReward(Album)` + `completionTable.ts`)이 이 슬롯을 읽으므로 도감 보상의 자기신고 뿌리도 같이 좁혀진다.

**아직 writer 0 이 아니다.** 디버그 3건(`OutgameDebugActions:234`·`:240`, `UnlockAllCardsButton:25`)과 되감기(`OutgameTutorialRewind` 의 와이프 `:79` + 재생 3건)가 남는다 — **P0 가 닫는다. P6 착수의 선행조건이다.**

**서버가 막는 것 / 못 막는 것.** 막는 것은 드롭 풀 밖 카드 지급(임의 cardId 주입 불가) · 유료 팩 무상 취득 · 총량 상한 · 중복 지급 무효화다. **못 막는 것은 순서·시점**이다 — 튜토리얼을 하지 않고 무료 팩 packId 를 바로 호출하면 그 팩 저작분이 그대로 들어온다. `tutorial` 슬롯이 동결 제외라 서버가 믿을 진행도가 없어 P6 이후에도 못 막는다. 다만 그 카드는 어차피 모든 유저가 튜토리얼에서 받으므로 실질 이득이 0에 수렴한다. P4 토너먼트와 같은 성격의 한계다.

---

### P3 — 한계돌파 callable — **구현 완료** (`b956f5cda` · **배포됨**)

서버 순수 로직(`functions/src/growth/cardGrowth.ts` 의 `applyLimitBreak`/`spendSnack`/`canAffordSnack`)은 이미 다 있었고 callable 이 없어 호출부가 0건이었다. 적립만 서버(`openPack`)고 소비·단계증가가 클라에 남아, 간식 0으로도 HP 보너스를 최대까지 올린 값이 `lockDeck` 의 덱 검증까지 통과했다.

1. **callable `limitBreakCard(cardId)` 신설**(`functions/src/commands/limitBreakCard.ts`). 형틀은 `enhanceCard` 가 아니라 **`grantTutorialCards`** 다 — 한계돌파는 지갑을 쓰지 않으므로 `SaveMutation` 에 `wallet` 키를 싣지 않는다(간식은 전역 잔액이 아니라 `cardGrowth` 슬롯 안 카드별 값이다). 멱등은 C8 영수증이 `mutateSave` 안에서 그대로 세워 준다. 거절 사유 4종 `RuleUnavailable`·`CardNotOwned`·`MaxStage`·`NotEnoughSnack`.
2. **`CardLimitBreak` 스펙 표 배선 — 진실원을 하나로 모았다.** 표를 새 callable 에만 물리면 곡선의 진실원이 **셋**이 된다(클라 `GrowthRules` · 서버 `deckValidation.ts:38,188` 도 같은 곡선을 따로 하드코딩하고 있었다). 새 순수 파서 `functions/src/growth/limitBreakTable.ts` 를 **새 callable 과 `deckValidation` 이 공유**하고, 순수 모듈이라 표를 못 읽는 `deckValidation` 에는 `lockDeck` 이 곡선을 **필수 인자**로 주입한다(선택 인자 + 폴백을 남기면 둘로 되돌아간다). 클라 `GrowthRules` 는 표시·낙관 선판정용으로 남는다.
   - **저작 규약 3건**(`Assets/Table/SpecDatas.cs:216-232` 필드 주석): 상한의 진실원은 `CardLimitBreak` 가 아니라 **`CardEnhanceRule.maxLimitBreak`** 인데 파서가 그 열을 버리고 있어 함께 읽게 했다 · **`hpGain` 은 누적**이다(지금까지 `result += limitBreak` 가 맞아떨어진 것은 단계당 1이 우연히 누적합과 같아서다) · **`snackCost` 는 1 미만이면 1** 로 올린다(공란이 공짜 한계돌파가 되지 않게).
   - `maxLimitBreak` 이 0이어도 `parseCardEnhanceRule` 은 `null` 을 내지 않는다 — 한계돌파 열 하나가 비었다고 카드 강화 전체를 죽일 이유가 없다. 코드 천장은 `LIMIT_BREAK_STAGE_CEILING = 3`(`CARD_MAX_LEVEL_CEILING` 형틀).
3. **클라 전환** — `TryLimitBreak` → `TryLimitBreakAsync`. 세이브에 대입하지도 `Save()` 하지도 않는다(응답 채택 → `ServerSlotRehydrator` → `CardGrowthManager.Init()`). 창구는 `OutGame/Growth/LimitBreakCommand` · 응답 DTO `LimitBreakCardResult`(`link.xml` 등록) · 결과 열거 `ELimitBreakOutcome`.
4. **죽은 코드 삭제** — `CardGrowthManager.AddSnack`(호출자 0, 적립은 서버가 한다) · **`KeywordGrowthManager.Save`/`SyncSaveData`**(호출자 0 — 채택 경로가 아니라 죽은 코드였다. 남으면 누가 부르는 순간 서버 채택이 세운 업로드 기준선을 깬다). `CardGrowthManager.Save` 는 디버그 둘이 여전히 부르므로 남기고 주석으로 못박았다.

**시공 중 갈린 판단 2건.**

- **표 사고가 유저 매치를 태우지 않게 갈랐다.** 곡선이 결손으로 깎이면 **서버가 이미 지급한** 단계가 범위를 벗어나는데, 그것을 `rejectLock` 으로 접으면 매치 문서에 낙인이 박혀 상대까지 탄다. 코드 천장(3) 초과는 위조라 종전대로 `saved_growth_out_of_range` 로 거절하고, 천장 이하인데 곡선만 넘으면 표가 깎인 것이라 새 코드 `limit_break_curve_shrunk` → `HttpsError("unavailable")` 로 내보낸다.
- **왕복 중 창이 잠기던 것을 풀었다.** `SkipPlayingFx` 가 `m_ritualPlaying` 만 보고 뒤로가기를 삼켰는데, 한계돌파는 스킵할 연출이 없고 대기가 네트워크라 상한이 없다. 재생 중인 연출이 실제로 있을 때(`m_activeRitual != null`)만 삼킨다 — 창을 연 뒤 **첫 강화의 왕복 구간도 같은 무한 대기**였고 함께 풀렸다.

**착수 게이트(표 실값 대조) 통과.** `SpecData.bytes` 가 암호화라 리포에서 못 읽어 유니티에서 덤프하고 Firestore 업로드분도 REST 로 직접 조회했다 — **test·live 양쪽** `maxLimitBreak = 3`, stage 1/2/3 이 `hpGain` 1/1/1 · `snackCost` 1/2/3 으로 **클라 하드코딩과 완전히 일치**한다. 그래서 `deckValidation` 전환을 미루지 않고 서버 전량을 한 번에 배포했다(불일치였다면 `hp_bonus_mismatch` 로 한계돌파한 카드가 든 덱이 전부 잠금 거절됐다).

**결과: `cardGrowth` 슬롯의 클라 writer 가 정상 플레이 경로에서 0이 됐다**(강화 2종은 이미 서버, 디버그 2건은 P0 몫). `keywordGrowth` 도 같이 0이 됐다.

**회귀** — 신규 `functions/scripts/test-limit-break.js`(파서 11케이스: id≠stage 정렬 · 중복 stage · `snackCost` 하한 · 누적 합 · 상한 초과 행 무시 · 결손 fail-closed · 천장 클램프) + `test-deck-validation.js` 에 위조/표사고 3케이스. `package.json` 의 `test` 에 배선했다.

---

### P4 — 토너먼트 진행 서버화 — **구현 완료**

착수 시 실측하니 문안보다 실태가 앞서 있었다. `ClearNodeAsync` · `ClaimChapterRewardAsync` 는 이미 서버 `RewardClaimCommand` 로 위임되어 있었고, 클라가 세이브를 직접 쓰는 자리는 **`MarkRewardPending` 하나**였다(`ResetForDebug` 는 P0 몫). 그래서 이번 작업의 본체는 "낙인을 서버가 소유하게 만드는 것"이 됐다.

1. **callable `reportTournamentWin(nodeId)` 신설**(`functions/src/commands/reportTournamentWin.ts`). 이름을 `clearTournamentNode` 로 하지 않은 것은 이 명령이 하는 일이 클리어 확정이 아니라 **격파 신고**이기 때문이다(확정은 여전히 `claimReward` 다). `claimReward.ts` 안의 동명 내부 함수는 `claimTournamentNode` 로 바꿔 이름 공간을 비웠다.
   **`won` 을 받지 않는다** — 서버가 검증할 방법이 없어 "항상 true 인 인자"가 되고, 그런 인자는 읽는 사람에게 검증되는 것처럼 보인다. 패배는 아예 호출하지 않는 것이 계약이다.
2. **사슬의 진실원은 새 `prevNodeId` 열이다.** 당초 문안은 `TournamentChapter` 표(24행)를 그대로 읽으라 했지만, 그 표의 `order` 는 챕터 **안**의 순서라 경계를 넘지 못하고 `id` 는 저작 순회 위치라 앞에 행 하나만 끼워도 밀린다. 클라 진실원(`StateOf` 의 `_index - 1` 인접)을 그대로 표에 새겨 정렬 의미론에 기대지 않게 했다. **뿌리(`prevNodeId` 가 빈 행)가 하나가 아니면 그 열이 없던 구 블롭**이라는 뜻이라 fail-closed 로 막는다(`ChainUnreadable`).
3. **랭크 잠금의 축은 등급이 아니라 점수(`requiredPoints` 열)다.** 서버에는 `ERankGrade` 가 없고 `RankGrade` 표의 `id` 채번과 `RankConfig.grades` 리스트 순서를 맞춰 줄 코드도 없어서, 등급 인덱스를 축으로 쓰면 두 진실원이 조용히 갈린다. **첫 등급은 0 으로 낮춘다** — `RankConfig.ResolveTierIndex` 가 첫 등급 진입 점수에 못 미쳐도 인덱스 0 을 돌려주므로, `entryPoints` 를 그대로 쓰면 `points` 0 인 신규 계정을 서버만 잠근다. 등가성이 기대는 두 불변식(`entryPoints` 오름차순 · 등급 오름차순)은 업로더가 검증한다.
4. **`claimReward` 에 사슬 검사를 겹치지 않았다.** `pending !== ownerId` 검사가 이미 서버 낙인을 통과한 정점만 받으므로, 낙인을 서버가 소유하는 순간 사슬 검증을 상속한다. 대신 방어선 하나를 더했다 — **낙인이 표 밖 정점을 가리키면 거절**한다(구 클라가 스스로 찍어 둔 임의 낙인이 신규 서버에서 그대로 수령되는 창구).
5. **표 파서를 `completionTable.ts` → `tournamentTable.ts` 로 이사**했다. 표 하나에 파서 하나가 기존 불변식이고(`rewardTable`↔Reward · `tutorialGrantTable`↔TutorialGrant), 이쪽은 완주 모수뿐 아니라 해금 사슬까지 잰다. `completionTable` 에는 도감과 공용 `isCompleted` 가 남는다.
6. **클라는 두 곳에서 신고한다.** 전투 씬(`BattleOutcome`, `Forget`)이 먼저인 것은 기존 낙인이 거기 있던 이유와 같다 — 캐리어가 메모리라 로비까지 미루면 씬 로딩 중 종료가 승리를 삼킨다. 로비 복귀(`TournamentReturnFlow`)가 한 번 더 쏘는 것은 그 순간 네트워크가 없었을 때를 메운다. 서버가 재신고를 `AlreadyPending` 으로 성공 처리하고 창구(`TournamentWinCommand`)가 겹친 왕복을 합치므로 비용은 왕복 1회다.
   **선물 등장은 신고가 끝난 뒤로 미뤘다** — 낙인이 서기 전에 내면 눌러도 수령이 튕긴다. 실패해도 신호는 조건 없이 낸다: 그것이 맵의 등장 예약(정점을 재워 두는 것)을 푸는 유일한 열쇠다.
   전투 씬 호출에 **취소 토큰을 물리지 않는다** — 씬 파괴가 업로드 봉인 해제(`InvokeAsync` 의 `finally`) 전에 취소를 던지면 이후 저장이 통째로 막힌다.
7. **재수화는 `Tournament` 슬롯만** 넣었다(P5 에서 당겨옴, `AlbumReward` 는 P5 에 남는다). 다른 슬롯과 달리 `Init` 계열이 아니라 **통지뿐**이다 — `TournamentProgress` 가 세이브를 직독하고 캐시를 두지 않아 채택 시점에 값은 이미 새것이고 모르는 것은 화면뿐이다.

**결과: 낙인을 *만드는* 판정이 서버로 갔다.** 도메인 코드에서 `tournament` 슬롯을 쓰는 경로는 `ResetForDebug` 하나만 남았다(P0 가 닫는다). `UserSaveData.VERSION` 은 8 그대로 — 세이브 필드 변화가 0이다.

**다만 낙인이 아직 서버 단독 소유는 아니다.** `PlayerSaveDocument` 가 여전히 문서 **전체**를 `SetOptions.Overwrite` 로 올리므로, 변조 클라는 정규 업로드 경로로 `pendingRewardNodeId` 를 세울 수 있다. 그렇게 세운 낙인이 표에 실재하는 정점을 가리키면 `claimReward` 의 `hasNode` 방어선을 통과하고 사슬 검증은 상속되지 않는다. **이 상속은 P6 슬롯 동결 이후에 완결된다** — P4 가 닫는 것은 "클라 코드가 사슬을 판정하던 것"이고, "클라가 슬롯에 쓸 수 있다는 것"은 P6 몫이다.

**선행 블로커였던 것.** 머지 `30f849809` 가 `saveDocument.ts` 를 C8-2 이전으로 되돌려 `mutateSave` 가 구 3인자로 남아 있었고, callable 6개가 신 5인자로 부르는 바람에 `functions` 전체가 컴파일되지 않았다(`TS2554` 2건, `predeploy` 도 같이 죽어 있었다). C8-2 본에 박형석 작업분이 이미 들어 있어 3자 병합이 아니라 그 버전으로의 복원이 정답이었다(`0ed2c830a`).

**한계(문안 그대로 남는다).** 서버가 막는 것은 "순서 건너뛰기"와 "중복 클리어"이지 **"전투 없이 이겼다고 주장하는 것"이 아니다**. `requiredGrade` 자격도 `rank.points` 가 클라 소유로 남는 한 자기신고 위에 선다. 그래도 한 번의 거짓 신고로 **한 정점만** 넘어가고 챕터 전체를 건너뛸 수는 없다. `pendingRewardNodeId` 직접 조작은 P6 슬롯 동결이 닫는다.

**배포보다 표 업로드가 먼저다.** 두 열이 없는 블롭에서는 해금이 전부 `ChainUnreadable` 로 막힌다.

---

### P5 — 재수화 구멍 메우기 (P6 선행조건)

`ServerSlotRehydrator.Rehydrate` 는 `Ownership`·`KeywordGrowth`·`CardGrowth` 세 슬롯만 재수화한다(파일 안 `TODO(R5+)`·`TODO(R7·R9)`).

- **`AlbumReward` 추가.** `Tournament` 는 P4 가 당겨가 이미 붙었다(통지만 — `TournamentProgress` 는 세이브 직독이라 재구축할 캐시가 없다). `AlbumRewardManager` 도 지금은 `DataSaveManager.Data` 를 매번 읽어 우연히 맞지만, static 캐시를 도입하는 순간 깨진다.
- **`Deck` 은 손대지 않는다** — 동결 제외 슬롯이고, TODO 가 지적한 "`DeckSaveManager.LoadFromSave` 가 Compact 후 SaveAll 을 타 채택 중 저장이 튄다"는 문제를 건드릴 이유가 없어졌다.
- `Rank`·`Tutorial`·`Profile` 도 동결 제외라 재수화 대상이 아니다.

---

### P6 — 룰 슬롯 동결 (마지막)

감사 문서가 "닫는 자리는 C7" 이라 한 그 자리. 지금 세이브 문서에 대해 클라가 가진 권한은 "revision 을 정확히 1 올리는 전체 덮어쓰기 update" 이고, 룰은 슬롯 **내부를 전혀 보지 않는다**(엔트리 수 상한만).

**`firestore.rules` 의 `isValidSave()` 에 동결 절 추가**

```
// 서버 전용 슬롯 — 클라 업로드가 이 다섯 중 하나라도 바꾸면 거부한다.
!request.resource.data.diff(resource.data).affectedKeys()
   .hasAny(['ownership','cardGrowth','keywordGrowth','albumReward','tournament'])
```

`diff().affectedKeys()` 는 map 전체 동등 비교보다 싸다(`ownership`·`cardGrowth` 는 엔트리 2000 상한이라 비용이 문제가 된다). `SetOptions.Overwrite` 업로드여도 **값이 같으면 `affectedKeys` 에 들어가지 않으므로**, 클라가 서버가 준 값을 그대로 재업로드하는 정상 경로는 통과한다.

**동결 제외(클라가 계속 쓴다)**: `deck` · `profile` · `tutorial` · `rank`

**하네스 케이스 추가** (`Tools/firestore-rules-tests/rules.test.js`, 현재 약 57케이스)
- 동결 슬롯 5종 각각을 바꾼 update → `assertFails`
- `deck`·`profile`·`tutorial`·`rank` 만 바뀐 update → `assertSucceeds`
- 동결 슬롯을 서버가 준 값 그대로 다시 실은 update → `assertSucceeds` (Overwrite 정상 경로 회귀)

**선행 블로커 — `devResetSave` (test env 전용).** 룰을 조이는 순간 되감기(`OutgameTutorialRewind:79` 의 `ownership` 와이프)와 디버그 해금이 **에디터에서도** 업로드 거부로 죽는다. P0 의 `#if UNITY_EDITOR` 가드는 리테일만 정리할 뿐 개발 워크플로를 살려 주지 않는다. `devGrantCurrency`(`env !== "test"` 거부) 형틀의 서버 리셋 callable 이 P6 착수 전에 서 있어야 한다.

**착수 판정은 "구 클라가 없는가" 다.** P2·P3·P4 가 배포되고 그 클라가 소멸한 뒤여야 한다 — 조이는 순간 옛 클라의 세이브 저장이 전부 거부된다. C7 currency 조이기 때와 같은 판정 기준이다.

**알아 둘 위험.** 클라가 서버 응답 채택에 실패한 채 업로드하면 옛 값으로의 롤백 시도가 룰에 거부되어 세션이 막힌다. `PlayerSaveCloud` 에 `BlockSession` + `LoadingCoverView` 복구 화면(안내 + 재시도·종료)이 이미 있고 `ServerSaveCommands` 가 명령 중 업로드를 봉인하므로 구조는 서 있다 — 다만 동결 후 첫 QA 에서 이 경로를 반드시 밟아 본다.

---

## 스킵 판정 — 서버 권위가 필요 없다

| 항목 | 근거 |
|---|---|
| **`deck` 슬롯** | 멀티 진입을 `lockDeck` 이 이미 전량 재검증한다(소유·레벨 1~4·limitBreak·`expectedHpBonus`·진화·키워드·시너지·6장·중복·정렬·`computeDeckHash` 대조). 덱 배열 위조 단독으로는 이득이 없고, 동결하면 덱 저장마다 서버 왕복이 붙어 편집 UI 응답성이 죽는다 |
| **`profile` 슬롯** | `IsAvatarOwned`/`IsFrameOwned` 가 무조건 `true` 라 소유 개념 자체가 없고 유상 아이템도 없다 — 현재 이득 0. **아바타·프레임이 유상화되는 시점에 재검토**(그때는 소유 판정이 곧 결제 검증이다). 닉네임 금칙어·길이 검사가 클라에만 있는 것은 별건 백로그 |
| **`tutorial` 진행 낙인** | 완주 낙인은 기능 잠금 해제만 하고 경제에 닿지 않는다. **단 튜토리얼 카드 지급은 `ownership` 슬롯이라 P2 로 이관한다** |
| **싱글 랭크 `rank.points`** | 전투 범위 밖(사용자 확정). **이 로드맵이 끝나도 남는 가장 큰 자기신고 축**이다 — `openPack` 랭크 잠금 · `claimReward(Rank)` 티어 자격 · 토너먼트 `requiredGrade` 가 전부 이 값을 읽는다 |
| **멀티 시뮬 권위 전환** | 전투 범위 밖. 섀도 로그(`shadow_compare`)는 계속 쌓이므로 다음 로드맵 착수 시 발산율 실측이 준비돼 있다 |
| **매치메이킹** | `FakeMatchmaker` 가 상대를 고르지만 실 PvP 자격 산정이 아니다 |
| **`RankManager.PreviewBattleResult` 이중구현** | 권위 문제가 아니라 중복 코드. `refactor-backlog` 로 |
| **`RankGrade` 드리프트** | 클라는 `RankConfig.asset`, 서버는 `RankGrade` 표를 본다. `rank` 가 클라 소유로 남는 한 갈릴 여지가 있다 — 관측 항목으로만 남긴다 |

---

## 병행 선결 — 어느 단계든 막을 수 있다

- **`envs/live/specs` 에 두 표가 없다.** 2026-08-31 실측 기준 `live` 는 10표다(`Card` 40 · `Card_Test` 40 · `CardPack` 11 · `CardPackDrop` 320 · `Reward` 84 · `RankGrade` 5 · `KeywordEnhance` 6 · `CardEnhance` 3 · `CardEnhanceRule` 1 · `CardLimitBreak` 3). 0표였던 당초 실측보다는 나아졌지만 **`TournamentChapter` 와 `AlbumEntry` 가 빠져 있어** `claimReward` 의 도감·토너먼트 수령이 live 에서 fail-closed 로 막힌다. 서버는 표를 못 읽으면 거절하므로 이 두 표 업로드가 P4·P5 배포의 선행조건이다
- **`envs/test` 는 4표가 블롭 없이 `rows/` 만 있다.** 블롭으로 선 8표(`Reward` 84 · `RankGrade` 5 · `KeywordEnhance` 6 · `CardEnhance` 3 · `CardEnhanceRule` 1 · `CardLimitBreak` 3 · `TournamentChapter` 24 · `AlbumEntry` 40)와 달리 `Card` · `Card_Test` · `CardPack` · `CardPackDrop` 은 서버가 행 폴백 경로로 읽어 표 크기에 비례한 읽기 과금이 붙는다(320행짜리 `CardPackDrop` 이 특히 그렇다). 블롭을 다시 구워 올린다
- **튜토리얼 지급용 무료 팩 행 저작.** P2 가 `TutorialGrant` 표를 걷어내면서 이 선결 조건이 `CardPack`/`CardPackDrop` 시트로 옮겨왔다 — 지금 시트에 선 무료 팩(`price` 0)은 `StarterPack`(드롭 6행) · `KeywordDeck`(6행) · `SynergyPack`(6행) 셋뿐이고, `RangePack` 은 SO(`Assets/SO/CardPack/TutorialPack/RangePack.asset`)에만 있고 시트에는 없다. step 2(7장) · step 3(1장) 지급을 덮을 팩도 아직 없어, 그 스텝들은 저작이 설 때까지 `GrantNotFound` 로 떨어진다. **시트와 SO 가 이미 한 곳에서 갈려 있다** — `docs/SpecData/CardPackDrop_sheet.csv` 의 `KeywordDeck` 풀은 `2·3·20·11·4·37` 인데 `KeywordPack.asset` 의 `poolIds` 는 첫 장이 26 이라, 서버가 주는 카드와 클라 저작이 한 장 어긋난다
- **`link.xml` 갱신.** 새 callable 응답 DTO 를 만들 때마다 `OutGame/Save/link.xml` 에 추가한다(IL2CPP 스트리핑 방어, `6f6a8885c` 가 팩 DTO 로 한 번 겪었다)
- **`functions-currency` 미러.** P1 이 `devGrantCurrency` 를 배선하면서 `functions-currency/scripts/shared-files.js` 의 미러 목록에 `currency/walletStore.ts` 와 `save/receiptId.ts` 가 함께 들어갔다. `npm test`(`test-wallet-mirror.js`)가 미러 순수성을 지킨다 — `walletStore` 에 `HttpsError` 를 넣으면 그 계약이 깨진다

---

## 검증

**단계별 게이트**

| 단계 | 검증 |
|---|---|
| P0 | 리테일 빌드 컴파일 통과(`Unity_ReadConsole`) + 릴리스 빌드에서 디버그 오버레이·버튼 부재 육안 확인 |
| P1 | **완료** — 회귀는 `functions/scripts/test-wallet-store.js`(영수증 줄 생성 · `readReceipt` 히트/미스 · 캐시본 왕복)와 에뮬레이터 전용 `functions/scripts/test-receipt-replay.js` 9케이스(같은 txId 재호출에서 `mutate` 콜백 미호출)다. **남은 것은 실기 4건**: ① `openPack` 1회 후 실서버 영수증에 `changes.Gold` 음수 · `after` 가 잔액과 일치 · `result` 에 뽑힌 카드 ② `devGrantCurrency` 로 **currency codebase** 에서도 영수증이 서는가(미러 경로는 회귀 밖이다) ③ 멀티 payout **지급 0건** ack 재시도가 `acked` 를 그대로 돌려주는가 ④ 기내모드로 타임아웃을 유발한 뒤 복구 → 클라 자동 재시도가 이중 지급 없이 통과하는가 |
| P2 | `functions/scripts/test-tutorial-grant.js`(드롭 풀 전량 지급 · `drawCount`/`weight` 무관 · 유료·price 결손 팩 거절 · 카탈로그 밖 cardId 탈락 · 재호출 멱등) · 튜토리얼 완주 실기 왕복(카드가 시트의 팩 풀대로 들어오는가 · 재호출이 소유를 늘리지 않는가) |
| P3 | **완료** — 회귀는 `test-growth.js` 가 아니라 **신규 `functions/scripts/test-limit-break.js`** 다(표 파서가 들어와 파일 성격이 갈렸다). 파서 11케이스(id≠stage 정렬 · 중복 stage · `snackCost` 하한 1 · `hpGain` 누적 합 · 상한 초과 행 무시 · 결손 fail-closed · 천장 클램프) + `test-deck-validation.js` 에 위조/표사고 3케이스 · `test-enhance.js` 에 `maxLimitBreak` 2케이스. 배포 후 전 함수 401 확인(403 없음 — 신규 `limitBreakCard` 도 invoker 바인딩 상속). **실기 합격선은 ⑤** — ① 한계돌파 1회 후 `revision` +1 · 지갑 무변경 ② 간식·단계·HP·`DeckPower` 재수화 ③ 왕복 중 연타·화살표 잠금·오버레이 재진입 ④ 최대 단계에서 `MaxStage` 거절이 세션을 안 끊는다 ⑤ **한계돌파한 카드가 든 덱으로 매치 잠금이 `hp_bonus_mismatch` 없이 통과** |
| P4 | **완료** — `functions/scripts/test-tournament-progress.js`(챕터 경계 건너뛰기 · 구 블롭 fail-closed · 점수 경계값 · 표 밖 낙인 대조) · Unity 컴파일 0건. **남은 것은 실기**: ① 정점 격파 후 로비에서 곧바로 수령 ② 격파 직후 기내모드 → 복귀 재시도, 두 번 다 실패하면 `Playable` 로 남는가 ③ 챕터 2 첫 정점을 디버그로 진입해 승리 → `ChainBlocked` ④ 등급 미달 챕터 ⑤ 다른 기기 수령 후 → `AlreadyClaimed` + 맵 즉시 갱신 |
| P5 | 서버가 `tournament`/`albumReward` 를 쓴 직후 매니저 캐시가 갱신되는지 — 재수화 없이 옛 값이 남으면 즉시 드러나게 로그 |
| P6 | `Tools/firestore-rules-tests` 하네스(`firebase emulators:exec`, **Java 21+ 필요** — Unity 번들 JDK 17 불가). 신규 케이스 3군 전부 통과 후 룰 릴리즈 |

**전체 왕복 (P6 배포 후)**

신규 계정 → 튜토리얼 완주(P2) → 팩 개봉 → 강화·한계돌파(P3) → 토너먼트 정점 격파·수령(P4) → 도감 보상 수령 → 재로그인. 각 지점에서 세이브 `revision` 이 정확히 +1 인지, 지갑 잔액이 원장과 일치하는지 확인.

**판정을 다시 내리는 방법** (감사 문서 §6 의 넷 + 하나)

1. `Assets/Scripts/` 에서 `SetAsync|UpdateAsync|DeleteAsync|RunTransactionAsync` — `OutGame/Save/4.Cloud/` 바깥은 0이어야 한다
2. 같은 범위 `InvokeAsync|InvokeReadOnlyAsync|CallAsync` — 어느 도메인이 서버로 갔는지 그대로 나온다
3. `firestore.rules` 에서 `affectedKeys` — P6 후에는 5슬롯이 잡혀야 한다
4. 서버에서 `nextWallet` 호출부 — 지갑 상태의 유일한 출구. **P1 이 끝나 전부 영수증(브랜드 타입 `WalletUpdate`)을 낸다.** `ledger` 로 grep 하면 0건이다 — 이름이 `receipt`/`receipts` 로 바뀌었다
5. **`devGrantCurrency` 를 `functions/src` 에서만 찾으면 오판한다** — C6.6 이 `functions-currency/` 로 옮겼다

**함정** (인계 문서에서) — 배포 로그는 호출 가능을 증명하지 않는다(URL POST 401 이 정상, 403 이 미바인딩) · `functions:log` 는 3~4분 늦는다 · 룰 하네스는 종료코드가 거짓말한다 · 배포는 `--only functions:default` / `functions:currency` 라벨로만(이름 나열은 삭제 프롬프트로 abort) · 서버 `SCHEMA_VERSION` 과 클라 `UserSaveData.VERSION` 은 반드시 함께 나간다

---

## 문서 정리

- `SERVER_AUTHORITY_AUDIT.md` 에 정정 두 건을 반영한다(C7 currency 분 완료 · 서버 재시뮬 도입, 섀도 모드)
- `CURRENCY_SERVICE_HANDOFF.md` 의 C7 절에서 이미 끝난 항목 3개를 완료 표시하고, 남은 C7 을 "슬롯 동결"(= P6)로 다시 정의한다
- **P1 실측 반영(2026-08-31)** — 이 로드맵이 서기 전에 C8 이 이미 착지해 있었다. `ledgerEntry`/`ledger` 라는 옛 이름을 좇던 자리를 `receipt`/`receipts` 로 고쳤고, 같은 낡은 이름을 `Tools/firestore-rules-tests/README.md` 와 `functions/src/currency/walletStore.ts` 주석 세 곳("아직 호출자가 없다")에서도 걷어냈다. `CURRENCY_SERVICE_HANDOFF.md` 의 C8 절 머리는 본문과 어긋난 "미배포" 표시를 배포일로 고쳤다
- **남은 정정 한 줄** — `SERVER_AUTHORITY_AUDIT.md` §4-(2) 끝의 "남은 것을 닫는 자리는 P1(`claimBattleReward` 멱등)이다" 는 이제 틀렸다. txId 멱등은 이미 섰고, 남은 것은 "전투 없이 1회 호출"이라 **전투 범위**다. 이 문서를 다른 세션이 동시에 고치고 있어 손대지 않았다
- 이 로드맵을 `docs/OutGamePlan/SERVER_AUTHORITY_ROADMAP.md` 로 앉힌다 — `c9f7d71d2` 가 지운 `SERVER_VALIDATION_ROADMAP.md` 자리를 대신한다
