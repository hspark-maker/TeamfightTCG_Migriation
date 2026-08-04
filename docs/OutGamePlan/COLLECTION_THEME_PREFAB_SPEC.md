# 도감 테마 뷰 — 프리팹/에셋 제작 사양서

> **이 문서 하나만 보고 제작할 수 있게 쓴 사양서다.** 코드(스크립트 7개)는 이미 커밋돼 있고, 남은 것은 SO 1개 + 프리팹 2개 + 기존 프리팹 1개 확장이다.
> 추측이 필요한 값은 남기지 않았다. 값이 안 적힌 항목은 **Unity 기본값 그대로** 두면 된다.

## 무엇을 만드는가

도감 탭 상단에 토글을 달아, 켜면 기존 4열 그리드 대신 **테마별 아코디언 목록**이 뜨게 한다. 테마 행을 탭하면 그 자리에서 펼쳐지고, 미소유 카드는 카드 그림 없이 **번호만 뜬 빈 슬롯**으로 보인다.

```
Tab_Collection                                  ← CollectionTabController
├── Panel_ViewSwitch (신규)   토글 바
├── Panel_Grid       (기존)   ← 손대지 않는다
└── Panel_Themes     (신규)   ← CollectionThemeListController
     └ ThemeScroll > Viewport > Content
        └ CollectionThemeRow xN                 ← CollectionThemeRowView
           ├ Header  (테마명 · "3/9" · 화살표)
           └ Body    (GridLayoutGroup, 접힘 = 비활성)
              └ CollectionThemeSlot xN          ← CollectionThemeSlotView
                 ├ CardUIView (기존 프리팹 인스턴스)
                 └ EmptySlot  (번호 텍스트)
```

## 작업 순서

**1 → 2 → 3 → 4 순서를 지킬 것.** 뒤 단계가 앞 단계 산출물을 참조한다. 각 단계 끝에 검증 항목이 있고, 통과 못 하면 다음으로 넘어가지 않는다.

---

## 1. SO 저작 — `CollectionThemeConfig.asset`

**경로**: `Assets/SO/CollectionTheme/CollectionThemeConfig.asset`
**생성**: `Create > Card Battle > Collection Theme Config`

### 필드

| 필드 | 타입 | 설명 |
|---|---|---|
| `themes` | `List<CollectionThemeDef>` | 순서 = 도감 표시 순서 |

`CollectionThemeDef` 항목마다:

| 필드 | 타입 | 필수 | 설명 |
|---|---|---|---|
| `themeId` | string | 권장 | **안정 키. 한 번 정하면 바꾸지 않는다.** 영문 스네이크 권장(`theme_forest`). 비우면 `displayName` → `theme_{index}` 순으로 폴백 |
| `displayName` | string | 필수 | 헤더에 뜨는 한글 표시명 |
| `icon` | Sprite | 선택 | 헤더 좌측 아이콘. 미지정이면 코드가 `Image`를 끈다 |
| `cards` | `List<CardData>` | 필수 | **순서가 곧 슬롯 번호**(index+1 = 001, 002…) |

카드 에셋은 `Assets/SO/Cards/` 아래에 있다(현재 30장 규모).

### 배선

`Assets/Assets/Prefabs/Boot.prefab` 열기 → `BootInstaller` 컴포넌트 → **`Collection Themes`** 슬롯에 위 에셋 드래그. (`Collection Layout` 바로 아래에 뜬다.)

### 검증 V1 — 이 단계에서 반드시 통과시킬 것

플레이 모드 진입 후 `CollectionThemeConfig.asset` 우클릭 → **`테마 배치 검증`** 실행.

- 기대: `[CollectionThemes] 배치 검증 통과 — 배치 N장 / 카탈로그 N장, 드리프트 없음`
- `카탈로그엔 있으나 배치 누락` 경고 = 어느 테마에도 안 들어간 카드가 있다는 뜻. 의도한 것이면 넘어가도 된다
- `배치엔 있으나 카탈로그 미존재` 경고 = `CardRegistry.asset`에 없는 카드를 테마에 넣었다는 뜻. **반드시 고칠 것**

> SO를 배선하기 전에는 경고 로그 1건이 뜨는 것이 정상이다(테마 0개).

---

## 2. 슬롯 프리팹 — `CollectionThemeSlot.prefab`

**경로**: `Assets/Assets/Prefabs/UI/Collection/CollectionThemeSlot.prefab`

