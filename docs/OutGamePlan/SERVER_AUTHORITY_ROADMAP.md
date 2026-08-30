# 서버 권위 이관 로드맵 — 아웃게임

> 실측 2026-08-31 · 브랜치 `feature_Firestore` · HEAD `87b493891`
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

**의도한 결말** — 세이브 9슬롯 중 **5슬롯(`ownership` · `cardGrowth` · `keywordGrowth` · `albumReward` · `tournament`)이 서버 전용**이 되고, 재화가 움직인 모든 기록이 원장에 남으며 재시도가 재과금이 되지 않는다.

---

## 현재 권위 배분 (실측)

**서버가 이미 소유** — 재화 지갑(클라 writer 0) · 팩 추첨 · 강화 2종 · 보상 수령(랭크/도감/챕터/정점) · 멀티 정산 · 신규 계정 · 멀티 덱 검증(`lockDeck` 이 소유·레벨·hpBonus·진화·키워드를 전량 재계산)

**클라가 아직 쥔 것** (부정행위 이득 순)

| # | 항목 | 위치 | 이득 | 이번 범위 |
|---|---|---|---|---|
| 1 | `claimBattleReward` 멱등 없음 | `functions/src/commands/claimBattleReward.ts` | 같은 페이로드 반복 → **무한 골드** | **P1** (멱등만) |
| 2 | 릴리스 빌드 디버그 치트 | `OutgameDebugActions.cs` · `UI/Debug/*` | 버튼 하나로 3~6번 전부 | **P0** |
| 3 | 소유 `ownership` | `OwnershipManager.Grant*` 6경로 | 전 카드 무료 → 팩 우회 + 도감 보상 연쇄 | **P2** |
| 4 | 한계돌파 | `CardGrowthManager.Snack.cs:TryLimitBreak` | 간식 0으로 HP 보너스 최대 · `lockDeck` 도 통과 | **P3** |
| 5 | 토너먼트 해금·낙인 | `TournamentProgress.StateOf` · `MarkRewardPending` | 챕터 건너뛰고 정점 보상 수령 | **P4** |
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

### P1 — 재화 원장 + 멱등 (C8) · 가장 큰 수치 구멍

감사 문서 §4-(2)와 `CURRENCY_SERVICE_HANDOFF.md` 의 C8 을 그대로 집행한다. 지금 `walletStore.ts:193` 의 `ledgerEntry` 는 **정의만 있고 호출부가 0건**이라 재화가 움직인 기록이 한 줄도 없다.

**저장소** — `envs/{env}/users/{uid}/wallet/current/ledger/{txId}`, append-only. 룰은 이미 read/write 전부 `if false`(Admin SDK 전용)라 손댈 것이 없다.

**강제 방법(타입 수준)** — `nextWallet` 이 `(다음 지갑, 원장 줄)` 한 쌍을 내고 `writeWallet` 이 그 쌍을 받게 한다. 잔액만 쓰고 원장을 빠뜨리는 경로가 컴파일 단계에서 사라진다.

**트랜잭션 순서** — ① `ledger/{txId}` 읽기 → 있으면 **기록된 결과를 그대로 반환**(재추첨·재과금 없음) → 없으면 처음 ② 판정·차감(기존 그대로) ③ 잔액 · 도메인 슬롯 · 원장 줄을 한 번에 쓴다. **문서 존재 자체가 중복 판정**이라 별도 멱등 장부를 두지 않는다. 반드시 같은 트랜잭션 안이어야 한다 — 밖으로 빼면 구멍 뚫린 원장이 생기고 동시 요청 둘이 같은 "처음"을 본다.

**배선 대상 = `nextWallet` 호출부 7곳** (grep 한 줄로 전수가 잡힌다):
`openPack.ts:134` · `enhanceCard.ts:137` · `enhanceKeyword.ts:128` · `claimReward.ts:401` · `claimBattleReward.ts:115` · `claimPayout.ts:110` · `functions-currency/src/commands/devGrantCurrency.ts:49`

**클라 변경 한 곳** — `OutGame/Save/4.Cloud/ServerSaveCommands.InvokeAsync` 가 호출마다 `txId`(UUID)를 만들어 payload 에 싣고, **재시도 시 같은 값을 유지**한다. 여기가 단일 창구라 도메인 호출부는 손대지 않는다. `claimBattleReward` 는 이것만으로 멱등이 선다.

