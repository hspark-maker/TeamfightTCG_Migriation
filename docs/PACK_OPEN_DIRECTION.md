# 카드팩 개봉 연출 — 기획 · 구현 기록

> 기획 의도와 구현 결과를 함께 담는다. 세부 규칙의 진실원은 항상 코드.
> 최종 갱신: 2026-07-28 (**2D uGUI 전환 — 씬 재구성 대기**). 코드·주석은 2D 기준으로 정리됐고, `CardPack.unity`의 노드 재구성은 사용자(에디터) 몫이다.

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

남은 리스크: **배선 지점이 늘었다.** 과거 4개(`packHandle`/`revealPanel`/`cardPrefab`/`cardGrid`) → 지금은 `cardLayer`·`stackAnchor`·`sealRoot`·`shakeTarget`·`skipButton` 등이 추가됐다. 소프트락은 막혔지만 "배선을 빠뜨리면 연출이 조용히 반쪽이 된다"는 성질은 그대로다.

또 하나, **뜯기 입력의 실제 함정은 카메라가 아니라 `raycastTarget`이다.** `PackTearHandle`은 `Update()`에서 `Input.mousePosition`을 폴링하고(`PackTearHandle.cs:85-100, 180`) 콜라이더·`Camera`를 전혀 참조하지 않는다. 대신 드래그 시작 직전에 `EventSystem.IsPointerOverGameObject()` 가드를 통과해야 한다(`:92, :183-184` — 버튼 같은 실제 UI를 제스처가 가로채지 않게 하는 장치다). 그래서 **팩·배경 Image의 `raycastTarget`이 켜져 있으면 팩 위에서 시작한 드래그가 이 가드에 막혀 개봉이 안 된다** — "팩을 정확히 그었는데 안 열린다"가 이 배선 실수의 증상이다. 신규 `BG`/`Pack`/`Seal` Image는 전부 `raycastTarget` **OFF**.

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
      Bursting  OnAnyPackOpened 발화 · 파티클 · 배경 셰이크 · 패널 fade · 더미 Build
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
| ~~`Assets/Assets/Prefabs/CardPack.prefab`~~ | **폐기**(2026-07-28 2D 전환). 3D 팩 프리팹 · 메시 2종 · 머티리얼 3종은 색 A/B 대조용으로 Play 검증 전까지만 보존한다. 참조는 `CardPack.unity` 하나뿐 |
| 팩 (2D 대체) | 씬 노드로 대체 — `Scenes/CardPack.unity > UICanvas > PackRoot > Pack`(Image + `PackTearHandle`) `> Seal`(Image). 프리팹 없음 |
| `Assets/Assets/Prefabs/UI/PackUI/PackCard.prefab` | 개봉 전용 카드. 860×1204. `Glow` / `NewBadge` / `RefundBadge` |
| `Scenes/CardPack.unity` | `UICanvas`(Overlay) > `BG`(Image, **`shakeTarget`**) · `PackRoot`(빈 컨테이너, **`packRoot`**) > `Pack`(Image + `PackTearHandle`, **`tearHandle`**) > `Seal`(Image, **`sealRoot`**) · `RevealPanel`(CanvasGroup) · `TearHint`. 그리고 `StackInput`(입력판+`PackCardStack`) > `CardLayer` > `StackAnchor`, `RemainingText`, `SummaryGroup`, `ResultGrid`, `SkipButton`(**`RevealPanel`의 자식** — §3 Stage 5 참고) |

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
- 표현은 `sealRoot`를 `sealTornOffset`만큼 밀어내는 최소 구현. **갈아끼우려면 `OnProgress`를 구독**하면 된다(셰이더 컷오프·스프라이트 분리 등).
  - `sealTornOffset`은 `sealRoot.localPosition`에 더해지므로(`PackTearHandle.cs:166`) 단위는 **부모(`Pack`)의 로컬 = 캔버스 참조px(1440×3120 기준)**다. 2D 전환 값은 `(0, 95, 0)`(구 3D의 0.35 world × 270.2). ↔ 같은 컴포넌트의 `tearDistance`는 참조px이 **아니다** — §7 표 참고.

