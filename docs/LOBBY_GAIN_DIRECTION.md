# 로비 획득 연출 (Lobby Gain Direction)

전투·카드팩에서 얻은 것을 **로비로 돌아온 순간 한 번** 보여주는 연출.

- 골드 → 재화 텍스트 근처에서 코인이 생겨 흩어졌다가 텍스트로 빨려들고, 숫자가 오르며 텍스트가 튄다.
- 카드 → 도감 탭 근처에서 카드가 펼쳐졌다가 탭으로 빨려들고, 탭 아이콘이 튄다.

두 연출은 **동시에** 돈다(획득 하나를 두 번에 걸쳐 알리지 않는다).
카드는 **신규 획득분만** 날아간다 — 중복 카드는 골드로 환급되므로 코인 쪽에 이미 표현된다.

지급·저장은 각 씬이 이미 끝냈다. 이 연출은 **표시만** 하며 재화를 건드리지 않는다.

## 흐름

```
[BattleScene]  TurnRunner.CaptureResult
   └ RewardService.GrantBattleReward(...)        ← 지급·Save (기존)
   └ BattleRewardHandoff.Set(보상골드)             ← 연출용 표시량만 싣는다

[CardPack]     PackAcquireController.OnAcquirePressed
   └ CardPackRewardHandoff.Set(환급골드, 신규카드)  ← 환급·소유는 개봉 시점에 이미 완료
                                                   신규 판정은 DrawnCard.IsNew (덱 저장용 m_cards와 별도로 m_newCards에 캐시)

[LobbyScene]   LobbyGainEffectDirector.Start → 1프레임 양보 → 캐리어 소비 → 두 단계를 0초에 함께 꽂아 동시 재생
   ├ 골드: CoinBurstEffect      → GoldHud.TextRect 로 수렴 + GoldHud.HoldDisplay 로 숫자 롤업 + UiPunch
   └ 카드: CardGainFlightEffect → 도감 탭(Button_Collection) 으로 수렴 + UiPunch
```

두 캐리어의 골드는 **합산해서 한 번에** 보여준다(전투 보상 + 중복 환급).
튜토리얼 경로처럼 로비를 거치지 않고 전투로 직행하면 캐리어가 남아 **다음 로비 진입 시** 재생된다(의도된 동작).

## 코드 구성

| 파일 | 역할 |
|---|---|
| `Assets/Scripts/UI/Common/UiGainBurst.cs` | 궤적 코어(static). "원점에서 흩어짐 → 목적지로 수렴" 시퀀스 빌더. 코인·카드가 이 규칙 하나를 공유한다. |
| `Assets/Scripts/UI/Common/UiPunch.cs` | 도착 강조(static). `DOPunchScale` 한 줄 규칙. |
| `Assets/Scripts/UI/Common/CoinBurstEffect.cs` | 코인을 만들고 걷는 일만 한다(궤적은 코어에 위임). 기존 배선·기본값·`BuildBurst` 시그니처 유지 → 전투 결과 팝업/랭크 수령 연출 동작 불변. |
| `Assets/Scripts/UI/Common/CardGainFlightEffect.cs` | 카드를 만들고 걷는다. 프리팹 미배선이면 카드 아트 Image 한 장으로 폴백. |
| `Assets/Scripts/UI/Lobby/LobbyGainEffectDirector.cs` | 캐리어 소비 + 단계 조립. 배선을 비우면 이름으로 자동 탐색. |
| `Assets/Scripts/OutGame/Reward/BattleRewardHandoff.cs` | 전투 보상 골드 캐리어. |
| `Assets/Scripts/OutGame/CardPack/CardPackRewardHandoff.cs` | 카드팩 환급 골드 + 획득 카드 캐리어. |

`GoldHud`에 추가된 연출용 API:
`RectTransform TextRect` / `void HoldDisplay(long)` / `void ReleaseDisplay()`
— 롤업 중에는 `OnCurrencyChanged`를 무시하고 연출이 지정한 값을 보여준다. `OnDisable`에서 고정이 자동 해제된다.

