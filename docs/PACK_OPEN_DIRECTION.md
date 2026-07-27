# 카드팩 개봉 연출 — 기획 · 구현 기록

> 기획 의도와 구현 결과를 함께 담는다. 세부 규칙의 진실원은 항상 코드.
> 최종 갱신: 2026-07-27 (구현·씬배선 완료, **Play 검증 대기**)

---

## 0. 30초 요약

- 이전: **클릭 1회 → 결과 전체가 한 번에**. 기대와 해소의 곡선이 없었다.
- 지금: **팩을 스와이프로 뜯고 → 카드 더미가 한 자리에 겹쳐 안착 → 맨 위부터 스와이프로 한 장씩 밀어냄 → 요약**.
- 카드는 **앞면**으로 쌓인다. 서스펜스는 "뒤집히는 순간"이 아니라 **"밀어냈을 때 그 아래 뭐가 있을까"** 에 있다.
- 연출 강도를 가르는 축은 **신규 / 중복** 하나뿐. 희귀도(rarity)는 도입하지 않았다 — `CardData` 무변경.
- 개봉 조작과 카드 넘기기가 **둘 다 스와이프**다. 제스처 언어 통일이 이 연출의 뼈대.
- **팩 1개 = 6장**(`NormalPack`·`StarterPack_*` 전부 `drawCount=6`). 스와이프를 6번 요구한다 — §6 참고.

---

## 1. 이력 — 한 번 접었던 방향이다

`STRUCTURE.md` G-28은 이 구조를 **의도적으로 폐기**한 기록이다.

> 드래그 뜯기·카드별 스와이프 스택은 상태·입력 축이 많아 **배선 실패가 곧 소프트락**이었다.
> 클릭 1회 + CanvasGroup fade + GridLayoutGroup으로 줄여, 좌표 계산 코드를 레이아웃 컴포넌트에 넘겼다.

이번 작업은 그 방향을 되살린 것이다. 폐기 사유가 "연출이 나빴다"가 아니라 **배선 복잡도·소프트락**이었으므로 연출 방향은 유지하되, 그 실패 모드를 구조로 막았다.

- 미배선 지점마다 경고 후 **다음 단계로 진행** — `tearHandle`·`cardStack`·`packRoot`·`revealPanel` 어느 것이 비어도 요약까지 도달하고 `OnRevealComplete`가 발화한다.
- 카드 0장·`Build` 실패도 요약으로 빠진다.
- `skipButton` + 스킵 2회(요약 직행)로 **어느 단계에서든 탈출 경로**가 있다.

남은 리스크: **배선 지점이 늘었다.** 과거 4개(`packHandle`/`revealPanel`/`cardPrefab`/`cardGrid`) → 지금은 `cardLayer`·`stackAnchor`·`discardArea`·`sealRoot`·`skipButton` 등이 추가됐다. 소프트락은 막혔지만 "배선을 빠뜨리면 연출이 조용히 반쪽이 된다"는 성질은 그대로다.

또 하나: 팩은 3D `BoxCollider` + `OnMouse*` 방식이라 **씬 카메라의 `MainCamera` 태그가 인터랙션 전체를 좌우**한다(미태그 시 컴파일 통과·런타임 무반응). `PackTearHandle`도 동일 의존.

---

## 2. 흐름

```
[LobbyScene] Tab_Pack
   PackShowcaseController.OnBuyPressed()
      CardPackOpener.TryPurchase(pack, refundGold)   // 차감·드로우·소유부여·환급까지 원자 영속
      PackHandoff.Set(opened, "LobbyScene", false)
      SceneManager.LoadScene("CardPack")
            │
[CardPack.unity]  ▼
   PackAcquireController.Start() → PackHandoff.Consume() → view.BeginOpen(opened)
            │
   PackRevealView (스테이지 진행자)
      Entering  팩이 아래에서 올라와 안착
      Tearing   PackTearHandle — 스와이프로 봉인 뜯기 (임계 미만 되감기 / 초과 시 자동 완주)
      Bursting  OnAnyPackOpened 발화 · 파티클 · 카메라 셰이크 · 패널 fade · 더미 Build
      Flicking  PackCardStack — 맨 위부터 스와이프로 한 장씩
      Summary   라인업 + 총 환급 + OnRevealComplete
            │
   AcquireButton → 덱 슬롯 0 저장 → LobbyScene 복귀
```

