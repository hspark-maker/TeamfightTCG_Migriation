using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 모험 경로의 정점 하나(AdventureNode 프리팹 루트에 부착).
// 인덱스만 들고 표시값은 매번 AdventureProgress에서 다시 받는다(스냅샷을 캐싱하면 클리어 후 stale).
public class AdventureNodeView : MonoBehaviour
{
    /// <summary>정점 종류 한 줄. 종류가 늘어도 저작 한 줄만 더하면 된다(CurrencyLook과 같은 관용구).</summary>
    [Serializable]
    public struct KindLook
    {
        public EAdventureNodeKind kind;

        [Tooltip("비우면 그 종류는 표식을 켜지 않는다.")]
        public Sprite badge;
    }

    [Tooltip("원판 한가운데 그림. 이 정점을 깨면 무엇이 오는지를 그림 한 장으로 말한다 —\n" +
             "보상 1건이면 그 재화 아이콘, 여러 건이면 상자다(수량은 어디에도 적지 않는다).")]
    [SerializeField] Image rewardImage;

    [SerializeField] Button tapButton;       // 정점 = 도전 버튼

    [Tooltip("보상이 여러 건일 때 재화 아이콘 대신 놓을 상자. 칸 = 보상 건수다 —\n" +
             "0번 칸이 2건, 1번 칸이 3건이고, 저작한 칸보다 보상이 많으면 마지막 칸이 계속 쓰인다.\n" +
             "비우면 첫 보상의 아이콘이 그대로 선다(상자 미저작으로 원판이 비지 않게).")]
    [SerializeField] Sprite[] multiRewardChests;

    [Header("상태 레이어(선택 — 미배선 시 null 가드)")]
    [Tooltip("상태마다 켜지는 '묶음'이다 — 표식 한 장이 아니라 그 상태에서만 보여야 할 것을 통째로 담는다.\n" +
             "잠김: 어두운 베일 + 자물쇠 / 클리어: 금테 + 체크 배지 / 도전 가능: 포커스 링.")]
    [SerializeField] GameObject lockedMark;      // 잠김(베일 + 자물쇠)
    [SerializeField] GameObject clearedMark;     // 클리어(금테 + 체크)
    [SerializeField] GameObject currentMark;     // 지금 도전할 정점(포커스 링)
    [SerializeField] CanvasGroup canvasGroup;

    [Header("수령 대기(깼지만 미수령)")]
    [Tooltip("선물 등장·대기 흔들림이 미는 대상(보통 Medallion). 비우면 연출 없이 그림만 바뀐다.\n" +
             "초상만 미는 것이 아니라 원판째 밀어야 '정점에 사건이 났다'로 읽힌다.")]
    [SerializeField] RectTransform giftPunchTarget;

    [Tooltip("잠긴 정점의 알파. 무채색화와 병용이라 너무 낮추면 배경에 묻힌다.\n" +
             "클리어는 따로 딤하지 않는다(체크 표식이 상태를 말한다).")]
    [SerializeField] float lockedAlpha = 0.7f;

    [Tooltip("잠긴 정점의 그림을 누를 색. 무채색화만으론 무엇이 걸렸는지가 그대로 보여 미리보기가 성립하지 않는다.\n" +
             "완전한 검정이 아닌 이유는 원판의 윤곽이 남아야 실루엣으로 읽히기 때문이다.")]
    [SerializeField] Color lockedSilhouette = new Color(0.12f, 0.14f, 0.20f, 1f);

    [Header("상태 위계 — 지나온 정점은 물러나고 지금 갈 정점만 나선다")]
    [Tooltip("클리어한 정점의 크기 배율. 후반엔 화면에 금테가 여럿 깔려 지금 갈 곳이 그 사이에 묻힌다 —\n" +
             "금색은 그대로 두고 크기와 빛만 현재 정점에 몰아준다.\n" +
             "루트에 거는 이유: 금테·자물쇠·체크가 원판의 형제라 원판만 줄이면 표식이 따로 논다.")]
    [SerializeField] float clearedScale = 0.86f;

    [Tooltip("도전할 정점의 크기 배율.")]
    [SerializeField] float playableScale = 1.1f;

    [Tooltip("도전할 정점의 원판 발광 호흡(원판의 UIEffect와 같은 오브젝트에 붙인다).\n" +
             "밝기만 오간다 — 자리·크기는 챕터 보스가 쥐고 있어 여기서 겹치면 두 자리가 같은 축으로 뛴다.\n" +
             "켜고 끄는 것은 이 뷰가 enabled로 한다(끄면 부품이 발광을 0으로 되돌린다).\n" +
             "원판의 UIEffect 자체는 끄지 마라 — 잠김 무채색화가 같은 컴포넌트를 toneFilter 축으로 재사용한다.")]
    [SerializeField] UiGlowBlink medallionBlink;

    [Tooltip("챕터 보스의 방사형 광채(원판 뒤). 상시로 돌면서 세기·크기가 함께 숨쉰다.\n" +
             "⚠ 가산 합성을 쓰지 마라 — 지도 배경이 밝은 하늘색이라 가산은 금색을 흰 안개로 밀어낸다.\n" +
             "진한 금색 + 알파 합성이어야 금색으로 읽힌다(캡처로 확인).")]
    [SerializeField] Image finalShine;
    [SerializeField] float shinePulseLow = 0.35f;
    [SerializeField] float shinePulseHigh = 0.65f;

    [Tooltip("챕터 보스의 원형 발광(방사광 뒤). 돌지 않는다 — 도는 빛 한 장만 있으면 회전이 그림째 흔들리는 것으로 읽힌다.\n" +
             "형제 순서가 원판보다 앞(뒤에 깔림)이라 코인 밖으로 삐져나온 만큼만 보인다 —\n" +
             "원판보다 크게 저작할 것.\n" +
             "⚠ 스프라이트는 부드러운 후광(Glow01_225)이어야 한다. Glow_Radial은 빛살 다발이라 방사광에 묻혀 안 보였다.")]
    [SerializeField] Image finalGlow;

    [Tooltip("광채가 한 바퀴 도는 데 걸리는 시간(초). 맥박보다 훨씬 느려야 회전이 배경으로 가라앉는다. 0 이하면 돌지 않는다.")]
    [SerializeField] float finalSpinPeriod = 12f;

    [Tooltip("챕터 보스임을 알리는 \"최종 보상\" 문구 묶음. 해금된 챕터에서 클리어 전까지 켜진다 —\n" +
             "순차로 아직 못 여는 보스에도 뜬다. 그 장의 목표를 미리 말해 주는 자리다.")]
    [SerializeField] GameObject finalLabel;

