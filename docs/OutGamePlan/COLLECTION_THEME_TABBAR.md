# 도감 상단 테마 탭바 (Tab_Collection)

수집형 도감처럼 **상단 탭으로 테마를 골라 그리드를 갈아끼우는** 구조. 지금 단계는 **와이어프레임까지**다
(레퍼런스: Anime TCG Merge Battle 컬렉션 화면 상단바).

## 지금 들어간 것 — 와이어프레임

`Assets/Assets/Prefabs/UI/LobbyUI/Tabs/Tab_Collection.prefab`

```
Tab_Collection                      Image + VerticalLayoutGroup (높이를 비율로 나눈다)
├── Panel_ThemeBar        [신규]    LayoutElement.flexibleHeight = 11
│   ├── ChapterLabel                TMP "01" + LayoutElement(폭 120 고정)
│   └── TabStrip                    LayoutElement(flexibleWidth 1) + HorizontalLayoutGroup(균등분할)
│       ├── ThemeTab_All            Image + Button + TabButtonView   ← 선택 상태로 저작
│       └── ThemeTab_01 … _06       (테마 목업 6개)
│           ├── Icon                Image + AspectRatioFitter(HeightControlsWidth, 1:1)
│           ├── Label               TMP (아이콘 탭에서는 off)
│           └── SelectedMark        하단 인디케이터 (비선택 탭에서는 off)
└── Panel_Grid            [내부불변] LayoutElement.flexibleHeight = 89
```

**상단바 : 그리드 비율은 인스펙터 숫자 두 개다.** 루트 VerticalLayoutGroup이 높이를 나누고,
두 패널의 `LayoutElement.flexibleHeight`(11 / 89)가 그 배분 비율이 된다. 둘 다 `minHeight`·
`preferredHeight`를 0으로 뒀기 때문에 **남는 높이 전부가 flexible 비율로만 갈린다** — 합이 100이라
두 숫자를 퍼센트로 읽으면 된다(11:89 → 194.5 : 1574, 20:80 → 353.7 : 1414.9). 해상도가 바뀌어도 비율은 유지된다.

> 루트 그룹의 `ChildForceExpandHeight`는 **꺼둬야 한다.** 켜면 UGUI가 각 자식의 flexible을 최소 1로
> 끌어올려 배분식에 개입한다. 상단바를 픽셀 고정으로 바꾸고 싶으면 그쪽 `flexibleHeight`를 0으로,
> `preferredHeight`를 원하는 픽셀로 주면 된다(그리드는 flexible이라 나머지를 먹는다).

**바 안쪽은 좌표를 박지 않는다.** 탭 폭은 `TabStrip`의 HorizontalLayoutGroup(`ChildForceExpandWidth`)이
균등분할하므로 탭 개수를 늘리거나 줄여도 알아서 나뉜다. 아이콘은 세로 앵커(18%~82%)로 높이를 잡고
AspectRatioFitter가 1:1 폭을 구동하므로 탭이 좁아져도 찌그러지지 않는다. 1080×1768 기준 실측:
바 194.5 / 그리드 1574 / 탭 119.4×150.5 / 아이콘 96.3². 탭이 4개면 각 216.5로 자동 확장된다.

조정 포인트 — 바:그리드 비율은 위 분할점 0.89,
좌측 "01" 폭은 `ChapterLabel > LayoutElement.preferredWidth`,
탭 간격은 `TabStrip > HorizontalLayoutGroup.spacing` 한 곳씩만 만지면 된다.

선택/비선택 겉모습은 **기존 `TabButtonView`** 가 소유한다(배경색·아이콘 틴트·하단 인디케이터).
새 뷰 컴포넌트를 만들지 않았다.

> `ChapterLabel`("01")은 레퍼런스에 있어 자리만 잡아둔 **미배선 장식**이다. 챕터/페이지 개념이
> 데이터에 없으므로 지금은 아무것도 바인딩하지 않는다. 불필요하면 노드째 지우면 된다.

## 남은 것 — 탭 ↔ 그리드 연결

그리드 배치 변경이 타 세션에서 진행 중이라 **의도적으로 붙이지 않았다.** 붙일 때의 계약만 적어둔다.

- 탭 = `전체` 1개 + `CollectionThemes.Themes` N개. 전체 탭 인덱스는 `-1`로 잡는다.
- 목업 탭 6개는 저작용이다. 런타임 컨트롤러는 `CollectionThemeListController.Build()`와 같은 방식으로
  목업을 지우고 `CollectionThemes.Themes` 수만큼 템플릿을 복제한다. 아이콘은 `CollectionTheme.Icon`,
  없으면 `DisplayName` 앞 두 글자를 `Label`에 넣는다.
- 선택이 바뀌면 그리드는 카드 목록만 갈아낀다 — 전체 탭은 `CardCatalog.All`,
  테마 탭은 `CollectionThemes.Themes[i].Cards`. **셀 크기·열 수·ScrollRect 구조는 그대로 둔다.**
- 탭바는 그리드를 직접 참조하지 않는다(선택 이벤트만 노출). 그리드가 구독하는 방향으로 붙인다.
