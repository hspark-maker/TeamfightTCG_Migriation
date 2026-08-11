using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 등급 승급 순간의 배지 안무(충전 → 파열 → 정적 → 강림 → 착지). RankHud에서 떼어 둔 건 튜닝 값이 열댓 개라
// 한 파일에 두면 랭크 표시 규칙이 연출 값에 묻히기 때문이다.
//
// 재생하지 않고 호출자 시퀀스에 붙인다 — 스킵 한 번으로 날아가던 파편까지 함께 최종 상태로 끌려가야 한다.
// 오버레이·파편·링·반짝임은 전부 런타임 생성물이다(전용 아트 없음). 어디서 끊겨도 Restore가 걷는다.
[System.Serializable]
public class RankPromoteEffect
{
    [Header("충전 · 파열")]
    [Tooltip("구 배지가 흰빛으로 차오르는 시간.")]
    [SerializeField] float chargeDuration = 0.22f;

    [Tooltip("차오르는 동안 배지가 부푸는 정도(1 기준 추가 배율).")]
    [SerializeField] float chargeSwell = 0.12f;

    [Tooltip("파편 수. 0이면 파편 없이 배지만 사라진다.")]
    [SerializeField] int shardCount = 8;
    [SerializeField] float shardRadius = 120f;
    [SerializeField] float shardDuration = 0.32f;

    [Tooltip("파열과 강림 사이, 배지 자리가 비어 있는 시간.\n" +
             "여기서 숨을 참아야 뒤의 낙하가 사건이 된다 — 0으로 두면 둘이 한 덩어리로 뭉개진다.")]
    [SerializeField] float silence = 0.1f;

    [Header("강림 · 착지")]
    [Tooltip("새 배지가 떨어지기 시작하는 높이(px).")]
    [SerializeField] float fallHeight = 220f;
    [SerializeField] float fallDuration = 0.2f;

    [Tooltip("낙하를 시작할 때의 배율. 멀리서 다가오는 것처럼 보이려면 1보다 커야 한다.")]
    [SerializeField] float overshootScale = 1.6f;

    [Tooltip("착지 후 제 크기로 눌러앉는 시간(OutBack이라 한 번 더 튄다).")]
    [SerializeField] float settleDuration = 0.16f;

    [Tooltip("착지 순간 퍼지는 충격 링의 최대 배율. 링은 새 배지 스프라이트를 희게 물들여 쓴다.")]
    [SerializeField] float ringScale = 2.2f;
    [SerializeField] float ringDuration = 0.34f;

    [SerializeField] float shakeStrength = 10f;
    [SerializeField] float shakeDuration = 0.22f;

    [Tooltip("착지 뒤 반짝임이 지나가기까지의 뜸.")]
    [SerializeField] float gleamDelay = 0.06f;
    [SerializeField] float gleamDuration = 0.35f;

    [Header("사운드")]
    [Tooltip("미배선이면 무음이다 — 연출은 그대로 돈다.")]
    [SerializeField] AudioClip burstSfx;
    [SerializeField] AudioClip landSfx;

    // 이번 연출이 만든 것들. 시퀀스가 죽을 때 Restore가 통째로 걷는다.
    readonly List<GameObject> m_spawned = new List<GameObject>();

    // 되돌릴 배지와 그 자리. 낙하가 anchoredPosition을 건드리므로 끊기면 어긋난 채 굳는다.
    Image m_badge;
    Vector2 m_basePos;