**`mutateSave` 도 같이 본다** — `saveDocument.ts:164` 가 기대 revision 을 받지 않고 현재값 +1 만 한다. 원장 조회가 그 앞에 서면 재시도가 걸러지므로 `mutateSave` 시그니처는 바꾸지 않아도 된다. 다만 원장을 안 타는 경로가 남으면 그 자리는 여전히 열려 있다 — 배선 7곳 전수를 지킨다.

**남는 구멍(명시)** — `claimBattleReward` 는 txId 로 **중복**은 막히지만 **전투를 하지 않고 1회 호출**하는 것은 여전히 통과한다. `won`·`remaining` 을 증명할 매치 문서 대조는 전투 범위라 다음 로드맵 몫이다.

`createWallet` 3곳(`ensureWallet.ts:134` · `saveDocument.ts:213` · `:318`)은 잔액 이동이 아니라 지갑 개설이다 — 원장에 남길지는 착수 시 정한다.

**하지 않을 것** — hold/capture, 이벤트 소싱, 복식부기, 웨어하우스 스트리밍.

---

### P2 — 소유(`ownership`) 서버 이관

서버가 소유를 주는 경로는 `openPack` 과 `ensureAccount`(`functions/src/save/starterCards.ts`) 뿐이고, 클라 6경로가 세이브를 직접 쓴다.

1. **튜토리얼 카드 지급을 스펙 표로 승격.** 인계 문서가 "SO 저작 중 아직 서버 근거가 없는 **유일한** 지급 축"이라 지목한 자리다. 진실원이 `OutgameTutorial.asset`/`TutorialScenario*.asset` 의 `playerDeckIds` 다. `AlbumEntry`(40행)·`TournamentChapter`(24행)를 올린 C5.5 의 업로더 오버로드를 그대로 재사용해 `TutorialGrant` 표(스텝 id → cardIds)를 만든다.
2. **callable `grantTutorialCards(stepId)` 신설.** 서버가 표를 보고 지급하고 재지급을 낙인으로 막는다. 낙인 자리는 `grants/current`(이미 룰이 `write: if false`, 강화 무료 한 방이 쓰는 원장) 확장이 자연스럽다 — `tutorial` 슬롯은 동결 제외라 낙인을 거기 두면 위조된다.
3. **클라 호출부 전환** — `Tutorial/Steps/TutorialStepExecutor.cs:203`·`:245`(`AcquireCard`)·`:269`·`:296`·`:358`.
4. **`OutgameTutorialRewind.cs:141`/`:165`/`:173`** 은 `test` env 디버그 경로다. P0 가드 안으로 넣고 서버 왕복을 붙이지 않는다.
5. **`StarterDeck.GrantIfNoDeck`**(`Deck/StarterDeck.cs:29`, 호출부 `Core/Initialization/SaveDependentManagersStep.cs:53`) — 파일 주석대로 신규 계정 정본은 이미 서버 `ensureAccount` 이고 이 경로는 "튜토리얼 되감기가 덱 슬롯을 비웠을 때만" 선다. **에디터 전용 가드로 격리**한다(카드는 서버가 이미 줬으므로 덱 삽입만 남긴다).
6. **`OwnershipManager.Grant`/`GrantAll`/`GrantEntireCatalog`/`RevokeAll` 을 채택 전용으로 축소** — `Init`(서버 슬롯 재수화) 외 진입점을 없앤다.

**결과: `ownership` 슬롯 클라 writer 0.** 도감 완성 판정(`claimReward(Album)` + `completionTable.ts`)이 이 슬롯을 읽으므로 도감 보상의 자기신고 뿌리도 같이 닫힌다.

---

### P3 — 한계돌파 callable

**서버 코드가 이미 다 있는데 callable 이 없어 호출부가 0건이다.** `functions/src/growth/cardGrowth.ts` 의 `applyLimitBreak`/`spendSnack`/`canAffordSnack` 이 전 레포 유일 참조가 정의부다. 적립만 서버(`openPack.ts:131 addSnack`)고 소비·단계증가가 클라로 갈려 있다.

1. **callable `limitBreakCard(cardId)` 신설** — `enhanceCard` 를 형틀로 삼는다(같은 `mutateSave` + 지갑 트랜잭션 구조, P1 원장 배선 포함).
2. **`CardLimitBreak` 스펙 표 배선.** 표(3행 `stage | hpGain | snackCost`)가 `envs/test` 에 이미 올라와 있는데 읽는 코드가 클라에도 서버에도 없다. 실제 판정은 `OutGame/Growth/GrowthRules.cs:71-77` 이 하드코딩한다(`hpGain = 1` 고정 · `snackCost = stage`). 서버가 표를 읽게 하고 클라 하드코딩을 표시용으로 격하한다.
3. **클라 전환** — `CardGrowthManager.Snack.cs:TryLimitBreak` 가 세이브를 쓰지 않고 `ServerSaveCommands.InvokeAsync` 로 보낸다. 응답 채택은 `ServerSlotRehydrator` 의 `CardGrowth` 경로가 이미 있다(강화 2종과 같은 자리).
4. **죽은 코드 삭제** — `CardGrowthManager.AddSnack`(호출자 0건). 주석의 "적립 지점은 `CardPackOpener` 하나"도 사실과 다르니 같이 고친다.

