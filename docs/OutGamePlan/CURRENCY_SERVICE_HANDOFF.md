# 재화 독립 서비스 분리 — 인계 문서

> 최종 갱신 2026-08-28 · 브랜치 `feature_Firestore` · HEAD `3f522a2e3`
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

**커밋 8개**

- `6c95b114a` C1·C2 — `functions-currency/` codebase · 미러 동기화 장치 · `walletStore.ts` · 룰 지갑 블록 · 순수 회귀 · 하네스 43케이스
- `1d0d1aa31` C3 — `claimReward`(랭크 티어 · 토너먼트 정점) + 클라 2곳 전환
- `120110833` 스펙시트 `Reward` 표 복구 — 머지가 옛 굽기본을 택해 부팅이 막혔다
- `6f6a8885c` `link.xml` 에 팩 개봉 응답 DTO — IL2CPP 스트리핑 방어
- `d2a4fdc3c` `claimReward` 의 보상 영구 손실·계정 영구 잠김 경로 2건
- `84f53288f` C4 — `enhanceCard`·`enhanceKeyword` + 튜토 무료 한 방 서버 이전 + 거절 사유 전달 경로 교정
- `fe4fe954d` C5.5 — 도감·챕터 **구성**을 스펙 표로 승격(`AlbumEntry` 40행 · `TournamentChapter` 24행) + 업로더 오버로드
- `3f522a2e3` C5.6 — 도감·챕터 **수령**을 `claimReward` 로 이전(ownerType `Album` 추가 · 챕터는 `chapter_` 접두사 분기)

**배포 상태 (실측 2026-08-28, URL POST 로 확인)**

| 함수 | 상태 |
|---|---|
| `currencyPing` (codebase `currency`) | 배포됨 · **401**(정상) |
| `claimReward` (codebase `default`) | **배포됨** · **401**(정상) · C3·C5.6 실기 왕복 통과 |
| `enhanceCard`·`enhanceKeyword` (codebase `default`) | **배포됨** · **401**(정상) |
| `openPack` (codebase `default`) | 배포됨 · **401**(정상) |
| `firestore.rules` 지갑 블록 | **배포됨** |
| `firestore.rules` 무료 한 방(`grants`) 블록 | **미배포.** 서버 동작은 Admin SDK 라 무관하지만, 클라가 그 문서를 읽게 되면 필수 |

**주의 — "재화 로직이 currency codebase 로 갔다"가 아니다.** 지금 그 codebase 에 있는 것은 진단용 `currencyPing` 뿐이다. `walletStore.ts` 는 callable 이 아니라 라이브러리라 배포 대상이 아니고, 실제로 재화를 움직이는 `claimReward`·`openPack` 은 세이브 슬롯도 함께 만져서 default codebase 에 있다(결정 4). 지갑만 만지는 명령은 C6 에서 이사한다.

---

## 배포·판정 절차 — 새 callable 마다 그대로 반복

C3 분(룰 · `claimReward` · 실기 왕복)은 2026-08-28 에 셋 다 닫혔다. 아래는 **C4 이후 새 callable 을 올릴 때마다 다시 타는 절차**다.

### 1) 배포

```bash
firebase deploy --only firestore:rules --project bm-cardbattle
firebase deploy --only functions:claimReward --project bm-cardbattle
```

- **`--only functions` 를 그냥 치면 abort 한다.** 남의 함수 `lockDeck` 삭제 여부를 묻고 non-interactive 라 멈춘다. 함수를 지정해라
- `--only functions:currency` 는 codebase 라벨이 조회 범위를 갈라 abort 하지 않는다(실측)

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

### C5 — 전투·디버그 2종 (**건너뛰었다 · 미결**)

C5.5·C5.6 을 먼저 했다. 이 단계는 아직 그대로다.

`RewardService.cs:61`(전투 보상) · `OutgameDebugActions.cs:179`(디버그 지급).

**착수 전에 답해야 할 것 셋** — 조사는 끝났고 판단만 남았다.