    /// <summary>
    /// 파열 → 강림을 _seq 뒤에 이어 붙인다(재생은 호출자 몫 — 프로젝트 규약).
    /// _onBurst는 배지가 터지는 프레임에 불린다 — 별을 터는 일은 핍을 가진 쪽이 해야 한다.
    /// _newBadge가 null이면 스프라이트를 갈지 않는다(배지 미저작 허용, RenderTier와 같은 관용).
    /// </summary>
    public void Build(Sequence _seq, Image _badge, TMP_Text _desc, string _newName, Sprite _newBadge,
                      System.Action _onBurst)
    {
        this.Restore();

        if (_seq == null) return;

        // 배지가 미배선이면 안무할 대상이 없다 — 표시만 갈고 넘어간다(핍 연출은 그대로 이어진다).
        if (_badge == null)
        {
            _seq.AppendCallback(() =>
            {
                if (_desc != null && _newName != null) _desc.text = _newName;
                _onBurst?.Invoke();
            });
            return;
        }

        this.m_badge = _badge;

        var t_rect = _badge.rectTransform;
        this.m_basePos = t_rect.anchoredPosition;

        Sprite t_old = _badge.sprite;
        Sprite t_new = _newBadge != null ? _newBadge : t_old;

        // 파편은 배지와 같은 앵커를 쓰고 배지 중심에서 출발한다. 로컬좌표(InverseTransformPoint)를 그대로
        // anchoredPosition에 넣으면 부모 pivot이 0.5가 아닐 때 어긋난다 — RankInfo는 pivot y=0이라 350px 튄다.
        var t_parent = t_rect.parent as RectTransform;
        Vector2 t_origin = this.m_basePos + (new Vector2(0.5f, 0.5f) - t_rect.pivot) * t_rect.rect.size;

        var t_overlay = this.SpawnOverlay(t_rect, _badge, t_old);
        var t_ring    = this.SpawnOverlay(t_rect, _badge, t_new);
        var t_gleam   = this.SpawnGleam(t_rect, _badge, t_new);
        var t_shards  = this.SpawnShards(t_parent, _badge, t_old, t_origin);

        // 링크를 걸지 않는다 — 부모 시퀀스에 중첩될 것이라 개별로 죽으면 부모가 죽은 자식을 물고 돈다.
        // 수명은 호출자의 시퀀스(RankHud에 링크됨)가 책임진다.
        var t_chore = DOTween.Sequence();

        // 충전 — 흰빛이 차오르며 배지가 부풀고 잘게 떤다.
        t_chore.Insert(0f, t_overlay.DOFade(1f, this.chargeDuration));
        t_chore.Insert(0f, t_rect.DOScale(1f + this.chargeSwell, this.chargeDuration).SetEase(Ease.OutQuad));
        t_chore.Insert(0f, t_rect.DOShakeAnchorPos(this.chargeDuration, 4f, 22, fadeOut: false));

        // 파열 — 배지는 이 프레임에 사라지고, 스프라이트·이름은 보이지 않는 동안 갈아 끼운다.
        // 낙하 시점에 갈면 오버슈트 배율로 커진 구 배지가 한 프레임 비칠 수 있다.
        float t_burst = this.chargeDuration;
        t_chore.InsertCallback(t_burst, () =>
        {
            t_rect.localScale       = Vector3.zero;
            t_rect.anchoredPosition = this.m_basePos;   // 충전 진동이 남긴 어긋남을 여기서 턴다
            t_overlay.color         = new Color(1f, 1f, 1f, 0f);

            if (_newBadge != null) _badge.sprite = _newBadge;
            if (_desc != null && _newName != null) _desc.text = _newName;

            SoundManager.Instance?.PlaySFX(this.burstSfx);
            _onBurst?.Invoke();
        });

        for (int t_i = 0; t_i < t_shards.Length; t_i++)
        {
            // 각도를 균등 분할해 난수 없이 고르게 퍼진다(UiGainBurst와 같은 규칙 — 매번 같은 그림).
            float t_angle = 90f + 360f / t_shards.Length * t_i;
            var t_dir     = new Vector2(Mathf.Cos(t_angle * Mathf.Deg2Rad), Mathf.Sin(t_angle * Mathf.Deg2Rad));
            float t_reach = this.shardRadius * (0.7f + 0.15f * (t_i % 3));

            var t_srt = t_shards[t_i].rectTransform;
            t_chore.Insert(t_burst, t_srt.DOAnchorPos(t_origin + t_dir * t_reach, this.shardDuration)
                                         .SetEase(Ease.OutQuad));
            t_chore.Insert(t_burst, t_srt.DOScale(0.25f, this.shardDuration).SetEase(Ease.InQuad));
            t_chore.Insert(t_burst, t_srt.DOLocalRotate(new Vector3(0f, 0f, t_i % 2 == 0 ? 140f : -140f),
                                                        this.shardDuration));
            // setImmediately: false — 조립은 로딩 커버 아래에서 끝나므로, 지금 흰 조각이 켜지면 커버가 걷힐 때 이미 보인다.
            t_chore.Insert(t_burst, t_shards[t_i].DOColor(new Color(1f, 1f, 1f, 0f), this.shardDuration)
                                                 .From(Color.white, setImmediately: false)
                                                 .SetEase(Ease.InQuad));
        }

        // 강림 — 출발 상태는 From으로 준다. 콜백으로 세우면 같은 시각의 트윈 시작과 순서를 다툰다.
        float t_fall = t_burst + this.silence;
        t_chore.Insert(t_fall, t_rect.DOAnchorPos(this.m_basePos, this.fallDuration)
                                     .From(this.m_basePos + Vector2.up * this.fallHeight, setImmediately: false)
                                     .SetEase(Ease.InCubic));
        t_chore.Insert(t_fall, t_rect.DOScale(0.9f, this.fallDuration)
                                     .From(Vector3.one * this.overshootScale, setImmediately: false)
                                     .SetEase(Ease.InCubic));

        // 착지 — 눌러앉음 + 충격 링 + 흔들림이 한 시각에 겹친다.
        float t_land = t_fall + this.fallDuration;
        t_chore.Insert(t_land, t_rect.DOScale(1f, this.settleDuration).SetEase(Ease.OutBack));
        t_chore.Insert(t_land, t_rect.DOShakeAnchorPos(this.shakeDuration, this.shakeStrength, 14));
        t_chore.Insert(t_land, t_ring.DOColor(new Color(1f, 1f, 1f, 0f), this.ringDuration)
                                     .From(new Color(1f, 1f, 1f, 0.75f), setImmediately: false));
        t_chore.Insert(t_land, t_ring.rectTransform.DOScale(this.ringScale, this.ringDuration)
                                                   .SetEase(Ease.OutQuad));
        t_chore.InsertCallback(t_land, () => SoundManager.Instance?.PlaySFX(this.landSfx));

        // 반짝임 — 배지 모양으로 잘린 띠가 한 번 지나간다.
        // 출발/도착은 배지 폭의 1.2배로 넉넉히 밀어 둔다. 여유가 좁으면 조립 직후(커버 아래 대기 중)
        // 기울어진 띠의 모서리가 마스크 안쪽에 걸려 흰 조각으로 비친다.
        float t_sweep = t_rect.rect.width * 1.2f;
        t_gleam.anchoredPosition = new Vector2(-t_sweep, 0f);
        t_chore.Insert(t_land + this.gleamDelay,
                       t_gleam.DOAnchorPosX(t_sweep, this.gleamDuration).SetEase(Ease.InOutQuad));

        _seq.Append(t_chore);
    }