**결과: `cardGrowth` 슬롯 클라 writer 0**(강화 2종은 이미 서버). `keywordGrowth` 는 `KeywordGrowthManager.cs:68` 의 `Save()` 하나가 남는데 이것이 채택 경로인지 확인하고 아니면 같이 걷는다.

---

### P4 — 토너먼트 진행 서버화 (범위 허용분)

지금 지급만 서버(`claimReward(ownerType="Tournament"` / `chapter_` 접두사))이고 **해금 사슬 판정·클리어 확정·낙인이 전부 클라**다. `tournament.pendingRewardNodeId` 를 임의 노드 id 로 직접 써 넣으면 전투 없이 수령 자격이 서고, 순차 사슬 검사(`TournamentProgress.StateOf`)도 클라라 챕터 전체를 건너뛴다.

1. **callable `clearTournamentNode(nodeId, won)` 신설** — 서버가 선행 정점 클리어 사슬과 `requiredGrade` 잠금을 재계산하고, 통과하면 `tournament` 슬롯에 클리어·낙인을 쓴다. 판정 표는 `TournamentChapter`(24행, C5.5 에 이미 업로드)를 읽는다. 판정부는 `completionTable.ts` 옆에 `tournamentTable.ts` 로 둔다 — `claimReward.ts` 가 지금 같은 판정을 자기 안에서 하고 있어 **이중 진실원**이니 그것을 이쪽으로 흡수한다.
2. **클라 전환** — `TournamentProgress.MarkRewardPending`(호출부 `Battle/BattleOutcome.cs:35`) · `MarkCleared` · `ClearPendingReward` 가 세이브를 쓰지 않는다. `StateOf`/`CanEnter`/`IsChapterComplete` 는 **표시용 낙관 판정**으로 격하(`CardPackOpener.Precheck` 와 같은 성격).
3. **재수화** — P5 가 `Tournament` 슬롯을 `ServerSlotRehydrator` 에 추가한다.

**한계를 문서에 명시한다.** 서버가 막는 것은 "순서 건너뛰기"와 "중복 클리어"이지 **"전투 없이 이겼다고 주장하는 것"이 아니다**(`won` 은 여전히 클라 신고, 전투는 범위 밖). `requiredGrade` 자격도 `rank.points` 가 클라 소유로 남는 한 자기신고 위에 선다. 그래도 사슬 판정이 서버로 가면 **한 번의 거짓 신고로 한 정점만** 넘어가고 챕터 전체를 건너뛸 수는 없다.

---

### P5 — 재수화 구멍 메우기 (P6 선행조건)

`ServerSlotRehydrator.Rehydrate` 는 `Ownership`·`KeywordGrowth`·`CardGrowth` 세 슬롯만 재수화한다(파일 안 `TODO(R5+)`·`TODO(R7·R9)`).

- **`AlbumReward` · `Tournament` 추가.** P4 가 `tournament` 슬롯을 서버가 쓰게 만드는 순간 필요해진다. 두 매니저는 지금 `DataSaveManager.Data` 를 매번 읽어 우연히 맞지만, static 캐시를 도입하는 순간 깨진다.
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

- **`envs/live/specs` 0표 업로드.** 실측 기준 `envs/test` 는 14표인데 `live` 는 비어 있다. 서버는 표를 못 읽으면 fail-closed 로 거절하므로 **live 에서 수령이 전부 막힌다.** P1~P4 가 표를 하나씩 더 요구하므로(`TutorialGrant` 신설 · `CardLimitBreak` 배선 · `TournamentChapter` 활용) 업로드 파이프라인을 먼저 세운다
- **`link.xml` 갱신.** 새 callable 응답 DTO 를 만들 때마다 `OutGame/Save/link.xml` 에 추가한다(IL2CPP 스트리핑 방어, `6f6a8885c` 가 팩 DTO 로 한 번 겪었다)
- **`functions-currency` 미러.** `devGrantCurrency` 가 P1 배선 대상이므로 `functions-currency/scripts/shared-files.js` 의 미러 5파일(`walletStore.ts` 포함)이 같이 움직인다. `npm test`(`test-wallet-mirror.js`)가 미러 순수성을 지킨다 — `walletStore` 에 `HttpsError` 를 넣으면 그 계약이 깨진다