    [Tooltip("상시 모션이 한 번 오갔다 돌아오는 데 걸리는 시간(초). 광채 맥박과 원판 부유가 이 한 박을 함께 쓴다 —\n" +
             "박이 갈리면 정점 하나가 두 군데서 따로 뛰는 것으로 읽힌다. 0이면 0초짜리 무한 루프가 된다.")]
    [Min(0.1f)]
    [SerializeField] float idleCycle = 1.6f;

    [Tooltip("맥박의 정점에서 광채가 부푸는 배율. 알파만 오가면 밝은 배경에 묻힌다 — 크기가 함께 변해야 맥이 뛴다.")]
    [SerializeField] float shinePulseScale = 1.12f;

    [Tooltip("챕터 보스 원판이 떠오르는 높이(px). 지도에서 자리를 옮기는 것은 이 정점들뿐이라, 빛과 달리 배경에 묻히지 않는다.\n" +
             "미는 축이 y라서 원판을 두고 다투는 스케일 연출들(선물·해금 펀치·도장 반동)과 겹치지 않는다.")]
    [SerializeField] float bobHeight = 6f;

    [Tooltip("발밑 그림자. 클리어한 정점은 낮게 앉아 길의 일부처럼 읽혀야 한다.\n" +
             "부유 중엔 이 그림자가 함께 물러나야 한다 — 고정된 그림자 위에서 원판만 오르내리면 뜨는 것이 아니라 떠는 것이 된다.")]
    [SerializeField] Image shadowImage;
    [SerializeField] float clearedShadowAlpha = 0.2f;

    [Tooltip("가장 높이 떴을 때의 그림자 크기·알파 배율(저작값 대비).")]
    [SerializeField] float bobShadowScale = 0.88f;

    [Range(0f, 1f)]
    [SerializeField] float bobShadowAlpha = 0.6f;

    [Tooltip("클리어한 정점의 그림 색. 채도를 빼면 잠김과 안 갈리므로 밝기만 한 단 낮춘다.")]
    [SerializeField] Color clearedPortraitTint = new Color(0.85f, 0.85f, 0.85f, 1f);

    [Header("클리어 도장 — 수령 직후 1회")]
    [Tooltip("내려꽂히는 배지(ClearedMark의 CheckBadge 그 자체다). 도장을 따로 저작하지 않는 이유는,\n" +
             "꽂힌 뒤 남는 그림이 곧 클리어 표식이어야 하기 때문이다 — 연출이 끝난 화면은 평소 화면과 같아야 한다.")]
    [SerializeField] RectTransform stampBadge;

    [Tooltip("배지 두 장(밑판·체크)을 한 손잡이로 묶는다. 낙하 중 함께 나타나야 한 덩어리로 읽힌다.")]
    [SerializeField] CanvasGroup stampBadgeGroup;

    [Tooltip("착지 프레임에 원판을 물들이는 흰 판.")]
    [SerializeField] Image stampFlash;

    [Tooltip("섬광의 최대 세기. 1에 가까우면 그림이 통째로 지워져 '정점이 사라졌다'로 읽힌다 —\n" +
             "밑그림이 비쳐 보이는 선까지만 올린다.")]
    [Range(0f, 1f)]
    [SerializeField] float stampFlashAlpha = 0.4f;

    [Tooltip("착지에서 밖으로 퍼지며 사라지는 링. 금테 바깥에서 시작해야 한다 —\n" +
             "겹치면 정작 금테가 켜지는 사건을 이 링이 덮어 버린다.")]
    [SerializeField] Image stampImpact;

    [Tooltip("임팩트 링의 최대 세기. 스케일로 키우면 두께도 함께 굵어지니 세기로 눌러 준다.")]
    [Range(0f, 1f)]
    [SerializeField] float stampImpactAlpha = 0.6f;

    [Tooltip("금테. 도장이 꽂히는 프레임에 같이 켜진다 —\n" +
             "미리 켜 두면 이미 깬 정점으로 읽혀 도장이 뒤늦게 붙는 장식이 된다.")]
    [SerializeField] Image stampRim;

    [Tooltip("도장이 떨어지기 시작하는 배율.")]
    [SerializeField] float stampFromScale = 3f;

    [Tooltip("도장이 떨어지는 시간(초). 짧을수록 세게 꽂힌다.")]
    [Min(0.02f)]
    [SerializeField] float stampFallTime = 0.1f;

    [Tooltip("착지 임팩트 링이 퍼지는 배율과 시간(초).")]
    [SerializeField] float stampImpactScale = 1.3f;
    [Min(0.02f)]
    [SerializeField] float stampImpactTime = 0.28f;

    [Header("해금 연출 — 맵 진입 1회")]
    [Tooltip("잠긴 모습으로 멈춰 서 있는 첫 박(초). 이 박이 없으면 무엇이 열렸는지는 못 보고 결과만 남는다.")]
    [SerializeField] float unlockHold = 0.25f;

    [Tooltip("베일이 걷히는 박(초). 자물쇠·딤·실루엣·무채색이 한 박에 함께 물러나야 '열렸다'로 읽힌다.")]
    [SerializeField] float unlockShed = 0.3f;

    [Tooltip("원판이 튀며 자리를 잡는 박(초). 이 박의 첫 프레임에 진실을 다시 세운다 —\n" +
             "연출이 끝난 화면은 연출이 없을 때와 같아야 한다.")]
    [SerializeField] float unlockSettle = 0.3f;

    [Header("정점 종류 표식")]
    [Tooltip("종류 표식이 앉는 칸. 상태 묶음 밖에 두어 잠겨도 보인다 — 잠긴 정점이 말할 수 있는 유일한 정보다.")]
    [SerializeField] Image kindBadge;

    [Tooltip("종류별 표식. 저작되지 않은 종류는 표식을 켜지 않는다(Battle이 그 자리다).\n" +
             "같은 종류가 여러 줄이면 위쪽 줄이 이긴다.")]
    [SerializeField] KindLook[] kindLooks;

    [Tooltip("클리어한 정점의 종류 표식 색. 정복한 보스는 왕관이 금색으로 물든다 —\n" +
             "표식이 상태 묶음 밖에 있어 색이 유일한 표현 수단이다.")]
    [SerializeField] Color clearedKindTint = new Color(1f, 0.761f, 0.29f, 1f);

    // 보상 조회용 공용 버퍼 — 건수와 첫 아이콘만 즉시 읽고 버리므로 뷰마다 리스트를 들 이유가 없다.
    static readonly List<RewardLine> s_rewardBuffer = new List<RewardLine>();

    // 표시 대상 정점. -1 = 미바인딩(Refresh 무시).
    int m_index = -1;

    // 직전에 그린 클리어 여부. null = 아직 한 번도 안 그렸다 — 맵을 여는 첫 Refresh 는 도장을 치지 않는다.
    bool? m_wasCleared;

    // 잠김 무채색화를 되돌릴 자리. null = 지금 색이 살아 있다.
    List<UiGrayscale.Toned> m_toned;