1. **`TurnRunner.CaptureResult`(`:498-550`)가 동기 `void` 다.** 실지급 호출부는 `:533` 하나뿐이지만 서버 왕복을 끼우려면 이 메서드가 async 가 되고 결과 팝업이 `lastReward` 를 읽는 시점과 얽힌다. `.Forget()` 으로 던지면 낙인 전에 연출이 붙는 그 버그를 되풀이한다
2. **이겼는데 왕복이 실패하면 무엇을 보여줄 것인가.** 지금까지의 지급과 달리 처음으로 "실패할 수 있는 지급" 이 된다
3. **멱등 축이 없다.** 랭크 티어·정점과 달리 싱글 전투에는 낙인이 없어 같은 요청을 두 번 보내면 두 번 지급된다. 서버가 `won`·`remaining` 을 검증할 수단도 없다(싱글 전투는 클라에서만 돈다)

**C8 이 서면 3번이 저절로 닫힌다** — 원장 `txId` 가 멱등 키다. 순서를 다시 볼 것.

**전투 골드 공식은 이미 있다** — `functions/src/payout.ts` 의 `computeCurrencyPayout(won, remaining, rows)` 가 `max(remaining × win.perCard, win.floor)` / 패배 `lose.flat` 을 낸다. 새로 쓰지 마라. 배선만 남는다.

**이 커밋이 닿는 것은 `CurrencyManager.Spend` 0 까지다.** `Earn` 호출부는 5곳이고 그중 여기서 옮기는 것은 2곳뿐이다(실측).

| `CurrencyManager.Earn` 호출부 | 옮기는 단계 |
|---|---|
| `Utils/RewardService.cs:61` (전투 보상) | C5 |
| `OutGame/Debug/OutgameDebugActions.cs:179` (디버그 지급) | C5 |
| `OutGame/Album/AlbumRewardManager.cs:80` (도감 페이지·테마 수령) | **C5.6** |
| `OutGame/Tournament/TournamentProgress.cs:310` (챕터 완주 보상) | **C5.6** |
| `Network/PayoutInbox.cs:97` (멀티 payout ack) | **C6** |

**`CurrencyManager.Earn/Spend/Save` 삭제는 C5 가 아니다.** 남은 3곳이 그 API 를 붙들고 있어 여기서 지우면 컴파일이 깨진다. **클라 재화 writer 0 이 실제로 성립하는 시점은 C6 완료 후**다. (`Core/GameManager.cs:145` 의 `CurrencyManager.Save()` 는 flush 일 뿐 잔액을 만들지 않는다 — C6 에서 currency 슬롯과 함께 사라진다.)

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

### C6 — 저장소 전환

- `ensureWallet` callable(**default codebase**. 지갑 생성과 세이브 v7→v8 승급이 **같은 트랜잭션**이어야 잔액을 안 잃는다)
- `SCHEMA_VERSION 8` + **전환 전용 `MIN_WRITABLE_SCHEMA_VERSION = 7`**. 없으면 v7 문서를 가진 구 클라의 `openPack` 이 `failed-precondition` → `Unusable` → `BlockSession` 으로 전부 끊긴다
- 룰: `isValidSave()` 를 `hasOnly(15키)` + `hasAll(currency 뺀 14키)` 로 바꾸고 currency 검증을 `(!hasAny(['currency']) || 기존 검증)` 으로 감싼다 → **구/신 클라 공존이 성립한다**
- 모든 callable 을 슬롯에서 지갑으로. `mutateSave` 에 지갑 참여 옵션 추가(**읽기는 콜백 진입 전에** — Firestore 는 모든 읽기가 모든 쓰기보다 앞서야 하고 `openPack` 처럼 mutate 가 재실행되면 순서가 깨진다)
- 클라: `WalletCloud`·`WalletCommands`·`WalletPatch` 신설. **`PlayerSaveCloud.AdoptServerResult` 의 revision+1 단언은 손대지 않는다** — 지갑 응답을 그 경로로 안 보내는 것이 답이다
- **`wallet.rev` 는 단조 증가만 보장한다**(세이브 revision 과 달리 "정확히 +1" 이 아니다). 지갑은 두 codebase 가 쓰고 장차 결제 웹훅처럼 클라가 모르는 정당한 쓰기가 생긴다 — +1 을 강제하면 첫 결제에서 전 유저 세션이 끊긴다
- 지갑만 만지는 명령(`claimBattleReward`·`devGrantCurrency`)을 `functions-currency/` 로 이사
- **`Network/PayoutInbox.cs:97` 의 크레딧이 여기서 사라진다.** `claimPayout.ack` 가 지갑에 직접 크레딧하게 되면서 클라의 마지막 `Earn` 이 없어진다 — **클라 재화 writer 0 은 이 항목이 닫히는 순간 성립한다**
- `SetOptions.Overwrite` 를 이용한다 — v8 클라의 `ToFieldMap` 에서 `FIELD_CURRENCY` 를 빼면 다음 업로드가 원격 잔여 필드를 알아서 지운다. **반대로 승급 전에 v8 클라가 저장하면 잔액 원본이 사라진다** → 부트에서 `ensureWallet` 이 첫 업로드보다 반드시 앞

