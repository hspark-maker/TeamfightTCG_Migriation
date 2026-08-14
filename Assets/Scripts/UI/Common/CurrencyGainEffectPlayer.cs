using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 재화 획득 코인 연출의 단일 재생기. "코인이 흩어졌다 수치로 빨려들며 숫자가 오른다"는 조립 순서를 여기 한 곳에만 둔다.
// 로비 진입(LobbyGainEffectDirector)·보상 수령이 같은 손맛을 쓰고, 각자 복붙하지 않게.
//
// 경계: 지급·저장은 호출부가 이미 끝냈다. 이 클래스는 표시만 하고 재화를 건드리지 않는다.
// 탭 전환에 꺼지고 재생성되는 화면에 연출기를 두면 OnDisable이 비행 중 코인을 걷어간다 —
// 그래서 항상 켜져 있는 연출 레이어에 자리 잡고, 없으면 TryGet이 런타임에 자가 설치한다(프리팹 편집 없이).
//
// 재생 상태는 전부 재화별이다. 골드와 다이아가 같이 들어와도 각자의 HUD로 각자의 코인이 날아간다.
public class CurrencyGainEffectPlayer : MonoBehaviour
{
    // 자가 설치 대상 노드 이름. 못 찾으면 캔버스 루트에 붙는다(연출 레이어가 없는 테스트 씬 대비).
    const string LAYER_NAME = "GainEffectLayer";

    [Tooltip("이 재생기를 공용 창구(TryGet)의 답으로 삼을지.\n\n" +
             "코인은 이 컴포넌트가 앉은 노드를 좌표계로 삼아 날아간다 — 즉 어느 캔버스에 있느냐가 곧 " +
             "어디에 그려지느냐다. 그래서 로비 위에 겹쳐 뜨는 화면(개봉 오버레이 등)은 자기 캔버스 안에 " +
             "재생기를 따로 두어야 코인이 그 화면 위에 보인다.\n\n" +
             "그런 종속 재생기는 반드시 이 값을 꺼 둘 것 — 켜 두면 공용 창구가 그것을 집을 수 있고, " +
             "그 화면이 닫힌 뒤 로비 연출이 꺼진 레이어에서 돌아 화면에 아무것도 뜨지 않는다.")]
    [SerializeField] bool shared = true;

    [Header("공통 연출 값")]
    [Tooltip("코인 장수 범위. 획득량을 이 사이로 클램프해 장수를 정한다(장수가 곧 연출 길이).")]
    [SerializeField] int coinCountMin = 4;
    [SerializeField] int coinCountMax = 12;
    [SerializeField] float punchScale = UiPunch.DEFAULT_SCALE;

    [Header("제자리 모드 (출발 == 수치)")]
    [Tooltip("수치 아래쪽으로 퍼뜨려 화면 밖으로 나가지 않게.")]
    [SerializeField] float nearAngleStart = 195f;
    [SerializeField] float nearAngleSpan = 150f;
    [SerializeField] float nearScatterRadius = 240f;
    [SerializeField] float nearScatterDuration = 0.28f;
    [SerializeField] float nearGatherDuration = 0.32f;
    [Tooltip("제자리 모드는 출발과 목적지가 같아 곡선이 고리로 보인다 — 기본 0(직선)을 유지할 것.")]
    [SerializeField] float nearArcHeight = 0f;

    [Header("원거리 모드 (출발 != 수치)")]
    [Tooltip("터짐의 시작 각(도). 폭이 360이면 링 전체를 돌리는 값이라 축에 정렬되지 않게만 두면 된다.")]
    [SerializeField] float farAngleStart = 20f;
    [Tooltip("360 = 사방으로 터진다. 아래로 뿌려도 되는 이유는 수렴이 직선 InBack이 아니라 " +
             "휘어 도는 곡선(farArcHeight)이기 때문 — 이 값을 0으로 되돌리면 아래 코인이 하단 탭바 뒤로 왕복한다.")]
    [SerializeField] float farAngleSpan = 360f;
    [Tooltip("터지는 거리. 이동거리가 이미 크므로 좁게 — 넓으면 행 여러 개를 덮어 노이즈가 된다.")]
    [SerializeField] float farScatterRadius = 150f;
    [Tooltip("터지는 시간. 짧을수록 '펑' 하고 열린다 — 0.3에 가까우면 밀려나는 것으로 읽힌다.")]
    [SerializeField] float farScatterDuration = 0.18f;
    [Tooltip("거리가 몇 배이므로 수렴 시간도 늘린다. 같으면 순간이동으로 보인다.")]
    [SerializeField] float farGatherDuration = 0.42f;
    [Tooltip("HUD로 빨려들 때 직선에서 부풀어 오르는 폭(px). 코인이 좌우 번갈아 휘어 든다. " +
             "0이면 예전처럼 직선으로 간다. 이동거리의 45%로 자동 제한되므로 가까운 코인은 덜 휜다.")]
    [SerializeField] float farArcHeight = 200f;