    // 실루엣을 되돌릴 자리. 그림의 저작색이 흰색이라는 보장이 없어 처음 볼 때 받아 둔다.
    Color? m_rewardColor0;

    // 그림자·종류 표식의 저작값. 상태가 풀리면 여기로 돌아간다.
    float? m_shadowAlpha0;
    Vector3 m_shadowScale0 = Vector3.one;
    Color? m_kindColor0;

    // 원판의 저작 자리. 부유가 끝나면 여기로 돌아간다(무한 Yoyo는 아무 자리에서나 죽는다).
    Vector2? m_bobHome;

    // 챕터 보스의 상시 모션(광채 맥박 + 원판 부유 + 그림자 연동)을 한 손잡이에 묶는다.
    // 따로 돌리면 트윈마다 시작 프레임이 갈려 같은 박으로 안 뛴다.
    Sequence m_idleSeq;

    // 광채의 상시 회전. 한 바퀴가 맥박보다 훨씬 길어 같은 시퀀스에 못 넣는다.
    Tween m_shineSpin;

    // 클리어 도장(1회). 도는 동안 Refresh가 정지 상태로 덮지 않게 여기로 확인한다.
    Sequence m_stampSeq;

    // 해금 선언(맵 진입 1회).
    Sequence m_unlockSeq;

    // 잠긴 모습을 일부러 세워 둔 구간. 이 동안의 Refresh는 진실로 덮어 첫 박을 지운다.
    bool m_unlockHold;

    Action<int> m_onTap;

    // 안내 타깃으로 등록된 상태. 남의 등록을 날리지 않으려고 자기 것만 해제한다
    bool m_anchored;

    // 수령 대기의 흔들림(상시).
    Tween m_giftIdle;

    // 그림의 프리팹 저작값. 보상이 0건이거나 아이콘이 비었을 때 원판이 비지 않게 여기로 떨어진다.
    Sprite m_rewardSprite0;

    // 정점 인덱스 배선 + 리스너 1회 등록(재빌드마다 중복 방지).
    public void Bind(int _index, Action<int> _onTap)
    {
        this.m_index = _index;
        this.m_onTap = _onTap;

        if (this.tapButton != null)
        {
            this.tapButton.onClick.RemoveAllListeners();
            this.tapButton.onClick.AddListener(this.OnTapped);
        }

        this.Refresh();
    }

    // 상태 표시 갱신. 진행 통지(OnChanged)마다 맵이 전 정점에 호출한다.
    public void Refresh()
    {
        if (this.m_unlockHold) return;
        if (this.m_index < 0) return;

        AdventureProgress.TryGetNode(this.m_index, out AdventureNodeDef t_node);
        EAdventureNodeState t_state = AdventureProgress.DisplayStateOf(this.m_index);

        // 랭크 잠금은 진행 낙인과 축이 다르다 — 정점 상태에 섞지 않고 여기서 곱한다.
        bool t_rankLocked = AdventureProgress.IsRankLocked(this.m_index);

        bool t_cleared = t_state == EAdventureNodeState.Cleared;

        // 미수령에서 클리어로 넘어온 그 프레임에만 도장이 꽂힌다. 낙관 표시든 서버 확정이든 같은 자리를 지나므로
        // 연출을 태우는 지점이 여기 하나다 — 맵이 정점을 지목해 부를 필요가 없다.
        bool t_justCleared = this.m_wasCleared == false && t_cleared;
        this.m_wasCleared  = t_cleared;

        bool t_playable = t_state == EAdventureNodeState.Playable && !t_rankLocked;
        bool t_gift = t_state == EAdventureNodeState.RewardPending && !t_rankLocked;

        // 잠김 표현의 유일한 조건. 순차로 아직 못 여는 정점은 여기 안 걸려 저작 원색 그대로 선다 —
        // 정점 대부분이 실루엣으로 눌리면 잠김이 말해 주는 정보가 없다.
        bool t_locked = t_rankLocked && !t_cleared;

        // 챕터의 마지막 정점만 지도의 랜드마크로 선다. 깨고 나면 물러난다.
        bool t_final = t_node.kind == EAdventureNodeKind.Elite && !t_rankLocked && !t_cleared;

        this.ApplyRewardIcon();

        // 미수령 정점도 눌러야 한다(진입이 아니라 수령이다) — 그래서 탭 자격은 CanEnter에 선물을 더한 값이다.
        if (this.tapButton != null) this.tapButton.interactable = t_gift || AdventureProgress.CanEnter(this.m_index);
        if (this.lockedMark != null) this.lockedMark.SetActive(t_locked);
        if (this.clearedMark != null) this.clearedMark.SetActive(t_cleared);
        if (this.currentMark != null) this.currentMark.SetActive(t_playable);
        if (this.canvasGroup != null) this.canvasGroup.alpha = t_locked ? this.lockedAlpha : 1f;

        this.ApplyKind(t_node.kind, t_cleared);
        this.ApplyGift(t_gift);
        this.ApplyRewardTone(t_locked, t_cleared);
        this.ApplyLockedTone(t_locked);
        this.ApplyStateScale(t_cleared, t_playable || t_gift);
        this.ApplyStampRest(t_cleared);
        this.ApplyPlayableBlink(t_playable || t_gift);
        this.ApplyFinalMark(t_final);
        this.ApplyIdleMotion(t_final);
        this.ApplyShadow(t_cleared);

        if (t_justCleared) this.PlayClearStamp();
    }

    // 원판 한 자리. 이 정점을 깨면 무엇이 오는지를 그림으로 말한다. 상태가 갈려도 그림은 그대로다 —
    // 수령 대기까지 그림을 바꾸면 원판이 상태마다 다른 물건이 되어 한 벌로 안 읽힌다.
    // 그 상태가 말할 것은 그림이 아니라 움직임(등장 + 흔들림)이 맡는다.
    void ApplyRewardIcon()
    {
        if (this.rewardImage == null) return;

        // 프리팹 저작값은 처음 볼 때 받아 둔다 — 보상이 없는 정점이 여기로 떨어진다.
        if (this.m_rewardSprite0 == null) this.m_rewardSprite0 = this.rewardImage.sprite;

        Sprite t_icon = this.RewardSprite();
        if (t_icon != null) this.rewardImage.sprite = t_icon;
    }

    // 보상 1건이면 그 재화 아이콘, 여러 건이면 상자. 수량은 어디에도 적지 않는다 —
    // 정점이 답해야 하는 것은 "무엇이 걸렸나"까지고, 얼마인지는 눌러서 여는 화면의 몫이다.
    Sprite RewardSprite()
    {
        AdventureProgress.FillRewards(this.m_index, s_rewardBuffer);

        if (s_rewardBuffer.Count == 0) return this.m_rewardSprite0;

        Sprite t_first = s_rewardBuffer[0].Icon;
        if (s_rewardBuffer.Count == 1) return t_first != null ? t_first : this.m_rewardSprite0;

        Sprite t_chest = this.ChestOf(s_rewardBuffer.Count);
        return t_chest != null ? t_chest : t_first != null ? t_first : this.m_rewardSprite0;
    }

