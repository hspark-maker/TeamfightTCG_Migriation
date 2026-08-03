using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class BattleFieldView : MonoBehaviour
{
    [SerializeField] CardView[] slotViews;  // length must equal BattleField.SLOT_COUNT
    [SerializeField] BattleField field;
    public BattleField Field => this.field;

    // 타이밍은 BattleTimingConfig 단일 진실원(배율 적용).
    float cardDealDelay    => GameTiming.Battle.CardDealDelay;
    float cardDealDuration => GameTiming.Battle.CardDealDuration;

    public void Refresh()
    {
        bool t_hasWaiting = this.field.WaitingCount > 0;
        for (int i = 0; i < BattleField.SLOT_COUNT; i++)
        {
            CardInstance t_card = this.field.GetSlot(i);
            this.slotViews[i].Render(t_card, this.field.Synergy);
            if (t_card == null && !t_hasWaiting)
                this.slotViews[i].HideSlot();
        }

        // 시너지 표시도 같은 갱신 지점에 붙인다 — 스냅샷(field.Synergy)이 바뀌는 시점이 곧 여기다.
        // 패널은 캔버스에 있고 스스로 등록한다(참조 배선 없음) — 없는 씬에선 조용히 무동작.
        FieldSynergyPanel.Show(IsLocalSide, this.field.Synergy);
    }

    /// <summary>이 필드가 로컬 플레이어(화면 아래쪽) 것인가. 표시 위치가 좌우/상하로 갈리는 기준.</summary>
    public bool IsLocalSide => this.field != null && this.field.OwnerIndex == TurnState.LocalOwnerIndex;

    public CardView GetSlotView(int _index) => this.slotViews[_index];

    /// <summary>이 필드의 가운데 자리(월드). 시네마에서 카드가 모이는 지점 —
    /// 화면 중앙(카메라 기준)이 아니라 **필드 격자 기준**이라 화면 비율이 바뀌어도 슬롯과 어긋나지 않는다.
    /// 슬롯 뷰의 배치 좌표(SlotPosition)를 쓴다 — 카드가 연출 중 움직여 있어도 원래 자리 기준.</summary>
    public Vector3 FieldCenter
    {
        get
        {
            CardView t_mid = this.slotViews[BattleField.SLOT_COUNT / 2];
            return t_mid != null ? t_mid.SlotPosition : transform.position;
        }
    }

    /// <summary>이 필드 슬롯 전체를 감싸는 화면 좌표 사각(px). 튜토리얼 포커스 딤이 뚫을 구멍.
    /// 기준은 각 슬롯의 <see cref="CardView.SlotWorldBounds"/> — 카드가 연출 중 움직여 있어도
    /// 구멍이 따라 흔들리지 않는다. 카메라가 없으면 빈 Rect(호출부가 포커스를 접는다).</summary>
    public Rect ScreenBounds(float _paddingPx = 0f)
    {
        Rect t_rect = new Rect();
        foreach (CardView t_view in this.slotViews)
        {
            if (t_view == null) continue;
            t_rect = CameraUtil.Union(t_rect, t_view.ScreenBounds(_paddingPx));
        }
        return t_rect;
    }

    public async UniTask PlayFillAnim(List<CardInstance> _placed)
    {
        if (_placed == null || _placed.Count == 0) return;

        float t_wz = this.slotViews[0].transform.position.z;
        Vector3 t_from = this.field.OwnerIndex == TurnState.LocalOwnerIndex
            ? CameraUtil.ScreenFractionToWorld( 2f, 0f, t_wz)
            : CameraUtil.ScreenFractionToWorld(-1f, 1f, t_wz);

        Vector3 t_mid = CameraUtil.ScreenFractionToWorld(0.5f, 0.5f, t_wz);

        // 배치 전 전원 화면 밖으로 선이동 — 순차 재생 중 아직 안 나온 카드가 슬롯에 먼저 보이지 않게.
        var t_dests = new Vector3[_placed.Count];
        for (int i = 0; i < _placed.Count; i++)
        {
            CardView t_view = this.slotViews[_placed[i].slotIndex];
            t_dests[i] = t_view.transform.position;
            Vector3 t_hide = t_from;
            t_hide.z = t_dests[i].z;
            t_view.transform.position = t_hide;
        }

        // 순차 배치: 한 장이 슬롯에 안착한 뒤 cardDealDelay 만큼 쉬고 다음 장(겹침 없음).
        for (int i = 0; i < _placed.Count; i++)
        {
            if (i > 0) await UniTask.Delay((int)(this.cardDealDelay * 1000));

            CardView t_view = this.slotViews[_placed[i].slotIndex];

            // 등장 순서(중앙 정지 → 컷씬 → 슬롯)는 CardAppearSequence 단독 — 교활 교대 등장과 공유한다.
            await CardAppearSequence.Play(t_view, _placed[i], t_from, t_mid, t_dests[i], this.cardDealDuration);
        }
    }

    public void InitializeAnimators()
    {
        foreach (var t_view in this.slotViews)
            t_view.InitializeAnimator();
    }

    public void ClearAllHighlights()
    {
        foreach (var t_view in this.slotViews)
            t_view.SetHighlight(false);
    }
}