---

## 검증

**단계별 게이트**

| 단계 | 검증 |
|---|---|
| P0 | 리테일 빌드 컴파일 통과(`Unity_ReadConsole`) + 릴리스 빌드에서 디버그 오버레이·버튼 부재 육안 확인 |
| P1 | `functions/scripts/test-wallet-store.js` 확장(원장 줄 생성·재시도 반환) · 같은 `txId` 2회 호출 후 잔액 1회분만 오르는지 실기 · `ledger/{txId}` 문서 생성 확인 |
| P2 | `functions/scripts/test-fresh-account.js` 옆에 튜토리얼 지급 회귀 추가 · 튜토리얼 완주 실기 왕복(카드가 서버 표대로 들어오는가 · 재호출이 낙인에 막히는가) |
| P3 | `functions/scripts/test-growth.js` 에 한계돌파 케이스 추가(간식 부족 거절 · 최대 단계 거절 · 표 결손 fail-closed) · 실기: 카드 상세에서 한계돌파 → `cardGrowth` 재수화 확인 |
| P4 | 정점 순서 건너뛰기 거절 · 중복 클리어 거절 회귀 · 실기: 정점 격파 후 로비에서 곧바로 수령(낙인 즉시 업로드 경로) |
| P5 | 서버가 `tournament`/`albumReward` 를 쓴 직후 매니저 캐시가 갱신되는지 — 재수화 없이 옛 값이 남으면 즉시 드러나게 로그 |
| P6 | `Tools/firestore-rules-tests` 하네스(`firebase emulators:exec`, **Java 21+ 필요** — Unity 번들 JDK 17 불가). 신규 케이스 3군 전부 통과 후 룰 릴리즈 |

**전체 왕복 (P6 배포 후)**

신규 계정 → 튜토리얼 완주(P2) → 팩 개봉 → 강화·한계돌파(P3) → 토너먼트 정점 격파·수령(P4) → 도감 보상 수령 → 재로그인. 각 지점에서 세이브 `revision` 이 정확히 +1 인지, 지갑 잔액이 원장과 일치하는지 확인.

**판정을 다시 내리는 방법** (감사 문서 §6 의 넷 + 하나)

1. `Assets/Scripts/` 에서 `SetAsync|UpdateAsync|DeleteAsync|RunTransactionAsync` — `OutGame/Save/4.Cloud/` 바깥은 0이어야 한다
2. 같은 범위 `InvokeAsync|InvokeReadOnlyAsync|CallAsync` — 어느 도메인이 서버로 갔는지 그대로 나온다
3. `firestore.rules` 에서 `affectedKeys` — P6 후에는 5슬롯이 잡혀야 한다
4. 서버에서 `nextWallet` 호출부 — 지갑 상태의 유일한 출구. **P1 후에는 전부 원장 줄을 반환해야 한다**
5. **`devGrantCurrency` 를 `functions/src` 에서만 찾으면 오판한다** — C6.6 이 `functions-currency/` 로 옮겼다

**함정** (인계 문서에서) — 배포 로그는 호출 가능을 증명하지 않는다(URL POST 401 이 정상, 403 이 미바인딩) · `functions:log` 는 3~4분 늦는다 · 룰 하네스는 종료코드가 거짓말한다 · 배포는 `--only functions:default` / `functions:currency` 라벨로만(이름 나열은 삭제 프롬프트로 abort) · 서버 `SCHEMA_VERSION` 과 클라 `UserSaveData.VERSION` 은 반드시 함께 나간다

---

## 문서 정리

- `SERVER_AUTHORITY_AUDIT.md` 에 정정 두 건을 반영한다(C7 currency 분 완료 · 서버 재시뮬 도입, 섀도 모드)
- `CURRENCY_SERVICE_HANDOFF.md` 의 C7 절에서 이미 끝난 항목 3개를 완료 표시하고, 남은 C7 을 "슬롯 동결"(= P6)로 다시 정의한다
- 이 로드맵을 `docs/OutGamePlan/SERVER_AUTHORITY_ROADMAP.md` 로 앉힌다 — `c9f7d71d2` 가 지운 `SERVER_VALIDATION_ROADMAP.md` 자리를 대신한다