### 책임 분담

| 파일 | 역할 |
|---|---|
| `UI/Shop/PackRevealView.cs` | 스테이지 진행자. 스킵 총괄. 진입 `BeginOpen`, 출력 `OnRevealComplete` |
| `UI/Shop/PackTearHandle.cs` | 봉인 뜯기 제스처. 진행도 산출 + `sealRoot` 훅. 구매·소유를 모른다 |
| `UI/Shop/PackCardStack.cs` | 더미 배치 · 스와이프 넘기기 · 라인업 정리 |
| `UI/Shop/PackCardView.cs` | 카드 한 장 표시 + 신규/중복 강조 |
| `UI/Shop/PackAcquireController.cs` | 캐리어 소비 · 획득 버튼 · 덱 저장 · 목적지 이동 (변경 없음) |
| `UI/Shop/PackStandaloneBoot.cs` | 씬 단독 실행 더미 주입. `alternateDuplicates`로 중복 케이스 검증 |
| ~~`UI/Shop/PackClickHandle.cs`~~ | **삭제** — `PackTearHandle`이 대체 |

### 에셋

| 경로 | 내용 |
|---|---|
| `Assets/Prefabs/CardPack.prefab` | 3D 팩. `Body` + **`Seal`(신규)** + `BoxCollider` + `PackTearHandle` |
| `Assets/Prefabs/UI/PackUI/PackCard.prefab` | 개봉 전용 카드. 860×1204. `Glow` / `NewBadge` / `RefundBadge` |
| `Scenes/CardPack.unity` | `StackInput`(입력판+`PackCardStack`) > `CardLayer` > `StackAnchor`·`DiscardArea`, `RemainingText`, `SummaryGroup`, `SkipButton` |

---

## 3. 단계별 의도

### Stage 0 — 입장
팩이 아래에서 올라와 안착(`packEnterDrop` / `packEnterDuration`).
> 정지 화면이 아니라 **살아 있는 물건**이 놓여 있다는 인상.

### Stage 1 — 뜯기 (스와이프)
봉인선을 그은 만큼 찢어지고, 끝까지 그으면 개봉 확정.
- 진행도가 손가락에 **연속으로** 붙는다(단계 스냅 아님).
- `commitThreshold` 미만에서 놓으면 되감겨 재시도 가능.
- 넘기면 손을 떼도 나머지가 자동 완주.
- 표현은 `sealRoot`를 `sealTornOffset`만큼 밀어내는 최소 구현. **갈아끼우려면 `OnProgress`를 구독**하면 된다(메시 분리·셰이더 컷오프 등).

### Stage 2 — 분출
팩이 사라지고 파티클·카메라 셰이크가 터지며 결과 화면이 올라온다.
- **카드는 흩어지지 않는다.** 곧장 최종 더미 자리에 선다.
- 첫 카드가 앞면이라 이미 보인다 — 분출 순간이 곧 첫 공개다.

### Stage 3 — 한 장씩 넘기기 (핵심)

**더미**
- 뽑힌 카드 전원이 한 자리에 겹쳐 등장. 겉보기엔 카드 한 장.
- `stackJitterPos`/`stackJitterAngle`로 미세 어긋남 — 완전 정렬은 종이 느낌이 죽는다.
- 남은 장수는 `RemainingText`.

**넘기기**
- 맨 위 카드를 좌우 어느 쪽으로든 스와이프. 미는 진행도에 카드가 따라붙고 그만큼 기운다(`dragTiltPerPixel`).
- `flickThreshold` 미만이면 제자리로 되돌아온다.
- 밀린 카드는 **최종 라인업 자리로 직행**한다(자리가 밀린 순서로 이미 정해져 재정렬이 없다).
- 아래 카드는 **올라오지 않는다** — 더미가 줄어드는 게 아니라 맨 위 한 장만 사라진다.
- 라인업이 화면을 넘지 않게 `discardMaxWidth`로 간격을 자동 축소한다(6장 대응).

**신규 / 중복** — `DrawnCard.IsNew`·`Refund`를 여기서 쓴다. 이전 구현은 `Bind(card, true)`로 **둘 다 버리고 있었다.**