    // 칸 = 보상 건수(0번 칸이 2건). 저작한 칸보다 보상이 많으면 마지막 칸이 계속 쓰인다 —
    // 보상을 한 건 더 얹었다고 원판이 비면 저작 실수가 화면에서 사고가 된다.
    Sprite ChestOf(int _count)
    {
        if (this.multiRewardChests == null || this.multiRewardChests.Length == 0) return null;

        int t_slot = Mathf.Clamp(_count - 2, 0, this.multiRewardChests.Length - 1);
        return this.multiRewardChests[t_slot];
    }

    // 종류 표식. 상태보다 먼저 세운다 — 잠김 무채색화가 이 표식까지 함께 덮어야 한 덩어리로 읽힌다.
    void ApplyKind(EAdventureNodeKind _kind, bool _cleared)
    {
        if (this.kindBadge == null) return;

        if (this.m_kindColor0 == null) this.m_kindColor0 = this.kindBadge.color;

        Sprite t_badge = this.BadgeOf(_kind);
        this.kindBadge.gameObject.SetActive(t_badge != null);
        if (t_badge != null) this.kindBadge.sprite = t_badge;

        this.kindBadge.color = _cleared ? this.clearedKindTint : this.m_kindColor0.Value;
    }

    Sprite BadgeOf(EAdventureNodeKind _kind)
    {
        if (this.kindLooks == null) return null;

        for (int t_i = 0; t_i < this.kindLooks.Length; t_i++)
            if (this.kindLooks[t_i].kind == _kind) return this.kindLooks[t_i].badge;

        return null;
    }

    /// <summary>이 정점을 튜토리얼 안내 타깃으로 켜고 끈다. 지목은 맵이 소유한다 — 정점은 스스로 켜지 않는다.</summary>
    public void ApplyTutorialAnchor(bool _on)
    {
        if (_on == this.m_anchored) return;
        this.m_anchored = _on;

        var t_rect = this.tapButton != null ? this.tapButton.transform as RectTransform : null;
        if (t_rect == null) return;

        if (_on) TutorialAnchorRegistry.Register(EOutgameTutorialAnchor.AdventureNode, t_rect, this.tapButton);
        else     TutorialAnchorRegistry.Unregister(EOutgameTutorialAnchor.AdventureNode, t_rect);
    }

    /// <summary>해금 직후 한 박 튄다(다음 정점이 열렸다는 신호).</summary>
    public void PlayUnlockPunch()
    {
        // 루트가 아니라 원판을 민다 — 루트 스케일은 상태 위계가 쥐고 있어, 여기서 함께 만지면
        // 펀치가 끝난 자리에 잘못된 크기가 굳는다(해금된 정점은 그 프레임에 커져야 하는 쪽이다).
        // 루트로 폴백하지 않는다 — 루트 스케일은 상태 위계가 쥐고 있어 손대면 그 배율이 1로 굳는다.
        // 펀치는 사슬의 마지막 사건이라 되돌려 줄 Refresh가 뒤에 없다. 원판이 없으면 그냥 접는다.
        RectTransform t_rect = this.giftPunchTarget;
        if (t_rect == null) return;

        // 이 대상은 선물 흔들림(무한 Yoyo)과 자리를 공유한다. DOTween은 무한 루프를 complete로 못 끝내
        // 커진 자리에서 그대로 죽으므로, 시작값을 손으로 세우지 않으면 그 배율이 펀치에 눌어붙는다.
        t_rect.DOKill(true);
        t_rect.localScale = Vector3.one;

        t_rect.DOPunchScale(Vector3.one * 0.15f, 0.35f, 8, 0.6f).SetLink(this.gameObject);
    }

    /// <summary>해금 무대에 세운다 — 잠긴 모습으로 굳히고 진실 갱신을 막는다. 재생 없이 세우기만 한다.</summary>
    public void StageUnlockLocked()
    {
        // 맵을 이미 떠났다면 그림만 진실로 남는다 — 꺼진 오브젝트 위에 무대를 세우지 않는다.
        if (!this.isActiveAndEnabled) return;
        if (this.m_index < 0) return;

        // 이미 선 무대는 다시 세우지 않는다 — 도는 트윈의 시작값을 덮으면 그 프레임이 튄다.
        if (this.m_unlockHold) return;

        // 해금은 진실을 거슬러 잠긴 모습을 세우는 자리다 — 그동안의 Refresh를 막지 않으면 첫 박이 지워진다.
        this.m_unlockHold = true;
        this.HoldTapInput(true);
        this.ApplyUnlockLocked();
    }

    /// <summary>해금 안무의 총 길이(초). 재생하기 전에 물어야 하는 자리가 있다 —
    /// 사슬(AdventureMapOverlayView)의 대기 길이가 이 안무를 품어야 시작하자마자 걷히지 않는다.</summary>
    public float UnlockRevealDuration(bool _immediate = false)
        => (_immediate ? 0f : Mathf.Max(0f, this.unlockHold))
           + Mathf.Max(0.01f, this.unlockShed) + Mathf.Max(0.01f, this.unlockSettle);

