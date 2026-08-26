using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 전투 이펙트를 "카드에 붙여서 풀에서 빌려 쓰는" 규약의 단일 지점.
///
/// 부착·flip·정렬·반납이 전부 같은 규칙이어야 하는데, 예전엔 AttackEffect(공격 순간 파티클) /
/// CardView(무장 이펙트) / VfxSlot(테스터)이 각자 구현을 들고 있었다 — 한쪽만 고치면 조용히 갈라진다.
/// 스폰 "시점"은 호출부가 정하고(무장/접촉/공격시작), "어떻게 붙이고 어떻게 반납하는가"는 여기만 안다.
/// </summary>
public static class BattleVfx
{
    // ── 규칙 기반 연출 라이브러리 ────────────────────────────────────────
    // 카드 고유 연출(AttackEffect)과 달리 "키워드/시너지/전투 이벤트가 발동시키는" 연출은
    // 프리팹 배선 지점이 하나여야 한다(씬마다 SerializeField를 늘리지 않기 위해).

    static BattleVfxLibrary s_library;

    public static bool HasLibrary => s_library != null;

    /// <summary>초기화 경로에서 실제 애셋을 주입한다. null이면 기존 값을 유지한다.</summary>
    public static void SetLibrary(BattleVfxLibrary _library)
    {
        if (_library == null) return;
        if (s_library != null && s_library != _library)
            Debug.LogWarning($"[BattleVfx] 이미 주입된 라이브러리를 교체합니다: {s_library.name} -> {_library.name}");
        s_library = _library;
    }

