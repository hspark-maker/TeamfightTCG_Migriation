using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>표시 수명과 Addressables 참조 수명을 맞춘다. 재사용된 타일에 이전 요청이 도착하지 않는다.</summary>
public sealed class CardArtBinding : MonoBehaviour
{
    string m_address;
    Action<Sprite> m_apply;
    CardArtCache.Lease m_lease;

    public static void Bind(Image image, string address, Action<Sprite> changed = null)
    {
        if (image == null) return;
        Bind(image.gameObject, address, sprite =>
        {
            if (image == null) return;
            image.sprite = sprite;
            image.enabled = sprite != null;
            changed?.Invoke(sprite);
        });
    }

    public static void Bind(GameObject owner, string address, Action<Sprite> apply)
    {
        var binding = owner.GetComponent<CardArtBinding>();
        if (binding == null) binding = owner.AddComponent<CardArtBinding>();
        if (binding.m_address != address)
        {
            binding.Release();
            binding.m_address = address;
        }
        binding.m_apply = apply;
        binding.Acquire();
    }

    public static void Clear(GameObject owner)
    {
        var binding = owner.GetComponent<CardArtBinding>();
        if (binding == null) return;
        binding.Release();
        binding.m_address = null;
        binding.m_apply = null;
    }

    void Acquire()
    {
        if (!isActiveAndEnabled) return;
        if (m_lease == null && !string.IsNullOrEmpty(m_address))
            m_lease = CardArtCache.Acquire(m_address, Apply);
        Apply(m_lease?.Sprite);
    }

    void Apply(Sprite sprite) => m_apply?.Invoke(sprite);

    void Release()
    {
        // 렌더러가 해제된 Texture를 붙잡지 않도록 참조 반환 전에 비운다.
        Apply(null);
        m_lease?.Dispose();
        m_lease = null;
    }

    void OnEnable() => Acquire();
    void OnDisable() => Release();
    void OnDestroy() => Release();
}
