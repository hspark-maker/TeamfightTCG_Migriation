# 카드팩 개봉 연출 — 기획

> 목표와 참고정보만 담는다. 트윈 수치·클래스 설계·배선 방식 같은 **구현 결정은 담당에게 위임**한다.
> 이 문서의 수치는 전부 "이 정도 체감"을 전하기 위한 예시일 뿐 확정값이 아니다.
> 최종 갱신: 2026-07-27

---

## 0. 30초 요약

- 지금은 **클릭 1회 → 결과 전체가 한 번에** 뜬다. 기대와 해소의 곡선이 없다.
- 바꿀 흐름: **팩을 스와이프로 뜯고 → 카드 더미가 한 자리에 겹쳐 안착 → 맨 위부터 스와이프로 한 장씩 옆으로 밀어냄 → 요약**.
- 카드는 **앞면**으로 쌓인다. 서스펜스는 "뒤집히는 순간"이 아니라 **"밀어냈을 때 그 아래 뭐가 있을까"** 에 놓인다.
- 연출 강도를 가르는 축은 **신규 / 중복** 하나뿐이다. 희귀도(rarity)는 **도입하지 않는다** — `CardData` 무변경.
- 개봉 조작과 카드 넘기기가 **둘 다 스와이프**다. 제스처 언어를 하나로 통일하는 것이 이 기획의 뼈대.

---

## 1. 현재 상태 (실태)

### 흐름
```
[LobbyScene] Tab_Pack
   PackShowcaseController.OnBuyPressed()
      CardPackOpener.TryPurchase(pack, refundGold)   // 골드 차감·드로우·소유부여·환급까지 전부 여기서 완결
      PackHandoff.Set(opened, "LobbyScene", false)   // 결과를 캐리어에 실음
      SceneManager.LoadScene("CardPack")
            │
[CardPack.unity]  ▼
   PackAcquireController.Start()
      PackHandoff.Consume() → view.BeginOpen(opened)
            │
   PackRevealView                                    ← 연출이 사는 곳. 이 문서의 개편 대상
      3D 팩 표시 → PackClickHandle 클릭 1회
      → 팩 SetActive(false)
      → revealPanel.DOFade(1, 0.35s)
      → CardGrid에 3장 동시 Instantiate
      → OnRevealComplete → AcquireButton 노출
            │
   AcquireButton → 덱 슬롯 0 저장 → LobbyScene 복귀
```

### 관련 파일
| 파일 | 역할 |
|---|---|
| `Assets/Scripts/UI/Shop/PackRevealView.cs` | **개편 핵심.** 개봉 연출 전체 |
| `Assets/Scripts/UI/Shop/PackClickHandle.cs` | 3D 팩 클릭 1회 인터랙션 (스와이프로 대체 대상) |
| `Assets/Scripts/UI/Shop/PackAcquireController.cs` | 개봉 씬 브레인. 연출 완료 → 획득 → 씬 전이 |
| `Assets/Scripts/OutGame/CardPack/OpenedPack.cs` | `DrawnCard { Card, IsNew, Refund }` — 연출 입력의 진실원 |
| `Assets/Scripts/UI/Collection/CollectionCardView.cs` | 카드 타일. 현재 도감용 `Bind(card, owned)`만 |
| `Assets/Scripts/UI/Shop/PackStandaloneBoot.cs` | 씬 단독 실행 더미 주입 — **연출 반복 검증에 그대로 활용** |
| `Assets/Scenes/CardPack.unity` | 개봉 전용 씬. `RevealPanel` / `CardGrid` / `AcquireButton` / `PackOpenDirector` |
| `Assets/Assets/Prefabs/CardPack.prefab` | 3D 팩. 루트 + `Body` 메시 |

### 가진 재료
- **DOTween** 사용 중 (`PackRevealView`가 이미 의존).
- **3D 팩 모델**: `CardPackBody` / `CardPackSeal` 메시, `CardPack_Front` / `_Back` / `_Seal` 머티리얼.
  단 **프리팹엔 `Body`만 붙어 있다** — Seal 메시는 에셋으로만 존재.
- **`SoundManager`** (싱글턴, SFX 풀 8, `SoundConfig` 기반).
- 스와이프/롱프레스 선례: `Assets/Scripts/UI/Input/` 의 `SwipeGuide`, `LongPressDetector`.
- 아웃게임 UI 키트: Layer Lab **GUI Pro - SimpleCasual** (플레인 Image 금지 — 기존 규약).

### 결정적 누락
> `PackRevealView.SpawnCards()`가 `t_view.Bind(t_drawn.Card, true)`로 호출한다.
> **`IsNew`와 `Refund`를 통째로 버리고 있다.** 신규든 중복이든 화면이 완전히 동일하다.