### 계층

```
CollectionThemeSlot        RectTransform + CollectionThemeSlotView
├── CardUIView             ← 기존 프리팹을 인스턴스로 배치
└── EmptySlot              Image (빈 슬롯 테두리/배경)
     └── NumberText        TextMeshProUGUI
```

### 노드별 지정값

**`CollectionThemeSlot`** (루트)
- RectTransform은 기본값 그대로 — 크기는 부모 `GridLayoutGroup`의 Cell Size가 정한다

**`CardUIView`**
- `Assets/Assets/Prefabs/UI/CardView/CardUIView.prefab` (guid `e114849808e011c4ea757d3c052285ca`)를 **프리팹 인스턴스로 드래그**
- ⚠️ **Ctrl+D 복사본이나 Unpack 금지.** 인스턴스여야 카드 비주얼 수정이 자동 전파된다. 복사하면 도감 카드만 다른 화면과 겉모습이 갈라진다
- 이 프리팹은 **수정하지 않는다** (`showName`/`showHp`/`showKeywords`/`showSynergies` 포함)
- RectTransform: Anchor Min `(0,0)` / Max `(1,1)` / Left·Right·Top·Bottom 전부 `0`

**`EmptySlot`**
- `Image` — 빈 슬롯 테두리/배경 스프라이트. 레퍼런스처럼 옅은 회색 라운드 사각
- RectTransform: Anchor Min `(0,0)` / Max `(1,1)` / offset 전부 `0`
- **활성 상태로 저작** (코드가 Bind에서 켜고 끈다)

**`NumberText`**
- `TextMeshProUGUI`, Alignment = **Center / Middle**
- 예시 텍스트 `001` (런타임에 코드가 덮어쓴다)
- 폰트는 프로젝트 한글 폰트 자산 사용

### 컴포넌트 배선 — `CollectionThemeSlotView`

| 필드 | 필수 | 대상 |
|---|---|---|
| `cardView` | 필수 | 자식 `CardUIView`의 `CardVisualView` 컴포넌트 |
| `emptySlot` | 필수 | 자식 `EmptySlot` (GameObject) |
| `numberText` | 필수 | `NumberText` (TMP_Text) |
| `numberFormat` | 기본 `000` | 자릿수. 테마가 100장을 넘으면 `000` 그대로 두면 된다 |

### 동작 (참고)

- 소유: `EmptySlot` 꺼지고 `CardUIView`가 정상 카드로 표시
- 미소유 / 카드 누락: `CardUIView` 통째로 꺼지고 `EmptySlot` + 번호만
- 미소유 슬롯은 롱프레스해도 상세가 열리지 않는다 — **의도된 동작**(빈 슬롯은 "카드가 아닌 것"이라서)

---

## 3. 행 프리팹 — `CollectionThemeRow.prefab`

**경로**: `Assets/Assets/Prefabs/UI/Collection/CollectionThemeRow.prefab`

### 계층

```
CollectionThemeRow    VerticalLayoutGroup + ContentSizeFitter + CollectionThemeRowView
├── Header            Image + Button + LayoutElement
│    ├── ThemeIcon    Image        (선택)
│    ├── NameText     TMP_Text
│    ├── ProgressText TMP_Text     ("3/9")
│    └── Arrow        Image
└── Body              GridLayoutGroup      ★ 비활성 상태로 저장
```

### 노드별 지정값

**`CollectionThemeRow`** (루트)
- `VerticalLayoutGroup`: Child Control Width ☑ / **Child Control Height ☑** / Child Force Expand Width ☑ / **Child Force Expand Height ☐**
- `ContentSizeFitter`: Horizontal = **Unconstrained** / Vertical = **Preferred Size**
- 루트에 `LayoutElement`는 **달지 않는다** (높이를 CSF가 계산해야 아코디언이 성립한다)

**`Header`**
- `Image` (헤더 배경) + `Button` (Target Graphic = 그 Image)
- `LayoutElement`: **Preferred Height = 120** (고정)
  - ⚠️ 이게 없으면 접힌 행 높이가 0이 될 수 있다
- 자식 배치는 자유(`HorizontalLayoutGroup`을 써도 되고 앵커로 직접 놓아도 된다)

**`NameText` / `ProgressText`**
- `TextMeshProUGUI`
- ⚠️ **Auto Size와 자동 줄바꿈(Wrapping)을 끌 것.** 텍스트 높이가 너비에 의존하면 중첩 ContentSizeFitter 계산이 한 프레임 밀린다
- `ProgressText` 예시 텍스트: `3/9`

