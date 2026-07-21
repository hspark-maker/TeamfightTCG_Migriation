using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class BattleFieldView : MonoBehaviour
{
    [SerializeField] CardView[] slotViews;  // length must equal BattleField.SLOT_COUNT
    [SerializeField] BattleField field;
    [SerializeField] float cardDealDelay    = 0.15f;
    [SerializeField] float cardDealDuration = 0.6f;
    public BattleField Field => this.field;

    public void Refresh()
    {
        bool t_hasWaiting = this.field.WaitingCount > 0;
        for (int i = 0; i < BattleField.SLOT_COUNT; i++)
        {
            CardInstance t_card = this.field.GetSlot(i);
            this.slotViews[i].Render(t_card);
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

        var t_tasks = new List<UniTask>();
        for (int i = 0; i < _placed.Count; i++)
        {
            CardView t_view = this.slotViews[_placed[i].slotIndex];
            Vector3 t_dest  = t_view.transform.position;
            Vector3 t_hide  = t_from;
            t_hide.z = t_dest.z;
            t_view.transform.position = t_hide;
            t_tasks.Add(DealWithDelay(t_view, t_from, t_mid, t_dest, i));
        }
        await UniTask.WhenAll(t_tasks);
    }

    async UniTask DealWithDelay(CardView _view, Vector3 _from, Vector3 _mid, Vector3 _dest, int _index)
    {
        if (_index > 0)
            await UniTask.Delay((int)(this.cardDealDelay * _index * 1000));
        await _view.PlayDealAnim(_from, _mid, _dest, this.cardDealDuration);
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