### C7 — 조이기 (구 클라 소멸 후)

룰을 14키 전용으로 조이고 `MIN_WRITABLE_SCHEMA_VERSION` 제거. 하네스의 15키 케이스를 `assertFails` 로 뒤집는다.

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

**배선 대상** (재화가 움직이는 자리 — `currencySlot(spend/grant(...))` 로 전수 조회한 결과)

`commands/openPack.ts:122` · `enhanceCard.ts:133` · `enhanceKeyword.ts:124` · `claimReward.ts:269` · `save/freshAccount.ts:39`
그리고 C5·C5.6·C6 이 늘리는 것(`claimBattleReward` · `devGrantCurrency` · `claimPayout.ack`).

**강제 방법 — 이게 핵심이다**

지금은 command 가 `currencySlot(spend(...))` 로 슬롯을 **직접 조립해 반환**한다. 모듈은 계산기일 뿐이라
"차감했으면 기록도 남긴다" 를 강제하는 자리가 없다. 새 command 를 쓰는 사람이 `spend` 를 안 부르고 잔액을 직접 만들어도 아무것도 안 막는다.

**재화를 움직이는 출구를 하나로 만들고 `(갱신된 잔액, 원장 줄)` 한 쌍을 반환하게 한다.**
둘이 같은 반환값이라 잔액만 쓰고 원장을 빠뜨리는 경로가 타입 수준에서 사라진다.

**원장 줄에 담을 것**

`txId` · `reason`(열거형 — `openPack`·`enhanceCard`·`claimReward:rank`…) · 재화별 증감 · 전/후 잔액 · `createdAt` · 결과 요약(재시도가 그대로 반환할 값).
**평탄하게 잡는다** — 중첩을 넣으면 나중에 웨어하우스로 내보낼 때 값을 치른다.

**되돌리기는 반대 줄로 한다.** 잘못 지급했어도 잔액을 손으로 고치지 않는다. 그래야 감사가 성립한다.

**C6 착수 시 같이 판단할 것 — 유상/무상 버킷**

Diamond 가 언젠가 판매 대상이면, 같은 다이아라도 **돈 주고 산 것과 게임에서 얻은 것을 별도 버킷**으로 나눠야 한다.
환불 시 유상분만 회수해야 하고, 미사용 유상 재화는 회계상 **선수수익(부채)** 이며, 일부 국가는 환불 의무가 있다.

**출시 후에는 소급 분류할 근거가 없어 사실상 불가능하다.** C6 이 어차피 v7→v8 승급과 지갑 신설을 하므로
그 순간의 한계비용이 거의 0 이다 — 이 항목은 C8 이 아니라 **C6 착수 시점에 결정한다.**
(소비 정책은 무상 먼저 소진을 권한다 — 부채를 늦게 털고 환불 회수분이 남는다.)

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

**`payouts` 는 "줄 것의 기록"이지 지급이 아니다.** `ack` 는 `status: "claimed"` 만 찍고 잔액을 안 건드린다 — 실제 크레딧은 여전히 클라가 한다. 이 작업이 닫으려는 구멍이 거기다. 그리고 그 경로는 **멀티 전용**이라 싱글 전투 보상은 아직 `RewardService.GrantBattleReward` 가 클라에서 준다.

