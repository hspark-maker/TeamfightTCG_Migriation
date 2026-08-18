using System.Collections.Generic;
using UnityEngine;

/// <summary>상단바의 **변동 칸** — 지금 열린 화면이 요구하는 재화를 따라간다.
///
/// 상단바는 두 종류로 갈린다. 골드·다이아는 어느 화면에서나 뜻이 있어 칸이 **고정**이고(그 칸의
/// <see cref="CurrencyHud.Type"/>이 곧 그 칸의 재화다), 조각·에너지는 쓰는 화면이 정해져 있어
/// 한 칸을 **돌려 쓴다**. 이 컴포넌트는 그 돌려 쓰는 칸 하나를 가리키는 것이 전부다 —
/// 칸이 셋뿐인데 재화가 넷이라 생긴 자리다.
///
/// 요청은 owner 키로 쌓이고 걷히는 재화는 **가장 마지막 요청**이다(<see cref="LobbyShellBars"/>와 같은 관용구).
/// 도감 탭이 에너지를 요구한 위에 카드 상세가 조각을 요구하면 조각이 뜨고, 상세를 닫으면 에너지로 돌아온다.
/// 요청이 하나도 없으면 <see cref="defaultType"/>으로 되돌아간다.
///
/// 이 칸이 중요한 이유는 잔액 표시보다 **획득 연출의 도착 지점**이다 —
/// 그 재화의 대표 HUD가 화면에 없으면 <see cref="CurrencyGainEffectPlayer"/>가 그 재화 연출을 통째로 건너뛴다.</summary>
public class ContextCurrencySlot : MonoBehaviour
{
    sealed class Entry
    {
        public object owner;
        public ECurrencyType type;
    }

    static readonly List<Entry> s_requests = new List<Entry>();
    static CurrencyHud s_slot;
    // 물릴 때 함께 받아 둔다 — 칸 자신은 런타임 값만 들고 있어 저작값을 되물을 수 없다.
    static ECurrencyType s_defaultType;

    [Tooltip("돌려 쓸 칸. 고정 칸(골드·다이아)을 여기 물리지 말 것 — 그 칸의 재화가 화면마다 갈려 버린다.")]
    [SerializeField] CurrencyHud slot;

    [Tooltip("요구가 하나도 없을 때 이 칸이 돌아갈 재화. 어느 화면에도 속하지 않는 기본 자리다.\n\n" +
             "여기가 이 칸의 **저작값**이다 — 칸에 붙은 CurrencyHud의 type은 런타임에 갈리므로 " +
             "그 값으로 기본을 판단하지 말 것(프리팹에 남는 값은 에디터에서 보이는 첫 그림일 뿐이다).")]
    [SerializeField] ECurrencyType defaultType = ECurrencyType.Shard;

    void OnEnable()
    {
        if (this.slot == null)
        {
            Debug.LogError("[ContextCurrencySlot] 돌려 쓸 칸이 미배선이라 재화가 화면을 따라가지 않는다.", this);

            return;
        }

        // 화면당 한 장. 겹치면 마지막이 이기는데, 그러면 진 쪽 칸이 옛 재화에 굳은 채 남아 조용히 틀린다.
        if (s_slot != null && s_slot != this.slot)
            Debug.LogWarning("[ContextCurrencySlot] 변동 칸이 둘 이상이다 — 마지막에 켜진 쪽만 화면을 따라간다.", this);

        // 물리는 순간 쌓여 있던 요구가 적용된다 — 탭이 먼저 서고 칸이 나중에 켜지는 순서에서도 재화가 맞는다.
        s_slot = this.slot;
        s_defaultType = this.defaultType;
        Apply();
    }

    void OnDisable()
    {
        // 씬 전환은 새 칸의 OnEnable이 먼저 도는 순서가 가능하다 — 본인일 때만 놓아야 새 등록을 밟지 않는다.
        if (s_slot == this.slot) s_slot = null;
    }

    /// <summary>_owner가 이 칸에 띄우길 원하는 재화.
    /// 같은 주인이 다시 부르면 요청이 갱신된다. 고정 칸이 이미 맡은 재화를 요구해도 되지만 무시된다(아래 Apply).</summary>
    public static void Request(object _owner, ECurrencyType _type)
    {
        if (_owner == null) return;

        Prune();
        RemoveOwner(_owner);
        s_requests.Add(new Entry { owner = _owner, type = _type });
        Apply();
    }

    /// <summary>요청을 물린다. 아래에 깔린 화면의 요청이 다시 적용된다.</summary>
    public static void Release(object _owner)
    {
        if (_owner == null) return;

        RemoveOwner(_owner);
        Prune();
        Apply();
    }

    static void Apply()
    {
        if (s_slot == null) return;

        // 위에서부터 훑되 고정 칸이 이미 맡은 재화는 건너뛴다 — 그런 요청은 "요구가 없는 것"과 같다.
        for (int t_i = s_requests.Count - 1; t_i >= 0; t_i--)
        {
            if (IsCoveredElsewhere(s_requests[t_i].type)) continue;

            s_slot.SetType(s_requests[t_i].type);

            return;
        }

        s_slot.SetType(s_defaultType);
    }

    /// <summary>그 재화를 이미 다른 칸이 띄우고 있는가.
    /// 고정 칸(골드·다이아)이 맡은 재화를 변동 칸까지 받으면 같은 재화가 두 칸을 먹고,
    /// 정작 변동 칸이 맡던 재화가 화면에서 사라진다 — 진화 게이트(다이아)에서 실제로 그렇게 됐다.</summary>
    static bool IsCoveredElsewhere(ECurrencyType _type)
        => CurrencyHud.TryGet(_type, out CurrencyHud t_hud) && t_hud != s_slot;

    static void RemoveOwner(object _owner)
    {
        for (int t_i = s_requests.Count - 1; t_i >= 0; t_i--)
            if (ReferenceEquals(s_requests[t_i].owner, _owner)) s_requests.RemoveAt(t_i);
    }

    // 요청자가 Release 없이 파괴되면 그 재화가 칸에 영영 붙박이가 된다 — 호출마다 걷어낸다.
    static void Prune()
    {
        for (int t_i = s_requests.Count - 1; t_i >= 0; t_i--)
            if (s_requests[t_i].owner is Object t_owner && t_owner == null) s_requests.RemoveAt(t_i);
    }
}
