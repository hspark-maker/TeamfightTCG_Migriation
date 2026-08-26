using System.Collections.Generic;
using UnityEngine;

/// <summary>카드 한 장의 **무장 이펙트 풀 대여/반납**만 소유한다.
/// 대여분(반납 키 + 오브젝트)을 들고 있는 유일한 자리라, 반납 누락 = 풀 누수라는 위험이 여기 한 곳에 모인다.
///
/// MonoBehaviour가 아니라 순수 C# 객체다 — CardView가 필드로 들고 생성한다.
/// 인스펙터 배선(armedVfxSortingOrder)은 CardView의 SerializeField에 그대로 남고 값만 생성자로 주입된다
/// (프리팹/씬 YAML 재직렬화 회피).
///
/// 프레임 무기 애니(<see cref="WeaponAnimSpec"/>)와는 별개 관심사다: 이쪽은 BattleVfx 풀 대여/반납,
/// 저쪽은 Instantiate/Destroy다.</summary>
public class CardArmedVfx
{
    readonly Transform owner;         // 이펙트를 붙일 부모(= 카드 루트). 반납 시 부모 확인에도 쓴다.
    readonly int       sortingOrder;  // 무장 이펙트를 카드 아트 위로 올릴 정렬 order.

    // 무장 중 카드에 붙어 있는 이펙트(풀 대여분 + 반납 키). 비어 있으면 꺼진 상태.
    readonly List<(string Id, GameObject Go)> armedVfx = new List<(string, GameObject)>();
    GameObject prefabOverride;   // 테스터가 후보를 갈아끼울 때만 사용. null=카드 에셋 값

    public CardArmedVfx(Transform _owner, int _sortingOrder)
    {
        this.owner        = _owner;
        this.sortingOrder = _sortingOrder;
    }

    /// <summary>무장(포커스) 이펙트 토글. 카드 자식으로 붙어 공격 이동/기울기를 그대로 따라간다.
    /// 켜지는 시점 = 무장, 꺼지는 시점 = 적에 닿는 순간(AttackSequence가 false로 호출).
    /// 중복 호출은 무시한다 — 드래그 중 여러 경로에서 불린다.</summary>
    public void SetActive(bool _active, CardInstance _card, bool _enemySide, int _layerId)
    {
        if (_active) Show(_card, _enemySide, _layerId);
        else         Hide();
    }

    /// <summary>무장 이펙트 프리팹을 갈아끼운다(null이면 카드의 AttackEffect가 정의한 Armed 항목 사용).
    /// AttackAnimTester가 후보를 넘겨보며 고를 때 쓴다 — 카드 에셋을 건드리지 않는 런타임 오버라이드.
    /// 켜져 있는 상태에서 바꾸면 즉시 교체된다.</summary>
    public void SetPrefabOverride(GameObject _prefab, CardInstance _card, bool _enemySide, int _layerId)
    {
        if (this.prefabOverride == _prefab) return;
        bool t_wasOn = this.armedVfx.Count > 0;
        Hide();
        this.prefabOverride = _prefab;
        if (t_wasOn) Show(_card, _enemySide, _layerId);
    }

    /// <summary>대여분 반납. 소유자 파괴/카드 교체 등 "무장이 끝나는 모든 경로"에서 불린다.
    /// 대여분을 물고 죽으면 풀이 파괴된 오브젝트를 들고 있게 되므로 CardView.OnDestroy도 이걸 부른다.</summary>
    public void Hide()
    {
        // 부모가 아직 나일 때만 반납 — 자기반납형(PooledParticle) 프리팹과 이중 반납 충돌 방지.
        foreach ((string t_id, GameObject t_go) in this.armedVfx)
            BattleVfx.Release(t_id, t_go, this.owner);
        this.armedVfx.Clear();
    }

    void Show(CardInstance _card, bool _enemySide, int _layerId)
    {
        if (this.armedVfx.Count > 0) return;                  // 이미 켜져 있음
        if (_card == null || !_card.isRevealed) return;       // 뒷면/빈 슬롯은 노출 금지

        // 적 카드는 위아래가 뒤집힌 배치라 오프셋/회전도 뒤집는다(AttackEffect.particles와 같은 flip 규약).
        bool t_flip    = _enemySide;
        int  t_layerId = _layerId;

        if (this.prefabOverride != null)
        {
            // 테스터 오버라이드: 배치값 없이 프리팹만 교체해 본다.
            Spawn(this.prefabOverride, Vector3.zero, Vector3.zero);
            return;
        }

        AttackEffect t_fx = _card.data?.attackEffect;
        if (t_fx == null) return;
        foreach (ParticleEntry t_entry in t_fx.ArmedEntries())
            Spawn(t_entry.prefab, t_entry.localOffset, t_entry.initialRotation);

        void Spawn(GameObject _prefab, Vector3 _offset, Vector3 _euler)
        {
            GameObject t_go = BattleVfx.SpawnAttached(_prefab, this.owner, _offset, _euler, t_flip, out string t_id);
            if (t_go == null) return;
            BattleVfx.ApplySorting(t_go, t_layerId, this.sortingOrder);
            this.armedVfx.Add((t_id, t_go));
        }
    }
}
