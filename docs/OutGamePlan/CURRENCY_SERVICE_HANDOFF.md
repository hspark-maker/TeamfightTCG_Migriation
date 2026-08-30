# 재화 독립 서비스 분리 — 인계 문서

> 최종 갱신 2026-08-30 · 브랜치 `feature_Firestore` · HEAD `eea13ea13`
> 상위 문서: `SERVER_VALIDATION_ROADMAP.md` (이 작업은 그 R6~R9를 재화 축으로 앞당긴 것)

## 왜 하는가

인앱결제가 붙을 예정이라 재화는 게임 내부 도메인으로 남지 않는다. 실화폐가 오간다.
지금 구조의 문제 셋: 잔액이 세이브 문서의 `currency` 슬롯 안에 있고 · 클라가 그 슬롯을 직접 쓰며 · 배포 단위가 하나라 결제 검증 코드와 장애 반경이 게임 로직과 섞인다.

## 확정된 결정 — 재론하지 말 것

| # | 결정 |
|---|---|
| 1 | **배포 단위 분리.** `firebase.json` 에 codebase `currency` 추가 |
| 2 | **저장소 분리.** `envs/{env}/users/{uid}/wallet/current` + `ledger/{txId}`. 세이브에서 `currency` 슬롯 제거(15키→14키) |
| 3 | **IAP 는 구조·자리만.** 원장 스키마와 진입점 자리만. 기록·멱등·영수증 검증은 IAP 착수 때 |
| 4 | **원자성 우선.** 지갑 문서를 쓰는 코드는 단일 원본. 복합 명령은 default codebase 에서 save+wallet 두 문서를 한 트랜잭션에 쓴다. Firestore 트랜잭션은 문서는 걸쳐도 **프로세스 경계는 못 넘는다** — 독립 functions 가 보장하는 것은 배포·의존성·IAP 진입점 격리이지 쓰기 배타성이 아니다 |
| 5 | **소유권 이전 먼저, 저장소 전환 나중.** 도메인 callable 이 당분간 기존 `currency` 슬롯에 쓴다. 그래야 모든 단계가 플레이 가능하다 |

## 지금 상태

**커밋 16개**

- `6c95b114a` C1·C2 — `functions-currency/` codebase · 미러 동기화 장치 · `walletStore.ts` · 룰 지갑 블록 · 순수 회귀 · 하네스 43케이스
- `1d0d1aa31` C3 — `claimReward`(랭크 티어 · 토너먼트 정점) + 클라 2곳 전환
- `120110833` 스펙시트 `Reward` 표 복구 — 머지가 옛 굽기본을 택해 부팅이 막혔다
- `6f6a8885c` `link.xml` 에 팩 개봉 응답 DTO — IL2CPP 스트리핑 방어
- `d2a4fdc3c` `claimReward` 의 보상 영구 손실·계정 영구 잠김 경로 2건
- `84f53288f` C4 — `enhanceCard`·`enhanceKeyword` + 튜토 무료 한 방 서버 이전 + 거절 사유 전달 경로 교정
- `fe4fe954d` C5.5 — 도감·챕터 **구성**을 스펙 표로 승격(`AlbumEntry` 40행 · `TournamentChapter` 24행) + 업로더 오버로드
- `3f522a2e3` C5.6 — 도감·챕터 **수령**을 `claimReward` 로 이전(ownerType `Album` 추가 · 챕터는 `chapter_` 접두사 분기)
- `c7ab0c290` C3.5 — 수령 경로의 낙인 즉시 업로드 · 폴백 콜백 계약 · `granted` 연출 입력
- `660963744` C5 — `claimBattleReward` · `devGrantCurrency`
- `c099173c6` C6.1 — 지갑 `paidBalances` 사이드카 · `nextWallet` 단일 출구 · `walletMigration`
- `0b2b6b3d6` C6.2 — `SCHEMA_VERSION 8` · `mutateSave` 가 지갑을 데리고 트랜잭션에 들어간다 · `ensureWallet` · 룰 `currency` optional
- `1416c4031` C6.3 — 명령의 재화 접점이 전부 지갑으로 · `currencySlot` 삭제
- `8edb278f2` C6.4·C6.5 — 클라 `UserSaveData.VERSION 8`(슬롯 9) · `WalletCloud` · `claimPayout.ack` 크레딧 → **클라 재화 writer 0**
- `cd1027476` C6.6 + 검수 — 승급 창 철회 · `devGrantCurrency` 를 `currency` codebase 로
- `eea13ea13` C7 — 룰 `hasOnly` 14키 · currency 검증 블록 제거 · 하네스 13b·13c·13e 를 13c 하나로

**배포 상태 (실측 2026-08-28 · C6 전량 배포 완료)**

| 함수 · 규칙 | 상태 |
|---|---|
| `firestore.rules` C6.2 완화분(`currency` optional · `hasAll` 14키) | **배포됨** · 컴파일 통과 후 릴리즈 |
| `firestore.rules` C7 조인분(`hasOnly` 14키 · currency 금지) | **미배포** — 하네스 초록, 릴리즈는 사람이 1회 |
| `ensureWallet` (codebase `default`) | **배포됨 · 신규 create** · **401**(정상) |
| `claimReward` · `enhanceCard` · `enhanceKeyword` · `openPack` · `ensureAccount` · `claimBattleReward` · `claimPayout` (codebase `default`) | **배포됨**(C6 코드) · **401**(정상) |
| `ping` · `devBumpRevision` · `createMatch` · `lockDeck` · `submitMatchResult` | 함께 갱신됨(동작 변화 없음) |
| `devGrantCurrency` (codebase **`currency`**) | **이사 완료 · 배포됨** · **401**(정상) |
| `currencyPing` (codebase `currency`) | 배포됨 · **401**(정상) |
| `firestore.rules` 지갑 블록 · 무료 한 방(`grants`) 블록 | **배포됨** — 룰은 파일 통째로 릴리즈되므로 C6 완화분과 같이 올라갔다 |

전 함수 10종을 URL POST 로 찔러 **403 이 하나도 없었다** — 신규 생성된 `ensureWallet` 조차 invoker 바인딩을 상속해 그 함정을 밟지 않았다.

**함정이 하나 사라졌다 — codebase 이동에 `functions:delete` 가 필요 없다.**
"함수 id 는 project+region 에서 유일하니 `default` 소유권을 먼저 풀어야 한다" 고 예상했으나, `firebase deploy --only functions:currency` 가 기존 `devGrantCurrency` 를 그대로 **update** 로 처리했다(CLI 15.28.1 실측). 삭제부터 했다면 되돌릴 수 없는 공백만 만들 뻔했다. **이사는 시도부터 하고, 충돌이 실제로 날 때만 삭제해라.**

**배포 순서 — 여기만은 순서가 계약이다**

서버 `SCHEMA_VERSION 8`(`functions/src/save/saveDocument.ts:33`)과 클라 `UserSaveData.VERSION 8`(`UserSaveData.cs:17`)은 **반드시 함께 나간다.**

- **서버만 먼저 올리면** 기존 계정이 첫 왕복에서 v8 로 승급되고, v7 클라는 부트가 `remote > VERSION` 을 보고 **강제 업데이트 화면**에 갇힌다(`PlayerSaveCloud` → `MarkUpdateRequired`). 첫 피해자는 에디터에서 도는 개발자 본인 클라다
- 그리고 이제 v7 클라의 callable 은 **첫 명령에서 `failed-precondition`** 으로 막힌다(`assertWritableSchema` 가 정확히 `== SCHEMA_VERSION`). 사고가 아니라 의도다 — 근거는 아래 C6 절의 "승급 창 철회"
- **`ensureWallet` 이 먼저 서야 한다.** 그게 없는데 v8 클라가 나가면 승급을 수행할 함수가 없어 부트가 통째로 막힌다

**주의 — 재화 codebase 는 더 이상 진단용이 아니다.** `devGrantCurrency` 가 C6.6 에서 `functions-currency/` 로 이사해, 그 codebase 가 `currencyPing` 너머로 **실제 지갑 쓰기를 왕복으로 증명**한다. 나머지는 여전히 `default` 다 — `openPack`·`enhanceCard`·`enhanceKeyword`·`claimReward` 는 세이브 슬롯도 함께 써서(결정 4), `claimBattleReward` 는 `Reward` 스펙 표 리더에 의존해서, `claimPayout` 은 `payouts` 낙인과 크레딧을 한 트랜잭션에 묶어야 해서다.