희귀도를 안 쓰기로 한 이상 **이 두 값이 연출의 유일한 감정 축**이다. 다른 무엇보다 먼저 화면에 나와야 한다.

---

## 2. 목표하는 체감

한 문장: **"내가 손으로 뜯었고, 내가 한 장씩 넘겨서 확인했다."**

- 개봉이 **버튼 누르기가 아니라 손동작**이어야 한다. 진행도가 손가락에 붙어야 하고, 도중에 놓으면 되감겨야 한다.
- 카드가 **한 장의 물건**으로 느껴져야 한다. 밀어낼 때 무게와 마찰이 있고, 아래 카드가 벗겨지듯 드러나야 한다.
- **신규는 사건, 중복은 정산.** 신규엔 화면이 반응하고, 중복은 조용히 골드로 환산돼 상단 재화로 흡수된다.
- **두 번째 개봉부터는 빨라야 한다.** 연출은 처음이 즐겁고 열 번째가 지겨운 법이라, 스킵이 기능이 아니라 설계의 일부다.

---

## 3. 단계별 목표

### Stage 0 — 입장
팩이 화면 아래에서 올라와 안착하고, 아주 미세하게 호흡(아이들 회전·부유)한다.
> 목표: 정지 화면이 아니라 **살아 있는 물건**이 놓여 있다는 인상. 스와이프하라는 유도(핸드 아이콘·힌트 화살표)를 여기서 띄운다 — `HintArrow`/`SwipeGuide` 선례 참고.

### Stage 1 — 뜯기 (스와이프)
봉인선을 손가락으로 긋는 만큼 찢어지고, 끝까지 그으면 개봉 확정.
- 진행도가 손가락 위치에 **연속적으로** 따라붙어야 한다(단계 스냅 아님).
- 임계점 미만에서 놓으면 **살짝 되감기**고 다시 시도 가능.
- 임계점을 넘기면 손을 떼도 나머지가 자동으로 완주.
- 찢기는 동안 SFX가 진행도에 맞물려야 한다(한 번 터지고 끝나는 원샷이면 촉감이 죽는다).

> **선행 정비 필요**: 봉인선이 찢어지려면 `CardPack.prefab`에 `CardPackSeal`이 붙고 상/하단이 분리 가능한 구조여야 한다. 지금은 `Body` 단일 메시뿐.
> 대안(메시 분리가 부담이면): 셰이더/머티리얼 컷오프나 UI 오버레이 마스크로 "찢긴 자국"을 처리하는 방식도 가능. **어느 쪽을 쓸지는 담당 판단.**

### Stage 2 — 분출
팩이 열리며 빛·파티클이 터지고, 카메라가 짧게 흔들리고, 팩 셸이 아래로 떨어진다.
- **카드는 이 순간 흩어지지 않는다.** 팩 안에서 곧장 최종 더미 자리로 이동해 안착한다.
- 첫 카드가 **앞면이라 이미 보인다** — 분출 순간이 곧 첫 공개다. 이 게임 개봉에서 가장 임팩트가 큰 지점이므로 여기에 힘을 몰아준다.

### Stage 3 — 한 장씩 넘기기 (핵심)

**더미 구성**
- 뽑힌 카드 전원이 한 자리에 **겹쳐서** 등장한다. 겉보기엔 카드 한 장.
- 좌표·회전이 서로 어긋나면 안 된다. 다만 **완전 정렬은 종이 느낌이 안 나므로** 1~2px·1° 수준의 미세 랜덤 오프셋은 허용(오히려 권장).
- 남은 장수를 어딘가 드러낸다(더미 두께로 보이거나, `3 / 3` 카운터).

**넘기기**
- 맨 위 카드를 좌우 어느 쪽으로든 스와이프하면 밀려난다.
- 미는 진행도에 따라 카드가 따라 움직이고, **그만큼 아래 카드가 벗겨지듯 드러난다.** 이 방식의 최대 강점이므로 이 감각을 최우선으로 지킨다.
- 임계 미만에서 놓으면 제자리로 되돌아온다.
- 밀려난 카드는 **하단 미니 슬롯에 순서대로 쌓인다.** 마지막 장까지 넘기면 그 슬롯이 그대로 Stage 4의 라인업이 되어 전환 없이 이어진다.
- 아래 카드는 **올라오지 않는다.** 더미가 줄어드는 게 아니라 맨 위 한 장만 사라지는 것처럼 보여야 한다.

**신규 / 중복 표현** — `DrawnCard.IsNew`, `DrawnCard.Refund`를 여기서 처음 쓴다.