**랭크 축이 둘이라는 것을 혼동하지 마라** — `computeRankPayout` 은 전투로 인한 **점수 변동**, `claimReward(Rank, tier)` 는 **티어 보상 수령**(`claimedTiers` 낙인)이다.

C6 에서 `claimPayout` 의 `ack` 가 지갑에 크레딧하는 자리가 된다. **저쪽 소유 코드라 착수 전 조율이 필요하다.**

---

## 스펙 표 실태 (실측 2026-08-28)

`envs/test/specs` **14표**(C5.5 가 `AlbumEntry`·`TournamentChapter` 를 더했다), **`envs/live/specs` 0표** — 이 작업의 모든 검증은 `test` 에서만 성립한다. 릴리즈 전 `live` 업로드는 별건(R3 몫).

**`Reward` 표(84행)가 통합 진실원**이다. 컬럼 `id | ownerType | ownerId | order | rewardType | rewardId | amount`. 이 브랜치의 세이브 키와 정확히 일치한다 — 앨범 `t:Theme_Nature`/`p:Theme_Nature/P1`/`b` · 정점 `node_01`~`node_24` · 챕터 `chapter_01`~`chapter_04` · 랭크 티어 인덱스 `0`~`19` · 전투 `win.perCard`=Gold 10 / `win.floor`=Gold 10 / `lose.flat`=Gold 5. **24정점 전부 보상이 저작돼 있다.**

표가 실제로 올라와 있는지는 브랜치 소스가 아니라 Firestore 를 직접 조회해 확인한다(자격증명 불필요 — 룰의 스펙 읽기가 `isSignedIn()` 뿐이다). 방법은 `functions/scripts/check-pack-spec.js` 관용구.

---

## 판정을 틀리게 만드는 것들

- **배포 로그는 호출 가능을 증명하지 않는다.** `Deploy complete!` 를 찍고도 403인 전례가 있다(`openPack`). 판정은 URL POST 의 401/403 으로만
- **`firebase functions:log` 는 3~4분 늦는다.** 방금 한 왕복이 안 보인다고 "호출이 안 갔다" 로 읽으면 멀쩡한 코드를 뒤진다
- **룰 하네스는 종료코드가 거짓말한다.** JDK 가 없으면 실패해도 exit 0 이다 — `# pass N` 출력 줄로만 판정
- **Unity 빈 콘솔은 "통과" 가 아니다.** 컴파일이 아직 안 돈 것일 수 있다 — `Library/ScriptAssemblies/Assembly-CSharp.dll` mtime 이 최근 `.cs` 수정보다 뒤인지 함께 본다. 강제 재컴파일은 MCP `Unity_RunCommand` 로 `AssetDatabase.Refresh()` + `UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation()`(**전체 한정 필수** — 샌드박스가 자체 네임스페이스로 감싸 `CompilationPipeline` 이 충돌한다)
- **거절은 무조건 `permission-denied`.** `failed-precondition`·`invalid-argument` 는 `CloudFailureClassifier` 가 `Unusable` → `BlockSession` 으로 본다. 잔액 부족·중복 수령으로 세션을 끊으면 안 된다
- **순수 모듈에 `firebase-admin`·`HttpsError` 금지.** `functions/scripts/` 회귀가 `lib/` 를 직접 require 한다
- **미러를 손으로 고치지 마라.** 원본은 `functions/src/currency/`, 미러는 `functions-currency/src/generated/`. `npm test` 끝의 `assert-shared-sync` 가 드리프트를 잡는다(줄끝 정규화 후 비교 — `core.autocrlf=true` 라 바이트 비교가 못 선다)
- **`SCHEMA_VERSION` 2중 동기화**(클라 `UserSaveData.VERSION` · `functions/src/save/saveDocument.ts:18`). 하나만 올리면 조용히 막힌다. **룰은 3번째 축이 아니다** — `firestore.rules` 에서 `== 7` 을 강제하던 자리는 이미 빠졌고(`:117-118` 주석) 지금은 `:123` 의 단조 증가만 본다
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
