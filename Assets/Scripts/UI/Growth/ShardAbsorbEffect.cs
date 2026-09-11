using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>강화 버튼의 샤드가 카드로 흡수되는 짧은 연출. 서버 처리와 별도로 재생한다.</summary>
public sealed class ShardAbsorbEffect : MonoBehaviour
{
    const int MaxIcons = 8;
    readonly Image[] m_icons = new Image[MaxIcons];
    RectTransform m_layer;
    readonly Sequence[] m_sequences = new Sequence[MaxIcons];
    int m_nextIcon;

    public bool IsPlaying
    {
        get
        {
            foreach (Sequence t_sequence in m_sequences)
                if (t_sequence != null && t_sequence.IsActive()) return true;
            return false;
        }
    }

    public void Play(RectTransform source, RectTransform target)
    {
        if (!isActiveAndEnabled || source == null || target == null) return;

        Sprite t_sprite = CurrencyLook.IconOf(ECurrencyType.Shard);
        Canvas t_canvas = GetComponentInParent<Canvas>();
        if (t_sprite == null || t_canvas == null) return;

        EnsureLayer(t_canvas);
        if (!TryGetCenter(source, out Vector2 t_from) || !TryGetCenter(target, out Vector2 t_to)) return;

        int t_slot = m_nextIcon;
        m_nextIcon = (m_nextIcon + 1) % MaxIcons;
        m_sequences[t_slot]?.Kill();
        var t_settings = new UiGainBurst.Settings(1, 18f, 0.07f, 0.25f,
            0f, 0.06f, 0f, 360f, _gatherScale: 0.15f, _spinDegrees: 30f, _arcHeight: 55f);
        m_layer.gameObject.SetActive(true);
        m_layer.SetAsLastSibling();
        Sequence t_sequence = UiGainBurst.Build(m_layer, t_from, t_to, t_settings,
            _spawn: _index => GetIcon(t_slot, t_sprite),
            _despawn: _icon => _icon.gameObject.SetActive(false));
        m_sequences[t_slot] = t_sequence;
        t_sequence.SetUpdate(true).SetLink(gameObject).OnComplete(() =>
        {
            m_sequences[t_slot] = null;
        });
    }

    public void Stop()
    {
        for (int t_i = 0; t_i < MaxIcons; t_i++)
        {
            m_sequences[t_i]?.Kill();
            m_sequences[t_i] = null;
        }
        HideIcons();
    }

    void OnDisable() => Stop();

    void OnDestroy()
    {
        Stop();
        if (m_layer != null) Destroy(m_layer.gameObject);
    }

    void EnsureLayer(Canvas canvas)
    {
        if (m_layer == null)
            m_layer = (RectTransform)new GameObject("ShardAbsorbLayer", typeof(RectTransform)).transform;

        m_layer.SetParent(canvas.transform, false);
        m_layer.anchorMin = Vector2.zero;
        m_layer.anchorMax = Vector2.one;
        m_layer.pivot = new Vector2(0.5f, 0.5f);
        m_layer.offsetMin = m_layer.offsetMax = Vector2.zero;
        m_layer.localScale = Vector3.one;
        m_layer.localRotation = Quaternion.identity;
    }

    bool TryGetCenter(RectTransform rect, out Vector2 position)
    {
        Vector3 t_world = rect.TransformPoint(rect.rect.center);
        Vector2 t_screen = RectTransformUtility.WorldToScreenPoint(CameraOf(rect), t_world);
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            m_layer, t_screen, CameraOf(m_layer), out position);
    }

    static Camera CameraOf(RectTransform rect)
    {
        Canvas t_canvas = rect.GetComponentInParent<Canvas>();
        if (t_canvas == null) return null;
        Canvas t_root = t_canvas.rootCanvas;
        return t_root.renderMode == RenderMode.ScreenSpaceOverlay ? null : t_root.worldCamera;
    }

    RectTransform GetIcon(int index, Sprite sprite)
    {
        Image t_icon = m_icons[index];
        if (t_icon == null)
        {
            var t_object = new GameObject("Shard", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            t_icon = t_object.GetComponent<Image>();
            t_icon.raycastTarget = false;
            t_icon.preserveAspect = true;
            t_icon.rectTransform.sizeDelta = new Vector2(36f, 36f);
            m_icons[index] = t_icon;
        }

        t_icon.sprite = sprite;
        t_icon.color = Color.white;
        t_icon.gameObject.SetActive(true);
        return t_icon.rectTransform;
    }

    void HideIcons()
    {
        foreach (Image t_icon in m_icons)
        {
            if (t_icon == null) continue;
            t_icon.rectTransform.localScale = Vector3.one;
            t_icon.rectTransform.localRotation = Quaternion.identity;
            t_icon.color = Color.white;
            t_icon.gameObject.SetActive(false);
        }
        if (m_layer != null) m_layer.gameObject.SetActive(false);
    }
}