    /// <summary>정점 해금(맵 진입 1회). 잠긴 모습을 한 박 보여준 뒤 베일이 걷히고 원판이 튄다. 총 길이를 돌려준다.</summary>
    /// <param name="_immediate">잠긴 모습을 보여주는 첫 박을 건너뛴다. 장이 열리며 정점들이 차례로 풀리는
    /// 계단에서 쓴다 — 띠 안무가 이미 "이 장이 잠겨 있었다"를 말했으므로 여기서 또 멈추면
    /// 그 정점만 제 차례에서 뒤처져 혼자 늦게 열리는 것으로 읽힌다.</param>
    public float PlayUnlockReveal(bool _immediate = false)
    {
        // 맵을 이미 떠났다면 그림만 진실로 남는다 — 꺼진 오브젝트 위에서 시퀀스를 돌리지 않는다.
        if (!this.isActiveAndEnabled) return 0f;
        if (this.m_index < 0) return 0f;

        // 걷는 것은 이전 안무뿐이다 — 미리 세워 둔 무대까지 되돌리면 진실이 한 프레임 새어 첫 박이 무너진다.
        this.KillUnlockSeq();

        // 미리 세워 두지 않았으면 여기서 세운다(멱등이라 이미 선 무대는 그대로 이어받는다).
        this.StageUnlockLocked();

        float t_shed   = Mathf.Max(0.01f, this.unlockShed);
        float t_settle = Mathf.Max(0.01f, this.unlockSettle);
        float t_shedAt = _immediate ? 0f : Mathf.Max(0f, this.unlockHold);

        // 무대를 이어받는 경우 앞선 안무가 중간값을 남겼을 수 있어, 걷을 것들의 시작값을 손으로 세운다.
        this.SetTonedIntensity(1f);
        this.ResetUnlockRing();

        // 베일은 자물쇠 묶음에 얹힌 CanvasGroup이 쥔다. 없으면 그 자리에서 즉시 걷는다(장식 축이라 부드러움만 빠진다).
        CanvasGroup t_veil = this.lockedMark != null ? this.lockedMark.GetComponent<CanvasGroup>() : null;
        if (t_veil != null) t_veil.alpha = 1f;

        Sequence t_seq = DOTween.Sequence().SetLink(this.gameObject);

        if (t_veil != null) t_seq.Insert(t_shedAt, t_veil.DOFade(0f, t_shed).SetEase(Ease.OutQuad));
        t_seq.InsertCallback(t_shedAt + (t_veil != null ? t_shed : 0f), this.ShedUnlockVeil);

        if (this.canvasGroup != null) t_seq.Insert(t_shedAt, this.canvasGroup.DOFade(1f, t_shed));

        // 실루엣이 원색으로 돌아온다 — 이 정점이 무엇을 걸고 있는지가 여기서 처음 보인다.
        if (this.rewardImage != null && this.m_rewardColor0 != null)
            t_seq.Insert(t_shedAt, this.rewardImage.DOColor(this.m_rewardColor0.Value, t_shed).SetEase(Ease.OutQuad));

        // 무채색은 손잡이를 걷어 되돌리면 한 프레임에 튀므로, 세기만 내려 두고 저작값 복원은 마지막 박에 맡긴다.
        t_seq.Insert(t_shedAt, DOVirtual.Float(1f, 0f, t_shed, this.SetTonedIntensity));

        this.InsertUnlockRing(t_seq, t_shedAt);

        t_seq.InsertCallback(t_shedAt + t_shed, this.SettleUnlock);

        this.m_unlockSeq = t_seq;
        t_seq.OnComplete(() =>
        {
            this.m_unlockSeq = null;
            this.ResetUnlockRing();
        });

        return t_shedAt + t_shed + t_settle;
    }

    /// <summary>해금 연출을 어디서 끊겨도 진실로 스냅시킨다(맵을 떠나거나 다시 그려질 때).</summary>
    public void AbortUnlockReveal()
    {
        // 손잡이 상태와 무관하게 먼저 되돌린다 — 여기 아래로 내려가면 이른 return 하나가 정점을 영영 못 누르게 만든다.
        this.HoldTapInput(false);

        // 손잡이 둘 중 하나만 서 있어도 내려간다 — 재생 없이 세우기만 한 무대(시퀀스 없음 + hold 참)가 그 경우다.
        if (this.m_unlockSeq == null && !this.m_unlockHold) return;

        this.KillUnlockSeq();

        this.ResetUnlockRing();
        this.ShedUnlockVeil();
        this.RestoreLockedTone();

        this.m_unlockHold = false;
        this.Refresh();
    }

    /// <summary>도는 해금 안무를 결말까지 당긴다. 당길 것이 있었으면 true.</summary>
    public bool RequestSkipReveal()
    {
        Sequence t_seq = this.m_unlockSeq;
        if (t_seq == null || !t_seq.IsActive()) return false;

        // 중첩까지 완료시켜야 SettleUnlock이 실제로 돌아 Refresh가 진실을 그린다 — Kill과 갈리는 자리가 여기다.
        t_seq.Complete(true);

        // 그 SettleUnlock이 탭 버튼을 되살리는데, 당기기는 사슬 한복판이라 아직 되살릴 때가 아니다.
        // 되살아난 버튼이 다음 스킵 탭을 삼켜 "탭 한 번에 대상 하나"가 그 자리에서 깨진다.
        // 사슬이 끝나면 AbortUnlockReveal이 첫 줄에서 되돌린다(ReleaseAllStaged 경유).
        this.HoldTapInput(true);

        return true;
    }

    void OnDisable()
    {
        // 꺼진 정점을 가리키는 등록이 남으면 안내 손가락이 화면 밖을 짚는다
        this.ApplyTutorialAnchor(false);

        // 반쯤 걷힌 베일이 굳으면 다음에 맵을 열었을 때 이미 열린 정점이 잠겨 보인다.
        // 뒤의 정리들보다 먼저 둔다 — 진실 스냅이 켜는 상시 모션을 그 줄들이 이어서 걷는다.
        this.AbortUnlockReveal();

        // 끊긴 연출이 버튼을 내린 채 굳으면 다음에 맵을 열었을 때 그 정점만 영영 안 눌린다.
        this.HoldTapInput(false);

        this.KillGiftTweens();
        this.KillIdleMotion();

        // 반쯤 떨어진 도장이 그대로 굳으면 다음에 맵을 열었을 때 배지가 3배로 떠 있다.
        this.KillStampTweens();
        if (this.m_index >= 0 && AdventureProgress.DisplayStateOf(this.m_index) == EAdventureNodeState.Cleared)
            this.SetStampSettled(true);
    }

    // 상태별 크기. 클리어는 물러나고 지금 누를 정점만 나선다 — 진행 지도에서 가장 싸고 강한 위계 축이다.
    // 수령 대기도 나서는 쪽이다: 그 구간엔 도전 가능한 정점이 하나도 없어(다음 정점은 아직 잠겨 있다)
    // 여기서 빼면 정작 눌러야 할 정점이 잠긴 정점과 같은 크기로 선다.
    //
    // 트윈은 원판(giftPunchTarget)이 쥐고 루트는 상태만 쥔다 — 축을 갈라 두어 여기서 값 비교만으로 족하다.
    void ApplyStateScale(bool _cleared, bool _forward)
    {
        float t_scale = _cleared ? this.clearedScale : _forward ? this.playableScale : 1f;

        var t_root = (RectTransform)this.transform;
        if (Mathf.Approximately(t_root.localScale.x, t_scale)) return;

        t_root.localScale = Vector3.one * t_scale;
    }

    // 도전할 정점의 원판을 밝힌다. 24정점 중 이 자리만 밝기가 오가면 "지금 여기"는 집힌다.
    void ApplyPlayableBlink(bool _on)
    {
        if (this.medallionBlink == null) return;

        // 부품만 끈다 — UIEffect를 끄면 잠김 무채색화가 같은 컴포넌트를 못 쓴다(부품이 발광을 0으로 되돌린다).
        this.medallionBlink.enabled = _on;
    }

    // 챕터 보스의 랜드마크(광채 + 문구). 광채의 맥박과 회전은 상시 모션이 이어서 세운다.
    void ApplyFinalMark(bool _final)
    {
        if (this.finalLabel != null) this.finalLabel.SetActive(_final);
        if (this.finalShine != null) this.finalShine.gameObject.SetActive(_final);
        if (this.finalGlow != null) this.finalGlow.gameObject.SetActive(_final);
    }