**`Arrow`**
- `Image` (∨ 모양 화살표)
- **Pivot을 중앙 `(0.5, 0.5)`으로.** 코드가 `localEulerAngles.z`를 `0 ↔ -90`으로 돌린다

**`Body`** ★ 가장 중요한 노드
- `GridLayoutGroup`:
  | 항목 | 값 |
  |---|---|
  | Padding | Left/Right/Top/Bottom = `20` |
  | Cell Size | `300 x 380` |
  | Spacing | `-30 , -10` |
  | Start Corner | Upper Left |
  | Start Axis | Horizontal |
  | Child Alignment | Upper Center |
  | **Constraint** | **Fixed Column Count** ★ |
  | **Constraint Count** | **4** ★ |
- `ContentSizeFitter`는 **달지 않는다** (GridLayoutGroup이 스스로 preferred height를 보고한다)
- **프리팹 저장 시 비활성(체크 해제) 상태로 둘 것** — 접힌 상태가 기본이다
- 목업 슬롯을 넣어뒀다면 그대로 둬도 된다(첫 펼침 때 코드가 전부 지운다)

> ### ⚠️ Constraint = Fixed Column Count 는 협상 불가
> `Flexible`로 두면 행 수가 자기 **너비**에 의존하는데, 그 너비는 같은 리빌드 패스에서 부모가 정한다.
> 결과적으로 펼친 높이가 한 프레임 늦거나 영영 틀린다 — "아코디언이 안 늘어나요"의 대표 원인이다.
> 기존 도감 그리드도 이미 Fixed Column Count 4를 쓰고 있다.

### 컴포넌트 배선 — `CollectionThemeRowView`

| 필드 | 필수 | 대상 |
|---|---|---|
| `headerButton` | 필수 | `Header`의 Button |
| `nameText` | 필수 | `NameText` |
| `progressText` | 필수 | `ProgressText` |
| `arrow` | 선택 | `Arrow`의 RectTransform |
| `themeIcon` | 선택 | `ThemeIcon`의 Image |
| `body` | 필수 | `Body` (GameObject) |
| `slotContainer` | 선택 | `Body`. **미배선이면 자동으로 `body`를 쓴다** |
| `slotPrefab` | 필수 | 2단계에서 만든 `CollectionThemeSlot.prefab` |
| `arrowExpandedZ` | 기본 `-90` | 펼쳤을 때 화살표 z 회전각 |

### 검증 V3 — 다음 단계 전에 확인

행 프리팹만으로는 확인이 어려우니 4단계 배선 후 함께 본다. 다만 **Cell Size 300x380 × 4열**이 화면 폭에 맞는지는 이 단계에서 눈으로 맞춰두는 게 좋다(레퍼런스는 더 작은 슬롯 5열이다 — 취향에 따라 조정해도 되지만 Constraint Count와 Cell Size를 같이 바꿀 것).

---

## 4. `Tab_Collection.prefab` 확장

**경로**: `Assets/Assets/Prefabs/UI/LobbyUI/Tabs/Tab_Collection.prefab`

현재 계층 (건드리지 않는 부분):
```
Tab_Collection (Image + VerticalLayoutGroup)
└── Panel_Grid  (LayoutElement: Preferred H 0 / Flexible H 1 / Priority 1)
     └── Gallery > Viewport > Content   ← CollectionGridController
```

### 4-1. `Panel_ViewSwitch` 추가 — Panel_Grid **위 형제**

```
Panel_ViewSwitch      RectTransform + LayoutElement (+ HorizontalLayoutGroup 권장)
├── Label_Grid        TMP_Text  "그리드"
├── Toggle_Switch     ← Layer Lab 프리팹 인스턴스 + Toggle 컴포넌트
└── Label_Theme       TMP_Text  "테마"
```

- `LayoutElement`: **Preferred Height = 110** / Flexible Height = `-1` (고정 높이 바)
- `Toggle_Switch` = `Assets/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_Component_UI_Etc/Toggle_Switch_Light.prefab` 인스턴스

**Toggle 컴포넌트 설정** — ⚠️ 이 프로젝트는 `UnityEngine.UI.Toggle`을 쓴 전례가 없다. 위 Layer Lab 프리팹은 **아트 전용(Image만 있고 Toggle 스크립트가 없다)으로 보이므로, 루트에 `Toggle`을 직접 Add Component** 해야 한다. 인스펙터에서 먼저 확인할 것.

