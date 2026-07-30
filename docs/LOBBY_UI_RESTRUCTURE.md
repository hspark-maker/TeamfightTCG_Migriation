# LobbyCanvas UI 구조 개편

대상: `Assets/Assets/Prefabs/UI/LobbyCanvas.prefab` (LobbyScene에 인스턴스로 배치됨)

---

## 0. 실행 방식이 바뀐 이유 (먼저 읽을 것)

원래 합의는 "에디터 스크립트 + Unity MCP 승인"이었다. 그런데 MCP는 승인 토글 문제가 아니라
**Unity 플랜 제한**으로 막혀 있었다. `Library/AI.MCP/connections-v2.asset`에 이 세션의 연결이
이렇게 기록돼 있다:

```
Status: 4
ValidationReason: Your Unity plan doesn't include MCP connections.
                  Upgrade your Unity plan to add more.
```

이전 버전 `claude.exe`들은 `Auto-approved: previously accepted`로 남아 있지만 현재 프로세스는
거부됐다. 이건 라이선스 한도라서 우회하지 않았다. 결과적으로 **에디터에서 실행·확인하는 경로가
전부 막혀** 있었고(콘솔 읽기, 플레이 모드, 스크린샷 모두 불가), 아침까지 결과를 내려면
남은 선택지는 프리팹 YAML 직접 편집뿐이었다.

Unity를 못 쓰는 대신 **검증을 코드로 대체**했다. UGUI 레이아웃 해석기(anchors/pivot,
LayoutElement, Horizontal/VerticalLayoutGroup, ContentSizeFitter)를 UGUI 소스와 동일한
연산으로 구현해서, 변경 전/후 프리팹의 **모든 노드 절대 좌표를 fileID 기준으로 대조**했다.
아래 수치는 전부 그 대조 결과다.

---

## 1. 무엇이 문제였나

### 1-A. 최상위 3분할이 고정 2270px 스택

`LobbyRoot`의 VerticalLayoutGroup이 `ChildControlHeight=0`, `ChildForceExpandHeight=0`,
padding·spacing 0. 이 설정에서 VLG는 자식의 `sizeDelta.y`를 그대로 높이로 쓴다.

| 자식 | 높이 |
|---|---|
| TopBar | 150 |
| Content | **1900 (매직넘버)** |
| BottomBar | 220 |
| 합계 | **2270 고정** |

CanvasScaler는 `1080x1920 / MatchWidthOrHeight=0`(폭 기준)이라 캔버스 높이가 기기 비율을 따라간다.
2270은 그중 한 비율에만 맞는 값이었고, 실제 해석 결과는 이랬다:

| 캔버스 | Content y | BottomBar y | 결과 |
|---|---|---|---|
| 1080×1920 (16:9) | **-130** | **-350** | 하단 탭바가 **화면 밖으로 완전히 사라짐** |
| 1080×2340 (19.5:9) | 290 | +70 | 탭바가 바닥에서 70px 뜸 |
| 1080×2400 (20:9) | 350 | +130 | 탭바가 바닥에서 130px 뜸 |

16:9는 단순히 "비율이 안 맞는" 수준이 아니라 하단 탭바가 아예 안 보이는 상태였다.

### 1-B. 프리팹 전체에 LayoutElement가 0개

레이아웃 그룹은 5개(VLG 2, HLG 2, Grid 4) 있는데 자식 크기를 선언할 수단이 없어서
전부 sizeDelta 하드코딩으로 우회하고 있었다.

### 1-C. 상점 탭 스크롤 리스트가 완전 수동 배치

`ShopList/Viewport/Content` 높이 `1398` 고정, `Row_0~3`이 `y = 0 / -356 / -712 / -1068`
직접 계산, Row 내부 `Cell_0/1/2`가 `x = 130 / 360 / 590`.

### 1-D. RankInfo가 자식을 못 감싸는 "가짜 컨테이너"

`RankInfo`는 `100x100`인데 자식 `RankBadge`가 `+450`, `RankPower`가 `+200`에 떠 있었다.
실제 컨텐츠 bounds는 `500x419`. rect가 컨텐츠와 아무 관계가 없었다.

---

## 2. 무엇을 바꿨나

### Stage 1 — 3분할을 LayoutElement 기반으로 (유일하게 시각 변화 있음)

- `LobbyRoot` VLG: `m_ChildControlHeight` **0 → 1**
- `TopBar` + LayoutElement `preferredHeight=150`
- `Content` + LayoutElement `preferredHeight=0, flexibleHeight=1` ← 남는 높이를 전부 차지
- `BottomBar` + LayoutElement `preferredHeight=220`

결과: **Content = 캔버스 높이 − 370**, BottomBar는 항상 바닥에 밀착.

| 캔버스 | Content 높이 (전 → 후) | BottomBar y (전 → 후) |
|---|---|---|
| 1080×1920 | 1900 → **1550** | -350 → **0** |
| 1080×2340 | 1900 → **1970** | +70 → **0** |
| 1080×2400 | 1900 → **2030** | +130 → **0** |