    /// <summary>형태값(힐러 커브 등) 접근용. 미배선이면 null — 호출부가 폴백을 정한다.</summary>
    public static BattleVfxLibrary Library => s_library;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        s_library = null;
        s_collectBuffer.Clear();
    }

    public static bool TryGetEntry(BattleVfxId _id, out VfxEntry _entry)
    {
        _entry = default;
        return s_library != null && s_library.TryGet(_id, out _entry);
    }

    /// <summary>라이브러리 항목을 월드 좌표에 1회 스폰하고 수명까지 맡긴다(터지고 사라지는 연출).</summary>
    public static VfxHandle Play(BattleVfxId _id, Vector3 _pos, int _sortingLayerId)
    {
        VfxHandle t_handle = Spawn(_id, _pos, _sortingLayerId);
        t_handle.ReleaseAfterLifetime();
        return t_handle;
    }

    /// <summary>월드 좌표에 1회 스폰하되 **방향으로 눕힌다**(항목의 alignToDirection이 켜져 있을 때만 —
    /// PlayAttached와 같은 규약). 카드에 붙이지 않는 이유: 이 연출을 쓰는 쪽(피니시 임팩트)은
    /// 대상이 곧 사망 연출로 사라지므로, 붙이면 이펙트가 같이 페이드된다.</summary>
    public static VfxHandle PlayDirected(BattleVfxId _id, Vector3 _pos, int _sortingLayerId, Vector3 _direction)
    {
        if (!TryGetEntry(_id, out VfxEntry t_entry)) return default;

        VfxHandle t_handle = Spawn(t_entry, _pos, _sortingLayerId);
        if (t_handle.Valid && t_entry.alignToDirection && _direction.sqrMagnitude > 1e-6f)
            t_handle.Go.transform.rotation = Quaternion.LookRotation(_direction.normalized, Vector3.back)
                                           * Quaternion.Euler(t_entry.initialRotation);

        t_handle.ReleaseAfterLifetime();
        return t_handle;
    }

    /// <summary>수명을 **호출부가 쥐는** 스폰(투사체처럼 살아 움직이는 것). 다 쓰면 Release를 불러야 한다.</summary>
    public static VfxHandle Spawn(BattleVfxId _id, Vector3 _pos, int _sortingLayerId)
    {
        if (!TryGetEntry(_id, out VfxEntry t_entry)) return default;

        string t_poolId = t_entry.prefab.GetInstanceID().ToString();
        ParticlePooler.Register(t_poolId, t_entry.prefab);
        GameObject t_go = ParticlePooler.Spawn(t_poolId, _pos + t_entry.localOffset,
                                               Quaternion.Euler(t_entry.initialRotation));
        if (t_go == null) return default;

        ApplySorting(t_go, _sortingLayerId, t_entry.sortingOrder);
        ApplyScale(t_go, t_entry.prefab, t_entry.scale);
        return new VfxHandle(t_poolId, t_go, t_entry.lifetime);
    }

    /// <summary>라이브러리(id)를 거치지 않고 **항목을 직접 받아** 스폰. 배선 지점이 라이브러리가 아니라
    /// 다른 에셋인 연출용 — 시너지 고유 연출은 그 시너지의 SynergyVfxConfig가 VfxEntry를 들고 있다.
    /// 스폰/반납/정렬 규약은 여전히 여기 하나뿐이다(id 경로와 같은 코드를 탄다).
    /// 프리팹이 비어 있으면 무효 핸들 — 호출부는 Valid로 보고 연출을 생략한다.</summary>
    public static VfxHandle Spawn(VfxEntry _entry, Vector3 _pos, int _sortingLayerId)
    {
        if (_entry.prefab == null) return default;

        string t_poolId = _entry.prefab.GetInstanceID().ToString();
        ParticlePooler.Register(t_poolId, _entry.prefab);
        GameObject t_go = ParticlePooler.Spawn(t_poolId, _pos + _entry.localOffset,
                                               Quaternion.Euler(_entry.initialRotation));
        if (t_go == null) return default;

        ApplySorting(t_go, _sortingLayerId, _entry.sortingOrder);
        ApplyScale(t_go, _entry.prefab, _entry.scale);
        return new VfxHandle(t_poolId, t_go, _entry.lifetime);
    }

    /// <summary>항목을 직접 받아 1회 스폰하고 수명까지 맡긴다(터지고 사라지는 연출).</summary>
    public static VfxHandle Play(VfxEntry _entry, Vector3 _pos, int _sortingLayerId)
    {
        VfxHandle t_handle = Spawn(_entry, _pos, _sortingLayerId);
        t_handle.ReleaseAfterLifetime();
        return t_handle;
    }

    /// <summary>라이브러리를 거치지 않고 **프리팹을 직접** 빌려 쓰는 스폰. 카드마다 다른 연출처럼
    /// 카드 데이터에 별도 배선된 경우에만 쓴다 — 규칙 기반 연출은 여전히 id(Spawn)로만 간다.
    /// 풀·정렬 규약은 Spawn과 동일하고, 수명은 호출부가 쥔다.</summary>
    public static VfxHandle SpawnPrefab(GameObject _prefab, Vector3 _pos, int _sortingLayerId, int _sortingOrder = 30)
    {
        if (_prefab == null) return default;

        string t_poolId = _prefab.GetInstanceID().ToString();
        ParticlePooler.Register(t_poolId, _prefab);
        GameObject t_go = ParticlePooler.Spawn(t_poolId, _pos, Quaternion.identity);
        if (t_go == null) return default;

        ApplySorting(t_go, _sortingLayerId, _sortingOrder);
        return new VfxHandle(t_poolId, t_go, 0f);
    }

    // 같은 id의 항목을 겹쳐 재생할 때 쓰는 재사용 버퍼(피격 = 섬광 + 먼지처럼 여러 프리팹).
    // 연출 스폰은 전부 메인 스레드 한 프레임 안에서 끝나므로 정적 버퍼로 충분하다(할당 0).
    static readonly List<VfxEntry> s_collectBuffer = new List<VfxEntry>();

    /// <summary>라이브러리에서 이 id의 항목을 **전부** _anchor 자식으로 붙여 스폰하고 수명까지 맡긴다 —
    /// 붙어 있으므로 연출 도중 카드가 움직여도(박치기/복귀) 같이 따라간다.
    /// 오프셋·회전 flip 규약은 SpawnAttached와 동일(적 카드는 배치가 위아래로 뒤집혀 있다).
    ///
    /// _direction을 주고 항목의 alignToDirection이 켜져 있으면 그 방향으로 눕혀 스폰한다
    /// (피격 반대 방향으로 튀는 먼지 등). 이때 flip은 적용하지 않는다 — 방향이 이미 명시됐다.
    ///
    /// _strength01(0~1)은 항목의 countByStrength/speedByStrength가 켜져 있을 때만 쓰인다 —
    /// 피격이면 HitImpact.Strength01(피해/최대체력). 세게 맞을수록 먼지가 많아지고 빨라진다.
    /// 반응 배선이 없는 항목은 이 값을 무시하므로, 호출부는 세기를 항상 넘겨도 된다.
    ///
    /// _isCounter=반격으로 되받는 피격이면 skipOnCounter 항목(먼지·파편)을 건너뛴다 —
    /// 주 타격과 반격이 같은 먼지를 양쪽에서 일으키면 누가 맞은 건지 안 읽힌다.</summary>
    public static void PlayAttached(BattleVfxId _id, Transform _anchor, bool _flip, int _sortingLayerId,
        Vector3 _direction = default, float _strength01 = 0f, bool _isCounter = false)
    {
        if (s_library == null || s_library.Collect(_id, s_collectBuffer) == 0) return;

        bool  t_hasDir = _direction.sqrMagnitude > 1e-6f;
        float t_s01    = Mathf.Clamp01(_strength01);

        // 방향을 못 받은 호출(공격자 뷰 없는 피해·디버그 재생)도 **화면 평면 안에서** 튀어야 한다.
        // 예전엔 이때 항목 회전값을 그대로 뒀는데, 그러면 콘이 월드 +Z(화면 안쪽)로 뿜어 아군 쪽은
        // 아무것도 안 보였다 — 적 쪽만 보였던 건 flip의 180도 X회전이 우연히 콘을 카메라 쪽으로
        // 돌려놨기 때문이다(연출이 아니라 사고였다). 아군은 위에서 맞으니 아래로, 적은 그 반대.
        Vector3 t_alignDir = t_hasDir ? _direction.normalized : (_flip ? Vector3.up : Vector3.down);

        foreach (VfxEntry t_entry in s_collectBuffer)
        {
            if (_isCounter && t_entry.skipOnCounter) continue;   // 반격 맞은 쪽엔 먼지를 안 일으킨다

            GameObject t_go = SpawnAttached(t_entry.prefab, _anchor, t_entry.localOffset,
                                            t_entry.initialRotation, _flip, out string t_poolId);
            if (t_go == null) continue;

            if (t_entry.alignToDirection)
                t_go.transform.rotation = Quaternion.LookRotation(t_alignDir, Vector3.back)
                                        * Quaternion.Euler(t_entry.initialRotation);

            ApplySorting(t_go, _sortingLayerId, t_entry.sortingOrder);
            ApplyScale(t_go, t_entry.prefab, t_entry.scale);
            ApplyStrength(t_go, t_entry, t_s01);
            new VfxHandle(t_poolId, t_go, t_entry.lifetime).ReleaseAfterLifetime();
        }
    }

    /// <summary>항목 배율을 **프리팹 원본 크기 기준으로** 다시 계산해 적용한다.
    /// 인스턴스의 현재 크기에 곱하지 않는 이유: 풀에서 재사용되면 지난 배율이 그대로 남아 누적된다.
    /// 배율이 0 이하(미설정)여도 원본 크기를 다시 써 넣어야 지난 스폰의 배율이 남지 않는다.</summary>
    static void ApplyScale(GameObject _go, GameObject _prefab, float _scale)
    {
        if (_go == null || _prefab == null) return;

        float t_mul = _scale > 0f ? _scale : 1f;
        _go.transform.localScale = _prefab.transform.localScale * t_mul;
    }

    /// <summary>항목에 세기 반응이 배선돼 있으면 파티클 방출량/속도를 그 배율로 덮어쓴다.
    /// y가 0 이하 = 반응 없음이라, 미배선 항목은 컴포넌트조차 붙지 않는다(기존 연출 무변경).</summary>
    static void ApplyStrength(GameObject _go, VfxEntry _entry, float _strength01)
    {
        float t_count = _entry.countByStrength.y > 0f
            ? Mathf.Lerp(_entry.countByStrength.x, _entry.countByStrength.y, _strength01) : 1f;
        float t_speed = _entry.speedByStrength.y > 0f
            ? Mathf.Lerp(_entry.speedByStrength.x, _entry.speedByStrength.y, _strength01) : 1f;

        if (Mathf.Approximately(t_count, 1f) && Mathf.Approximately(t_speed, 1f)) return;
        VfxStrengthScaler.Apply(_go, t_count, t_speed);
    }

    /// <summary>_anchor 자식으로 붙여 스폰. 붙어 있으므로 카드가 움직이면 이펙트도 따라간다.
    /// _flip=true(적 카드)면 오프셋 부호와 X축 180도 회전을 적용한다 — 아군/적 배치가 위아래로 뒤집혀 있어서다.
    /// 반환값이 null이면 스폰 실패(프리팹/앵커 없음).</summary>
    public static GameObject SpawnAttached(GameObject _prefab, Transform _anchor,
        Vector3 _localOffset, Vector3 _euler, bool _flip, out string _id)
    {
        _id = null;
        if (_prefab == null || _anchor == null) return null;

        Vector3    t_off = _flip ? -_localOffset : _localOffset;
        Quaternion t_rot = Quaternion.Euler(_euler);
        if (_flip) t_rot = Quaternion.Euler(180f, 0f, 0f) * t_rot;

        _id = _prefab.GetInstanceID().ToString();
        ParticlePooler.Register(_id, _prefab);
        return ParticlePooler.Spawn(_id, _anchor.TransformPoint(t_off), t_rot, _anchor);
    }

    /// <summary>풀 반납. _expectedParent를 주면 그 부모일 때만 반납한다 —
    /// PooledParticle이 붙은 프리팹은 스스로 반납하며 부모를 끊으므로, 그 뒤 또 반납하면
    /// 같은 오브젝트가 풀에 두 번 들어간다.</summary>
    public static void Release(string _id, GameObject _go, Transform _expectedParent = null)
    {
        if (_go == null || string.IsNullOrEmpty(_id)) return;
        if (_expectedParent != null && _go.transform.parent != _expectedParent) return;
        ParticlePooler.Release(_id, _go);
    }

    /// <summary>정렬 보정. 구매 에셋 VFX는 대개 Default 레이어라, Card 레이어인 카드 아트 **뒤로** 깔려 안 보인다.
    /// 정렬은 레이어가 order보다 먼저 판정되므로 레이어부터 맞춘 뒤 order를 올린다.
    /// 풀 재사용분은 지난 값이 남아 있어 매 스폰마다 다시 잡아야 한다.</summary>
    public static void ApplySorting(GameObject _go, int _sortingLayerId, int _order)
    {
        if (_go == null) return;
        foreach (Renderer t_r in _go.GetComponentsInChildren<Renderer>(true))
        {
            t_r.sortingLayerID = _sortingLayerId;
            t_r.sortingOrder   = _order;
        }
    }

    /// <summary>PooledParticle이 없어 스스로 반납 못 하는 프리팹용 수명 타이머.
    /// 있으면 호출하지 말 것(이중 반납).</summary>
    public static async UniTask ReleaseAfter(string _id, GameObject _go, float _seconds)
    {
        await UniTask.Delay((int)(Mathf.Max(0.05f, _seconds) * 1000));
        if (_go != null) ParticlePooler.Release(_id, _go);
    }

    /// <summary>이 프리팹이 스스로 풀에 돌아가는가(PooledParticle 보유). false면 호출부가 수명을 걸어야 한다.</summary>
    public static bool SelfReleasing(GameObject _go) => _go != null && _go.GetComponent<PooledParticle>() != null;
}