| Toggle 항목 | 값 |
|---|---|
| `Is On` | **☐ (꺼짐)** |
| `Target Graphic` | 루트 또는 `Switch_Off`의 Image |
| `Graphic` | **`Switch_On`의 Image** (Toggle이 isOn에 따라 이것만 켜고 끈다) |
| `Transition` | None 또는 Color Tint |
| `On Value Changed` | **아무것도 배선하지 말 것** — 코드가 `RemoveAllListeners()`로 지운다 |

- `Switch_On`이 `Switch_Off`보다 **형제 인덱스 뒤**에 있어야 켜졌을 때 위에 덮여 그려진다. 아니면 순서를 바꿀 것

### 4-2. `Panel_Themes` 추가 — Panel_Grid **아래 형제**

```
Panel_Themes          LayoutElement + CollectionThemeListController
└── ThemeScroll       ScrollRect (+ Image 배경, 선택)
     └── Viewport     Image + RectMask2D
          └── Content VerticalLayoutGroup + ContentSizeFitter
```

**`Panel_Themes`**
- `LayoutElement`: Preferred Height `0` / **Flexible Height `1`** / Layout Priority `1`
  → Panel_Grid와 **동일값**이라 두 패널이 같은 자리를 차지한다
- ★ **비활성 상태로 저장**

**`ThemeScroll`** (ScrollRect)
| 항목 | 값 |
|---|---|
| Horizontal | ☐ |
| Vertical | ☑ |
| **Movement Type** | **Clamped** ★ (긴 테마를 접을 때 스크롤이 튕기는 것을 막는다) |
| Inertia | ☑ |
| Scroll Sensitivity | `30` |
| Content | 아래 `Content` |
| Viewport | 아래 `Viewport` |

**`Viewport`**
- `Image` + `RectMask2D`
- Anchor Min `(0,0)` / Max `(1,1)` / offset 전부 `0`

**`Content`** — 기존 그리드 Content와 같은 앵커 규약
| 항목 | 값 |
|---|---|
| Anchor Min | `(0, 1)` |
| Anchor Max | `(1, 1)` |
| Pivot | `(0.5, 1)` |
| Anchored Position | `(0, 0)` |
| Size Delta | `(0, 0)` |
| VerticalLayoutGroup | Child Control W ☑ / **Child Control H ☑** / Child Force Expand W ☑ / **Child Force Expand H ☐** / Spacing `8` |
| ContentSizeFitter | Horizontal = Unconstrained / **Vertical = Preferred Size** |

**컴포넌트 배선 — `CollectionThemeListController`** (Panel_Themes에 부착)

| 필드 | 필수 | 대상 |
|---|---|---|
| `scrollRect` | 선택(스크롤 이동용) | `ThemeScroll`. ScrollRect의 `Viewport`도 배선해 둘 것 |
| `content` | 필수 | `Content` |
| `rowPrefab` | 필수 | 3단계 `CollectionThemeRow.prefab` — ⚠️ **프로젝트 에셋을 드래그.** Content 안에 놓은 목업 노드를 물리면 안 된다(Build가 Content 자식을 전부 지운다) |
| `emptyNotice` | 선택 | 테마 0개 안내 노드. ⚠️ **`Content`의 자식으로 두지 말 것** — 같은 이유로 지워진다. Panel_Themes 직속 형제로 |
| `scrollToExpandedRow` | 기본 ☑ | 펼친 행을 뷰포트 상단으로 스크롤 |

### 4-3. 루트에 `CollectionTabController` 추가

`Tab_Collection` **루트 GameObject**에 Add Component.

| 필드 | 필수 | 대상 |
|---|---|---|
| `viewToggle` | 필수 | 4-1의 Toggle |
| `gridPanel` | 필수 | `Panel_Grid` |
| `themePanel` | 필수 | `Panel_Themes` |

### 4-4. 저장 전 최종 확인

세 값이 **서로 일치**해야 첫 진입이 그리드로 뜬다:

- `Panel_Grid` 활성 **☑**
- `Panel_Themes` 활성 **☐**
- `Toggle.isOn` **☐**

---

## 5. 절대 손대지 말 것

