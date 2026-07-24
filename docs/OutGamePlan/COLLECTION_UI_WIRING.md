# 도감 생산 UI 손 배선 인계 (F-17/F-18)

> MCP 씬/프리팹 편집 도구가 없어 코드만 커밋됨. 아래 컴포넌트 부착·필드 연결은 에디터에서 손으로 해야 런타임 동작한다.
> 컴포넌트를 붙여도 `[SerializeField]`가 비면 컴파일은 통과하고 런타임에 조용히 no-op(모두 null 가드) 되므로, 표대로 빠짐없이 연결할 것.
>
> 대상 씬: `Assets/Scenes/CollectionTest.unity` (독립 테스트). **실제 통합 씬도 동일 컴포넌트·동일 필드 매핑**을 그대로 적용한다.
> 프리팹: `Assets/Assets/Prefabs/UI/CollectionUI/CollectionRow.prefab`, `Assets/Assets/Prefabs/UI/CollectionUI/Card.prefab`

---

## 1. Row 프리팹 (CollectionRow.prefab) — CollectionRowView 확장

`CollectionRowView`는 이미 이 프리팹 루트에 부착되어 있음. 기존 `cardsContainer`/`cardPrefab` 연결은 유지하고, **생산 표시용 자식 3개를 새로 추가**해 연결한다.

### 추가할 자식 오브젝트 (프리팹 안, 카드 컨테이너 옆 "상태 영역"에 배치)

| 새 자식 | 컴포넌트 | 용도 |
|---|---|---|
| `StateChip` | TMP_Text (TextMeshProUGUI) | 상태칩: "잠김" / "생산 중" / "만땅" |
| `AmountText` | TMP_Text (TextMeshProUGUI) | 누적/상한: "12 / 100" |
| `HarvestButton` | UnityEngine.UI.Button (+자식 TMP_Text 라벨) | 수확 버튼 |

### CollectionRowView 필드 매핑 (프리팹 루트의 컴포넌트 인스펙터)

| 필드 | 연결 대상 | 비고 |
|---|---|---|
| `cardsContainer` | (기존 유지) 카드 타일 부모 | 변경 없음 |
| `cardPrefab` | (기존 유지) Card.prefab | 변경 없음 |
| `stateLabel` | `StateChip` 의 TMP_Text | 선택(비어도 됨, null 가드) |
| `amountText` | `AmountText` 의 TMP_Text | 선택 |
| `harvestButton` | `HarvestButton` 의 Button | 선택. 클릭 리스너는 코드가 `Build`에서 자동 배선 — **인스펙터 OnClick에 수동 등록하지 말 것**(중복 호출됨) |

> 버튼 라벨/오브젝트 활성 토글은 코드가 `interactable`만 제어한다. "잠김일 때 버튼 숨기기"가 필요하면 별도 요청.

---

## 2. 헤더 — CollectionProgressView (진행바)

도감 헤더 GameObject(예: `Header`)에 `CollectionProgressView` 컴포넌트를 부착하고 아래를 연결.

| 필드 | 연결 대상 | 비고 |
|---|---|---|
| `fillImage` | 진행바 Image | **Image Type = Filled** 로 설정해야 fillAmount가 반영된다 |
| `progressText` | "12 / 30" TMP_Text | 선택 |

- 데이터 파생: 분모는 `CardCatalog.Count`, 분자는 `OwnershipManager.OwnedCount`. 하드코딩 숫자 없음.

---

## 3. 푸터 — CollectionCompletionRewardView (완성 보상)

도감 푸터 GameObject(예: `Footer`)에 `CollectionCompletionRewardView` 컴포넌트를 부착하고 아래를 연결.

| 필드 | 연결 대상 | 비고 |
|---|---|---|
| `root` | 완성 배너 GameObject | 수령 가능할 때만 SetActive(true). **root를 컴포넌트가 붙은 오브젝트 자신으로 지정하지 말 것**(OnDisable로 자기 구독이 끊김) — 별도 자식 배너를 지정 |
| `claimButton` | 수령 Button | 클릭 리스너는 OnEnable에서 자동 배선 — 인스펙터 OnClick 수동 등록 금지 |
| `rewardText` | 보상량 TMP_Text | 선택 |

---

## 4. 헤더 — GoldHud (골드 표시, 코드 기존/수정 없음)

헤더의 골드 표시 GameObject(예: `GoldHud`)에 `GoldHud` 컴포넌트를 부착.

| 필드 | 연결 대상 | 비고 |
|---|---|---|
| `goldText` | 골드 수치 TMP_Text | 비우면 Awake가 같은 오브젝트의 TMP_Text를 자동 탐색 |

---

## 5. 컨트롤러 — CollectionGalleryController (기존, 필드 1개 추가)

CollectionScreen에 부착된 기존 컨트롤러 인스펙터에 폴링 간격 필드가 새로 생김.

| 필드 | 값 | 비고 |
|---|---|---|
| `refreshInterval` | 0.5 (기본) | 생산 누적 표시 갱신 주기(초). 시간 누적은 이벤트가 없어 폴링 필요 |
| `content` / `rowPrefab` / `fallbackAllCards` | (기존 유지) | 변경 없음 |

---

## 확인 체크리스트 (에디터)

- [ ] CollectionRow.prefab에 StateChip/AmountText/HarvestButton 자식 추가 후 3개 필드 연결
- [ ] HarvestButton·claimButton의 인스펙터 OnClick 은 **비워둠**(코드가 배선)
- [ ] fillImage 의 Image Type = Filled
- [ ] CompletionRewardView 의 root 는 컴포넌트 오브젝트 자신이 아닌 별도 배너
- [ ] 헤더 GoldHud 오브젝트에 GoldHud 컴포넌트 부착
- [ ] 재생 후: 시간 경과(또는 디버그 시간 점프)로 생산중 행의 누적 텍스트가 0.5초마다 증가, 1 이상에서 수확 버튼 활성, 수확 시 골드 증가 확인