### Stage 2 — 분출
팩이 사라지고 **배경이 흔들리며** 결과 화면이 올라온다.
- **카드는 흩어지지 않는다.** 곧장 최종 더미 자리에 선다.
- 첫 카드가 앞면이라 이미 보인다 — 분출 순간이 곧 첫 공개다.
- **셰이크 대상은 배경(`BG` Image)이다.** `shakeTarget`에 팩을 걸면 no-op가 된다 — `EnterBursting`이 셰이크를 시작한 **직후** 같은 프레임에 `packRoot`를 `SetActive(false)` 하기 때문(`PackRevealView.cs:221-228`). 계속 화면에 남아 있는 것을 걸어야 한다.
- ⚠ **Overlay 캔버스 위에는 `ParticleSystem`이 렌더되지 않는다.** `burstEffect`를 실제로 붙이려면 Screen Space-Camera 캔버스로 바꾸거나 UI 파티클 솔루션이 필요하다. 현재는 미배선(null 허용)이라 무해하다 — §5 남은 작업 3번.

### Stage 3 — 한 장씩 넘기기 (핵심)

**더미**
- 뽑힌 카드 전원이 한 자리에 겹쳐 등장. 겉보기엔 카드 한 장.
- `stackJitterPos`/`stackJitterAngle`로 미세 어긋남 — 완전 정렬은 종이 느낌이 죽는다.
- 남은 장수는 `RemainingText`.

**넘기기**
- 맨 위 카드를 **어느 방향으로든** 스와이프(좌우·상하·대각 전부). 미는 진행도에 카드가 따라붙고 그만큼 기운다(`dragTiltPerPixel` — 기울기만 좌우 성분을 쓴다).
- `flickThreshold` 미만이면 제자리로 되돌아온다. 단 짧아도 `flickSpeed` 이상으로 튕기면 넘어간다.
- **밀린 카드는 민 방향으로 날아가며 페이드아웃하고 `Destroy`된다**(`PackCardStack.DismissCard`). 라인업 자리 같은 건 없다.
- 아래 카드는 **올라오지 않는다** — 전원이 처음부터 같은 자리(`stackAnchor` + 지터)에 놓이고 밀린 뒤에도 재배치가 없다. 더미가 줄어드는 게 아니라 맨 위 한 장만 사라진다. *(코드 확인: `Build`가 모든 카드에 같은 home을 주고, `OnEndDrag`는 `m_stack.RemoveAt(0)` 외에 남은 카드의 좌표를 건드리지 않는다.)*
- **결과는 `PackResultGrid`가 새 인스턴스를 3열로 세운다.** 더미의 카드는 이미 파괴됐으므로 요약에서 보는 것은 사본이다 — `PackCardStack`과 `PackResultGrid`는 **서로를 모른다**(밀어내기의 좌표 직접 조작과 결과 배치의 수명·좌표계를 갈라 놓은 분리). 그래서 넘기기 감도를 바꿔도 요약 레이아웃은 영향받지 않는다.

> ⚠️ ~~"밀린 카드는 최종 라인업 자리로 직행"~~ · ~~`discardArea`~~ · ~~`discardMaxWidth`~~ 는 **구버전 서술**이었다(2026-07-28 정정, `PACK_FEEL_PLAN.md`에서 이관). `PackCardStack`에 그런 필드는 없고 씬에 `DiscardArea` 노드도 없다. **코드가 진실원.**

**신규 / 중복** — `DrawnCard.IsNew`·`Refund`를 여기서 쓴다. 이전 구현은 `Bind(card, true)`로 **둘 다 버리고 있었다.**

| | 신규 | 중복 |
|---|---|---|
| 드러날 때 | 광채가 퍼졌다 잦아들고 `NEW` 리본이 튀어나옴 | 담백하게 그대로 |
| 부가 | — | 환급 숫자가 떠오르며 사라짐(`Refund > 0`일 때만) |