| 대상 | 이유 |
|---|---|
| `Assets/Assets/Prefabs/UI/CardView/CardUIView.prefab` | 8개 화면이 공유하는 카드 비주얼 단일 진실원 |
| `Panel_Grid` 이하 전체 (Gallery/Viewport/Content/CollectionGridController) | 기존 그리드는 무수정이 이번 작업의 전제 |
| `Assets/SO/CollectionLayout/CollectionLayoutConfig.asset` | 방치생산용 행 데이터. 테마와 **별개 축**이다 |
| `CollectionScreen.prefab`, `CollectionRow.prefab`, `Scenes/TEST/CollectionTest.unity` | 기존 도감 생산 흐름 |

---

## 6. 검증 목록

프리팹이 다 붙은 뒤 순서대로 확인한다.

- **V1 데이터 정합** — (1단계 참조) `테마 배치 검증` 컨텍스트 메뉴로 드리프트 0건
- **V2 진행도** — 도감 탭의 전체 해금 버튼(`UnlockAllCardsButton`)으로 전량 소유 → 모든 헤더가 `N/N`. 되돌린 뒤 팩 1회 개봉 → 해당 테마 진행도만 증가
- **V3 아코디언**
  - 첫 테마 펼침 → 아래 행들이 밀려나고, 탭한 헤더는 제자리
  - 두 번째 테마 펼침 → 첫 번째가 자동으로 접힘
  - 같은 헤더 재탭 → 접힘
  - 마지막 테마를 펼쳤다 접기 → 스크롤이 튀지 않음(Movement Type = Clamped)
  - **카드 수가 4의 배수가 아닌 테마**(예: 9장)에서 마지막 줄이 잘리지 않는지 ★ Fixed Column Count 높이 산출 검증
  - 콘솔에 레이아웃 경고 0건
- **V4 슬롯 표현** — 미소유는 번호만(`001` 형식), 소유 카드는 **그리드 탭의 같은 카드와 나란히 놓고 겉모습 대조**(글자 크기 포함)
- **V5 실시간 반영** — 테마 뷰를 열어둔 채 팩 개봉 → 도감 복귀 시 슬롯이 번호→카드로 바뀌고 헤더 진행도 상승. **접혀 있던 테마**도 다시 펼쳤을 때 최신 상태
- **V6 상태 유지** — 테마 모드 + 3번째 테마 펼침 + 중간 스크롤 → 덱 탭 갔다 도감 복귀 → 세 상태 그대로 + 진행도만 갱신
- **V7 롱프레스** — 소유 카드 롱프레스 → 상세 오버레이, 좌우 넘기기가 **그 테마의 001→002→… 순서**를 따름. 미소유 슬롯은 무반응(의도)
- **V8 회귀** — 기존 도감 생산 흐름(`CollectionTest.unity`의 수확/일괄수령) 스모크 1회. 코드 접점이 `BootInstaller` 한 줄뿐이라 가볍게 확인만

---

## 7. 실기에서 틀어질 수 있는 3가지

설계 단계에서 코드로 확정할 수 없었던 항목이다. 어긋나면 아래 대응을 쓴다.

**① `Toggle_Switch_Light.prefab`에 Toggle이 이미 붙어 있는 경우**
→ Add Component 하지 말고 기존 것을 쓴다. `On Value Changed`에 배선된 게 있으면 비운다.

**② 펼친 높이가 안 맞는 경우** (Fixed Column Count 전제가 깨질 때)
→ 폴백: `CollectionThemeRowView`가 `body`의 GridLayoutGroup에서 `cellSize`/`spacing`/`padding`을 읽어 `rows = ceil(카드수 / constraintCount)`로 높이를 계산해 행 루트 `LayoutElement.preferredHeight`에 직접 넣고, ContentSizeFitter 2개를 제거하는 방식. **코드 수정이 필요하니 이 경우 알려줄 것.**

**③ 펼친 행으로 스크롤할 때 반대 방향으로 튀는 경우**
→ `CollectionThemeListController.ScrollToRow`의 부호가 Content pivot y=1 전제로 짜여 있다. Content pivot을 위 표대로 `(0.5, 1)`로 맞추면 해결된다. 그래도 틀리면 `-t_row.anchoredPosition.y`의 부호 반전이 필요하다(코드 수정).

**④ (부수) 스크롤 관성 중 헤더를 누르면 위치가 겨루는 경우**
→ `ScrollToRow` 앞에 `scrollRect.StopMovement()` 한 줄 추가로 해결된다(현재는 넣지 않았다).
