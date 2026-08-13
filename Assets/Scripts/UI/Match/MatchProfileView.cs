using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 매칭 화면 프로필 카드 한 장의 렌더러. 내 쪽과 상대 쪽이 같은 컴포넌트를 쓴다.
// 상태를 들지 않는다 — 무엇을 언제 그릴지는 셸이 정하고 여기는 받은 값을 칠하기만 한다.
//
// 로비 RankHud를 재사용하지 않는 이유: 그쪽은 OnEnable에서 자기 정적 인스턴스를 잡아
// 이 오버레이가 열리는 동안 로비의 승급 연출이 대상을 잃는다. 게다가 항상 내 랭크만 그린다.
public class MatchProfileView : MonoBehaviour
{
    [Tooltip("상대를 찾는 동안 보일 빈 틀. 내 쪽은 처음부터 확정이라 비워 둔다 — 비면 탐색중 표시를 건너뛴다.")]
    [SerializeField] GameObject searchingRoot;

    [Tooltip("상대가 확정된 뒤 보일 채워진 틀. 비우면 항상 보이는 것으로 친다.")]
    [SerializeField] GameObject foundRoot;

    [Header("칠할 곳")]
    [SerializeField] Image    avatarImage;
    [SerializeField] TMP_Text nicknameText;
    [SerializeField] Image    rankBadge;
    [SerializeField] TMP_Text rankNameText;

    // 대치 연출에서 셸이 카드를 통째로 민다.
    public RectTransform Rect => (RectTransform)transform;

    public void ShowSearching()
    {
        if (searchingRoot != null) searchingRoot.SetActive(true);
        if (foundRoot     != null) foundRoot.SetActive(false);
    }

    // 스프라이트가 null이면 저작값을 그대로 둔다 — 풀이 비어 있어도 칸이 뚫리지 않게.
    public void Render(in MatchProfile _profile)
    {
        if (searchingRoot != null) searchingRoot.SetActive(false);
        if (foundRoot     != null) foundRoot.SetActive(true);

        if (nicknameText != null) nicknameText.text = _profile.Nickname;
        if (rankNameText != null) rankNameText.text = _profile.RankName;

        if (avatarImage != null && _profile.Avatar    != null) avatarImage.sprite = _profile.Avatar;
        if (rankBadge   != null && _profile.RankBadge != null) rankBadge.sprite   = _profile.RankBadge;
    }
}
