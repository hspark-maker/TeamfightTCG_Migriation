using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 카드팩 구매가 확정된 순간의 임팩트. 이 구간이 맡는 감각은 예감이 아니라 확정감이다
// — 예감은 개봉 흐름이 이미 둘 갖고 있다(스와이프 대기, 카드 넘기기).
// 여기서 만들 것은 "되돌릴 수 없는 일이 방금 시작됐다" 하나다.
//
// 축이 둘뿐이다:
//   1) 팩이 눌렸다 부풀어 오른다 — 눌림이 먼저인 것이 핵심이다. 부풀기만 하면 반동이 죽는다.
//   2) 화면 플래시가 차올라 그 밑에서 개봉 화면으로 갈아치운다.
//
// 지출은 연출하지 않는다. 골드가 팩으로 빨려드는 그림은 결국 나간 돈을 세어 보이는 것이고,
// 그러면 시작의 설렘 자리에 아까움이 남는다. 골드는 조용히 줄어드는 편이 낫다.
//
// ⚠ 스케일 트윈 대상은 페이지 루트다. 자식(Art)은 PackIdleMotion이 매 프레임 덮어쓰고 있어
//   거기 트윈을 걸면 둘이 같은 값을 두고 싸운다(팩 플래시만 그 자식에 얹는다 — 부유를 함께 타야 하므로).
//
// 시퀀스를 조립해 즉시 재생하고 돌려준다. 개봉 화면을 여는 시점(_onCover)은 이 시퀀스의 시간축에 박는다
// — 플래시 정점과 화면 교체가 각자 다른 타이머로 돌면 언젠가 반드시 갈린다.
public class PackPurchaseImpact : MonoBehaviour
{
    // 자가 설치 대상 노드 이름. 못 찾으면 캔버스 루트에 붙는다(GoldGainEffectPlayer와 같은 관용구).
    const string LAYER_NAME = "GainEffectLayer";

    // 팩 위에 얹는 흰빛 노드의 이름. 팩 아트를 찾을 때 이 이름을 걸러야 한다 —
    // Destroy는 프레임 끝에 실행되므로, 연달아 사면 직전 흰빛이 아직 자식으로 남아 있고 그것이 아트로 잡힌다
    // (그러면 곧 파괴될 노드의 자식으로 새 흰빛이 붙어 함께 사라진다).
    const string FLASH_NAME = "PurchaseFlash";

    [Header("팩 반응")]
    [Tooltip("눌릴 때의 배율. 부풀기 전에 한 번 들어가야 반동이 산다.")]
    [Range(0.8f, 1f)] [SerializeField] float pressScale = 0.94f;
    [SerializeField] float pressDuration = 0.06f;
    [Tooltip("부풀어 오르는 배율. 팩이 결제를 먹고 반응하는 그림이다.")]
    [Range(1f, 1.5f)] [SerializeField] float swellScale = 1.14f;
    [SerializeField] float swellDuration = 0.2f;
    [Tooltip("제 크기로 되돌아오는 시간. 화면 플래시 밑에서 진행되므로 보이지는 않는다 " +
             "— 부푼 채로 굳지 않게 하는 안전장치에 가깝다.")]
    [SerializeField] float restoreDuration = 0.2f;

    [Header("팩 플래시")]
    [Tooltip("팩 그림 위에 겹치는 흰빛의 최대 알파. 팩 아트와 같은 스프라이트를 써서 팩 모양대로만 밝아진다.")]
    [Range(0f, 1f)] [SerializeField] float packFlashAlpha = 0.4f;
    [SerializeField] float packFlashRise = 0.08f;
    [SerializeField] float packFlashFall = 0.18f;

    [Header("화면 플래시")]
    // 플래시의 '생김새'(차오르는 시간·머무는 시간·색·빛)는 여기 두지 않는다 — ScreenFlashCover가 그 단일 진실원이고
    // 호출부(PackShowcaseController)가 인스펙터로 쥔다. 여기 남는 것은 '이 임팩트 안에서 언제 터지는가' 하나뿐이다.
    [Tooltip("플래시가 차오르기 시작하는 시각(초). 팩이 부풀기를 마치는 시각(눌림+부풀기)과 맞춰야 " +
             "정점에서 터지는 것으로 읽힌다 — 이르면 팩 반응이 잘리고, 늦으면 빈 박자가 생긴다.")]
    [SerializeField] float flashAt = 0.26f;