    [Header("빛 줄기 (보상 수령)")]
    [Tooltip("아이콘 자리에서 빛이 피어나는 시간. 아이콘이 사그라드는 시간과 겹쳐야 '변했다'로 읽힌다 — " +
             "짧게 잡으면 아이콘이 사라진 뒤에 빛이 켜져 두 사건으로 보인다.")]
    [SerializeField] float lightBloom = 0.16f;
    [Tooltip("HUD까지 흐르는 시간. 코인 수렴보다 길게 — 하나뿐인 이동체라 서두르면 눈이 놓친다.")]
    [SerializeField] float lightTravel = 0.5f;
    [Tooltip("빛덩이 지름(px). 보상 아이콘을 덮을 만해야 아이콘이 그 아래서 사라진 것으로 읽힌다.")]
    [SerializeField] float lightHeadSize = 130f;
    [Tooltip("뒤따르는 꼬리 조각 수. 0이면 점 하나가 날아간다.")]
    [SerializeField] int lightTailCount = 6;
    [Tooltip("꼬리 한 조각씩 늦는 간격. 이 값 × 조각 수만큼 연출이 길어진다.")]
    [SerializeField] float lightTailInterval = 0.045f;
    [Tooltip("직선에서 부푸는 폭(px). 재화마다 휘는 쪽이 갈리므로, 두 줄기가 겹쳐 보이면 이 값을 올린다.")]
    [SerializeField] float lightBow = 180f;
    [Tooltip("빛 색. 재화별로 가르지 않는다 — 어느 아이콘에서 피었는지가 이미 종류를 말해 준다.")]
    [SerializeField] Color lightTint = new Color(1f, 0.95f, 0.78f);

    static CurrencyGainEffectPlayer s_instance;

    // 코인 잔해 정리는 인스턴스별 목록을 보므로 재화마다 자기 분출기가 있어야 서로의 코인을 걷지 않는다.
    readonly CoinBurstEffect[] m_bursts = new CoinBurstEffect[(int)ECurrencyType.Count];
    readonly Sequence[] m_current = new Sequence[(int)ECurrencyType.Count];
    readonly Sprite[] m_coinSprites = new Sprite[(int)ECurrencyType.Count];

    /// <summary>
    /// 재생기를 얻는다. 씬에 없으면 연출 레이어(없으면 캔버스 루트)에 자가 설치한다.
    /// 캔버스조차 없어 설치할 자리가 없으면 false — 호출부는 연출만 건너뛰면 된다(지급은 이미 끝났으므로 무해).
    /// </summary>
    public static bool TryGet(Component _context, out CurrencyGainEffectPlayer _player)
    {
        // 비활성 노드에 앉은 재생기는 채택하지 않는다 — CoinBurstEffect.OnDisable이 코인을 즉시 걷어 숫자만 오른다.
        if (s_instance == null) s_instance = FindShared();
        if (s_instance == null) s_instance = Install(_context);

        _player = s_instance;
        return _player != null;
    }

    // 공용 창구의 답이 될 자격이 있는 재생기만 고른다(shared 필드 툴팁 참고).
    static CurrencyGainEffectPlayer FindShared()
    {
        var t_all = FindObjectsByType<CurrencyGainEffectPlayer>(FindObjectsSortMode.None);
        for (int t_i = 0; t_i < t_all.Length; t_i++)
            if (t_all[t_i].shared) return t_all[t_i];

        return null;
    }

    /// <summary>
    /// 도착할 HUD를 지정해 재생한다. 공용 창구(CurrencyHud.TryGet)가 내주는 대표 HUD가 지금 화면에서
    /// 보이지 않을 때 쓴다 — 겹쳐 뜨는 화면이 자기 잔액 표시로 코인을 받는 경우다.
    /// _hud가 null이거나 재화가 어긋나면 평소처럼 대표 HUD로 간다.
    /// 세울 것이 없으면 false(호출부는 그 획득을 다른 화면으로 넘길지 판단할 수 있다).
    /// </summary>
    public bool Play(RectTransform _from, CurrencyGain _gain, CurrencyHud _hud)
    {
        if (!_gain.HasAmount) return false;

        int t_slot = (int)_gain.Type;

        // 되감기(BeginGainRollUp)보다 먼저 죽여야 한다 — 순서가 뒤집히면 옛 시퀀스의 OnKill이
        // 새 고정을 풀어 최종 잔액을 미리 노출하고, 이후 도착마다 중간값이 걸려 숫자가 뒤로 점프한다.
        // 같은 재화만 정리한다(다이아 재생이 진행 중인 골드 연출을 죽이지 않게).
        if (this.m_current[t_slot] != null && this.m_current[t_slot].IsActive()) this.m_current[t_slot].Kill();

        this.m_current[t_slot] = this.BuildGain(_from, _gain, _hud);
        this.m_current[t_slot]?.Play();

        return this.m_current[t_slot] != null;
    }