    /// <summary>배지를 저작 상태로 되돌리고 런타임 생성물을 걷는다. 호출자의 OnKill에서 부른다.</summary>
    public void Restore()
    {
        for (int t_i = 0; t_i < this.m_spawned.Count; t_i++)
        {
            var t_go = this.m_spawned[t_i];
            if (t_go == null) continue;

            t_go.transform.DOKill();

            var t_img = t_go.GetComponent<Image>();
            if (t_img != null) t_img.DOKill();

            Object.Destroy(t_go);
        }

        this.m_spawned.Clear();

        if (this.m_badge == null) return;

        var t_rect = this.m_badge.rectTransform;
        t_rect.localScale       = Vector3.one;
        t_rect.anchoredPosition = this.m_basePos;
        this.m_badge            = null;
    }

    // 배지 위에 정확히 겹치는 흰 복제본(화이트아웃용·충격 링용). 그리는 규칙은 원본을 따라야 어긋나지 않는다.
    Image SpawnOverlay(RectTransform _badgeRect, Image _source, Sprite _sprite)
    {
        var t_img = this.SpawnImage(_badgeRect, "PromoteOverlay");

        var t_rect = t_img.rectTransform;
        t_rect.anchorMin = Vector2.zero;
        t_rect.anchorMax = Vector2.one;
        t_rect.offsetMin = Vector2.zero;
        t_rect.offsetMax = Vector2.zero;

        t_img.sprite         = _sprite;
        t_img.type           = _source.type;
        t_img.preserveAspect = _source.preserveAspect;
        t_img.color          = new Color(1f, 1f, 1f, 0f);
        return t_img;
    }