    static PackPurchaseImpact s_instance;

    // 재생 중인 임팩트. 연달아 사면 앞 연출이 끝나기 전에 또 불린다.
    Sequence m_current;

    /// <summary>
    /// 재생기를 얻는다. 씬에 없으면 연출 레이어(없으면 캔버스 루트)에 자가 설치한다.
    /// 설치할 자리가 없으면 false — 호출부는 연출을 건너뛰고 곧장 개봉 화면을 열면 된다.
    /// </summary>
    public static bool TryGet(Component _context, out PackPurchaseImpact _impact)
    {
        if (s_instance == null) s_instance = FindFirstObjectByType<PackPurchaseImpact>();
        if (s_instance == null) s_instance = Install(_context);

        _impact = s_instance;
        return _impact != null;
    }

    /// <summary>
    /// 구매 임팩트를 즉시 재생한다. _onCover는 화면이 플래시로 완전히 덮인 순간 1회 불린다
    /// — 개봉 화면은 반드시 그때 열어야 전환 프레임이 드러나지 않는다.
    /// 연출이 어떤 이유로 끊겨도 _onCover는 반드시 불린다(카드는 이미 지급됐고, 화면을 못 보는 상태가 최악이다).
    /// </summary>
    public void Play(RectTransform _packRect, ScreenFlashCover _cover, Action _onCover)
    {
        if (m_current != null && m_current.IsActive()) m_current.Kill();

        // 덮개를 안 넘겼으면 기본 생김새로 만든다 — 이 축이 통째로 빠지면 전환이 하드컷으로 드러난다.
        var t_style = _cover ?? new ScreenFlashCover();

        // 덮임 통지는 정확히 1회. 시간축과 중단 안전망 양쪽에서 부르므로 여기서 잠근다.
        bool t_fired = false;
        void Fire()
        {
            if (t_fired) return;
            t_fired = true;
            _onCover?.Invoke();
        }

        var t_seq = DOTween.Sequence().SetLink(gameObject);

        // OnKill은 덮어쓰기라 단계마다 걸 수 없다 — 정리는 여기 하나로 모아 마지막에 한 번만 건다.
        Action t_cleanup = StagePack(t_seq, _packRect);
        StageScreenFlash(t_seq, t_style);

        // 덮임 시각은 덮개가 정한다 — 이 값이 어긋나면 아직 비치는 화면 위에서 교체가 일어난다.
        t_seq.InsertCallback(this.flashAt + t_style.rise, Fire);

        // 정상 종료든 중단이든 여기로 온다 — 덮임 통지가 빠지면 개봉 화면이 영영 열리지 않는다.
        t_seq.OnKill(() => { Fire(); t_cleanup?.Invoke(); });

        m_current = t_seq;
        t_seq.Play();
    }

    // 연출 레이어(없으면 캔버스 루트)에 자가 설치.
    static PackPurchaseImpact Install(Component _context)
    {
        if (_context == null) return null;

        var t_canvas = _context.GetComponentInParent<Canvas>();
        if (t_canvas == null) return null;

        var t_root = t_canvas.rootCanvas != null ? t_canvas.rootCanvas : t_canvas;
        var t_layer = FindActiveByName(t_root.transform, LAYER_NAME);

        return (t_layer != null ? t_layer.gameObject : t_root.gameObject).AddComponent<PackPurchaseImpact>();
    }

    static RectTransform FindActiveByName(Transform _root, string _name)
    {
        var t_all = _root.GetComponentsInChildren<RectTransform>(true);
        for (int t_i = 0; t_i < t_all.Length; t_i++)
            if (t_all[t_i].name == _name && t_all[t_i].gameObject.activeInHierarchy) return t_all[t_i];

        return null;
    }