## 배선 (완료됨)

`LobbyCanvas.prefab`에 `GainEffectLayer` 오브젝트가 이미 추가돼 있다.

- `LobbyCanvas` **직속 자식 / 마지막 형제** — 카드가 하단 탭 바에 가려지지 않게.
  (런타임에도 `SetAsLastSibling()`으로 한 번 더 보정한다.)
- `RectTransform`: anchorMin `(0,0)` / anchorMax `(1,1)` / sizeDelta `(0,0)` (전체 stretch)
- `Image` 없음 — 붙이면 터치를 가로챈다.
- `LobbyGainEffectDirector` 1개. **필드는 전부 비어 있고, 그 상태가 정상이다**(자동 탐색).

자동 탐색 규칙:

| 필드 | 비웠을 때 |
|---|---|
| `goldHud` | 씬에서 `GoldHud` 하나를 찾는다(비활성 포함) → `LobbyRoot/TopBar/Currency` |
| `coinSprite` | 골드 텍스트의 부모(`Status_Coin`) 하위에서 이름에 `Icon`이 든 `Image`의 스프라이트를 빌린다 → `Icon_Coin` |
| `collectionTabTarget` | 캔버스 하위에서 `Button_Collection` 이름으로 찾는다. 도감 탭이 선택돼 그 버튼이 꺼져 있으면 `Button_Focus`를 쓴다. |
| `cardPrefab` (CardGainFlightEffect) | 카드 아트(`deckPreview` → `fullImage` → `portrait`) Image 한 장으로 대체 |

탐색이 실패하면 **그 단계만 건너뛰고 경고 로그**를 남긴다(로비 진입 자체는 막지 않는다).

### 선택: 카드를 실제 카드 프리팹으로 날리기

`GainEffectLayer`에 `CardGainFlightEffect`를 미리 추가하고 `cardPrefab`에 `CardVisualView`를 가진 프리팹
(예: `Assets/Assets/Prefabs/UI/CollectionUI/Card.prefab`)을 꽂는다.
프리팹은 **원본 크기를 유지한 채** `cardSize.y` 기준으로 축소된다 — `sizeDelta`를 강제하지 않는 이유는
`CardVisualView`가 카드 크기 대비 비율로 폰트·키워드 아이콘을 배치하기 때문이다.

## 튜닝 포인트

`LobbyGainEffectDirector`
- `coinCountMin/Max` — 코인 장수 범위(획득 골드량을 이 사이로 클램프). 기본 4~12
- `coinAngleStart/Span` — 코인이 흩어지는 부채꼴. 기본 `195°~345°`(수치 아래쪽으로 퍼뜨려 화면 밖 이탈 방지)
- `goldPunch` / `tabPunch` — 도착 강조 세기

`CardGainFlightEffect`
- `cardSize` / `scatterRadius` / `angleStart`(기본 55°) / `angleSpan`(기본 70°, 위쪽 부채꼴)
- `gatherScale` 0.15 — 탭 안으로 삼켜지는 축소량, `spinDegrees` 20 — 비행 중 좌우 흔들림

## 확인 방법

플레이 모드에서만 눈으로 확인 가능하다(에디트 모드에서는 트윈이 돌지 않는다).

1. 로비 → 배틀 → 전투 승/패 → 결과 팝업 터치 → 로비 복귀 → 코인 연출 + 골드 롤업
2. 로비 → 뽑기 탭 → 구매 → 개봉 → 획득 → 로비 복귀 → **신규** 카드가 도감 탭으로 빨려듦
   - 중복 카드가 섞여 있으면 그 카드는 날아가지 않고, 대신 환급 골드가 코인 연출로 **동시에** 나온다
   - 전부 중복이면 코인 연출만, 전부 신규면 카드 연출만 나온다
