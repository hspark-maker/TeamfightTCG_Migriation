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

    [Tooltip("상대를 찾는 동안 이름 자리에 세울 문구. 이름·랭크 텍스트는 foundRoot 밖(배너 직속)에 있어 " +
             "틀을 감춰도 같이 감춰지지 않는다 — 비워 두면 원본 프리팹(Layer Lab 데모)의 더미 이름이 " +
             "상대를 찾기도 전에 노출된다.")]
    [SerializeField] string searchingNickname = "???";

    [Header("칠할 곳")]
    [Tooltip("판·얼굴·링 3층을 그리는 공용 프로필 뷰. 비우면 프로필 그림 갱신만 건너뛴다 — 이름·랭크는 그대로 칠한다.")]
    [SerializeField] ProfileAvatarView profileView;
    [SerializeField] TMP_Text nicknameText;
    [SerializeField] Image    rankBadge;
    [SerializeField] TMP_Text rankNameText;

    // 대치 연출에서 셸이 카드를 통째로 민다.
    public RectTransform Rect => (RectTransform)transform;

    // 아래 셋은 안무(MatchmakingFx·MatchHandoffFx)가 집는 부품이다. 뷰는 여전히 상태를 들지 않는다 —
    // 무엇을 언제 움직일지는 전부 바깥이 정하고 여기는 어디에 있는지만 알려준다.
    public TMP_Text NicknameText => nicknameText;
    public TMP_Text RankNameText => rankNameText;

    /// <summary>상대를 찾는 동안 훑을 빈 틀. 없으면(내 쪽) 스캔 축이 통째로 빠진다.</summary>
    public RectTransform SearchingRect => searchingRoot != null ? (RectTransform)searchingRoot.transform : null;

    /// <summary>채워진 틀. 기다리는 동안 숨쉬는 대상이다 — 배너 전체가 아니라 이 틀만 움직여야 화면이 맥동하지 않는다.
    /// 비면(항상 보이는 것으로 치는 배선) 이 쪽 호흡 축만 빠진다.</summary>
    public RectTransform FoundRect => foundRoot != null ? (RectTransform)foundRoot.transform : null;

    // 전환이 카드를 통째로 흐린다. 저작에 없어도 되게 런타임에 붙인다 — 프리팹마다 하나씩 꽂게 하면 배선이 늘기만 한다.
    CanvasGroup m_group;

    public CanvasGroup Group
    {
        get
        {
            if (m_group == null) m_group = GetComponent<CanvasGroup>();
            if (m_group == null) m_group = gameObject.AddComponent<CanvasGroup>();

            return m_group;
        }
    }

    public void ShowSearching()
    {
        if (searchingRoot != null) searchingRoot.SetActive(true);
        if (foundRoot     != null) foundRoot.SetActive(false);

        // 이름·랭크는 foundRoot 밖에 있어 틀과 함께 감춰지지 않는다 — 여기서 직접 비우지 않으면
        // 원본 프리팹의 더미 이름이 상대를 찾기도 전에 그대로 보인다.
        if (nicknameText != null) nicknameText.text = searchingNickname;
        if (rankNameText != null) rankNameText.text = string.Empty;

        // 배지도 같은 이유로 직접 감춘다. 스프라이트를 비우지 않고 오브젝트를 내리는 이유는
        // Render가 "스프라이트가 null이면 저작값 유지" 규약이라 비워 두면 되살릴 값이 사라지기 때문이다.
        if (rankBadge != null) rankBadge.gameObject.SetActive(false);
    }

    // 스프라이트가 null이면 저작값을 그대로 둔다 — 풀이 비어 있어도 칸이 뚫리지 않게.
    public void Render(in MatchProfile _profile)
    {
        if (searchingRoot != null) searchingRoot.SetActive(false);
        if (foundRoot     != null) foundRoot.SetActive(true);

        if (nicknameText != null) nicknameText.text = _profile.Nickname;
        if (rankNameText != null) rankNameText.text = _profile.RankName;

        // 판·얼굴·링은 공용 뷰가 그린다. 층별 스프라이트가 null이면 그 층은 저작값이 유지된다(상대·모험는 판·링이 null).
        if (profileView != null)
        {
            profileView.Render(new ProfileLook(_profile.Plate, _profile.PlateColor,
                                               _profile.Avatar, _profile.Frame, _profile.FrameColor));
        }

        // 배지는 랭크가 있을 때만 세운다 — 모험 정점은 랭크를 비워 오므로(MatchProfile.OfTournamentNode)
        // 저작 스프라이트가 남아 있으면 없는 랭크전을 있는 것처럼 보이게 한다.
        if (rankBadge == null) return;

        if (_profile.RankBadge != null) rankBadge.sprite = _profile.RankBadge;

        rankBadge.gameObject.SetActive(_profile.RankBadge != null);
    }
}