| | 신규 | 중복 |
|---|---|---|
| 카드가 드러날 때 | 광채가 퍼지고 `NEW` 배지가 튀어나옴 | 담백하게 그대로 |
| SFX | 상승음 | 낮고 짧은 톤 |
| 부가 | 도감 카운터 `+1` 팝(선택) | `+10` 골드 숫자가 상단 재화 HUD로 빨려 들어감 |

> 신규 연출의 발화 시점은 **카드가 완전히 드러난 뒤**다. 반쯤 벗겨진 상태에서 터지면 무엇이 강조된 건지 읽히지 않는다.
> 중복 환급은 이미 `TryPurchase` 시점에 지갑에 들어가 있다 — 연출은 **이미 일어난 일의 시각화**이지 이때 지급하는 게 아니다. `GoldHud` 갱신 타이밍과 어긋나 보이지 않게 처리할 것.

### Stage 4 — 요약
하단 슬롯의 카드들이 정돈된 라인업으로 정렬되고, 총 환급 골드(`OpenedPack.TotalRefund`)와 획득 버튼이 뜬다.
> 목표: 소유감의 마무리. 지금 `AcquireButton`이 하는 일(덱 슬롯 0 저장 → 씬 복귀)은 그대로 유지된다.

### Stage 5 — 스킵
- 연출 중 화면 아무 곳이나 탭 = **현재 단계 즉시 완료**.
- 한 번 더 = **Stage 4로 점프**(남은 카드 전부 라인업에 즉시 배치).
- 어느 시점에 스킵해도 **`OnRevealComplete`는 반드시 1회 발화**한다. 현재 코드도 미배선·0장일 때 발화를 보장하고 있는데(획득 버튼 데드락 방지), 그 계약을 그대로 지킨다.

---

## 4. 지켜야 할 경계

- **연출은 경제를 건드리지 않는다.** 차감·드로우·소유·환급은 `CardPackOpener.TryPurchase`에서 이미 원자적으로 끝나 영속됐다. 개봉 화면은 `OpenedPack`을 **읽어서 보여줄 뿐**이다.
- **`PackRevealView`는 구매·소유·덱을 모른다.** 진입은 `BeginOpen(OpenedPack)` 하나, 출력은 `OnRevealComplete` 하나. 이 계약을 넓히지 않는다.
- **`PackClickHandle`은 순수 인터랙션 뷰**다. 스와이프로 바뀌어도 이 성격(개봉·소유를 모르고 콜백만 발화)은 유지한다.
- `OnAnyPackOpened` / `OnAnyPurchased` static 이벤트의 발화 시점을 옮기지 않는다 — 튜토리얼(`OutgameTutorialRunner`)이 물려 있다.
- **튜토리얼 경로가 같은 화면을 쓴다.** 첫 실행 흐름(`PackHandoff.StartTutorial=true`)도 이 연출을 그대로 통과하므로, 스킵 없이 끝까지 봤을 때의 총 길이가 첫 경험의 길이가 된다.

---

## 5. 선행 정비

1. **`CardPack.prefab`에 Seal 부착 + 상/하단 분리** — Stage 1 없이는 뜯기가 성립하지 않는다. (또는 §Stage 1의 대안 방식 채택)
2. **카드 타일의 개봉용 표현** — `CollectionCardView`는 도감 전용(`owned` 잠금 오버레이)이다. `NEW` 배지·광채·환급 숫자를 여기에 얹을지, 개봉 전용 뷰를 따로 둘지는 **담당 판단**.
3. **SFX 목록 확정** — 뜯기(루프성), 분출, 카드 슬라이드, 신규 획득, 중복 정산. `SoundConfig`에 슬롯 추가 필요.
4. **`PackStandaloneBoot` 활용** — `CardPack` 씬 단독 실행으로 연출을 반복 검증할 수 있다. 다만 더미는 **전부 신규·환급 0 고정**이라, 중복 케이스를 보려면 더미 주입에 신규/중복 혼합 옵션이 필요하다.

---

## 6. 열린 항목

- 팩 종류가 늘면 팩별 연출 차등이 필요한가? (현재 팩 1종 진열, `CardPackData`엔 연출 관련 필드 없음)
- 다연차(10연 등) 계획이 있는가? 있다면 더미 넘기기가 10장까지 늘어나므로 스킵 설계의 무게가 달라진다.
- 희귀도는 이번엔 도입하지 않지만, 성장 루프가 확장되면 다시 올라올 축이다. **연출 구조가 나중에 등급 강도를 끼워 넣을 수 있게** 열려 있으면 좋다(강제는 아님).