    // 광채는 한 방향으로만 돈다. 오가는 맥박과 축을 갈라 둬야 "숨쉬는 빛"과 "도는 빛"이 겹쳐 하나로 읽힌다.
    void StartShineSpin()
    {
        if (this.m_shineSpin != null && this.m_shineSpin.IsActive()) return;
        if (this.finalShine == null || this.finalSpinPeriod <= 0f) return;

        RectTransform t_rect = this.finalShine.rectTransform;
        t_rect.localRotation = Quaternion.identity;

        this.m_shineSpin = t_rect
            .DOLocalRotate(new Vector3(0f, 0f, -360f), this.finalSpinPeriod, RotateMode.FastBeyond360)
            .SetEase(Ease.Linear)
            .SetLoops(-1, LoopType.Restart)
            .SetLink(this.gameObject);
    }

    // 상시 회전은 오브젝트를 꺼도 멈추지 않는다 — 손으로 죽이고 각도까지 세워야 다음에 열 때 기울어 있지 않다.
    void KillShineSpin()
    {
        if (this.m_shineSpin != null && this.m_shineSpin.IsActive()) this.m_shineSpin.Kill();
        this.m_shineSpin = null;

        if (this.finalShine != null) this.finalShine.rectTransform.localRotation = Quaternion.identity;
    }

    // 챕터 보스만 상시 움직인다 — 도전할 정점은 발광이 맡고, 움직이는 자리는 그 장의 목표로 남긴다.
    // 광채의 세기·크기와 원판의 높이·그림자를 한 시퀀스에 넣어 같은 박으로 뛰게 한다.
    void ApplyIdleMotion(bool _final)
    {
        if (!_final)
        {
            this.KillIdleMotion();
            return;
        }

        if (this.m_idleSeq != null && this.m_idleSeq.IsActive()) return;

        this.EnsureIdleHome();

        float t_half = this.idleCycle * 0.5f;
        Sequence t_seq = DOTween.Sequence().SetLink(this.gameObject);
        bool t_any = false;

        // 광채 — 세기와 크기가 함께 오른다. 알파만으로는 밝은 배경 위에서 맥이 안 잡힌다.
        if (this.finalShine != null)
        {
            SetAlpha(this.finalShine, this.shinePulseLow);
            this.finalShine.rectTransform.localScale = Vector3.one;

            t_seq.Insert(0f, this.finalShine.DOFade(this.shinePulseHigh, t_half).SetEase(Ease.InOutSine));
            t_seq.Insert(0f, this.finalShine.rectTransform.DOScale(this.shinePulseScale, t_half).SetEase(Ease.InOutSine));
            t_any = true;

            this.StartShineSpin();
        }

        // 원판 — y로만 민다. 이 대상의 스케일은 선물·해금 펀치·도장이 나눠 쥐고 있어 건드리면 안 된다.
        if (this.giftPunchTarget != null && this.bobHeight > 0f && this.m_bobHome != null)
        {
            this.giftPunchTarget.anchoredPosition = this.m_bobHome.Value;

            t_seq.Insert(0f, this.giftPunchTarget
                .DOAnchorPosY(this.m_bobHome.Value.y + this.bobHeight, t_half).SetEase(Ease.InOutSine));
            t_any = true;

            // 그림자가 함께 물러나야 "떠올랐다"가 된다(고정 그림자 위의 상하 운동은 떨림으로 읽힌다).
            if (this.shadowImage != null)
            {
                RectTransform t_shadow = this.shadowImage.rectTransform;
                t_shadow.localScale = this.m_shadowScale0;
                SetAlpha(this.shadowImage, this.m_shadowAlpha0.Value);

                t_seq.Insert(0f, t_shadow.DOScale(this.m_shadowScale0 * this.bobShadowScale, t_half).SetEase(Ease.InOutSine));
                t_seq.Insert(0f, this.shadowImage
                    .DOFade(this.m_shadowAlpha0.Value * this.bobShadowAlpha, t_half).SetEase(Ease.InOutSine));
            }
        }

        // 아무것도 안 물렸으면 빈 시퀀스가 즉시 완료돼 손잡이만 남는다.
        if (!t_any)
        {
            t_seq.Kill();
            return;
        }

        t_seq.SetLoops(-1, LoopType.Yoyo);
        this.m_idleSeq = t_seq;
    }

    // 상시 모션이 되돌릴 자리. 그림자 저작값은 ApplyShadow와 함께 쓴다.
    void EnsureIdleHome()
    {
        if (this.m_bobHome == null && this.giftPunchTarget != null)
            this.m_bobHome = this.giftPunchTarget.anchoredPosition;

        this.EnsureShadowHome();
    }

    void EnsureShadowHome()
    {
        if (this.shadowImage == null || this.m_shadowAlpha0 != null) return;

        this.m_shadowAlpha0 = this.shadowImage.color.a;
        this.m_shadowScale0 = this.shadowImage.rectTransform.localScale;
    }

    // 클리어 도장(1회). 낙관 표시가 서는 프레임에 Refresh 가 스스로 태운다 —
    // 서버 확정은 그보다 뒤라, 여기서 기다리면 팝업이 닫힌 뒤에야 꽂힌다.
    void PlayClearStamp()
    {
        // 맵을 이미 떠났다면 그림만 진실로 남는다 — 꺼진 오브젝트 위에서 시퀀스를 돌리지 않는다.
        if (!this.isActiveAndEnabled) return;
        if (AdventureProgress.DisplayStateOf(this.m_index) != EAdventureNodeState.Cleared) return;
        if (!this.HasStampRig) return;

        this.KillStampTweens();

        // 착지 전까지는 금테도 배지도 없다 — 도장이 그것들을 데려오는 그림이어야 한다.
        this.SetStampSettled(false);

        this.m_stampSeq = DOTween.Sequence().SetLink(this.gameObject);

        // 낙하. 가속(InQuad)이라야 꽂히는 것이고, 감속이면 내려앉는 것이 된다.
        this.m_stampSeq.Insert(0f, this.stampBadge.DOScale(1f, this.stampFallTime).SetEase(Ease.InQuad));
        this.m_stampSeq.Insert(0f, this.stampBadgeGroup.DOFade(1f, this.stampFallTime * 0.6f));

        // 착지 — 한 프레임에 사건을 몰아넣는다(금테 점등 · 흰 섬광 · 퍼지는 링 · 원판 반동).
        float t_land = this.stampFallTime;

        this.m_stampSeq.Insert(t_land, this.stampRim.DOFade(1f, 0.08f).SetEase(Ease.OutQuad));
        this.m_stampSeq.InsertCallback(t_land, () => SoundManager.Instance?.PlayCue(EOutgameSound.AdventureClear));

        this.m_stampSeq.Insert(t_land, this.stampFlash.DOFade(this.stampFlashAlpha, 0.03f));
        this.m_stampSeq.Insert(t_land + 0.03f, this.stampFlash.DOFade(0f, 0.18f).SetEase(Ease.OutQuad));

        this.m_stampSeq.Insert(t_land, this.stampImpact.DOFade(this.stampImpactAlpha, 0.03f));
        this.m_stampSeq.Insert(t_land + 0.03f, this.stampImpact.DOFade(0f, this.stampImpactTime).SetEase(Ease.OutQuad));
        this.m_stampSeq.Insert(t_land, this.stampImpact.rectTransform
            .DOScale(this.stampImpactScale, this.stampImpactTime + 0.03f).SetEase(Ease.OutCubic));

        // 원판째 반동한다 — 배지만 튀면 배지에 사건이 난 것이지 정점에 난 것이 아니다.
        if (this.giftPunchTarget != null)
        {
            this.giftPunchTarget.localScale = Vector3.one;
            this.m_stampSeq.Insert(t_land, this.giftPunchTarget.DOPunchScale(Vector3.one * 0.14f, 0.26f, 9, 0.7f));
        }

        this.m_stampSeq.OnComplete(() =>
        {
            this.m_stampSeq = null;
            this.SetStampSettled(true);
        });
    }

