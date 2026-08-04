using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 화면을 덮는 플래시 한 번의 생김새. MonoBehaviour가 아니라 씬 저작 뷰가 필드로 소유한다 —
// ScreenFlash는 런타임 자가설치라 프리팹에 배선할 자리가 없고, 거기에 [SerializeField]를 달아도 값이 영영 들어오지 않는다.
// (같은 이유의 선례: PopupTransition — 씬 저작 뷰가 연출 설정을 필드로 쥔다.)
//
// ⚠ 모든 필드에 C# 이니셜라이저로 기본값을 준다. 기존 프리팹 YAML에는 이 필드들이 아직 없어 역직렬화가 건드리지 않고,
//   그래서 이니셜라이저 값이 그대로 살아난다 — "아무것도 배선하지 않아도 도는 플래시"가 여기 적힌 값이다.
[Serializable]
public class ScreenFlashCover
{
    [Header("덮개(전환 은폐)")]
    [Tooltip("차오르는 시간. 이 시각에 화면이 완전히 덮이고, 곧바로 다음 화면으로 갈아치운다. " +
             "0.1을 넘기면 '번쩍'이 아니라 '화이트 페이드'로 읽힌다 — 눈이 하얘지는 과정을 따라가 버린다.")]
    [Min(0f)] public float rise = 0.04f;
    [Tooltip("덮인 채로 머무는 시간. 화면 교체가 이 사이에 일어난다 — 0이면 교체 프레임이 드러날 수 있다.")]
    [Min(0f)] public float hold = 0.03f;
    [Tooltip("걷히는 시간. 이 동안 도착 화면이 이미 올라오고 있다.")]
    [Min(0f)] public float fall = 0.35f;
    [Tooltip("최대 알파. 1 미만이면 완전히 덮이지 않아 교체 프레임이 비친다 — 가리는 것이 목적이므로 1이 기본이다.")]
    [Range(0f, 1f)] public float peak = 1f;
    [Tooltip("덮는 색. 흰색이 기본 — 어두운 화면에서 가장 확실하게 프레임을 지운다.")]
    public Color color = Color.white;

    [Header("빛(질감)")]
    [Tooltip("덮개 위에 얹는 빛의 모양. 비우면 이 축을 통째로 건너뛰고 예전처럼 단색 판만 남는다. " +
             "에셋 후보: Sprites/CardPack/Shine_Radial, Glow_Radial.")]
    public Sprite burstSprite;
    [Tooltip("빛의 색. 덮개가 순백이므로 여기서 살짝 온도를 주면 '흰 판'이 아니라 '빛'으로 읽힌다.")]
    public Color burstColor = new Color(1f, 0.95f, 0.8f, 1f);
    [Range(0f, 1f)] public float burstAlpha = 1f;
    [Tooltip("시작 배율. 기준 크기는 화면의 긴 변이다.")]
    [Min(0f)] public float burstStartScale = 0.35f;
    [Tooltip("끝 배율. 화면 밖으로 밀어내야 '삼켜졌다'로 읽히므로 1보다 크게 잡는다.")]
    [Min(0f)] public float burstEndScale = 1.6f;
    [Tooltip("빛이 걷히는 시간. 덮개(fall)보다 길게 잡으면 도착 화면 위로 잔광이 남는다 — 이 구간이 질감의 대부분이다.")]
    [Min(0f)] public float burstFall = 0.45f;
    [Tooltip("떠 있는 동안 도는 각도(도). 0이면 회전 없음.")]
    public float burstSpin = 0f;
    [Tooltip("가산 합성. 켜면 덮개 위에서 '달아오르고', 끄면 그냥 겹쳐진 그림이 된다.")]
    public bool burstAdditive = true;
}