> 강조 발화 시점은 **카드가 완전히 드러난 뒤**(= 위 장이 비켜난 직후).
> 환급은 이미 `TryPurchase` 시점에 지갑에 들어가 있다 — 연출은 **이미 일어난 일의 시각화**이지 이때 지급하는 게 아니다.

### Stage 4 — 요약
결과 격자(3열, `PackResultGrid`) + 총 환급(`OpenedPack.TotalRefund`) + 획득 버튼.
- 환급 0이면 줄 자체를 숨긴다 — `+0`은 정보가 아니라 잡음.
- 세로 순서: 남은장수 → 더미 → 결과 격자 → 총환급 → 획득버튼.

### Stage 5 — 스킵
- **1회차**: 현재 단계만 즉시 완료. 넘기기 단계에서는 "남은 전부 정리"다(한 장만 넘기는 건 스킵이 아니다).
- **2회차부터**: 요약 직행.
- 입력 경로 둘 — 넘기기 단계는 **화면 탭**(`StackInput`이 전체를 덮는다), 그 외 단계는 **`SkipButton`**.
  뜯기 단계에서 전체 화면 탭 캐처를 쓰지 않은 이유는 **`RevealPanel`의 `CanvasGroup`이 단계를 갈라놓기 때문**이다. `ResetPanel()`이 분출 전까지 `alpha 0` + `blocksRaycasts false`로 만들고(`PackRevealView.cs:400-408`), `EnterBursting`의 fade가 끝날 때 비로소 `blocksRaycasts = true`가 된다(`:241-247`). `StackInput`은 그 패널 아래에 있으므로 뜯기 단계에는 애초에 입력을 받지 못한다 — 같은 이유로 `PackTearHandle`의 `IsPointerOverUI()` 가드도 이 패널에 걸리지 않는다(`PackTearHandle.cs:182`).
  > ~~"3D `OnMouse*`와 uGUI raycast가 충돌하기 때문"~~ 은 **근거가 소멸한 서술**이다(2026-07-28 정정). `PackTearHandle`은 `OnMouse*`를 쓰지 않고, 2D 전환으로 3D 자체가 사라졌다.
- **⚠️ 알려진 문제 — 뜯기 단계에서 `SkipButton`이 보이지도 눌리지도 않는다.** `SkipButton`은 씬에서 `RevealPanel`의 **자식**이라(`CardPack.unity` 실측: `SkipButton`의 부모 = `RevealPanel`), 위의 `alpha 0` + `blocksRaycasts false`를 그대로 뒤집어쓴다. 즉 Entering·Tearing 동안 탈출 수단이 실질적으로 없다 — `PackRevealView.cs:52`의 주석("뜯기 단계에서도 빠져나갈 수 있게")과 **실배선이 어긋나 있다**. 소프트락은 아니다(뜯으면 진행된다). 2D 전환과 무관한 기존 이슈지만, **씬을 재구성하는 김에 `SkipButton`을 `UICanvas` 직속(=`TearHint` 형제)으로 올리고 `skipButton` 참조를 다시 거는 것이 권장안**이다(코드 변경 없음).
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
3. **분출 파티클** — `burstEffect` 미배선(null 허용). ⚠ **에셋만 만들어선 안 붙는다**: 캔버스가 Screen Space-Overlay라 `ParticleSystem`이 UI 위로 렌더되지 않는다. 선행 결정이 필요하다 — (a) 캔버스를 Screen Space-Camera로 전환 (b) UI 파티클 솔루션 도입 (c) 파티클 대신 Image 기반 플래시·광선 연출로 대체. 현재는 미배선이라 무해하다.
4. **봉인 표현 고도화** — 현재는 `Seal` 오브젝트를 통째로 미는 최소 구현. 실제 "찢김"을 원하면 셰이더 컷오프나 스프라이트 분리로 교체(`OnProgress` 구독).
5. **`SkipButton` 리페어런팅** — §3 Stage 5의 알려진 문제. 씬 재구성 시 `UICanvas` 직속으로.

---

## 6. 열린 항목