TopBar는 모든 해상도에서 변화 없음.

> **확인 필요**: Content가 커진 만큼 그 안의 탭 5개가 전부 세로로 늘어난다. 덱 편집 탭은
> 영역을 분수 앵커(0.44H / 0.047H / 0.415H)로 잡고 있어서 H에 비례해 같이 커진다.
> 20:9 기준 약 +6.8%. 의도한 방향이지만 눈으로 한 번 봐줄 것.

> **남겨둔 것**: `Content.m_SizeDelta.y`는 1900 그대로 뒀다. 이제 VLG가 구동(driven)하는
> 값이라 무시되고 Unity가 다음 프리팹 저장 때 알아서 덮어쓴다. 일부러 안 지운 이유는,
> 만에 하나 레이아웃 리빌드가 안 걸려도 **오늘 상태로 degrade**되지 뷰가 통째로 사라지지는
> 않게 하려는 것. 아침에 정상 동작 확인되면 0으로 지워도 된다.

### Stage 2 — 상점 탭을 레이아웃 그룹으로 (시각 변화 없음)

먼저 손으로 넣은 좌표가 정확한 격자인지 검증했고, 그대로 떨어졌다:

- `330 × 4행 + 26 × 3간격 = 1398` — Content 높이와 **정확히 일치**
- 셀 `205px` 폭, 간격 `25px`, 3개 묶음 폭 `665px`, 중심 `x=360`

그래서 등가 변환이 가능했다:

- `Content` + VerticalLayoutGroup(`spacing=26`) + ContentSizeFitter(`Vertical=PreferredSize`)
- `Row_0~3` + LayoutElement(`preferredHeight=330`)
- 각 Row에 **`Cells` 컨테이너 신규 생성** + HorizontalLayoutGroup(`spacing=25`),
  `Cell_0/1/2`를 그 밑으로 이동 + LayoutElement(`205x215`)

이제 행 추가·삭제나 셀 크기 변경에 좌표 재계산이 필요 없다.

### Stage 3 — RankInfo가 자식을 감싸도록 (rect만 변경, 자식 좌표 불변)

- `RankInfo`: `100x100 @ (0,100)` → **`500x419 @ (0,296)`** (실제 컨텐츠 bounds)
- `RankBadge`: `(0,450)` → `(0,94.5)` — 절대 위치 그대로
- `RankPower`: `(0,200)` → `(0,-155.5)` — 절대 위치 그대로
  (중첩 프리팹이라 PrefabInstance 오버라이드를 수정)

---

## 3. 검증 결과

검증기는 두 리비전의 **모든 노드**를 fileID로 매칭해 절대 rect를 비교한다. fileID 기준이라
재부모화(Stage 2의 셀 이동)가 있어도 정확히 추적된다. 캔버스 3종에서 각각 실행.

**기준선 확인**: 원본 vs 원본 → 181개 노드 전부 diff 0 (검증기 자체가 맞다는 확인)

| 검증 | 결과 |
|---|---|
| Stage 2 단독 | **changed = 0**, added = 4 (`Cells` 컨테이너) — 셀 12개가 재부모화 + HLG 구동으로 바뀌었는데 좌표 전부 동일 |
| Stage 3 단독 | **changed = 1** (RankInfo 자기 rect만), 자식 3개 전부 좌표 동일 |
| Stage 1 단독 | changed = 60 — 전부 Content 높이 변화의 정상 전파 |
| 통합본 vs Stage1 | changed = 1 + added = 4 — 단계 간 간섭 **0** |

**구조 무결성 검사** (dangling 참조, 중복 fileID, GameObject↔컴포넌트 대칭,
부모↔자식 대칭, MonoBehaviour 스크립트 참조): 원본 0건, 최종본 **0건**.
문서 수 724 → 757 (+33: LayoutElement 19, LayoutGroup 5, ContentSizeFitter 1, 신규 GameObject 4 + RectTransform 4).

**파서 무손실 확인**: 편집 없이 읽고 다시 쓴 결과가 원본과 **바이트 단위 동일**(691,360 bytes).

**참조 안전성**: GameObject를 삭제하거나 새로 만들어 대체한 곳이 없다(신규 생성은 `Cells` 4개뿐).
`LobbyTabController`, `DeckEditController`, `PackCarouselView` 등이 들고 있는 직렬화 참조는
전부 fileID 기반이라 그대로 유효하다. LobbyScene 인스턴스의 오버라이드도 확인했는데,
TopBar/Content/BottomBar에 `m_SizeDelta.y` 오버라이드가 없어서 프리팹 변경이 씬까지 정상 전파된다.

---

### Unity 쪽 상태 (작업 종료 시점)

Unity는 켜진 채로 변경을 감지했다. `Assets/Scripts/Editor/LobbyLayoutAudit.cs`의 `.meta`가
자동 생성되고 `Library/ScriptAssemblies/Assembly-CSharp-Editor.dll`이 재빌드됐다
(내 마지막 편집 시각과 일치). Editor.log 후반부에 `error CS`·컴파일 실패·예외 없음 →
**추가한 에디터 스크립트는 컴파일 통과**.