// 화면 전체를 덮는 번쩍임. 목적은 하나 — 그 밑에서 화면을 갈아치우는 것이다.
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

    // 덮개 위에 얹는 빛 노드의 이름.
    const string BURST_NAME = "ScreenFlashBurst";

    // 어떤 UI보다도 위. 오버레이 캔버스와 다투지 않도록 넉넉히 띄운다.
    const int SORTING_ORDER = 32000;

    static ScreenFlash s_instance;

    // 호출부가 아무것도 넘기지 않았을 때 쓰는 한 벌. 빛 스프라이트가 비어 있으므로 예전과 같은 단색 판이다.
    static readonly ScreenFlashCover s_default = new ScreenFlashCover();

    Image m_image;

    // 이번 커버에만 쓰는 색. 필드로 눌러두지 않는 이유는 다음 호출이 앞 호출의 색을 물려받지 않게 하기 위해서다.
    Color? m_coverColor;

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
    /// 화면이 완전히 덮이는 시각은 _cover.rise다. 그 뒤 hold 동안 덮인 채로 있으니 화면 교체는 그 사이에 한다.
    /// 주입한 색은 이 시퀀스가 끝날 때까지만 유효하고 Clear에서 비워진다 — 다음 호출이 오염되지 않게.
    /// </summary>
    public Sequence BuildCover(ScreenFlashCover _cover)
    {
        var t_image = ResolveImage();
        if (t_image == null) return null;

        var t_c = _cover ?? s_default;

        m_coverColor = t_c.color;   // 알파를 굴리기 전에 정한다 — SetAlpha가 이 색을 읽는다.
        SetAlpha(0f);
        t_image.gameObject.SetActive(true);

        var t_seq = DOTween.Sequence().SetLink(gameObject);
        t_seq.Insert(0f, DOTween.To(GetAlpha, SetAlpha, t_c.peak, t_c.rise).SetEase(Ease.OutQuad));
        t_seq.Insert(t_c.rise + t_c.hold, DOTween.To(GetAlpha, SetAlpha, 0f, t_c.fall).SetEase(Ease.InQuad));

        // 빛은 덮개보다 오래 남을 수 있다 — 시퀀스 길이는 DOTween이 늘려 잡는다.
        Action t_cleanup = StageBurst(t_seq, t_c);

        // 정상 종료든 중단이든 여기로 온다 — 밝은 채로 굳으면 화면이 하얗게 잠기고, 빛 노드가 남으면 잔해가 된다.
        t_seq.OnKill(() => { Clear(); t_cleanup?.Invoke(); });
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

    // 덮개 위에 얹는 빛. 스프라이트가 없으면 이 축만 건너뛴다(예전과 같은 단색 판으로 돌아갈 뿐이다).
    // 자식으로 붙는 이유: uGUI는 자식을 부모 그래픽 '위'에 그린다 — 덮개가 걷히는 동안 이 빛이 남아야 질감이 산다.
    Action StageBurst(Sequence _seq, ScreenFlashCover _c)
    {
        if (_c.burstSprite == null) return null;

        var t_root = (RectTransform)transform;

        // 캔버스 rect가 곧 화면이다. 긴 변을 기준으로 잡아야 세로/가로 어느 화면비에서도 화면을 채운다.
        // ⚠ 자가설치된 바로 그 프레임에는 rect가 아직 0이다(캔버스가 한 번도 갱신되지 않았다).
        //   그때 접어버리면 '첫 구매만 빛이 없다'가 되므로 화면 크기로 대신한다 —
        //   이 캔버스는 CanvasScaler가 없어 rect가 곧 화면 픽셀이라 두 값이 같다.
        float t_base = Mathf.Max(t_root.rect.width, t_root.rect.height);
        if (t_base <= 0f) t_base = Mathf.Max(Screen.width, Screen.height);
        if (t_base <= 0f) return null;

        var t_go = new GameObject(BURST_NAME, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var t_rt = (RectTransform)t_go.transform;

        t_rt.SetParent(t_root, false);
        t_rt.anchorMin = t_rt.anchorMax = t_rt.pivot = new Vector2(0.5f, 0.5f);
        t_rt.anchoredPosition = Vector2.zero;
        t_rt.sizeDelta = new Vector2(t_base, t_base);
        t_rt.localScale = Vector3.one * _c.burstStartScale;

        var t_image = t_go.GetComponent<Image>();
        t_image.sprite = _c.burstSprite;
        t_image.preserveAspect = true;
        t_image.raycastTarget = false;   // 덮인 동안 터치를 가로채지 않는다(덮개와 같은 이유).
        t_image.color = new Color(_c.burstColor.r, _c.burstColor.g, _c.burstColor.b, 0f);

        if (_c.burstAdditive) ApplyAdditive(t_go);

        float t_life = _c.rise + _c.hold + _c.burstFall;

        _seq.Insert(0f, t_rt.DOScale(_c.burstEndScale, t_life).SetEase(Ease.OutQuad));
        if (!Mathf.Approximately(_c.burstSpin, 0f))
            _seq.Insert(0f, t_rt.DOLocalRotate(new Vector3(0f, 0f, _c.burstSpin), t_life,
                                               RotateMode.LocalAxisAdd).SetEase(Ease.OutQuad));

        _seq.Insert(0f, t_image.DOFade(_c.burstAlpha, _c.rise).SetEase(Ease.OutQuad));
        _seq.Insert(_c.rise + _c.hold, t_image.DOFade(0f, _c.burstFall).SetEase(Ease.InQuad));

        // 잔해를 남기지 않는다(CoinBurstEffect.ClearCoins와 같은 정리 규칙).
        return () => { if (t_go != null) Destroy(t_go); };
    }

    // 가산 합성. 프로젝트에 범용 UI Additive 머티리얼이 없어 UIEffect로 블렌드만 바꾼다(PackCardView와 같은 관용구).
    // ⚠ blendType 세터는 쓰지 않는다 — 넘긴 값을 필드에 넣지 않고 기존 값으로 되돌리는 패키지 버그가 있다.
    //   dst를 먼저 지정해야 세터가 Additive로 역산한다.
    static void ApplyAdditive(GameObject _go)
    {
        var t_fx = _go.AddComponent<Coffee.UIEffects.UIEffect>();
        t_fx.dstBlendMode = UnityEngine.Rendering.BlendMode.One;
        t_fx.srcBlendMode = UnityEngine.Rendering.BlendMode.One;
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

        // 색의 진실원은 ScreenFlashCover다. 여기 폴백은 커버 밖에서 알파만 0으로 지우는 경로(Awake/Clear)를 위한 것.
        var t_c = m_coverColor ?? Color.white;
        t_c.a = _a;
        m_image.color = t_c;
    }

    // 투명하게 되돌리고 노드를 꺼 둔다. 꺼 두는 이유는 알파 0짜리 전체 화면 이미지가 매 프레임 그려지지 않게.
    void Clear()
    {
        SetAlpha(0f);
        m_coverColor = null;   // 주입색은 한 번 쓰고 버린다(SetAlpha 뒤에 비워야 마지막 프레임이 제 색으로 지워진다).
        if (m_image != null) m_image.gameObject.SetActive(false);
    }
}