/// <summary>라이브러리에서 빌려온 연출 1개. 반납은 이 핸들로만 — 풀ID를 호출부가 들고 다니지 않게 한다.
/// 미배선/스폰 실패면 Valid=false이고 모든 호출이 무동작이라, 호출부에 null 분기가 늘지 않는다.</summary>
public readonly struct VfxHandle
{
    public readonly string     PoolId;
    public readonly GameObject Go;
    public readonly float      Lifetime;

    public VfxHandle(string _poolId, GameObject _go, float _lifetime)
    {
        this.PoolId   = _poolId;
        this.Go       = _go;
        this.Lifetime = _lifetime;
    }

    public bool Valid => this.Go != null && !string.IsNullOrEmpty(this.PoolId);

    /// <summary>_extraDelay 뒤 반납. 자기반납형(PooledParticle) 프리팹은 건드리지 않는다(이중 반납).</summary>
    public void Release(float _extraDelay = 0f)
    {
        if (!Valid || BattleVfx.SelfReleasing(this.Go)) return;
        BattleVfx.ReleaseAfter(this.PoolId, this.Go, _extraDelay).Forget();
    }

    /// <summary>항목에 적힌 수명만큼 두고 반납(터지고 사라지는 연출의 기본).</summary>
    public void ReleaseAfterLifetime() => Release(this.Lifetime);
}
