using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 골드 획득 코인 연출의 단일 재생기. "코인이 흩어졌다 수치로 빨려들며 숫자가 오른다"는 조립 순서를 여기 한 곳에만 둔다.
// 로비 진입(LobbyGainEffectDirector)·도감 수확이 같은 손맛을 쓰고, 각자 복붙하지 않게.
//
// 경계: 지급·저장은 호출부가 이미 끝냈다. 이 클래스는 표시만 하고 재화를 건드리지 않는다.
// 도감 화면·행 뷰는 탭 전환에 꺼지고 재생성되므로 연출기를 거기 두면 OnDisable이 비행 중 코인을 걷어간다 —
// 그래서 항상 켜져 있는 연출 레이어에 자리 잡고, 없으면 TryGet이 런타임에 자가 설치한다(프리팹 편집 없이).
public class GoldGainEffectPlayer : MonoBehaviour
{
    // 자가 설치 대상 노드 이름. 못 찾으면 캔버스 루트에 붙는다(연출 레이어가 없는 테스트 씬 대비).
    const string LAYER_NAME = "GainEffectLayer";

    [Header("배선 (비우면 자동 탐색)")]
    [Tooltip("골드 수치 HUD. 코인의 도착지이자 숫자가 오르는 대상.")]
    [SerializeField] GoldHud goldHud;
    [Tooltip("코인 스프라이트. 비우면 골드 수치 옆 아이콘 Image의 스프라이트를 그대로 쓴다.")]
    [SerializeField] Sprite coinSprite;

    [Header("공통 연출 값")]
    [Tooltip("코인 장수 범위. 획득량을 이 사이로 클램프해 장수를 정한다(장수가 곧 연출 길이).")]
    [SerializeField] int coinCountMin = 4;
    [SerializeField] int coinCountMax = 12;
    [SerializeField] float goldPunch = UiPunch.DEFAULT_SCALE;

    [Header("제자리 모드 (출발 == 수치)")]
    [Tooltip("수치 아래쪽으로 퍼뜨려 화면 밖으로 나가지 않게.")]
    [SerializeField] float nearAngleStart = 195f;
    [SerializeField] float nearAngleSpan = 150f;
    [SerializeField] float nearScatterRadius = 240f;
    [SerializeField] float nearGatherDuration = 0.32f;

    [Header("원거리 모드 (출발 != 수치)")]
    [Tooltip("이동 방향(위)과 같은 쪽으로 퍼뜨린다. 아래로 뿌리면 수렴 InBack이 한 번 더 물어 하단 탭바 뒤로 왕복한다.")]
    [SerializeField] float farAngleStart = 20f;
    [SerializeField] float farAngleSpan = 140f;
    [Tooltip("이동거리가 이미 크므로 흩어짐은 좁게 — 넓으면 행 여러 개를 덮어 노이즈가 된다.")]
    [SerializeField] float farScatterRadius = 140f;
    [Tooltip("거리가 몇 배이므로 수렴 시간도 늘린다. 같으면 순간이동으로 보인다.")]
    [SerializeField] float farGatherDuration = 0.42f;

    static GoldGainEffectPlayer s_instance;

    CoinBurstEffect m_coinBurst;

    // 즉시 재생 중인 시퀀스. 새 재생이 오면 이것부터 정리한다(코인·수치 고정이 겹치지 않게).
    Sequence m_current;

    /// <summary>
    /// 재생기를 얻는다. 씬에 없으면 연출 레이어(없으면 캔버스 루트)에 자가 설치한다.
    /// 캔버스조차 없어 설치할 자리가 없으면 false — 호출부는 연출만 건너뛰면 된다(지급은 이미 끝났으므로 무해).
    /// </summary>
    public static bool TryGet(Component _context, out GoldGainEffectPlayer _player)
    {
        // 비활성 노드에 앉은 재생기는 채택하지 않는다 — CoinBurstEffect.OnDisable이 코인을 즉시 걷어 숫자만 오른다.
        if (s_instance == null) s_instance = FindFirstObjectByType<GoldGainEffectPlayer>();
        if (s_instance == null) s_instance = Install(_context);

        _player = s_instance;
        return _player != null;
    }

    /// <summary>
    /// 골드 획득 연출을 즉시 재생한다. 잔액이 이미 최종값이라는 전제 — 지급·저장이 끝난 뒤에 부른다.
    /// _from을 비우면 수치 자리에서 튀어 제자리로 돌아오고, 주면 그 지점에서 수치까지 날아간다.
    /// </summary>
    public void Play(RectTransform _from, long _gain)
    {
        // 되감기(BeginGainRollUp)보다 먼저 죽여야 한다 — 순서가 뒤집히면 옛 시퀀스의 OnKill이
        // 새 고정을 풀어 최종 잔액을 미리 노출하고, 이후 도착마다 중간값이 걸려 숫자가 뒤로 점프한다.
        if (this.m_current != null && this.m_current.IsActive()) this.m_current.Kill();

        this.m_current = this.BuildGoldGain(_from, _gain);
        this.m_current?.Play();
    }