| | 신규 | 중복 |
|---|---|---|
| 드러날 때 | 광채가 퍼졌다 잦아들고 `NEW` 리본이 튀어나옴 | 담백하게 그대로 |
| 부가 | — | 환급 숫자가 떠오르며 사라짐(`Refund > 0`일 때만) |

> 강조 발화 시점은 **카드가 완전히 드러난 뒤**(= 위 장이 비켜난 직후).
> 환급은 이미 `TryPurchase` 시점에 지갑에 들어가 있다 — 연출은 **이미 일어난 일의 시각화**이지 이때 지급하는 게 아니다.

### Stage 4 — 요약
라인업 + 총 환급(`OpenedPack.TotalRefund`) + 획득 버튼.
- 환급 0이면 줄 자체를 숨긴다 — `+0`은 정보가 아니라 잡음.
- 세로 순서: 남은장수 → 더미 → 라인업 → 총환급 → 획득버튼.

### Stage 5 — 스킵
- **1회차**: 현재 단계만 즉시 완료. 넘기기 단계에서는 "남은 전부 정리"다(한 장만 넘기는 건 스킵이 아니다).
- **2회차부터**: 요약 직행.
- 입력 경로 둘 — 넘기기 단계는 **화면 탭**(`StackInput`이 전체를 덮는다), 그 외 단계는 **`SkipButton`**.
  뜯기 단계에서 전체 화면 탭 캐처를 쓰지 않은 이유는 3D `OnMouse*`와 uGUI raycast가 충돌하기 때문.
- 어느 시점에 스킵해도 `OnRevealComplete`는 **반드시 1회** 발화한다.

---

## 4. 지켜야 할 경계

- **연출은 경제를 건드리지 않는다.** 차감·드로우·소유·환급은 `CardPackOpener.TryPurchase`가 원자 영속했다. 개봉 화면은 `OpenedPack`을 **읽어서 보여줄 뿐**이다.
- **`PackRevealView`는 구매·소유·덱을 모른다.** 진입 `BeginOpen(OpenedPack)` 하나, 출력 `OnRevealComplete` 하나.
- **`PackTearHandle`은 순수 인터랙션**이다(`PackClickHandle`의 성격 계승).
- `OnAnyPackOpened` / `OnAnyPurchased` 발화 시점을 옮기지 않는다 — 튜토리얼(`OutgameTutorialRunner`)이 물려 있다. `OnAnyPackOpened`는 "팩이 열린 순간" = **뜯기 확정** 시점.
- **튜토리얼 경로가 같은 화면을 쓴다.** 첫 실행 흐름도 이 연출을 통과하므로, 스킵 없이 끝까지 본 길이가 곧 첫 경험의 길이다.

---

## 5. 남은 작업

1. **Play 검증** — 씬 단독 실행(`PackStandaloneBoot`, `alternateDuplicates=true`)으로 뜯기·넘기기·신규/중복·스킵을 확인.
2. **SFX** — 뜯기(진행도에 맞물리는 루프성), 분출, 카드 슬라이드, 신규 획득, 중복 정산. `SoundConfig`에 슬롯 추가 필요. 현재 무음.
3. **분출 파티클** — `burstEffect` 미배선(null 허용). 파티클 에셋 제작 후 연결.
4. **봉인 표현 고도화** — 현재는 `Seal` 오브젝트를 통째로 미는 최소 구현. 실제 "찢김"을 원하면 메시 분리나 셰이더 컷오프로 교체(`OnProgress` 구독).

---

## 6. 열린 항목

- **6장 스와이프가 적정한가.** 기획 당시 3장으로 가정했으나 실제 팩은 전부 `drawCount=6`이다. 반복 구매 시 6번 스와이프가 피로가 될 수 있다. 선택지: (a) 그대로 두고 스킵에 의존 (b) 장수를 줄임 (c) 넘기기를 "마지막 몇 장은 자동" 같은 하이브리드로.
- 팩 종류가 늘면 팩별 연출 차등이 필요한가? (`CardPackData`엔 연출 관련 필드 없음)
- 다연차(10연 등) 계획이 있는가? 있다면 더미가 10장까지 늘어 스킵 설계의 무게가 달라진다.
- 희귀도는 이번엔 도입하지 않았지만, 성장 루프가 확장되면 다시 올라올 축이다. 연출 구조는 등급 강도를 나중에 끼워 넣을 수 있게 열려 있다(`PackCardView.PlayRevealAccent` 분기).