    // 종류 하나치 시퀀스. 배선을 못 찾거나 줄 것이 없으면 null.
    // 이 경로는 m_current에 잡히지 않는다 — 호출자 시퀀스를 여기서 죽이면 형제 단계(카드)까지 정리 없이 끊긴다.
    Sequence BuildGain(RectTransform _from, CurrencyGain _gain, CurrencyHud _hud = null)
    {
        if (!_gain.HasAmount) return null;

        // 코인은 anchoredPosition으로 날린다 — 캔버스 좌표계 위가 아니면 궤적이 성립하지 않는다.
        if (transform is not RectTransform)
        {
            Debug.LogWarning("[CurrencyGainEffectPlayer] RectTransform이 아닌 오브젝트에 붙어 있어 연출을 건너뛴다.");
            return null;
        }

        // 지정 HUD가 다른 재화면 쓰지 않는다 — 골드가 다이아 자리로 빨려드는 그림은 어떤 경우에도 틀렸다.
        var t_hud = _hud != null && _hud.Type == _gain.Type ? _hud : null;
        if (t_hud == null && !CurrencyHud.TryGet(_gain.Type, out t_hud))
        {
            Debug.LogWarning($"[CurrencyGainEffectPlayer] {_gain.Type} HUD를 찾지 못해 연출을 건너뛴다.");
            return null;
        }
        if (t_hud.TextRect == null)
        {
            Debug.LogWarning($"[CurrencyGainEffectPlayer] {_gain.Type} HUD에 수치 텍스트가 없어 연출을 건너뛴다.");
            return null;
        }

        var t_textRect = t_hud.TextRect;

        var t_sprite = this.ResolveSprite(_gain.Type, t_textRect);
        if (t_sprite == null)
        {
            Debug.LogWarning($"[CurrencyGainEffectPlayer] {_gain.Type} 코인 스프라이트를 찾지 못해 연출을 건너뛴다.");
            return null;
        }

        // 출발이 수치 자신이면 이동이 없는 제자리 연출 — 흩어짐 규칙이 원거리와 반대여야 한다.
        bool t_near = _from == null || _from == t_textRect;
        int t_count = (int)Mathf.Clamp(_gain.Amount, this.coinCountMin, this.coinCountMax);

        // 값이 인스턴스에 남으므로 두 모드 모두에서 전부 명시 전달한다(직전 모드 값 누수 방지).
        var t_burst = this.EnsureBurst(_gain.Type);
        t_burst.Configure(t_sprite, t_near ? t_textRect : _from, t_textRect, t_count,
                          t_near ? this.nearAngleStart : this.farAngleStart,
                          t_near ? this.nearAngleSpan : this.farAngleSpan,
                          t_near ? this.nearScatterRadius : this.farScatterRadius,
                          t_near ? this.nearGatherDuration : this.farGatherDuration,
                          _scatterDuration: t_near ? this.nearScatterDuration : this.farScatterDuration,
                          _arcHeight: t_near ? this.nearArcHeight : this.farArcHeight);

        var t_onArrived = t_hud.BeginGainRollUp(_gain.Amount, out var t_releaseDisplay, this.punchScale);
        var t_seq = t_burst.BuildBurst(t_onArrived);

        // 연출이 어떤 이유로 끊겨도 수치 고정은 반드시 풀린다(중간 도착 통지가 빠지는 경우의 안전망).
        t_seq.OnKill(() => t_releaseDisplay?.Invoke());
        return t_seq;
    }