    /// <summary>
    /// 재생하지 않고 시퀀스만 만들어 돌려준다(카드 연출과 한 시퀀스에 묶어 동시에 돌릴 때).
    /// 배선을 못 찾거나 줄 것이 없으면 null.
    /// 이 경로는 m_current에 잡히지 않는다 — 호출자 시퀀스를 여기서 죽이면 형제 단계(카드)까지 정리 없이 끊긴다.
    /// </summary>
    public Sequence BuildGoldGain(RectTransform _from, long _gain)
    {
        if (_gain <= 0L) return null;

        // 코인은 anchoredPosition으로 날린다 — 캔버스 좌표계 위가 아니면 궤적이 성립하지 않는다.
        if (transform is not RectTransform)
        {
            Debug.LogWarning("[GoldGainEffectPlayer] RectTransform이 아닌 오브젝트에 붙어 있어 연출을 건너뛴다.");
            return null;
        }

        var t_hud = this.ResolveHud();
        var t_textRect = t_hud != null ? t_hud.TextRect : null;
        if (t_textRect == null)
        {
            Debug.LogWarning("[GoldGainEffectPlayer] GoldHud를 찾지 못해 골드 연출을 건너뛴다.");
            return null;
        }

        var t_sprite = this.ResolveSprite(t_textRect);
        if (t_sprite == null)
        {
            Debug.LogWarning("[GoldGainEffectPlayer] 코인 스프라이트를 찾지 못해 골드 연출을 건너뛴다.");
            return null;
        }

        // 출발이 수치 자신이면 이동이 없는 제자리 연출 — 흩어짐 규칙이 원거리와 반대여야 한다.
        bool t_near = _from == null || _from == t_textRect;
        int t_count = (int)Mathf.Clamp(_gain, this.coinCountMin, this.coinCountMax);

        // 값이 인스턴스에 남으므로 두 모드 모두에서 전부 명시 전달한다(직전 모드 값 누수 방지).
        var t_burst = this.EnsureCoinBurst();
        t_burst.Configure(t_sprite, t_near ? t_textRect : _from, t_textRect, t_count,
                          t_near ? this.nearAngleStart : this.farAngleStart,
                          t_near ? this.nearAngleSpan : this.farAngleSpan,
                          t_near ? this.nearScatterRadius : this.farScatterRadius,
                          t_near ? this.nearGatherDuration : this.farGatherDuration);

        var t_onArrived = t_hud.BeginGainRollUp(_gain, this.goldPunch);
        var t_seq = t_burst.BuildBurst(t_onArrived);

        // 연출이 어떤 이유로 끊겨도 수치 고정은 반드시 풀린다(중간 도착 통지가 빠지는 경우의 안전망).
        t_seq.OnKill(() => { if (this.goldHud != null) this.goldHud.ReleaseDisplay(); });
        return t_seq;
    }

    static GoldGainEffectPlayer Install(Component _context)
    {
        if (_context == null) return null;

        var t_canvas = _context.GetComponentInParent<Canvas>();
        if (t_canvas == null) return null;

        var t_root = t_canvas.rootCanvas != null ? t_canvas.rootCanvas : t_canvas;
        var t_layer = FindActiveByName(t_root.transform, LAYER_NAME);

        return (t_layer != null ? t_layer.gameObject : t_root.gameObject).AddComponent<GoldGainEffectPlayer>();
    }

    static RectTransform FindActiveByName(Transform _root, string _name)
    {
        var t_all = _root.GetComponentsInChildren<RectTransform>(true);
        for (int t_i = 0; t_i < t_all.Length; t_i++)
            if (t_all[t_i].name == _name && t_all[t_i].gameObject.activeInHierarchy) return t_all[t_i];

        return null;
    }

    // 골드 수치와 같은 묶음에 놓인 아이콘 Image에서 코인 스프라이트를 빌린다(별도 에셋 배선 없이).
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

    GoldHud ResolveHud()
    {
        if (this.goldHud == null) this.goldHud = FindFirstObjectByType<GoldHud>(FindObjectsInactive.Include);
        return this.goldHud;
    }

    Sprite ResolveSprite(RectTransform _textRect)
    {
        if (this.coinSprite == null) this.coinSprite = FindIconSpriteNear(_textRect);
        return this.coinSprite;
    }

    // 코인 잔해 정리는 같은 인스턴스의 다음 BuildBurst가 맡는다(ClearCoins가 인스턴스별 목록을 본다) — 반드시 하나만 쓴다.
    CoinBurstEffect EnsureCoinBurst()
    {
        if (this.m_coinBurst == null) this.m_coinBurst = GetComponent<CoinBurstEffect>();
        if (this.m_coinBurst == null) this.m_coinBurst = gameObject.AddComponent<CoinBurstEffect>();
        return this.m_coinBurst;
    }
}
