# 랭킹 UI 구현 사전 조사

**Git 상태**: Modified —  
- Assets/Scripts/OutGame/Rank/RankManager.cs
- Assets/Scripts/OutGame/Rank/RankRewardManager.cs
- Assets/Scripts/OutGame/Match/MatchmakingProfile.cs
- Assets/Scripts/OutGame/Match/RankSnapshotResult.cs
- functions/src/commands/getRankSnapshot.ts

**AGENTS.md**: 없음. 하위 지침 미존재.

---

## 1. 기존 구조 분석

### RankRewardPanel.cs (패턴 재사용)
```csharp
public class RankRewardPanel : PooledUIBase
{
    [SerializeField] ScrollRect scrollRect;
    [SerializeField] Transform content;           // 행 컨테이너(VerticalLayoutGroup)
    [SerializeField] RankRewardRowView rowPrefab; // 행 프리팹
    
    readonly List<RankRewardRowView> m_rows = new List<RankRewardRowView>();
    bool m_built;  // 최초 1회만 생성, 이후 Refresh만
    
    public void Open() 
    {
        int t_top = RankRewardManager.TopClaimableIndex;
        this.OpenAt(t_top >= 0 ? t_top : RankManager.GetInfo().TierIndex);
    }
    
    public void Close()
    {
        this.SetVisible(false);
        LobbyShellBars.DropTopAfter(this, this.transition.CloseDuration);
    }
}
```

### RankRewardRowView.cs (행 로직)
```csharp
public class RankRewardRowView : MonoBehaviour
{
    [SerializeField] Image badgeImage;
    [SerializeField] TMP_Text tierNameText;
    [SerializeField] Button rewardBox;
    [SerializeField] CurrencyRewardSlotView[] rewardSlots;
    [SerializeField] GameObject highlight;      // 수령 가능 최상위
    [SerializeField] GameObject claimedMark;   // 수령 완료
    [SerializeField] GameObject lockDim;       // 미달성
    
    int m_tierIndex = -1;
    Action<int> m_onClick;
    
    public void Bind(int _tierIndex, bool _isLast, Action<int> _onClick)
    {
        this.m_tierIndex = _tierIndex;
        this.m_onClick = _onClick;
        // 버튼 리스너 등록
    }
}
```

### RankHud.cs (로비 HUD)
- Image badgeImage (티어 배지)
- TMP_Text descText (티어명 "브론즈 1")
- RankProgressGauge gauge (단계 진행)
- GameObject pipsRoot (별 컨테이너)
- OnEnable/Start에서 렌더 실행, 클릭 배선 없음 (표시 전용)

### UIPoolManager 호출 패턴 (LobbyMatchTabPanel)
```csharp
public void OpenRankRewards() => OpenPooled<RankRewardPanel>();
```

---

## 2. 데이터 API

### RankManager (정적)
- `Points` (long) — 현재 포인트
- `TierIndex` (int) — 현재 등급 인덱스
- `BestTierIndex` (int) — 최고 도달 등급
- `IsRanked` (bool) — 첫 티어 도달 여부
- `IsTierRewardClaimed(tierIndex)` (bool)
- `GetInfo()` → RankInfo (Points, TierIndex, BestTierIndex 포함)
- `AdoptServerProgress(points, seasonId, bestTierIndex, claimedTierIndexes)` — 서버 값 채택

### ProfileManager (정적)
- `Nickname` (string)
- `AvatarId` (string)
- `FrameId` (string)
- `AvatarLarge/Small` (Sprite) — AvatarId로 조회
- `Frame` (Sprite) — FrameId로 조회

### OpponentProfilePool (SO)
- `PickName()` → string (닉네임 샘플)
- `PickAvatar()` → Sprite

### RankSnapshotResult (DTO)
```csharp
internal sealed class RankSnapshotResult : RankProgressResult
{
    [JsonProperty("tierIndex")] public int TierIndex { get; set; }
    [JsonProperty("ticket")] public string Ticket { get; set; }
    // 상속: Points, SeasonId, BestTierIndex, ClaimedTierIndexes
}
```

---

## 3. 프리팹 경로

- **RankRewardOverlay** (오버레이 루트)  
  `Assets/Prefabs/UI/PooledUI/RankRewardOverlay.prefab`  
  → RankRewardPanel 스크립트 부착

- **RankRewardRow** (행 프리팹)  
  `Assets/Prefabs/UI/LobbyUI/RankUI/RankRewardRow.prefab`  
  → RankRewardRowView 스크립트 부착

- **UI_Frame_RankReward** (프레임)  
  `Assets/Prefabs/UI/Common/UI_Frame_RankReward.prefab`

---

## 4. 신규 구현 가이드

### 신규 파일
1. **RankingBoardPanel.cs** — RankRewardPanel 규약 복사
   - PooledUIBase 상속
   - Open/Close 공개
   - RefreshRows (행 동적 생성/갱신)

2. **RankingRowView.cs** — RankRewardRowView 규약 복사
   - Bind(tierIndex, onClick)
   - 표시: rank, playerNickname, tierIndex, points (내/표본만)

### 프리팹 변환 방안
1. **RankRewardRow** 복제 → **RankingRow.prefab**
   - rewardSlots 컴포넌트/자식 제거 또는 비활성
   - 대신 nickname, points, tierDisplay 필드 배치

2. **RankRewardOverlay** 복제 → **RankingBoardOverlay.prefab**
   - content 자식 전부 제거
   - RankingBoardPanel 스크립트 부착 (RankRewardPanel 코드 복사 후 수정)

### 로비 탭 통합
- LobbyMatchTabPanel에 `OpenRanking() => OpenPooled<RankingBoardPanel>();` 추가
- 클릭 배선: 로비 탭 UI 저작 또는 코드 버튼 추가

---

## 5. 홍춍 체크리스트

- [ ] RankingBoardPanel.cs 작성 (RankRewardPanel 기반)
- [ ] RankingRowView.cs 작성 (RankRewardRowView 기반)
- [ ] RankingBoardOverlay.prefab 프리팹 수정
- [ ] RankingRow.prefab 프리팹 수정
- [ ] LobbyMatchTabPanel.OpenRanking() 메서드 추가
- [ ] 로비 UI 탭 버튼 클릭 배선 (로비 프리팹 저작)
- [ ] 테스트: 로비 가려 → 탭 클릭 → 랭킹 표시 (내/상위 샘플)