    /// <summary>섞인 획득을 종류별 시퀀스로 만들어 한 시퀀스에 묶는다. 세울 단계가 없으면 null.</summary>
    public Sequence BuildGain(RectTransform _from, CurrencyGainBucket _gains)
    {
        if (_gains == null || _gains.IsEmpty) return null;

        Sequence t_master = null;
        for (int t_i = 0; t_i < (int)ECurrencyType.Count; t_i++)
        {
            var t_type = (ECurrencyType)t_i;

            var t_seq = this.BuildGain(_from, new CurrencyGain(t_type, _gains[t_type]));
            if (t_seq == null) continue;

            t_master ??= DOTween.Sequence().SetLink(gameObject);
            t_master.Insert(0f, t_seq);   // 재화가 갈려도 한 번의 획득이다 — 같은 0초에 함께 돈다.
        }

        return t_master;
    }

    /// <summary>
    /// 보상 수령용 획득 연출. 코인 다발 대신 <b>재화당 빛 한 줄기</b>가 그 재화 아이콘 자리에서 피어나
    /// HUD로 흘러든다(이동체가 하나여야 눈이 따라갈 대상이 정해진다).
    /// 잔액이 이미 최종값이라는 전제는 BuildGain과 같다 — 지급·저장이 끝난 뒤에 부른다.
    /// _origins[재화]는 그 재화의 빛이 피어날 자리, _lightSprite는 빛 그림(둘 다 호출부가 공급한다).
    /// </summary>
    public Sequence BuildLightGain(CurrencyGainBucket _gains, RectTransform[] _origins, Sprite _lightSprite)
    {
        if (_gains == null || _gains.IsEmpty) return null;

        Sequence t_master = null;
        for (int t_i = 0; t_i < (int)ECurrencyType.Count; t_i++)
        {
            var t_type   = (ECurrencyType)t_i;
            var t_origin = _origins != null && t_i < _origins.Length ? _origins[t_i] : null;

            var t_seq = this.BuildLightStreak(t_type, _gains[t_type], t_origin, _lightSprite, t_i);
            if (t_seq == null) continue;

            t_master ??= DOTween.Sequence().SetLink(gameObject);
            t_master.Insert(0f, t_seq);   // 재화가 갈려도 한 번의 획득이다 — 같은 0초에 함께 돈다.
        }

        return t_master;
    }

    // 종류 하나치 빛 줄기. 배선을 못 찾거나 줄 것이 없으면 null —
    // 판정은 전부 BeginGainRollUp보다 앞에 둔다(고정만 걸고 빠져나가면 수치가 영영 안 풀린다).
    Sequence BuildLightStreak(ECurrencyType _type, long _amount, RectTransform _origin,
                              Sprite _lightSprite, int _lane)
    {
        if (_amount <= 0) return null;

        var t_layer = transform as RectTransform;
        if (t_layer == null)
        {
            Debug.LogWarning("[CurrencyGainEffectPlayer] RectTransform이 아닌 오브젝트에 붙어 있어 연출을 건너뛴다.");
            return null;
        }

        if (!CurrencyHud.TryGet(_type, out var t_hud) || t_hud.TextRect == null)
        {
            Debug.LogWarning($"[CurrencyGainEffectPlayer] {_type} HUD를 찾지 못해 연출을 건너뛴다.");
            return null;
        }

        // 빛 그림이 없으면 코인 그림으로라도 흘려보낸다 — 줄기 하나가 가는 그림은 그대로다.
        var t_art = _lightSprite != null ? _lightSprite : this.ResolveSprite(_type, t_hud.TextRect);
        if (t_art == null)
        {
            Debug.LogWarning($"[CurrencyGainEffectPlayer] {_type} 빛 스프라이트를 찾지 못해 연출을 건너뛴다.");
            return null;
        }

        var t_settings = new UiLightStreak.Settings(this.lightBloom, this.lightTravel, this.lightHeadSize,
                                                    this.lightTailCount, this.lightTailInterval,
                                                    this.lightBow, this.lightTint);

        var t_onArrived = t_hud.BeginGainRollUp(_amount, out var t_releaseDisplay, this.punchScale);

        // 조각 목록은 이 줄기 것만 담는다 — 재화별로 갈라 두지 않으면 먼저 끝난 쪽이 남의 빛까지 걷어간다.
        var t_lights = new List<Graphic>();

        var t_seq = UiLightStreak.Build(t_layer,
                                        UiGainBurst.ToLayerLocal(t_layer, _origin != null ? _origin : t_hud.TextRect),
                                        UiGainBurst.ToLayerLocal(t_layer, t_hud.TextRect),
                                        in t_settings, _lane,
                                        _spawn: _i => this.CreateLight(t_art, t_lights),
                                        _despawn: _light => { if (_light != null) _light.gameObject.SetActive(false); },
                                        _onArrived: () => t_onArrived?.Invoke(1, 1));

        t_seq.SetLink(gameObject);

        // 정상 종료든 강제 종료든 여기서 걷는다. 수치 고정 해제도 같은 자리에 둔다(코인 경로와 같은 안전망).
        t_seq.OnKill(() =>
        {
            ClearLights(t_lights);
            t_releaseDisplay?.Invoke();
        });

        return t_seq;
    }