---

## 배포·판정 절차 — 새 callable 마다 그대로 반복

C3 분(룰 · `claimReward` · 실기 왕복)은 2026-08-28 에 셋 다 닫혔다. 아래는 **C4 이후 새 callable 을 올릴 때마다 다시 타는 절차**다.

### 1) 배포

```bash
firebase deploy --only firestore:rules --project bm-cardbattle
firebase deploy --only functions:claimReward --project bm-cardbattle
```

- **`--only functions` 를 그냥 치면 abort 한다.** 남의 함수 `lockDeck` 삭제 여부를 묻고 non-interactive 라 멈춘다
- **함수 이름을 나열하지 말고 codebase 라벨로 겨눠라** — `--only functions:default` · `--only functions:currency`. 라벨이 조회 범위를 갈라 삭제 프롬프트 자체가 안 뜬다(실측). C6 전량 배포를 이 두 줄로 끝냈다
- codebase 를 옮긴 함수도 그냥 그 codebase 를 배포하면 된다. `functions:delete` 선행은 불필요하다(위 "함정이 하나 사라졌다")

### 2) 배포 직후 판정 — 배포 로그는 근거가 아니다

```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST \
  -H "Content-Type: application/json" -d '{"data":{}}' \
  https://asia-northeast3-bm-cardbattle.cloudfunctions.net/claimReward
```

**401 = 정상**(함수가 실행됐다) · **403 = Cloud Run invoker 바인딩 없음** → 콘솔에서 **소문자** 서비스명 `claimreward` → 권한 → `allUsers` + `Cloud Run 호출자`. 바인딩은 서비스를 새로 만들 때만 걸려서 신규 함수마다 이 함정을 밟는다.

### 3) 실기 왕복 (사람 1회) — 앞이 실패하면 뒤는 볼 필요 없다

C3 왕복은 **통과했다**(2026-08-28). 항목 형태는 C4 이후에도 그대로 쓴다 — 명령 이름과 낙인 필드만 갈아끼운다.

1. 랭크 티어 수령 → 잔액이 `Reward` 표 값만큼 늘고 `revision` 정확히 +1
2. 토너먼트 정점 격파 → 수령 → 잔액 증가 **그리고** 그 정점이 Cleared 로 굳고 `pendingRewardNodeId` 가 빈다(콘솔 문서 확인)
3. **이미 받은 티어를 다시 수령 → 경고만, 로비 유지.** 재시작 모달이 뜨면 실패다(거절이 `permission-denied` 가 아니라는 뜻)
4. 분출·잔액 롤업이 왕복 뒤로 밀렸으니 숫자가 뒤로 튀지 않는지

---

## 남은 작업

### C3.5 — 검수 미해결 3건 ✅ (`c7ab0c290`)

C3 리뷰가 남긴 것들이다. 셋 다 **이미 서버로 옮긴 수령 경로 안에서** 어긋나 있었다 —
지급 판정은 서버가 가져갔는데 그 앞뒤의 영속·연출 배관이 옛 로컬 지급 시절 형태로 남아 있었다.
C4·C5.5·C5.6 이 그냥 지나가 세 번 미뤄졌던 몫이라 다른 단계에 섞지 않고 독립 커밋으로 닫았다. **서버 무변경 · 클라 전용.**

- `TournamentProgress.MarkRewardPending` 의 디바운스 `DataSaveManager.Save()` → **`SaveImmediate()`**.
  자격을 재는 쪽이 서버라, 격파 직후 바로 수령하면 낙인이 원격에 없어 `NotEligible` 로 튕겼다
- `TournamentRewardFlow.Open` 의 폴백(보상 0건·팝업 미배선)이 `ClearNodeAsync` 를 던져 두고 false 를 돌려,
  호출부가 그 자리에서 부른 `PlayClaimSequence` 가 아직 false 인 `IsCleared` 가드에 걸려 **점등·해금이 통째로 빠졌다**.
  **`Open` 의 반환값 의미를 "팝업이 떴는가" → "`_onClosed` 가 오는가" 로 바꿨다** — 폴백은 `ClaimThenNotify` 가
  왕복 뒤에(거절돼도) 콜백을 부르고, false 는 시작조차 못 한 경우(빈 id·이미 클리어)뿐이라 호출부는 `AbortClaimSequence` 로 억제만 푼다
- **연출 입력이 `granted` 로 갈렸다.** `CurrencyHud` 는 롤업 시작값을 `(최종 잔액 − 지급량)` 으로 **역산**하고,
  응답 채택은 `await m_onConfirm()` 안에서 이미 끝나 잔액이 최종값이다 — 지급량이 서버와 다르면 시작 숫자가 틀려 눈에 보이게 튄다

**배관 (수령 4도메인이 공유)** — `RewardClaimCommand.ClaimAsync` 가 `UniTask<bool>` → **`UniTask<RewardClaimOutcome>`**
(`Succeeded` + `IReadOnlyList<CurrencyGain> Granted`. 변환은 기존 `CurrencyCode.TryParse`, 못 읽는 표기·0 이하는 버린다).
`RankRewardManager.ClaimAsync` · `AlbumRewardManager.Claim*` · `TournamentProgress.ClearNodeAsync` · `ClaimChapterRewardAsync` 가 그대로 흘리고,
`RewardClaimPopup` 이 `BuildLightGain` 직전에 `AdoptGranted` 로 분출 버킷을 실지급으로 세운다.

- **표시 슬롯은 여전히 클라 스펙이다 — 표시는 예고, 분출·롤업만 서버가 이긴다.** 수령 전엔 서버 값이 없다
- 버킷의 유일한 writer 가 `AdoptGranted` 다(`Show` 시점 채우기는 제거 — 이중 진실원이었다)
- `Succeeded` 인데 `Granted` 가 비면 **경고 + 분출 생략**. 잔액이 안 늘었는데 예고량으로 롤업하면 숫자가 뒤로 뛴다
- 호출부가 0건이던 동기 `Show(… Func<bool> …)` 오버로드는 삭제했다

**남은 확인** — 실기 왕복 3건. ① 정점 격파 후 로비에서 **곧바로** 수령(낙인 즉시 업로드) ② 보상 미저작 정점 수령 시 팝업 없이도 점등·해금 ③ 롤업 시작 숫자가 `(최종 − 서버지급)` 인지

### C4 — 성장 2종 ✅ (`84f53288f`)

`enhanceCard` · `enhanceKeyword` 가 섰다. 클라 `CardGrowthManager.TryEnhanceAsync` · `KeywordGrowthManager.TryEnhanceAsync` 는
왕복을 아끼는 낙관 선검사(미초기화·만렙)만 남기고 차감·성공률·레벨을 전부 서버에 넘겼다. **클라 재화 writer 2곳이 사라졌다.**

- 곡선 근거는 스펙 표다 — `CardEnhanceRule`(전역 1행) · `CardEnhance`(레벨 2·3·4 오버라이드 25/75/150 Shard) · `KeywordEnhance`(6종 · Energy `5 + 5×(N−1)` · maxLevel 10). 순수 파서는 `functions/src/growth/enhanceRules.ts`
- 성공률 RNG 경로는 만들었으나 **지금은 발화하지 않는다**(표가 전 행 `successPermille 1000`). 실패해도 비용은 나가고 레벨은 안 내려가는 기존 규칙 그대로다
- `GrowthRules.MaxLevel` = `CardSpec.MaxHpCurveLevel` = **4** 로 서버 `CARD_MAX_LEVEL_CEILING` 과 일치(드리프트 없음)
- 응답 채택 뒤 캐시 재구축은 기존 `ServerSlotRehydrator` 가 이미 `CardGrowth`·`KeywordGrowth` 를 덮고 있어 추가 배선이 없었다