    // 배지 스프라이트의 알파로 잘라 내는 Mask 아래에 반짝임 띠를 넣는다.
    // 띠 형태는 ShineBandSprite가 단일 진실원 — 텍스처를 새로 굽지 않는다.
    RectTransform SpawnGleam(RectTransform _badgeRect, Image _source, Sprite _sprite)
    {
        var t_mask = this.SpawnOverlay(_badgeRect, _source, _sprite);
        t_mask.color = Color.white;
        t_mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        var t_band = this.SpawnImage(t_mask.rectTransform, "PromoteGleamBand");
        t_band.sprite         = ShineBandSprite.Get();
        t_band.preserveAspect = false;
        t_band.color          = new Color(1f, 1f, 1f, 0.85f);

        var t_rect = t_band.rectTransform;
        t_rect.anchorMin = t_rect.anchorMax = t_rect.pivot = new Vector2(0.5f, 0.5f);
        t_rect.sizeDelta = new Vector2(_badgeRect.rect.width * 0.35f, _badgeRect.rect.height * 2.4f);
        t_rect.localRotation = Quaternion.Euler(0f, 0f, 20f);
        return t_rect;
    }

    // 파편은 배지의 형제로 둔다 — 사라진 배지를 따라가면 안 된다.
    Image[] SpawnShards(RectTransform _parent, Image _source, Sprite _sprite, Vector2 _origin)
    {
        if (_parent == null || this.shardCount <= 0) return System.Array.Empty<Image>();

        var t_badgeRect = _source.rectTransform;
        float t_size    = Mathf.Max(t_badgeRect.rect.width, t_badgeRect.rect.height) * 0.4f;

        var t_shards = new Image[this.shardCount];
        for (int t_i = 0; t_i < this.shardCount; t_i++)
        {
            var t_img = this.SpawnImage(_parent, "PromoteShard");
            t_img.sprite         = _sprite;
            t_img.preserveAspect = true;
            t_img.color          = new Color(1f, 1f, 1f, 0f);

            // 앵커가 배지와 다르면 anchoredPosition의 기준점부터 갈린다 — 원점 계산이 배지 기준이므로 앵커도 배지를 따른다.
            var t_rect = t_img.rectTransform;
            t_rect.anchorMin = t_badgeRect.anchorMin;
            t_rect.anchorMax = t_badgeRect.anchorMax;
            t_rect.pivot     = new Vector2(0.5f, 0.5f);
            t_rect.sizeDelta = new Vector2(t_size, t_size);
            t_rect.anchoredPosition = _origin;

            t_shards[t_i] = t_img;
        }

        return t_shards;
    }

    Image SpawnImage(RectTransform _parent, string _name)
    {
        var t_go = new GameObject(_name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));

        var t_rect = (RectTransform)t_go.transform;
        t_rect.SetParent(_parent, false);
        t_rect.SetAsLastSibling();

        var t_img = t_go.GetComponent<Image>();
        t_img.raycastTarget = false;   // 연출 조각이 탭 터치를 가로채지 않게.

        this.m_spawned.Add(t_go);
        return t_img;
    }
}
