using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 화면 전체를 덮는 흰 번쩍임. 목적은 하나 — 그 밑에서 화면을 갈아치우는 것이다.
//
// 페이드로 넘기는 것과 다르다. 페이드는 두 화면이 겹쳐 보이는 구간을 만들지만, 플래시는 그 구간을 지운다.
// 그래서 출발 화면과 도착 화면의 구도가 서로 달라도 눈이 그 차이를 보지 못한다
// — 카드팩 구매가 히어로 전환 없이도 "내가 산 그 팩"으로 이어지는 이유가 이것이다.
//
// ⚠ 독립 루트 캔버스로 선다(어느 캔버스의 자식도 아니다). 중첩 캔버스는 sortingOrder를 아무리 올려도
//   부모 루트가 그려지는 자리 안에서만 정렬되므로, 별도 루트인 개봉 오버레이(sortingOrder 100)가 그 위에 선다
//   — 정작 가려야 할 전환이 플래시 위로 드러난다.
// ⚠ GraphicRaycaster를 붙이지 않는다. 덮여 있는 동안 터치를 먹으면 도착 화면이 첫 입력을 잃는다.
public class ScreenFlash : MonoBehaviour
{
    // 자가 설치 노드 이름. 씬에 미리 둘 수도 있지만 없으면 런타임에 세운다(프리팹 편집 없이).
    const string NODE_NAME = "ScreenFlashLayer";

    // 어떤 UI보다도 위. 오버레이 캔버스와 다투지 않도록 넉넉히 띄운다.
    const int SORTING_ORDER = 32000;

    [Tooltip("덮는 색. 흰색이 기본 — 어두운 화면에서 가장 확실하게 프레임을 지운다.")]
    [SerializeField] Color flashColor = Color.white;

    static ScreenFlash s_instance;

    Image m_image;

    /// <summary>
    /// 플래시를 얻는다. 씬에 없으면 자가 설치한다(프리팹·씬 편집 없이).
    /// 씬이 바뀌면 함께 파괴되고 다음 호출이 다시 세운다 — 화면을 덮는 물건이 씬을 넘어 살아남지 않게.
    /// </summary>
    public static bool TryGet(out ScreenFlash _flash)
    {
        if (s_instance == null) s_instance = FindFirstObjectByType<ScreenFlash>(FindObjectsInactive.Include);
        if (s_instance == null) s_instance = Install();

        _flash = s_instance;
        return _flash != null;
    }

    /// <summary>
    /// 차올랐다 걷히는 시퀀스를 만들어 돌려준다(재생은 호출자 시퀀스에 맡긴다 — 중단이 함께 걷히도록).
    /// 화면이 완전히 덮이는 시각은 _rise다. 그 뒤 _hold 동안 덮인 채로 있으니 화면 교체는 그 사이에 한다.
    /// </summary>
    public Sequence BuildCover(float _rise, float _hold, float _fall, float _peakAlpha)
    {
        var t_image = ResolveImage();
        if (t_image == null) return null;

        SetAlpha(0f);
        t_image.gameObject.SetActive(true);

        var t_seq = DOTween.Sequence().SetLink(gameObject);
        t_seq.Append(DOTween.To(GetAlpha, SetAlpha, _peakAlpha, _rise).SetEase(Ease.OutQuad));

        if (_hold > 0f) t_seq.AppendInterval(_hold);

        t_seq.Append(DOTween.To(GetAlpha, SetAlpha, 0f, _fall).SetEase(Ease.InQuad));

        // 정상 종료든 중단이든 여기로 온다 — 밝은 채로 굳으면 화면이 하얗게 잠긴다.
        t_seq.OnKill(Clear);
        return t_seq;
    }

    void Awake()
    {
        ResolveImage();
        Clear();
    }

    void OnDisable()
    {
        // 연출 도중 꺼지면 시퀀스의 마지막 콜백이 오지 않는다.
        Clear();
    }

    // 씬 최상위에 독립 루트 캔버스를 세운다. 어느 캔버스의 자식도 아니어야 sortingOrder가 전역으로 먹는다.
    // SafeArea 안쪽에 두지 않는 것도 같은 이유다 — 노치까지 덮어야 프레임이 완전히 지워진다.
    static ScreenFlash Install()
    {
        var t_go = new GameObject(NODE_NAME, typeof(RectTransform), typeof(Canvas), typeof(CanvasRenderer), typeof(Image));

        var t_canvas = t_go.GetComponent<Canvas>();
        t_canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        t_canvas.sortingOrder = SORTING_ORDER;

        // 캔버스 rect가 곧 화면이므로 늘려 붙이면 스케일러 없이도 전체를 덮는다.
        var t_rt = (RectTransform)t_go.transform;
        t_rt.anchorMin = Vector2.zero;
        t_rt.anchorMax = Vector2.one;
        t_rt.offsetMin = Vector2.zero;
        t_rt.offsetMax = Vector2.zero;

        return t_go.AddComponent<ScreenFlash>();
    }

    Image ResolveImage()
    {
        if (m_image != null) return m_image;

        m_image = GetComponent<Image>();
        if (m_image == null) return null;

        m_image.raycastTarget = false;   // 덮인 동안 터치를 가로채지 않는다.
        return m_image;
    }

    float GetAlpha() => m_image != null ? m_image.color.a : 0f;

    void SetAlpha(float _a)
    {
        if (m_image == null) return;

        var t_c = this.flashColor;
        t_c.a = _a;
        m_image.color = t_c;
    }

    // 투명하게 되돌리고 노드를 꺼 둔다. 꺼 두는 이유는 알파 0짜리 전체 화면 이미지가 매 프레임 그려지지 않게.
    void Clear()
    {
        SetAlpha(0f);
        if (m_image != null) m_image.gameObject.SetActive(false);
    }
}