    // 도장이 돌지 않을 때의 클리어 표식은 늘 "꽂힌 뒤" 모습이다 — 맵을 다시 열어도 도장이 재생되지 않는다.
    void ApplyStampRest(bool _cleared)
    {
        if (!_cleared || !this.HasStampRig) return;
        if (this.m_stampSeq != null && this.m_stampSeq.IsActive()) return;

        this.SetStampSettled(true);
    }

    // 도장 리그의 두 극. 연출이 어디서 끊겨도 이 둘 중 하나로만 굳는다.
    void SetStampSettled(bool _settled)
    {
        if (!this.HasStampRig) return;

        this.stampBadge.localScale = Vector3.one * (_settled ? 1f : this.stampFromScale);
        this.stampBadgeGroup.alpha = _settled ? 1f : 0f;

        SetAlpha(this.stampRim, _settled ? 1f : 0f);
        SetAlpha(this.stampFlash, 0f);
        SetAlpha(this.stampImpact, 0f);

        this.stampImpact.rectTransform.localScale = Vector3.one;
    }

    bool HasStampRig => this.stampBadge != null && this.stampBadgeGroup != null
                        && this.stampFlash != null && this.stampImpact != null && this.stampRim != null;

    static void SetAlpha(Image _image, float _alpha)
    {
        Color t_color = _image.color;
        t_color.a = _alpha;
        _image.color = t_color;
    }

    void KillStampTweens()
    {
        if (this.m_stampSeq != null && this.m_stampSeq.IsActive()) this.m_stampSeq.Kill();
        this.m_stampSeq = null;
    }

    // 무한 Yoyo는 완료로 끝나지 않아 아무 자리에서나 죽는다 — 뜬 원판·부푼 광채·줄어든 그림자를 손으로 세운다.
    void KillIdleMotion()
    {
        // 회전은 시퀀스 밖에서 도니 시퀀스가 없어도 먼저 걷는다.
        this.KillShineSpin();

        if (this.m_idleSeq == null) return;

        if (this.m_idleSeq.IsActive()) this.m_idleSeq.Kill();
        this.m_idleSeq = null;

        if (this.finalShine != null) this.finalShine.rectTransform.localScale = Vector3.one;
        if (this.giftPunchTarget != null && this.m_bobHome != null) this.giftPunchTarget.anchoredPosition = this.m_bobHome.Value;

        if (this.shadowImage == null) return;

        this.shadowImage.rectTransform.localScale = this.m_shadowScale0;
        if (this.m_shadowAlpha0 != null) SetAlpha(this.shadowImage, this.m_shadowAlpha0.Value);
    }

    // 클리어한 정점은 그림자를 낮춰 바닥에 앉힌다. 크기 축과 같은 말을 그림자로 한 번 더 한다.
    void ApplyShadow(bool _cleared)
    {
        if (this.shadowImage == null) return;

        this.EnsureShadowHome();

        // 부유가 도는 중이면 그림자는 그쪽이 쥔다 — 여기서 덮으면 매 Refresh마다 한 프레임이 튄다.
        if (this.m_idleSeq != null && this.m_idleSeq.IsActive()) return;

        SetAlpha(this.shadowImage, _cleared ? this.clearedShadowAlpha : this.m_shadowAlpha0.Value);
    }

    // 수령 대기의 상시 상태(흔들림). 1회짜리 등장 연출은 두지 않는다 —
    // 복귀 직후의 보상은 팝업이 곧바로 말하고, 이 흔들림은 그것을 놓친 정점을 가리키는 자리다.
    void ApplyGift(bool _gift)
    {
        if (!_gift)
        {
            this.KillGiftTweens();
            return;
        }

        this.StartGiftIdle();
    }

