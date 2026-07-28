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
    }

    public CardView GetSlotView(int _index) => this.slotViews[_index];

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
            await this.slotViews[_placed[i].slotIndex]
                .PlayDealAnim(t_from, t_mid, t_dests[i], this.cardDealDuration);
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