    // 팩 그림을 찾는다. 스프라이트가 실린 첫 Image가 곧 팩 아트다(캐러셀 페이지의 'Art' 자식).
    static Image FindPackArt(RectTransform _packRect)
    {
        var t_images = _packRect.GetComponentsInChildren<Image>(true);
        for (int t_i = 0; t_i < t_images.Length; t_i++)
        {
            if (t_images[t_i].sprite == null) continue;
            if (t_images[t_i].name == FLASH_NAME) continue;   // 직전 개봉의 흰빛(파괴 대기 중)을 아트로 오인하지 않게.

            return t_images[t_i];
        }

        return null;
    }

    // 팩이 눌렸다 부풀었다 되돌아온다. _packRect가 없으면 이 축만 건너뛴다.
    // 연출이 끝나거나 끊길 때 할 정리를 돌려준다(호출부가 한 OnKill에 모아 건다).
    Action StagePack(Sequence _seq, RectTransform _packRect)
    {
        if (_packRect == null) return null;

        // 직전 임팩트의 스케일 트윈이 남아 있으면 출발 크기가 어긋난다.
        _packRect.DOKill();
        _packRect.localScale = Vector3.one;

        _seq.Insert(0f, _packRect.DOScale(this.pressScale, this.pressDuration).SetEase(Ease.InQuad));
        _seq.Insert(this.pressDuration,
                    _packRect.DOScale(this.swellScale, this.swellDuration).SetEase(Ease.OutBack));
        _seq.Insert(this.pressDuration + this.swellDuration,
                    _packRect.DOScale(1f, this.restoreDuration).SetEase(Ease.OutQuad));

        // 캐러셀 페이지는 재구축 전까지 살아 있는 노드다 — 중단으로 트윈이 끊기면 부푼 채로 굳는다.
        Action t_restore = () => { if (_packRect != null) _packRect.localScale = Vector3.one; };

        return t_restore + StagePackFlash(_seq, _packRect);
    }

    // 팩 모양대로 밝아지는 흰빛. 사각 판을 씌우면 팩 밖으로 흰 네모가 튀어나오므로 팩 아트의 스프라이트를 그대로 쓴다.
    Action StagePackFlash(Sequence _seq, RectTransform _packRect)
    {
        var t_art = FindPackArt(_packRect);
        if (t_art == null) return null;   // 그림을 못 찾으면 밝힐 모양도 없다.

        var t_go = new GameObject(FLASH_NAME, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var t_rt = (RectTransform)t_go.transform;

        // 아트의 자식으로 붙어야 부유(PackIdleMotion)를 함께 타고, 팩과 어긋나지 않는다.
        t_rt.SetParent(t_art.rectTransform, false);
        t_rt.anchorMin = Vector2.zero;
        t_rt.anchorMax = Vector2.one;
        t_rt.offsetMin = Vector2.zero;
        t_rt.offsetMax = Vector2.zero;

        var t_image = t_go.GetComponent<Image>();
        t_image.sprite = t_art.sprite;
        t_image.type = t_art.type;
        t_image.preserveAspect = t_art.preserveAspect;
        t_image.raycastTarget = false;
        t_image.color = new Color(1f, 1f, 1f, 0f);

        float t_at = this.pressDuration;   // 부풀기와 같은 시각에 터진다 — 갈라지면 두 연출로 읽힌다.
        _seq.Insert(t_at, t_image.DOFade(this.packFlashAlpha, this.packFlashRise).SetEase(Ease.OutQuad));
        _seq.Insert(t_at + this.packFlashRise,
                    t_image.DOFade(0f, this.packFlashFall).SetEase(Ease.InQuad));

        // 잔해를 남기지 않는다(CoinBurstEffect.ClearCoins와 같은 정리 규칙).
        return () => { if (t_go != null) Destroy(t_go); };
    }

    // 화면을 덮는 플래시. 설치할 자리가 없으면 이 축만 건너뛴다(전환이 하드컷으로 돌아갈 뿐이다).
    void StageScreenFlash(Sequence _seq, ScreenFlashCover _style)
    {
        if (!ScreenFlash.TryGet(out var t_flash)) return;

        var t_cover = t_flash.BuildCover(_style);
        if (t_cover == null) return;

        _seq.Insert(this.flashAt, t_cover);
    }
}