    void StartGiftIdle()
    {
        if (this.giftPunchTarget == null) return;
        if (this.m_giftIdle != null && this.m_giftIdle.IsActive()) return;

        this.giftPunchTarget.localScale = Vector3.one;

        this.m_giftIdle = this.giftPunchTarget
            .DOScale(1.06f, 0.7f)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo)
            .SetLink(this.gameObject);
    }

    void KillGiftTweens()
    {
        if (this.m_giftIdle != null && this.m_giftIdle.IsActive()) this.m_giftIdle.Kill();

        this.m_giftIdle = null;

        // 도장 반동이 같은 원판을 쥐고 있다 — 여기서 되돌리면 그 한 프레임이 튄다.
        // (도장이 도는 정점은 Cleared라 애초에 선물 흔들림이 없다.)
        if (this.m_stampSeq != null && this.m_stampSeq.IsActive()) return;

        // 원판을 되돌린다 — 흔들림이 중간에 끊기면 커진 채로 굳는다.
        if (this.giftPunchTarget != null) this.giftPunchTarget.localScale = Vector3.one;
    }

    // 잠긴 정점의 그림을 눌러 실루엣으로 만든다. 무채색화는 채도만 빼서 무엇이 걸렸는지 그대로 읽히는데,
    // 잠긴 정점이 말해야 하는 건 "잠겼다"가 아니라 "아직 볼 것이 아니다"다.
    // 클리어는 밝기만 한 단 내린다 — 채도까지 빼면 잠김과 같은 자리로 떨어진다.
    void ApplyRewardTone(bool _locked, bool _cleared)
    {
        if (this.rewardImage == null) return;

        if (this.m_rewardColor0 == null) this.m_rewardColor0 = this.rewardImage.color;

        this.rewardImage.color = _locked  ? this.lockedSilhouette
                               : _cleared ? this.clearedPortraitTint
                                          : this.m_rewardColor0.Value;
    }

    // 잠긴 정점은 딤만으로 안 갈린다 — 비활성 버튼과 같은 축으로 노드 전체의 채도를 뺀다.
    // 자물쇠 묶음은 제외한다(잠김을 말하는 표식이 저 혼자 회색이면 읽히지 않는다).
    // 종류 표식도 제외한다 — 실루엣이 "누구인지"를 감춘 자리에서 이것만이 앞을 말해 주는 정보다.
    void ApplyLockedTone(bool _locked)
    {
        if (_locked)
        {
            if (this.m_toned != null) return;
            this.m_toned = UiGrayscale.Apply(this.gameObject,
                this.lockedMark != null ? this.lockedMark.transform : null,
                this.kindBadge != null ? this.kindBadge.transform : null);
        }
        else
        {
            this.RestoreLockedTone();
        }
    }

    // 저작값 복원이라 손잡이를 비워야 다음 잠김이 다시 걸린다(Restore는 목록도 비운다).
    void RestoreLockedTone()
    {
        if (this.m_toned == null) return;

        UiGrayscale.Restore(this.m_toned);
        this.m_toned = null;
    }

    // 걷히는 도중의 무채색. 손잡이는 그대로 두고 세기만 오간다.
    void SetTonedIntensity(float _intensity)
    {
        if (this.m_toned == null) return;

        for (int t_i = 0; t_i < this.m_toned.Count; t_i++)
        {
            var t_fx = this.m_toned[t_i].Effect;
            if (t_fx == null) continue;

            t_fx.toneIntensity = _intensity;
        }
    }

    // 해금의 첫 박. 진실은 이미 열려 있으므로 잠긴 모습을 손으로 세운다 —
    // Refresh의 꼬리와 같은 순서·같은 부품을 쓴다(그림이 갈리면 걷히는 것이 잠김으로 안 읽힌다).
    void ApplyUnlockLocked()
    {
        AdventureProgress.TryGetNode(this.m_index, out AdventureNodeDef t_node);

        this.ApplyRewardIcon();

        if (this.tapButton != null) this.tapButton.interactable = false;
        if (this.lockedMark != null) this.lockedMark.SetActive(true);
        if (this.clearedMark != null) this.clearedMark.SetActive(false);
        if (this.currentMark != null) this.currentMark.SetActive(false);
        if (this.canvasGroup != null) this.canvasGroup.alpha = this.lockedAlpha;

        this.ApplyKind(t_node.kind, false);
        this.ApplyGift(false);
        this.ApplyRewardTone(true, false);
        this.ApplyLockedTone(true);
        this.ApplyStateScale(false, false);
        this.ApplyPlayableBlink(false);
        this.ApplyFinalMark(false);
        this.ApplyIdleMotion(false);
        this.ApplyShadow(false);

        this.ResetUnlockRing();
    }

    // 연출이 도는 동안 이 정점의 클릭을 통째로 흘려보낸다. interactable 로는 안 되는 이유는,
    // uGUI가 컴포넌트의 isActiveAndEnabled만 보고 이벤트를 넘길지 정해 비활성 버튼도 클릭을 삼키기 때문이다 —
    // 해금 연출이 화면 한가운데 세워 둔 바로 그 정점에서만 맵의 스킵 탭이 죽는다.
    // interactable 축은 Refresh가 계속 소유한다(여기서 만지면 두 축이 서로를 덮는다).
    void HoldTapInput(bool _held)
    {
        if (this.tapButton == null) return;

        this.tapButton.enabled = !_held;
    }

    // 도장 링을 해금 링으로 다시 쓴다 — 밖으로 퍼지며 사라지는 그림 하나면 "열렸다"의 밀도가 한 눈금 오른다.
    void InsertUnlockRing(Sequence _seq, float _at)
    {
        if (this.stampImpact == null) return;

        _seq.Insert(_at, this.stampImpact.DOFade(this.stampImpactAlpha, 0.03f));
        _seq.Insert(_at + 0.03f, this.stampImpact.DOFade(0f, this.stampImpactTime).SetEase(Ease.OutQuad));
        _seq.Insert(_at, this.stampImpact.rectTransform
            .DOScale(this.stampImpactScale, this.stampImpactTime + 0.03f).SetEase(Ease.OutCubic));
    }

    // 안무만 걷는다 — 무대(m_unlockHold)는 건드리지 않아 세우기와 재생을 따로 되돌릴 수 있다.
    void KillUnlockSeq()
    {
        if (this.m_unlockSeq != null && this.m_unlockSeq.IsActive()) this.m_unlockSeq.Kill();
        this.m_unlockSeq = null;
    }

    void ResetUnlockRing()
    {
        if (this.stampImpact == null) return;

        // 도장이 도는 중이면 같은 링을 그쪽이 쥔다.
        if (this.m_stampSeq != null && this.m_stampSeq.IsActive()) return;

        SetAlpha(this.stampImpact, 0f);
        this.stampImpact.rectTransform.localScale = Vector3.one;
    }

    // 끄는 프레임에 알파를 함께 세운다 — 걷힌 채로 굳으면 다음에 진짜 잠긴 정점의 베일이 투명하게 선다.
    void ShedUnlockVeil()
    {
        if (this.lockedMark == null) return;

        this.lockedMark.SetActive(false);

        var t_veil = this.lockedMark.GetComponent<CanvasGroup>();
        if (t_veil != null) t_veil.alpha = 1f;
    }

    // 마지막 박은 그리지 않는다 — 진실을 다시 세우고 그 프레임에 한 번 튄다.
    // 끝난 화면이 연출 없을 때와 같음은 이 Refresh 한 번이 산술로 보장한다.
    void SettleUnlock()
    {
        this.m_unlockHold = false;
        this.HoldTapInput(false);
        this.RestoreLockedTone();
        this.Refresh();

        if (this.giftPunchTarget == null) return;

        // 수령 대기 흔들림이 같은 원판을 쥐고 있으면 손대지 않는다 — 그 상태는 흔들림이 스스로 말한다.
        if (this.m_giftIdle != null && this.m_giftIdle.IsActive()) return;

        this.giftPunchTarget.localScale = Vector3.one;
        this.giftPunchTarget
            .DOPunchScale(Vector3.one * 0.15f, Mathf.Max(0.01f, this.unlockSettle), 8, 0.6f)
            .SetLink(this.gameObject);
    }

    // 도전 요청은 맵으로 올린다(정점은 씬 전환을 모른다). 잠김 판정은 맵이 한 번 더 본다.
    void OnTapped()
    {
        if (this.m_index < 0) return;
        SoundManager.Instance?.PlayCue(EOutgameSound.AdventureNodeTap);
        this.m_onTap?.Invoke(this.m_index);
    }
}
