using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 룰렛 프레임을 두르는 전구 링. 만지는 것은 Image.sprite 와 알파뿐이고 Transform 은 한 번도 건드리지 않는다 —
// 링 노드가 기울어 앉아 있고 전구 8개도 손배치라, 회전·스케일을 얹는 순간 전부 제각각 어긋난다.
public class RouletteBulbRing : MonoBehaviour
{
    [Tooltip("전구 8개입니다. 비워 두면 이 노드의 Image와 자식 Image를 자동으로 모읍니다 — " +
             "목업 저작이 \"첫 전구 밑에 나머지 7개\" 모양이라 그대로 성립합니다.\n\n" +
             "여기 배선하는 순서는 아무래도 좋습니다. 평시 마퀴는 저작 패턴과 그 반전을 번갈아 켤 뿐이라 배선 순서를 보지 않습니다.")]
    [SerializeField] Image[] bulbs;

    [SerializeField] Sprite onSprite;
    [SerializeField] Sprite offSprite;

    [Tooltip("꺼진 전구에 씌울 알파입니다. 1이면 그림만 바뀌고 밝기 차이는 스프라이트에만 맡깁니다.")]
    [Range(0f, 1f)] [SerializeField] float offAlpha = 1f;

    [Header("평시 마퀴")]
    [Tooltip("저작 패턴과 그 반전을 번갈아 켭니다. 값이 작을수록 빨리 깜빡입니다. 0 이하면 마퀴를 돌리지 않습니다.")]
    [SerializeField] float idleInterval = 0.45f;

    // 저작 그림·색. 연출이 끝나면 여기로 되돌린다 — 안 되돌리면 다음 진입에서 전구가 꺼진 채 남는다.
    Sprite[] m_authoredSprites;
    Color[] m_authoredColors;

    Sequence m_idle;

    void Awake()
    {
        this.CollectBulbs();
        this.CaptureAuthored();

        // 켜짐·꺼짐 그림이 없으면 마퀴가 화면에 아무 변화를 못 낸다 — 조용히 죽지 않게 드러낸다.
        if (this.onSprite == null || this.offSprite == null)
            Debug.LogError($"[RouletteBulbRing] 전구 그림이 미배선이라 연출이 보이지 않는다 — on {this.onSprite} / off {this.offSprite}", this);
    }

    void OnDisable()
    {
        this.KillIdle();
        this.RestoreAuthored();
    }

    /// <summary>평시 마퀴로 되돌린다. 저작 패턴에서 시작하므로 여는 순간에는 저작 그림이 그대로 보인다.</summary>
    public void PlayIdle()
    {
        this.KillIdle();
        this.RestoreAuthored();

        if (this.bulbs == null || this.bulbs.Length == 0) return;
        if (this.idleInterval <= 0f) return;

        this.m_idle = DOTween.Sequence().SetUpdate(true).SetLink(gameObject, LinkBehaviour.KillOnDisable);
        this.m_idle.AppendInterval(this.idleInterval);
        this.m_idle.AppendCallback(this.ApplyInvertedAuthored);
        this.m_idle.AppendInterval(this.idleInterval);
        this.m_idle.AppendCallback(this.RestoreAuthored);
        this.m_idle.SetLoops(-1);
        this.m_idle.Play();
    }

    /// <summary>연출을 걷고 저작 패턴으로 복원한다.</summary>
    public void Stop()
    {
        this.KillIdle();
        this.RestoreAuthored();
    }

    // 목업 저작이 "첫 전구가 부모, 나머지 7개가 그 자식"이라 자기 자신도 후보에 넣는다.
    void CollectBulbs()
    {
        if (this.bulbs != null && this.bulbs.Length > 0) return;

        var t_found = new List<Image>();

        var t_self = GetComponent<Image>();
        if (t_self != null) t_found.Add(t_self);

        for (int t_i = 0; t_i < transform.childCount; t_i++)
        {
            var t_image = transform.GetChild(t_i).GetComponent<Image>();
            if (t_image != null) t_found.Add(t_image);
        }

        this.bulbs = t_found.ToArray();
    }

    void CaptureAuthored()
    {
        int t_count = this.bulbs != null ? this.bulbs.Length : 0;
        this.m_authoredSprites = new Sprite[t_count];
        this.m_authoredColors = new Color[t_count];

        for (int t_i = 0; t_i < t_count; t_i++)
        {
            if (this.bulbs[t_i] == null) continue;

            this.m_authoredSprites[t_i] = this.bulbs[t_i].sprite;
            this.m_authoredColors[t_i] = this.bulbs[t_i].color;
        }
    }

    void SetBulb(int _index, bool _on)
    {
        if (this.bulbs == null || _index < 0 || _index >= this.bulbs.Length) return;

        Image t_bulb = this.bulbs[_index];
        if (t_bulb == null) return;

        Sprite t_sprite = _on ? this.onSprite : this.offSprite;
        if (t_sprite != null) t_bulb.sprite = t_sprite;

        Color t_color = this.m_authoredColors != null && _index < this.m_authoredColors.Length
            ? this.m_authoredColors[_index]
            : t_bulb.color;

        t_color.a = _on ? 1f : this.offAlpha;
        t_bulb.color = t_color;
    }

    void RestoreAuthored()
    {
        int t_count = this.bulbs != null ? this.bulbs.Length : 0;
        for (int t_i = 0; t_i < t_count; t_i++)
        {
            if (this.bulbs[t_i] == null) continue;

            if (this.m_authoredSprites != null && t_i < this.m_authoredSprites.Length && this.m_authoredSprites[t_i] != null)
                this.bulbs[t_i].sprite = this.m_authoredSprites[t_i];

            if (this.m_authoredColors != null && t_i < this.m_authoredColors.Length)
                this.bulbs[t_i].color = this.m_authoredColors[t_i];
        }
    }

    // 저작 패턴의 반전. 켜져 있던 자리가 꺼지고 꺼져 있던 자리가 켜진다 — 마퀴가 도는 것처럼 읽힌다.
    void ApplyInvertedAuthored()
    {
        int t_count = this.bulbs != null ? this.bulbs.Length : 0;
        for (int t_i = 0; t_i < t_count; t_i++)
        {
            bool t_authoredOn = this.m_authoredSprites != null
                                && t_i < this.m_authoredSprites.Length
                                && this.m_authoredSprites[t_i] == this.onSprite;

            this.SetBulb(t_i, !t_authoredOn);
        }
    }

    void KillIdle()
    {
        if (this.m_idle != null && this.m_idle.IsActive()) this.m_idle.Kill();
        this.m_idle = null;
    }
}