    // ⚠ 가산 합성을 걸지 않는다(파편과 같은 이유) — 가산은 알파가 RGB에 곱해지지 않아 DOFade로 지워지지 않는다.
    Graphic CreateLight(Sprite _sprite, List<Graphic> _tracked)
    {
        var t_go = new GameObject("LightStreak", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        t_go.transform.SetParent(transform, false);

        var t_img = t_go.GetComponent<Image>();
        t_img.sprite         = _sprite;
        t_img.preserveAspect = true;
        t_img.raycastTarget  = false;   // 빛이 뒤 화면의 터치를 가로채지 않게.

        _tracked.Add(t_img);
        return t_img;
    }

    static void ClearLights(List<Graphic> _lights)
    {
        for (int t_i = 0; t_i < _lights.Count; t_i++)
        {
            if (_lights[t_i] == null) continue;

            _lights[t_i].DOKill();             // 색 트윈은 그래픽에, 이동·배율은 트랜스폼에 물려 있다
            _lights[t_i].transform.DOKill();
            Destroy(_lights[t_i].gameObject);
        }

        _lights.Clear();
    }

    static CurrencyGainEffectPlayer Install(Component _context)
    {
        if (_context == null) return null;

        var t_canvas = _context.GetComponentInParent<Canvas>();
        if (t_canvas == null) return null;

        var t_root = t_canvas.rootCanvas != null ? t_canvas.rootCanvas : t_canvas;
        var t_layer = FindActiveByName(t_root.transform, LAYER_NAME);

        return (t_layer != null ? t_layer.gameObject : t_root.gameObject).AddComponent<CurrencyGainEffectPlayer>();
    }

    static RectTransform FindActiveByName(Transform _root, string _name)
    {
        var t_all = _root.GetComponentsInChildren<RectTransform>(true);
        for (int t_i = 0; t_i < t_all.Length; t_i++)
            if (t_all[t_i].name == _name && t_all[t_i].gameObject.activeInHierarchy) return t_all[t_i];

        return null;
    }

    // 수치와 같은 묶음에 놓인 아이콘 Image에서 코인 스프라이트를 빌린다(재화별 에셋 배선 없이).
    static Sprite FindIconSpriteNear(RectTransform _textRect)
    {
        var t_group = _textRect.parent;
        if (t_group == null) return null;

        var t_images = t_group.GetComponentsInChildren<Image>(true);
        Sprite t_any = null;
        for (int t_i = 0; t_i < t_images.Length; t_i++)
        {
            var t_sprite = t_images[t_i].sprite;
            if (t_sprite == null) continue;
            if (t_images[t_i].name.Contains("Icon")) return t_sprite;
            t_any ??= t_sprite;
        }

        return t_any;
    }

    Sprite ResolveSprite(ECurrencyType _type, RectTransform _textRect)
    {
        int t_slot = (int)_type;
        if (this.m_coinSprites[t_slot] == null) this.m_coinSprites[t_slot] = FindIconSpriteNear(_textRect);
        return this.m_coinSprites[t_slot];
    }

    // 재화별 분출기. 한 GameObject의 GetComponent는 첫 장만 돌려주므로 자식 노드로 갈라 둔다.
    CoinBurstEffect EnsureBurst(ECurrencyType _type)
    {
        int t_slot = (int)_type;
        if (this.m_bursts[t_slot] != null) return this.m_bursts[t_slot];

        var t_go = new GameObject($"Burst_{_type}", typeof(RectTransform), typeof(CoinBurstEffect));
        var t_rt = (RectTransform)t_go.transform;
        t_rt.SetParent(transform, false);

        // 코인은 이 노드 기준 anchoredPosition으로 날아간다 — 원점을 부모와 맞추고 pivot을 중앙에 둬야
        // localPosition == anchoredPosition이 성립해 부모에 직접 붙었을 때와 궤적이 같다.
        t_rt.anchorMin = t_rt.anchorMax = t_rt.pivot = new Vector2(0.5f, 0.5f);
        t_rt.sizeDelta = Vector2.zero;
        t_rt.localPosition = Vector3.zero;
        t_rt.localScale = Vector3.one;

        this.m_bursts[t_slot] = t_go.GetComponent<CoinBurstEffect>();
        return this.m_bursts[t_slot];
    }
}