- **6장 스와이프가 적정한가.** 기획 당시 3장으로 가정했으나 실제 팩은 전부 `drawCount=6`이다. 반복 구매 시 6번 스와이프가 피로가 될 수 있다. 선택지: (a) 그대로 두고 스킵에 의존 (b) 장수를 줄임 (c) 넘기기를 "마지막 몇 장은 자동" 같은 하이브리드로.
- 팩 종류가 늘면 팩별 연출 차등이 필요한가? (`CardPackData`엔 연출 관련 필드 없음)
- 다연차(10연 등) 계획이 있는가? 있다면 더미가 10장까지 늘어 스킵 설계의 무게가 달라진다.
- 희귀도는 이번엔 도입하지 않았지만, 성장 루프가 확장되면 다시 올라올 축이다. 연출 구조는 등급 강도를 나중에 끼워 넣을 수 있게 열려 있다(`PackCardView.PlayRevealAccent` 분기).

---

## 7. 2D 전환 이후의 좌표 단위

**이 화면의 인스펙터 값은 단위가 한 가지가 아니다.** 전부 "픽셀"처럼 보이지만 세 종류가 섞여 있고, 그 차이가 기기 해상도에 따라 체감으로 드러난다. 값을 만질 때 반드시 이 표를 먼저 볼 것.

**3D → 2D 환산 기준: 270.2 참조px / world unit.** (구 카메라 가시 높이 `2 · 10 · tan30° = 11.547` world, 캔버스 3120px ÷ 11.547.)

| 값 | 소속 | 단위 | 왜 그런가 | 현재 값 |
|---|---|---|---|---|
| `packEnterDrop` | `PackRevealView` | **참조px**(1440×3120) | `packRoot.localPosition`을 다루는데 그 부모가 캔버스 자식이라 캔버스 좌표계다 | `811` (= 3 world × 270.2) |
| `sealTornOffset` | `PackTearHandle` | **참조px** | `sealRoot.localPosition`에 더한다(`:166`). 부모는 `Pack` = 캔버스 좌표계 | `(0, 95, 0)` (= 0.35 world × 270.2) |
| `tearDistance` | `PackTearHandle` | **raw 디바이스px** | `Input.mousePosition` 차이를 그대로 쓴다(`:122-123`). `scaleFactor` 정규화 **없음** | `160` (불변) |
| `flickThreshold` | `PackCardStack` | **참조px** | `OnDrag`가 `_e.delta / canvas.scaleFactor`로 나눈 뒤 누적한다(`:184-189`) | `90` |
| `shakeStrength` | `PackRevealView` | **raw 디바이스px** | `DOShakePosition`이 `transform.position`(**월드**)을 흔든다. Overlay 캔버스의 월드 = 디바이스 스크린px | `68` |

### 알려진 편차 — `tearDistance` ↔ `flickThreshold`

같은 화면의 두 스와이프 임계인데 **단위가 다르다.** 1440폭 기준기에서는 둘 다 의도대로지만, 720폭 기기에서는 `scaleFactor`가 0.5라 `flickThreshold`는 자동으로 절반(45 디바이스px)으로 줄어드는 반면 `tearDistance`는 160 디바이스px 그대로다 → **뜯기만 상대적으로 어려워진다.**

**고치지 않는다(스코프 밖).** 2D 전환이 만든 문제가 아니고(`PackTearHandle`은 원래부터 `Input.mousePosition` 폴링이었다), 고치면 비참조 기기의 체감이 바뀌어 이번 작업의 **동작 동등성 유지** 원칙과 충돌한다. 나중에 통일하려면 `tearDistance`를 `canvas.scaleFactor`로 나누는 쪽이 `PackCardStack` 선례와 맞는다.

`shakeStrength`도 같은 성질이다 — `68`은 1440폭 기기에서만 정확히 68 참조px다. 참조px로 통일하려면 `DOShakePosition` → `DOShakeAnchorPos` 교체가 필요한데 역시 이번 스코프 밖이라 값만 두고 주석에 명시했다(`PackRevealView.cs:36`).