참고로 로그에 `LobbyTabController.cs(52): error CS1061 ... 'selectedMark'` 6건이 보이는데,
로그 11,393행대(전체 58,434행)의 **오래된 기록**이다. 현재 소스는 `tint`로 바뀌어 있어
지금 상태와 무관하다.

프리팹 자체의 재임포트 로그는 남지 않았다(에러가 없으면 보통 안 찍힌다). 아침에 Unity로
포커스를 옮기면 확실히 반영된다.

## 4. 아침에 확인할 것

1. Unity로 포커스 이동 → 프리팹 자동 재임포트됨. **콘솔에 에러 없는지** 먼저 확인.
2. LobbyScene 열고 **Game View 해상도를 16:9 / 19.5:9 / 20:9로 바꿔가며** 하단 탭바가
   항상 바닥에 붙는지 확인. (기존에는 16:9에서 사라졌음)
3. 메뉴 **`Tools > Lobby > 레이아웃 점검`** 실행 — 새로 추가한
   `Assets/Scripts/Editor/LobbyLayoutAudit.cs`. 3분할 높이 합계, 가짜 컨테이너, 붕괴 노드를
   실제 배치 상태에서 수치로 찍어준다.
4. 상점 탭 / 랭크 배지 위치가 그대로인지 눈으로 확인.

## 5. 롤백

프리팹은 git에 추적되고 있다(마지막 커밋 `3e556b02`). 가장 깔끔한 롤백:

```
git checkout -- Assets/Assets/Prefabs/UI/LobbyCanvas.prefab
```

git 이력과 무관한 파일 백업도 `.backup_lobby/LobbyCanvas.prefab.orig`에 남겼다.

```
cp .backup_lobby/LobbyCanvas.prefab.orig Assets/Assets/Prefabs/UI/LobbyCanvas.prefab
```

변경 규모: `726 insertions(+), 29 deletions(-)`. `git diff`로 바로 볼 수 있다.

부분 롤백이 필요하면 `.backup_lobby/tools/`의 `apply.js`로 단계를 골라 다시 만들 수 있다
(`node apply.js <원본> <출력> 1,2` 형태로 Stage 지정).

---

## 6. 일부러 안 한 것

### 팩 탭 세로 배치 — 건드리지 않는 게 맞다
`Carousel`은 상단 앵커, `Title`/`ButtonRow`는 하단 앵커로 잡혀 있다. 반대 모서리에 고정돼
가운데 여백이 해상도 차이를 흡수하는 구조라 **이미 적응형**이다. VLG로 바꾸면 오히려 나빠진다.
여기 매직넘버(-500, 350, 100)는 레이아웃 회피가 아니라 정당한 디자인 오프셋이다.

### 덱 편집 패널 분수 앵커 — 손실 없는 변환이 불가능
`DeckArea(0.44H)` / `ButtonBar(0.047H)` / `CollectionArea(0.415H)`. VLG로 옮기려면
영역 사이 간격이 `0.003H`와 `0.01H`로 서로 달라서 단일 `spacing` 값으로 표현이 안 된다.
스페이서 오브젝트를 넣거나 픽셀을 밀어야 하는데, **비주얼 유지가 조건**이고 눈으로 확인할
수단이 없는 상태라 하지 않았다.

다만 진짜 문제는 따로 있다: `ButtonBar` 높이가 `0.047H`라 **화면 비율에 따라 버튼 크기가
변한다**(16:9에서 73px, 20:9에서 95px). 버튼은 고정 픽셀이어야 한다. 이건 값 조정이 아니라
디자인 판단이라 확인 후 같이 정하는 게 맞다.

### 그 외 남은 것
- **상점 Row 프리팹화**: 4개 행이 통째로 복제돼 있고 컨트롤러가 없다(순수 목업). 실데이터를
  붙일 때 Row 프리팹 + 컨트롤러로 가는 게 맞다. 지금 구조 변경으로 그 작업이 훨씬 쉬워졌다.
- **SubTabBar 높이 중복**: `SubTabBar` 높이 110이 `Panel_Grid`/`Panel_Production`의
  `size(0,-110)` 오프셋으로 두 곳에 복제돼 있다. 컨테이너 하나 넣으면 정리되지만 이득이 작아서
  보류.
- **`ShopContent`/`PackContent`/`MatchContent` 패스스루**: 부모 `Tab_*`와 동일한 stretch라
  계층 한 겹이 무의미하다. 제거는 파괴적 변경이라 보류.

---

## 7. 검증 도구 위치

이번에 만든 파서·레이아웃 시뮬레이터·검증기는 스크래치패드에 있다:
`%LOCALAPPDATA%\Temp\claude\C--mgJeon-TeamfightTCG-Migriation\b82efe3b-.../scratchpad\`
(`prefablib.js`, `sim.js`, `apply.js`, `diff.js`, `validate.js`)

앞으로도 프리팹을 구조 변경할 일이 있으면 재사용 가치가 있다. 프로젝트로 옮길지는 판단 필요.
