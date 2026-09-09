using System.Collections.Generic;
using Cysharp.Threading.Tasks;

internal static class RewardPackChoice
{
    internal static async UniTask<string> ChooseAsync(IReadOnlyList<string> _choices)
    {
        if (UIPoolManager.Instance == null) return null;
        if (_choices == null || _choices.Count == 0)
        {
            UIPoolManager.Instance.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData {
                titleText = "해금된 테마 팩이 없습니다. 랭크를 올린 뒤 수령할 수 있습니다.", yesText = "확인", noText = "닫기",
            });
            return null;
        }
        for (int i = 0; i < _choices.Count; i++)
        {
            string t_id = _choices[i];
            var t_done = new UniTaskCompletionSource<bool>();
            UIPoolManager.Instance.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData {
                titleText = $"테마 팩 선택 ({i + 1}/{_choices.Count})\n{PackSpec.DisplayName(t_id)}\n선택한 팩을 즉시 개봉합니다.",
                yesText = "이 팩 받기", noText = i + 1 < _choices.Count ? "다음 팩" : "취소",
                yesAction = () => t_done.TrySetResult(true), noAction = () => t_done.TrySetResult(false),
                onHide = () => t_done.TrySetResult(false),
            });
            bool t_selected = await t_done.Task;
            // SimpleYNPopup은 콜백 뒤 Hide하므로 다음 팝업은 다음 프레임에 연다.
            await UniTask.Yield();
            if (t_selected) return t_id;
        }
        return null;
    }
}
