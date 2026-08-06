using System;
using DG.Tweening;
using UnityEngine;

// 연출 동안 사라졌다 돌아오는 패널 묶음.
//
// ⚠ SetActive로 끄지 않는다 — LayoutGroup 아래에서 형제가 남는 높이를 먹어 다른 요소 크기가 튄다.
//   그래서 알파만 내리는데, 투명해도 입력은 그대로 먹으므로 blocksRaycasts를 따로 내려야
//   그 위를 탭해도 반응이 없는 죽은 영역이 생기지 않는다.
[Serializable]
public class RetractingPanels
{
    [SerializeField] CanvasGroup[] groups;

    public void Insert(Sequence _seq, float _at, float _alpha, float _dur)
    {
        if (this.groups == null) return;

        foreach (CanvasGroup t_g in this.groups)
        {
            if (t_g == null) continue;
            _seq.Insert(_at, t_g.DOFade(_alpha, _dur));
        }
    }

    public void SetBlocking(bool _on)
    {
        if (this.groups == null) return;

        foreach (CanvasGroup t_g in this.groups)
        {
            if (t_g == null) continue;
            t_g.blocksRaycasts = _on;
        }
    }

    public void Reset()
    {
        if (this.groups == null) return;

        foreach (CanvasGroup t_g in this.groups)
        {
            if (t_g == null) continue;

            t_g.alpha          = 1f;
            t_g.blocksRaycasts = true;
        }
    }
}