**튜토리얼 무료 한 방을 같이 옮겼다(로드맵 미결 #5 해소).** 안 옮기면 C4 가 온보딩을 그 자리에서 세운다 —
신규 계정 지급은 `STARTER_GOLD` 뿐이라 Shard·Energy 가 0인데 온보딩이 카드 강화(25)·키워드 강화(5)를 시킨다.

- 새 문서 `envs/{env}/users/{uid}/grants/current` — 축별(`enhanceCard`·`enhanceKeyword`) **계정당 1회**. 서버 전용 쓰기(룰 `write: if false`), 소유자 읽기만 연다
- **세이브 스키마와 무관하다** — 별도 경로라 15키 계약도 `SCHEMA_VERSION` 도 안 건드린다. C6 의 v7→v8 승급과 부딪히지 않는다
- 읽기·쓰기 모두 `mutateSave` 콜백 **안**이다(트랜잭션). 동시 호출 둘이 같은 "미사용"을 보는 갈래가 막힌다
- 무료는 **비용만 0** 으로 만들고 성공률은 안 건드린다. 소진 낙인은 **성공했을 때만** 찍는다 — 실패로 닫으면 안내가 시킨 성장을 유저 돈으로 다시 해야 한다
- 클라는 여전히 `OutgameTutorialGuide.HasFreeShot` 으로 **요청 시점**을 정하고, 실제 소진은 응답 `freeShotUsed` 를 보고 찍는다. 진실원은 서버다

**거절 사유 전달 경로를 고쳤다(계약 파손 발견).** `rejectDomain` 은 사유를 `details.reason` 으로 보냈는데
**Unity SDK 가 그것을 버린다** — `Assets/Firebase/FirebaseFunctions/Internal/FunctionsErrorParser.ParseError` 가
`status` 와 `message` 만 살리고 `FunctionsException` 에 details 프로퍼티 자체가 없다. 즉 `openPack` 의
`InsufficientGold` 도 지금껏 클라에 닿은 적이 없다(`CardPackOpener` 가 `Precheck` 재실행으로 사유를 되짚어 가려 온 것).

- `rejectDomain` 이 message 를 `"Reason: 설명"` 으로 만든다. 떼어 내는 자리는 `ServerCommandRejectedException.Reason` **하나**다
- `ServerCommandRejectedException.Message` 는 명령 이름으로 한 번 감싸므로 앞머리 파싱은 **내부 예외 기준**이어야 한다. 이미 그렇게 돼 있다
- 이 경로가 열렸으니 `CardPackOpener` 의 `Precheck` 재실행도 사유 직독으로 갈아탈 수 있다(별건)

**남은 것**

- **배포·실기 왕복이 아직이다.** 위 "배포·판정 절차" 를 그대로 탄다
- **한계돌파(Snack) 는 여전히 클라 판정이다.** `CardLimitBreak` 표(3행 · `stage | hpGain | snackCost`)도 서버 `canAffordSnack`·`spendSnack`·`applyLimitBreak` 도 이미 있는데 callable 이 없다. 재화 축이 아니라 "재화 writer 0" 집계엔 안 잡히지만 성장 판정 소유권 목표에는 걸린다 — **미결**
- `enhanceCard` 는 **카드 소유 여부를 안 본다.** 미보유 카드도 조각을 내고 강화된다. 덱 편성이 소유 필터를 걸고 덱 검증도 서버에 있어 이득 없는 자해 경로라 막지 않았다
- `KeywordGrowthManager.Save()` 는 호출부가 없어졌다. 누가 부르면 이중 진실원이 되므로 제거 후보

### C5 — 전투·디버그 2종 ✅ (`660963744` · 배포됨)

`claimBattleReward` · `devGrantCurrency` 가 섰다. 클라 `Earn` 호출부가 2곳 사라져 그때 남은 것은 `PayoutInbox` 하나였고, **C6.5 가 그것마저 닫았다.**

**금액 공식은 새로 쓰지 않았다** — `functions/src/payout.ts` 의 `computeCurrencyPayout(won, remaining, rows)` 를 그대로 배선했다. `functions/scripts/test-claim-reward.js` 에 Battle 경계 회귀(생존 0=floor · 생존 6 · 패배는 생존 무관 · 표 결손 3종 throw)를 보강했다.

**착수 전 미결 셋을 이렇게 답했다.**

1. **`TurnRunner.CaptureResult` 는 동기 `void` 로 남겼다.** 팝업은 왕복을 기다리지 않는다 — `lastReward` 는 승패·싱글/멀티 무관하게 `RewardService.CalculateReward` **예상액**이고, 싱글이면 그 자리에서 지급 호출만 띄운다. `resultCaptured`·`resultFinalized` 게이트는 무변경
2. **실패하면 아무것도 보여주지 않는다** — `BattleRewardHandoff` 를 세우지 않아 로비 획득 연출이 아예 안 돈다. 들어오지도 않은 재화의 연출을 도는 것보다 낫다. **재시도하지 않는다**(서버가 멱등이 아니라 두 번째 호출이 그대로 이중 지급)
3. **멱등 축은 여전히 없다.** C8 원장 `txId` 가 닫는다 — 순서 그대로 둔다

**멀티가 이미 갖고 있던 모양에 싱글을 합류시켰다.** `submitMatchResult` 는 양쪽 제출을 대조해야 `payouts/{matchId}` 를 세우므로 결과 팝업 시점에 서버 확정액이 존재할 수 없다. 그래서 멀티는 **팝업=예상액(전투 씬) · 지급=서버 · 연출=캐리어(로비)** 로 나뉘어 있었다. 싱글도 같은 세 박자다 — C6 에서 고칠 자리가 하나로 준다.

**늦게 도착하는 연출 구멍을 같이 닫았다.** `LobbyGainEffectDirector` 는 캐리어를 `Start` 와 팩 오버레이 닫힘에서만 소비해, 응답이 로비 진입보다 늦으면 잔액만 조용히 올랐다(**멀티는 상대 제출이 늦을 때마다 이미 그랬다**). `BattleRewardHandoff.OnGainAdded` 를 **네 번째 진입점**으로 붙였다.

- 이 경로는 **`OnAnyFinished` 를 내지 않는다**(`Play(_silent: true)`). 내면 그 신호를 기다리던 튜토리얼 `CardGain` 스텝과 `TournamentReturnFlow` 의 선물 등장이 자기 차례로 오인해 조기 통과한다 — 튜토리얼 전투도 전투 골드를 받으므로 실재하는 갈래다
- 돌고 있는 연출(`m_master.IsActive()`)·도감 삽입(`AlbumInsertSession`/`Queue`)·튜토 러너 2종 중 하나라도 살아 있으면 **비켜선다**. 캐리어는 남아 다음 `Start` 가 집는다(`PlayWhenReady` 가 `m_master.Complete(true)` 로 진행 중인 카드 비행을 잘라 버리기 때문)

**이 표는 닫혔다 — `CurrencyManager.Earn`/`Spend`/`Save` 는 C6.4 에서 삭제됐고 호출부는 0 이다.** 이력만 남긴다: 전투 보상 `Utils/RewardService`(C5) · 디버그 지급 `OutGame/Debug/OutgameDebugActions`(C5) · 도감·정점 `AlbumRewardManager`·`TournamentProgress`(C5.6) · 멀티 payout `Network/PayoutInbox`(C6.5). 지금 잔액을 세우는 클라 코드는 `CurrencyManager.Adopt` 하나이고 그 호출부는 `WalletCloud` 하나다.

**알려진 위험 4건 — 전부 이번 구조가 만든 것이고, 닫는 자리는 C6·C8 이다.**

- **에디터 멀티 테스트 세션은 잔액이 0 으로 고정된다.** `PlayerSaveCloud.Initialize` 의 `IsTestAccountSessionDisabled` 갈래가 `State=Disabled` 로 세워 `CanRunServerCommand` 가 false 다. C6 이후엔 그 갈래가 **지갑 읽기까지 함께 끈다** — 잔액의 진실원이 서버 문서 하나뿐이라 전투 보상만이 아니라 표시되는 재화 전부가 0 이다. 버그가 아니다: 여기서 지갑만 살리려면 auth·읽기·`ensureWallet`(서버 쓰기)이 되살아나 그 분기의 목적이 사라진다. QA 가 반드시 밟는 자리
- **응답만 유실되면(타임아웃) 세션이 끊긴다.** 서버는 revision 을 올렸는데 클라가 못 받으면 `ResumeUploads` 가 태운 업로드가 `RevisionConflictException` → `BlockSession(RemoteAhead)`. **매판 도는 자리라 다른 명령과 빈도가 다르다**
- **오프라인은 거절도 차단도 아니다.** `Offline` 은 `CanRunServerCommand` 를 통과해 호출이 나갔다 죽고, 보상은 그대로 소실된다. 유저에겐 "골드가 안 늘었다" 로만 보인다
- **결과 팝업 숫자와 로비 롤업 숫자가 갈릴 수 있다.** 팝업은 클라 `RewardSpec` 예상액, 캐리어는 서버 확정액이다 — 표가 드리프트하면 팝업만 틀린다(롤업은 맞는다)

**배포됐다.** `firebase functions:list` 로 둘 다 서 있는 것을 확인했고(2026-08-28), 이후 C6 코드로 재배포되면서 `devGrantCurrency` 는 `currency` codebase 로 옮겨 갔다. Cloud Run invoker 바인딩(403)은 이 둘에도, 신규 `ensureWallet` 에도 걸리지 않았다.

**남은 것 — 실기 왕복 (사람).**

1. 싱글 **승리** → 결과 팝업 금액이 `max(생존 × win.perCard, win.floor)` 와 일치하고, 로비 도착 시 획득 연출이 돌며 잔액이 그만큼 증가 · `revision` 정확히 +1
2. 싱글 **패배** → `lose.flat` 만큼 증가(생존 수 무관)
3. **토너먼트 전투** → 전투 골드가 붙지 않는다(정점 보상만)
4. **멀티 전투** → 기존 `PayoutInbox` 경로 그대로, 이중 지급 없음
5. **튜토리얼 전투 직후** → 로비 안내(`CardGain` 스텝)가 조기 통과하지 않는다
6. 디버그 오버레이 재화 버튼 → `test` env 에서 +1000 반영

### C5.5 — 도감·챕터 구성 표 승격 ✅ (`fe4fe954d`)

서버가 판정할 근거가 SO 에만 있었다. 두 표로 올렸고 `envs/test` 에 rev 1 로 서 있다.

- **`AlbumEntry`** (`id | themeId | pageId | cardId | order`) — **40행.** `Theme_Nature` · `P1`~`P5`(9/9/9/9/4) · cardId 1~40 유니크
- **`TournamentChapter`** (`id | chapterId | nodeId | order`) — **24행.** `chapter_01`~`04` 각 6정점

**업로드는 기존 커밋 기계를 그대로 쓴다.** `SpecFirestoreUploader.Upload` 에서 SpecData 매니저에 묶인 두 줄과 그 뒤를 갈라 `UploadSnapshot`·`TryBuildSnapshotFrom` 으로 뺐고, 새 경로가 같은 함수를 부른다 — 해시·정렬·문서 ID·`updateTime` precondition·500writes/10MiB 가드가 한 벌이다. 진입점은 릴리즈 관리 창의 "구성 표" 칸이고 **사람이 1회 실행**한다(admin 클레임 필요).

**클라는 손대지 않았다.** `SpecPayloadCodec.TableNames` 에 넣을 이유가 없다 — 서버 전용 근거이고 넣으면 부팅 왕복만 는다.

**함정 — `Assets/Table/SpecDatas.cs` 에 이 두 표의 `[GeneratorSpecData]` 클래스를 만들지 마라.** 한 번 들어갔다가 되돌렸다. 그게 있으면 `ListTables` 에 두 표가 떠서, 디자이너 시트에 해당 탭이 없는 채로 **일반 표 업로드를 돌리면 0행으로 덮고 stale 삭제까지 돌아 판정 표가 사라진다.** 서버가 fail-closed 라 그 순간 도감·챕터 수령이 전부 막힌다. 원본은 SO 하나여야 한다.

행을 짓는 단계에서 저작 결함을 막는다 — `cardId <= 0` · `Card` 표 미존재/비Live · 페이지 내 중복은 칸을 빼고 보고하며, `themeId`·`chapterId` 중복과 **모수 0**(페이지 0개 테마 · 유효 칸 0개 페이지 · 정점 0개 챕터)은 업로드를 중단한다.

### C5.6 — 도감·챕터 수령 이전 ✅ (`3f522a2e3`)

`claimReward` 의 **ownerType 확장**이다. 새 callable 이 아니다.

- 도감 → ownerType **`"Album"`**, ownerId 는 `b` / `t:{theme}` / `p:{theme}/{page}`. **이 문자열이 이미 세 곳에서 같다** — `AlbumSpec.OwnerIdOf` 가 만드는 값 · 세이브 `claimedKeys` · `Reward` 표 `ownerId`. 그래서 스키마 변경이 없었다
- 챕터 → ownerType **`"Tournament"` 유지** + `chapter_` 접두사로 분기. 새 ownerType 을 만들면 `Reward` 표의 챕터 행까지 고쳐 표를 다시 올려야 한다
- 자격은 서버가 **`ownership.cardIds`·`clearedNodeIds` 로 재계산**한다. 판정은 순수 모듈 `functions/src/completionTable.ts` 로 떼어 회귀가 `lib` 를 직접 부른다
- **모수 0 은 완성이 아니다.** 표에 그 페이지·챕터 행이 없으면 `NotEligible`. 표를 못 읽어도 fail-closed
- `judgeRewardClaim` 의 미저작 통과는 **정점만** 남는다. 판정을 `node_` 접두사가 아니라 **`chapter_` 부정**으로 한 이유: nodeId 는 저작 자유값이라 접두사를 강제하면 미저작 정점이 소급해 막힌다
- 클라는 로컬 지급·낙인·`Save` 를 지우고 `ClaimAsync` 한 줄로. 수령 3종과 챕터가 `UniTask` 가 되면서 흐름 2개와 호출부 4곳이 따라갔다 — **폴백도 `await` 로 낙인을 기다린다**(같은 프레임에 연출을 붙이면 `TournamentMapOverlayView` 의 스킵 버그를 되풀이한다)

**`CurrencyManager.Earn` 호출부가 5 → 3 이 됐다.** 남은 셋은 전투 보상 · 디버그 지급(C5) · 멀티 payout(C6).

**이 판정이 지금 서 있는 지반** — 서버가 완성을 재계산하지만 그 입력인 `ownership.cardIds` 의 진실원은 **아직 클라다.** 서버가 소유를 쓰는 곳은 `openPack`·`freshAccount` 둘뿐이고 스타터 덱·튜토 지급·되감기·디버그 해금까지 12개 호출부가 클라에서 직접 쓴다. `firestore.rules:99` 도 `ownership` 은 `is map && size() <= 2000` 만 본다(`currency.balances` 가 타입·범위까지 조여지는 것과 대조적). **지금은 자기신고 집계 위에 선 판정이고, 튜토 지급이 callable 로 넘어오는 순간 진짜가 된다.** 그 선행 조건이 아래 "튜토리얼 지급 표" 다.

### C6 — 저장소 전환 ✅ (`c099173c6` `0b2b6b3d6` `1416c4031` `8edb278f2` `cd1027476` · **룰·서버 배포 완료 · 실기 왕복 미확인**)

잔액이 세이브 `currency` 슬롯을 떠나 `envs/{env}/users/{uid}/wallet/current` 로 갔다.
**클라 재화 writer 0 이 성립했다** — `CurrencyManager` 는 읽기 전용 캐시고, 잔액을 바꾸는 자리는 `CurrencyManager.Adopt` 하나이며 그 호출부는 `WalletCloud` 하나다. `Earn`·`Spend`·`Save` 는 삭제됐다.

| 축 | 진실원 |
|---|---|
| 잔액 | `wallet/current` 의 `balances` |
| 상태 전이 | **`nextWallet` — 지갑 상태를 만드는 유일한 출구. `rev` 도 여기서만 오른다** |
| 직렬화 | `writeWallet` — 받은 값을 그대로 싣는 직렬화기다. 호출부가 `rev+1` 을 손으로 얹게 두면 빠뜨린 명령의 쓰기가 앞선 쓰기를 덮는다 |
| 승급(v7→v8) | `commands/ensureWallet` **하나** |
| 클라 채택 | `WalletCloud.Adopt` — 뒤처진 rev 는 조용히 무시하고 **절대 세션을 끊지 않는다** |

`wallet.rev` 가 보장하는 것은 **단조 증가뿐이다**(세이브 revision 과 달리 "정확히 +1" 이 아니다). 지갑은 두 codebase 가 쓰고, 장차 결제 웹훅처럼 클라가 모르는 정당한 쓰기가 생긴다 — +1 을 강제하면 첫 결제에서 전 유저 세션이 끊긴다.

**`MIN_WRITABLE_SCHEMA_VERSION = 7` 은 넣었다가 걷어냈다(`cd1027476`). 전제가 틀렸다.**

이 상수는 "구 클라의 callable 을 살려 둔다" 는 목적이었는데 실동작이 정반대였다. v7 클라가 v7 문서로 부팅에 성공한 뒤 `openPack` 을 부르면 서버가 v7 을 받아 승급 낙인을 걸고 **지갑에서** 차감한다. 그런데 v7 클라는 `wallet` 응답 필드를 모른다 — 골드가 한 푼도 줄지 않은 채 카드만 받는다. 이어지는 디바운스 업로드가 `schemaVersion 7` 을 싣고, 룰의 `schemaVersion >= resource` 가 `7 >= 8` 로 false 라 거부된다. 누적되면 `BlockSession` 이고 그 세션의 덱·튜토·랭크 진행이 유실된다. **구 클라를 살리려던 장치가 구 클라로 하여금 정확히 한 번 성공한 뒤 스스로를 벽돌로 만들게 했다.**

지갑을 모르는 클라는 v8 서버와 **원리상 공존할 수 없다** — 잔액을 바꾸는 명령이 하나라도 성공하면 그 순간 desync 다. 구 클라는 상태가 갈라지기 **전에** 멈춰야 한다. 그래서 `assertWritableSchema` 는 정확히 `== SCHEMA_VERSION` 이고, v7 을 받는 곳은 승급을 수행하는 `ensureWallet` **하나**뿐이다(자기 판정 `assertMigratableSchema` — `saveDocument` 의 것을 일부러 안 쓴다).

**유상/무상 버킷 — 결정했다. 자리만 만들고 비워 뒀다.**

- `balances` 와 **같은 평면**의 `paidBalances` 사이드카다. 재화별 `{free, paid}` 중첩을 쓰면 `Balances = Record<string, number>` 가 깨져 순수 산술도 룰도 클라도 전부 유니온을 다뤄야 한다
- 지금은 **항상 비어 있다.** 결제가 처음으로 채운다
- **`clampPaid` 한 줄이 "무상 먼저 소진" 정책 전부다** — 잔액이 줄면 유상분이 새 잔액까지 따라 깎이므로, 감소분은 무상분에서 먼저 나간 셈이 된다. 별도의 소비 순서 코드는 없다
- 출시 후에는 소급 분류할 근거가 없다. 지갑을 신설하는 그 순간이 한계비용 0 인 유일한 지점이었다

**채택을 둘로 갈랐다 — `PlayerSaveCloud.AdoptServerResult` 의 revision+1 단언은 그대로다.**

`ServerSaveCommands` 가 응답의 `wallet` 을 **먼저** 채택하고, **`Revision > 0` 일 때만** 세이브를 채택한다. 지갑만 쓰는 명령은 응답에 `revision` 키를 **싣지 않는다** — `revision > 0` 이 곧 "이 명령이 세이브를 썼다" 의 센티널이고, 안 쓴 명령을 세이브 채택 경로로 보내면 그 자리에서 "+1" 단언이 걸려 전 세션이 끊긴다. 지갑을 먼저 채택하는 것은 단조 판정이라 손해가 없고, 뒤의 세이브 채택이 던져도 잔액은 이미 맞다.

**`mutateSave` 는 지갑을 항상 읽지만 승급하지 않는다.**

- 참여 옵션 플래그를 두지 않았다 — 잊었을 때 조용히 깨지는 축을 만들지 않는다
- 지갑 읽기는 콜백 진입 **전**에 끝낸다. Firestore 는 모든 읽기가 모든 쓰기보다 앞서야 하고 `openPack` 은 재실행된다
- 승급 낙인은 `cd1027476` 에서 사라졌고 **지갑 부재 안전망(`createWallet`)만 남는다.** v8 문서는 `currency` 가 없으니 지갑이 사라진 계정은 잔액을 주장하는 곳이 어디에도 없다 — 0 으로 세우는 것이 그 상태의 정답이고, 안 세우면 지갑을 쓰는 명령이 전부 실패해 계정이 굳는다. `set` 이 아니라 `create` 인 것은 트랜잭션 밖에서 `ensureWallet` 이 먼저 세운 이관 잔액을 0 으로 덮지 않기 위해서다
- **`transaction.create` 는 콜백 뒤다.** 앞에 두면 그건 쓰기고, `enhanceCard`·`enhanceKeyword` 가 **콜백 안에서 거는 무료 한 방(`grants/current`) 읽기가 쓰기 뒤로 밀려 트랜잭션이 통째로 거부된다.** 읽기·이관 계산·콜백 노출은 앞, 문서 쓰기만 뒤다
- 세이브를 아예 안 만지는 명령용으로 `mutateWallet` 이 따로 있다. 지갑이 없으면 `failed-precondition` — 부트가 안 돌았다는 뜻이라 도메인 거절이 아니라 세션 문제다

**신규 계정의 스타터 골드는 세이브 create 와 같은 트랜잭션에서 지갑 create 로 들어간다.** 두 문서가 갈라지면 "세이브는 있는데 지갑이 없는" 계정이 생기고, 부트의 `ensureWallet` 이 0 잔액 지갑을 세워 스타터 골드를 영영 잃는다. `ensureSaveDocument` 가 지갑을 `create` 가 아니라 **`get` 으로 먼저 묻는** 이유는, 세이브만 지워지고 지갑이 남은 계정에서 `ALREADY_EXISTS` 로 터지면 그 계정이 세이브를 영영 못 만들기 때문이다.

**codebase 배치 — 이사한 것은 `devGrantCurrency` 하나다(C6.6).**

- 순수 지갑 쓰기라 깨끗하게 옮겨지고, `currencyPing` 너머로 그 codebase 가 실제 지갑 쓰기를 왕복으로 증명한다
- **`claimBattleReward` 는 `default` 에 남겼다.** `Reward` 스펙 표 리더(`packs/packSpecReader`·`rewardTable`)에 의존해서, 재화 codebase 로 끌고 가면 격리하려던 의존성이 그대로 따라온다
- **`walletTransaction` 은 일부러 두 벌이다.** 거기 남은 것은 재화 로직이 아니라 codebase 자기 `db` 핸들에 묶인 배관이고, 미러하려면 `HttpsError` 를 미러 파일에 넣어야 하는데 그건 **순수 회귀가 `lib/` 를 직접 require 하는 계약**을 깬다
- **환경 화이트리스트 `save/environments.ts` 는 단일 원본으로 올렸다.** 유틸이 아니라 지갑을 열지 말지 정하는 **데이터**라, 목록이 갈리면 같은 uid 의 지갑을 codebase 마다 다르게 거절한다
- **`WalletPatch` 선언도 `walletStore` 로 올렸다.** 응답 모양이 갈리면 클라가 같은 지갑을 두 가지로 읽는다

**룰은 완화만 했다 — 조인 것은 C7 이다(`eea13ea13`).**

C6 은 `hasOnly` **15키를 그대로 두고**, `hasAll` 에서 `currency` **만** 뺐으며, currency 검증 블록을 `(!hasAny(['currency']) || 기존 전수 검증)` 으로 감쌌다. 검증을 통째로 빼지 않은 이유는 승급 전 구 클라가 여전히 그 필드를 싣기 때문이었다 — 그 구간에 값 위조가 열리면 안 됐다. 그 공존(15키 구 · 14키 신)은 **C7 이 닫았다.**

**클라**

- 세이브 슬롯 10 → **9**, `UserSaveData.VERSION` **8**, `CurrencySaveData` 삭제. 같은 목록을 손으로 나열하던 7곳이 함께 움직였다
- `CurrencyManager` 의 첫실행 골드 100 지급을 지웠다 — 서버 `freshAccount.STARTER_GOLD` 가 이미 진실원이라 남겨두면 이중 진실원이고, 지갑을 못 읽은 부트에서 공짜 골드가 생긴다
- 부트가 세이브와 지갑을 **겹쳐 읽고**, 세이브가 v7 이거나 지갑이 없으면 `ensureWallet` 으로 승급한다. **이것이 첫 업로드보다 반드시 앞**이다 — 업로드가 `SetOptions.Overwrite` 라, v8 `ToFieldMap` 이 `currency` 를 빼고 나면 다음 업로드가 원격 잔액 원본을 지운다
- 승급 응답이 `Created=false` 인데 손에 든 스냅샷이 아직 v7 이면(다른 기기가 방금 승급을 커밋한 경우) **세이브를 한 번 다시 읽는다.** 안 그러면 멀쩡한 계정이 복구 화면을 본다
- `link.xml` 에 새 응답 DTO 를 넣었다. 빠뜨리면 **에디터는 통과하고 기기에서만 값이 빈다**

**C6.5 — `claimPayout.ack` 이 낙인과 같은 트랜잭션에서 지갑에 크레딧한다.** 갈라 놓으면 낙인만 성공해 보상이 증발하거나, 크레딧만 성공해 무한 재지급이 열린다. `PayoutInbox` 는 로컬 크레딧을 놓고 공통 창구로 편입됐다 — **이것이 클라의 마지막 재화 writer 였다.** 재화 핸드오프는 `ack` **성공 뒤**로 옮겼다(그전에는 ack 가 실패해도 결과 화면이 "+N 골드" 를 띄우는 동안 잔액은 그대로여서 화면이 거짓말을 했다).

**같이 걷힌 것**

- **`currencySlot` 삭제** — 호출부 0 이 됐다. `readBalances` 는 `walletMigration` 이 이관 때 계속 쓰므로 남는다
- `claimReward` 가 지급 0 일 때 `rev` 를 올리던 것을 다른 명령과 맞췄다. 빈 지급으로 rev 만 오르면 클라가 달라진 것 없는 잔액을 채택하고 사고를 못 알아챈다
- `claimPayout` 의 **빈 `matchIds` 단락 회로**를 되살렸다 — 아무것도 요청하지 않은 호출이 지갑 부재로 세션을 끊는 모양이었다
- **되감기의 재화 와이프가 사라졌다**(`OutgameTutorialRewind`). 클라가 지갑을 못 쓰기 때문이고, `test` env 디버그 경로라 수용한 **의도된 동작 변화**다
- `UI/HUD/CurrencyHud` 의 죽은 소모 연출 필드, `link.xml` 의 `EnsureAccountResult` 누락

**남은 것 — 배포와 실기 왕복 전부.** 위 "배포 순서" 를 지킨 뒤, 최소 확인은 ① v7 계정 부트 → `ensureWallet` 이 승급하고 잔액이 그대로인가 ② 신규 계정 → 스타터 골드 100 이 지갑에 서는가 ③ `openPack`·강화 → 차감이 지갑에서 일어나고 세이브 `revision` 은 정확히 +1 인가 ④ `claimBattleReward`·`devGrantCurrency` → `revision` 없이 잔액만 오르고 세션이 안 끊기는가 ⑤ 멀티 payout `ack` → 크레딧이 한 번만인가.
### C7 — 조이기 ✅ (`eea13ea13` · **룰 배포 미실행**)

C6 이 **완화만** 해 둔 자리를 되돌렸다. `isValidSave()` 의 `hasOnly` 가 **14키**가 되어 위아래 두 목록이 같아졌고, 그로써 도달 불가가 된 `(!hasAny(['currency']) || 전수 검증)` 블록을 통째로 걷었다. **세이브에 `currency` 가 실리면 모양과 무관하게 거부된다** — 잔액의 진실원은 `wallet/current` 하나다.

`MIN_WRITABLE_SCHEMA_VERSION` 은 걷을 것이 없었다 — C6.6 이 이미 되돌렸다. 서버 함수·클라는 무변경이다.

**착수 판정은 "v7 문서가 없는가" 가 아니라 "v7 클라가 없는가" 였다.** 룰의 `update` 는 클라 직접 쓰기만 본다(승급은 Admin SDK 라 룰을 우회한다). v7 클라는 callable 이 이미 `failed-precondition` 으로 막히지만 **세이브 업로드는 여전히 15키로 나가서**, 그 클라가 남아 있는 동안 조이면 그쪽 저장이 전부 거부된다. **게이트는 "이전 유저의 세이브를 전량 제거한다" 는 결정(2026-08-30)으로 성립했다** — 판정 근거는 이 줄 하나다.

**하네스는 셋을 하나로 접었다.** 조인 뒤 13b·13c·13e 는 같은 사실(키 자체가 금지)을 세 번 주장한다 — 통과는 하지만 통과 이유가 값 검증이 아니게 되고 주석은 사라진 계약을 말한다.

- `13c` 를 `assertFails` 로 뒤집고 이름을 **"currency 가 실리면 모양과 무관하게 거부"** 로 바꿔, 13b·13e 가 보던 모양(정상 4재화 · 키 누락 · 타입 변조 · 미지 재화 `Ruby` · 문자열 · `null`)을 그 안에 모았다. `13b`·`13e` 삭제
- `13d` 의 `t_old` 갈래와 `13` 의 `currency: 'x'` 줄은 슬롯 누락·타입 이전에 **currency 로 먼저 거부돼 아무것도 못박지 못한다** — 제거
- `legacyCurrencySlot()` 은 **남긴다.** `freshAccountDocument` 화석이 쓰고 그것을 `14d` 가 본다. 주석만 "이제 룰이 거부하는 모양" 으로 갱신

**검증 실측(2026-08-30)** — `cd Tools/firestore-rules-tests && npm test` 로 **pass 52 · fail 0**. 뒤집기가 실제 룰을 보는지까지 봤다: `RULES_FILE` 로 조이기 전 룰(`git show HEAD:firestore.rules`)을 물리면 **13c 만 실패**한다(pass 51 · fail 1). 이 머신에는 firebase CLI 가 없어 `npm i -g firebase-tools`(15.28.2)를 먼저 깔아야 했다.

**남은 것 — 룰 배포 1회.** `firebase deploy --only firestore:rules --project bm-cardbattle`. 함수 배포는 없다. 배포 뒤 신 클라(v8) 부트로 세이브 업로드가 통과하고 `revision` 이 정상적으로 오르는지 본다 — 여기서 막히면 14키 목록을 잘못 줄인 것이다.
---

### C8 — 재화 원장 (C7 뒤 · **IAP 착수 앞**)

재화가 움직인 기록이 **한 줄도 없다.** 잔액 숫자만 있고 그 숫자가 어떻게 됐는지는 아무도 모른다.
그리고 그 기록이 없어서 **재시도 중복 과금이 열려 있다** — 원장 한 장치가 둘을 같이 닫는다.

**지금 열려 있는 것**

- `mutateSave`(`save/saveDocument.ts:134`)는 **클라의 기대 revision 을 받지 않는다.** 현재값을 읽어 +1 할 뿐이다.
  응답을 놓친 클라가 재시도하면 `openPack` 이 **다시 뽑고 다시 과금**한다. 강화도 다시 차감된다.
  `claimReward` 만 낙인(`claimedTiers`·정점 Cleared) 덕에 우연히 안전하다 — 설계가 아니라 부작용이다
- `logger.info` 의 `goldBefore`/`goldAfter` 는 원장이 **아니다.** `functions:log` 는 3~4분 늦고 보존기간이 있고
  무엇보다 **코드가 되읽을 수 없다.** 로그는 사람이 사후에 보는 것, 원장은 시스템이 읽고 판단하는 데이터다

**무엇을 만드나**

`envs/{env}/users/{uid}/wallet/current/ledger/{txId}` — 재화 이동 한 건이 한 줄. append-only, 수정·삭제 없다.

```
트랜잭션 안에서
  ① ledger/{txId} 읽기 → 있으면 재시도다. 기록된 결과 그대로 반환 (재과금·재추첨 없음)
                       → 없으면 처음이다
  ② 판정·차감 (기존 그대로)
  ③ 잔액 · 도메인 상태 · 원장 줄을 한 번에 쓴다
```

**문서 존재 자체가 중복 판정이다.** 멱등 장부를 따로 두지 않는다.

**반드시 같은 트랜잭션 안이어야 한다.** 별도 callable 로 빼면 (a) 차감은 됐는데 기록 호출이 실패해 구멍 뚫린 원장이 되고
(b) ① 의 조회가 트랜잭션 밖이라 동시 요청 둘이 같은 "처음" 을 보는 갈래가 열린다. **원장은 거래의 일부지 사후 기록이 아니다.**

**왜 여기 놓았나 (순서 근거)**

- **C6 뒤라 호출부를 한 번만 만진다.** 앞에 두면 세이브 슬롯 기준으로 배선한 뒤 C6 에서 같은 자리를 지갑 기준으로 다시 고친다
- 출시 전이라(`envs/live` 0표) 기록 공백 구간에 실유저 데이터가 없다
- **IAP 보다는 반드시 앞.** 스토어 거래 id 가 곧 멱등 키다. 원장 없이는 결제 재시도도 환불 회수도 다룰 수 없다

**이미 있는 것 — 새로 만들지 마라**

- `currency/walletStore.ts` 의 `ledgerEntry`·`writeWallet`·`createWallet`·`readWallet`·`walletRef` — **호출부 0건.** 코덱은 이미 써 있다
- 룰 배포됨: `ledger/{txId}` 는 `read, write: if false`(`firestore.rules:142`) — Admin SDK 전용. 클라에 보이면 잔액 추론 표면만 넓어진다
- txId 규약이 `walletStore.ts:114` 주석에 있다 — **결제는 스토어 주문 id, 도메인은 `{command}:{seed}`**

**배선 대상** (재화가 움직이는 자리 — `nextWallet` 호출부 전수, 실측 `cd1027476`)

| 파일:줄 | codebase |
|---|---|
| `functions/src/commands/openPack.ts:134` | default |
| `functions/src/commands/enhanceCard.ts:137` | default |
| `functions/src/commands/enhanceKeyword.ts:128` | default |
| `functions/src/commands/claimReward.ts:401` | default |
| `functions/src/commands/claimBattleReward.ts:115` | default |
| `functions/src/commands/claimPayout.ts:110` | default |
| `functions-currency/src/commands/devGrantCurrency.ts:49` | currency |

**`currencySlot(spend/grant(...))` 로 조회하던 방식은 더 이상 안 통한다 — 그 함수는 C6.3 에서 삭제됐다.** 지금 조회 축은 `nextWallet` 이고, 미러(`functions-currency/src/generated/`)는 생성물이니 세지 마라.

지갑이 **처음 서는** 자리는 축이 다르다(잔액 이동이 아니라 개설이다). 셋 다 `createWallet` 이다 — `functions/src/commands/ensureWallet.ts:134`(v7 이관) · `functions/src/save/saveDocument.ts:294`(신규 계정 스타터 골드, 세이브 create 와 같은 트랜잭션) · `functions/src/save/saveDocument.ts:213`(`mutateSave` 의 지갑 부재 안전망, 잔액 0). 이 셋을 원장에 남길지는 C8 착수 때 정한다.

한계돌파(Snack) callable 이 서면 배선 대상이 하나 는다.
**강제 방법 — 절반은 C6.1 이 이미 세웠다**

C6.1 이 `nextWallet` 을 **지갑 상태를 만드는 유일한 출구**로 만들었다(명령이 `{rev, balances, paidBalances}` 를 손으로 조립하면 유상분 불변식이 명령마다 갈리기 때문이다). 덕분에 "재화를 움직이는 자리" 를 위 표처럼 한 줄 grep 으로 셀 수 있다 — 예전 `currencySlot` 시절에 없던 성질이다.

남은 것은 **원장을 그 반환값에 묶는 것**이다. `nextWallet` 이 `(다음 지갑, 원장 줄)` 한 쌍을 내고 `writeWallet` 이 그 쌍을 받아 지갑과 `ledger/{txId}` 를 같은 트랜잭션에 실으면, 잔액만 쓰고 원장을 빠뜨리는 경로가 타입 수준에서 사라진다.
**원장 줄에 담을 것**

`txId` · `reason`(열거형 — `openPack`·`enhanceCard`·`claimReward:rank`…) · 재화별 증감 · 전/후 잔액 · `createdAt` · 결과 요약(재시도가 그대로 반환할 값).
**평탄하게 잡는다** — 중첩을 넣으면 나중에 웨어하우스로 내보낼 때 값을 치른다.

**되돌리기는 반대 줄로 한다.** 잘못 지급했어도 잔액을 손으로 고치지 않는다. 그래야 감사가 성립한다.

**유상/무상 버킷 — C6.1 이 답했다(재론 불필요)**

`balances` 와 같은 평면의 `paidBalances` 사이드카 + `clampPaid`(무상 먼저 소진). 근거와 함정은 위 C6 절에 있다. 원장 줄에 유상/무상 구분을 실을지는 결제가 실제로 `paidBalances` 를 채우기 시작할 때 정한다 — 지금은 항상 비어 있다.
**하지 않을 것**

- **예약/확정(hold·capture)** — 지갑이 같은 트랜잭션에 참여하는 라이브러리인 지금 구조가 이 규모의 정답이다. 미아 hold 청소만 는다
- **원장을 진실원으로(이벤트 소싱)** — 잔액 문서가 권위, 원장은 감사 기록이다. 재생은 복구 도구로만 쓴다
- **복식부기** — `reason` 열거형 + 전/후 잔액이면 수도꼭지·배수구 집계까지 선다
- **웨어하우스 스트리밍** — 나중. 위의 "평탄하게" 만 지키면 그때 공짜다

**남은 미결과 겹친다** — 한계돌파(Snack)는 여전히 클라 판정이라 callable 이 없다(C4 항목). 그것이 서면 배선 대상이 하나 는다.

## 통합처(`origin/박형석작업용`)와 겹치는 것 — 새로 만들지 마라

머지로 들어온 것들이다.

| 위치 | 내용 |
|---|---|
| `functions/src/payout.ts` | `RewardRow`·`parseRewardRows`(**이번 작업에서 `id`·`order` 를 선택 필드로 덧붙이고 `resolveRewards` 를 같은 파일에 뒀다**) · `resolveTierIndex` · `computeRankPayout`(전투 랭크 점수) · `computeCurrencyPayout`(전투 골드) |
| `commands/submitMatchResult.ts` | 멀티 대조 확정 시 `envs/{env}/users/{uid}/payouts/{matchId}` 에 지급 예정액을 쓴다 |
| `commands/claimPayout.ts` | 클라가 그 우편함을 `list`/`ack` |

**`ack` 는 이제 지급이다.** C6.5 가 `claimPayout` 의 `ack` 안에서 낙인(`status: "claimed"`)과 지갑 크레딧을 **한 트랜잭션**으로 묶었다(`claimPayout.ts:110`). 싱글 전투 보상도 C5 에서 `claimBattleReward` 로 갔으니 클라가 재화를 만드는 경로는 남아 있지 않다.

**랭크 축이 둘이라는 것을 혼동하지 마라** — `computeRankPayout` 은 전투로 인한 **점수 변동**, `claimReward(Rank, tier)` 는 **티어 보상 수령**(`claimedTiers` 낙인)이다.

**저쪽 소유 파일(`claimPayout.ts`)을 이 작업이 고쳤다는 사실 자체가 합류 시 부딪히는 지점이다.** 크레딧 블록은 `ack` 트랜잭션 안에 있고, 빈 `matchIds` 단락 회로도 거기서 복원됐다.

---

## 스펙 표 실태 (실측 2026-08-28)

`envs/test/specs` **14표**(C5.5 가 `AlbumEntry`·`TournamentChapter` 를 더했다), **`envs/live/specs` 0표** — 이 작업의 모든 검증은 `test` 에서만 성립한다. 릴리즈 전 `live` 업로드는 별건(R3 몫).

**`Reward` 표(84행)가 통합 진실원**이다. 컬럼 `id | ownerType | ownerId | order | rewardType | rewardId | amount`. 이 브랜치의 세이브 키와 정확히 일치한다 — 앨범 `t:Theme_Nature`/`p:Theme_Nature/P1`/`b` · 정점 `node_01`~`node_24` · 챕터 `chapter_01`~`chapter_04` · 랭크 티어 인덱스 `0`~`19` · 전투 `win.perCard`=Gold 10 / `win.floor`=Gold 10 / `lose.flat`=Gold 5. **24정점 전부 보상이 저작돼 있다.**

표가 실제로 올라와 있는지는 브랜치 소스가 아니라 Firestore 를 직접 조회해 확인한다(자격증명 불필요 — 룰의 스펙 읽기가 `isSignedIn()` 뿐이다). 방법은 `functions/scripts/check-pack-spec.js` 관용구.

---

## 판정을 틀리게 만드는 것들

- **배포 로그는 호출 가능을 증명하지 않는다.** `Deploy complete!` 를 찍고도 403인 전례가 있다(`openPack`). 판정은 URL POST 의 401/403 으로만
- **`firebase functions:log` 는 3~4분 늦는다.** 방금 한 왕복이 안 보인다고 "호출이 안 갔다" 로 읽으면 멀쩡한 코드를 뒤진다
- **룰 하네스는 종료코드가 거짓말한다.** JDK 가 없으면 실패해도 exit 0 이다. 러너는 `cd Tools/firestore-rules-tests && npm test`(자체 에뮬레이터 8081 · Java 21+ 필요 · `firebase login` 불필요)이고, **판정 줄은 `# pass N` 이 아니라 `ℹ pass N` / `ℹ fail 0`** 이다(`node --test` 출력). `fail` 이 0 인지를 봐라
- **`functions/scripts/test-firestore-rules.js` 는 잔재다.** 어떤 `npm test` 에도 안 물려 있다(`SERVER_VALIDATION_ROADMAP.md` 에 이름만 남았다). 룰을 고친 뒤 그 파일을 손봐서 초록을 봤다면 아무것도 검증하지 않은 것이다 — **고치지 마라**
- **Unity 빈 콘솔은 "통과" 가 아니다.** 컴파일이 아직 안 돈 것일 수 있다 — `Library/ScriptAssemblies/Assembly-CSharp.dll` mtime 이 최근 `.cs` 수정보다 뒤인지 함께 본다. 강제 재컴파일은 MCP `Unity_RunCommand` 로 `AssetDatabase.Refresh()` + `UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation()`(**전체 한정 필수** — 샌드박스가 자체 네임스페이스로 감싸 `CompilationPipeline` 이 충돌한다)
- **거절은 무조건 `permission-denied`.** `failed-precondition`·`invalid-argument` 는 `CloudFailureClassifier` 가 `Unusable` → `BlockSession` 으로 본다. 잔액 부족·중복 수령으로 세션을 끊으면 안 된다
- **순수 모듈에 `firebase-admin`·`HttpsError` 금지.** `functions/scripts/` 회귀가 `lib/` 를 직접 require 한다
- **미러는 커밋되지 않는다.** 원본은 `functions/src/`(공유 목록은 `functions-currency/scripts/shared-files.js` — 지금 5파일: `currencyKeys`·`wallet`·`walletStore`·`environments`·`saveValues`), 미러 `functions-currency/src/generated/` 는 `.gitignore` 대상이고 `prebuild` 의 `sync:shared` 가 빌드마다 새로 만든다(`364c6c538` 이후). 손으로 고칠 대상 자체가 없어졌다 — 대신 `functions-currency` 의 `npm test`(`scripts/test-wallet-mirror.js`)가 **미러 순수성**(`firebase-admin`·`firebase-functions` 미적재)과 **환경·재화 키 화이트리스트 일치**를 지킨다
- **`SCHEMA_VERSION` 2중 동기화**(클라 `UserSaveData.VERSION:17` · `functions/src/save/saveDocument.ts:33` — 지금 둘 다 **8**). 하나만 올리면 조용히 막히고, **하나만 배포해도 막힌다**(위 "배포 순서"). **룰은 3번째 축이 아니다** — `firestore.rules` 에서 `== 7` 을 강제하던 자리는 이미 빠졌고 지금은 단조 증가(`>=`)만 본다
- **`Assets/Resources/SpecData.bytes` 는 암호화 바이너리라 머지가 조용히 한쪽을 삼킨다.** 2026-08-28 머지 `04170beb5` 가 `SpecDatas.cs` 는 박형석작업용 것을, `.bytes` 는 feature_Firestore 것을 택해 **코드와 데이터의 짝이 갈렸다**. 증상은 "게임 진입 불가" 로만 보인다(`Core/Initialization/SpecSheetPreloadStep.cs:19` → `GameInitialization.MarkRecoveryRequired`). 판정은 표 복호화로만 — AES-128-CBC · key `cRM1fuNZDwvqnjzY` · IV = key 바이트 역순. 그리고 `OutGame/Spec/SpecSource`·`RewardSpec` 은 **정적 캐시**라(`s_loaded`) 파일을 바꿔도 도메인 리로드 전엔 옛 값을 보고한다
- **`envs/live` 는 여전히 0표다.** `d2a4fdc3c` 이후 `Reward` 표가 비면 수령이 **fail-closed 로 거절**된다 — live 는 R3 업로드 전까지 수령이 전부 막힌다. 의도한 동작이지만 릴리즈 순서에 걸린다
- **미커밋으로 오래 두지 마라.** 2026-08-28 에 C1~C3 미커밋분이 브랜치 전환 + 머지로 통째로 날아갔고, GitHub Desktop 자동 stash 는 `.gitignore` 대상(`node_modules`·`lib`)만 담고 untracked 소스를 안 담았다. **각 단계는 검증이 초록으로 뜬 그 순간 커밋한다**

## 별건으로 남은 것

`envs/live/specs` **0표 업로드**(R3 몫). 그 전까지 live 는 수령이 전부 fail-closed 로 막힌다.

**SO 저작 중 아직 서버 근거가 없는 것 — 하나뿐이다.** `Assets/SO/` 69개를 전수로 갈랐다(C5.5 착수 시 실측).

- **튜토리얼 카드 지급 표** (`OutgameTutorial.asset` + `TutorialScenario*.asset` 의 `playerDeckIds`) — `RewardSpec` 이 못 덮는 **유일한** 지급 축이다. 재화 지급은 랭크·앨범·챕터·정점·전투가 전부 `Reward` 표 84행 안에 있는데, 카드 소유권만 클라가 저작값을 읽어 직접 쓴다(`TutorialStepExecutor.cs:203`·`:245`·`:296`, 되감기 `OutgameTutorialRewind.cs:140`·`:164`·`:172`). **C5.6 의 앨범 판정이 자기신고 위에 서 있는 뿌리가 여기다**
- 나머지는 올릴 필요가 없다 — 14개는 이미 표가 덮었고(그중 `RewardConfig.asset` 은 읽는 코드가 0이라 **삭제 후보**), 47개는 연출·사운드·아트 배선이라 서버와 무관하다

**표는 있는데 아무도 안 읽는 것 2건** (표 신설이 아니라 배선 문제)

- **`CardLimitBreak`** — `envs/test` 에 3행이 올라와 있는데(`stage | hpGain | snackCost`) 읽는 코드가 클라에도 서버에도 없다. 실제 판정은 `OutGame/Growth/GrowthRules.cs:71-77` 이 하드코딩한다(`hpGain = 1` 고정 · `snackCost = stage`). 간식 적립은 이미 서버인데(`OpenPackResult.snack`) 소비만 클라에 남았다 — 한계돌파 callable 미결과 같은 항목이다
- **`RankGrade`** — 서버는 읽는데 **클라만 `RankConfig.asset` 의 임계치를 본다**(`RankConfig.cs:60`). 랭크 판정이 더 넘어가면 이 드리프트가 "수령 가능한데 서버가 거절" 로 나타난다

**닫힘** — `link.xml` 의 팩 개봉 응답 DTO 누락은 `6f6a8885c` 에서 해결됐다(`OutGame/Save/link.xml:29-30`). 새 callable 응답 DTO 를 만들 때마다 같은 자리에 추가할 것.
