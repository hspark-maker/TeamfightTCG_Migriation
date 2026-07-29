#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 테스트 씬용 "이펙트 후보 슬롯". 구매 에셋 폴더(수천 개 프리팹)를 통째로 후보로 잡고
/// 키 입력으로 하나씩 넘겨보며 공격/피격 이펙트를 눈으로 고르기 위한 도구.
/// 에디터에서만 폴더 스캔(AssetDatabase). 빌드에서는 prefabs 배열만 사용.
/// 프리팹은 넘길 때 지연 로드 → 폴더에 1000개가 있어도 시작이 무겁지 않다.
/// 프로덕션 경로(AttackEffect/AttackSequence)는 건드리지 않는다. 테스트 전용.
/// </summary>
[System.Serializable]
public class VfxSlot
{
    [Tooltip("에디터 전용. 이 폴더 아래 프리팹 전부가 후보. 비우면 prefabs 배열만 사용.")]
    public string folder = "Assets/PurchasedAssets";
    [Tooltip("파일명에 이 문자열이 포함된 프리팹만 후보(대소문자 무시). 예: hit, slash, fire.")]
    public string filter = "";
    [Tooltip("폴더 대신 직접 지정할 후보들. 비어있지 않으면 이쪽이 우선.")]
    public GameObject[] prefabs;

    [Header("배치/재생")]
    public bool    use          = true;
    public Vector3 localOffset  = Vector3.zero;
    public float   scale        = 1f;
    public float   lifetime     = 2f;    // 이 시간 뒤 파괴.
    public int     sortingOrder = 100;   // 카드(SpriteRenderer) 위로 올리기.
    [Min(0f)] public float spawnDelay = 0f;
    [Tooltip("대상 트랜스폼에 붙여 따라가게 한다(박치기로 카드가 움직여도 이펙트가 같이 이동).")]
    public bool follow = false;

    List<string> paths;          // 에디터 폴더 스캔 결과(경로만, 프리팹은 지연 로드).
    int          index;
    GameObject   cached;
    string       cachedKey;

    public int  Count  => this.prefabs != null && this.prefabs.Length > 0 ? this.prefabs.Length : (Candidates()?.Count ?? 0);
    public int  Index  => this.Count == 0 ? -1 : ((this.index % this.Count) + this.Count) % this.Count;
    public string CurrentName
    {
        get
        {
            if (!this.use)        return "(끔)";
            if (this.Count == 0)  return "(후보 없음)";
            if (this.prefabs != null && this.prefabs.Length > 0)
                return this.prefabs[this.Index] != null ? this.prefabs[this.Index].name : "(null)";
            string t_p = Candidates()[this.Index];
            return System.IO.Path.GetFileNameWithoutExtension(t_p);
        }
    }

    /// <summary>현재 후보의 에셋 경로(직접 지정 배열이면 프리팹 이름). 콘솔 로그로 고른 것 남길 때 사용.</summary>
    public string CurrentPath
    {
        get
        {
            if (this.prefabs != null && this.prefabs.Length > 0) return this.CurrentName;
            var t_c = Candidates();
            return (t_c != null && t_c.Count > 0) ? t_c[this.Index] : "(none)";
        }
    }

    List<string> Candidates()
    {
        if (this.paths != null) return this.paths;
        this.paths = new List<string>();
#if UNITY_EDITOR
        if (!string.IsNullOrEmpty(this.folder) && AssetDatabase.IsValidFolder(this.folder))
        {
            string[] t_guids = AssetDatabase.FindAssets("t:Prefab", new[] { this.folder });
            foreach (string t_g in t_guids)
            {
                string t_path = AssetDatabase.GUIDToAssetPath(t_g);
                if (!string.IsNullOrEmpty(this.filter) &&
                    t_path.IndexOf(this.filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                this.paths.Add(t_path);
            }
            this.paths.Sort(string.CompareOrdinal);
        }
#endif
        return this.paths;
    }

    /// <summary>폴더/필터를 인스펙터에서 바꾼 뒤 다시 스캔하게 만든다.</summary>
    public void Rescan() { this.paths = null; this.cached = null; this.cachedKey = null; this.index = 0; }

    public void Cycle(int _delta)
    {
        if (this.Count == 0) return;
        this.index = this.Index + _delta;
        this.cached = null; this.cachedKey = null;
    }

    GameObject CurrentPrefab()
    {
        if (this.prefabs != null && this.prefabs.Length > 0) return this.prefabs[this.Index];
        var t_c = Candidates();
        if (t_c == null || t_c.Count == 0) return null;
        string t_path = t_c[this.Index];
        if (this.cached != null && this.cachedKey == t_path) return this.cached;
#if UNITY_EDITOR
        this.cached    = AssetDatabase.LoadAssetAtPath<GameObject>(t_path);
        this.cachedKey = t_path;
#endif
        return this.cached;
    }

    /// <summary>대상 위치에 현재 후보를 즉시 생성. spawnDelay는 호출부가 처리(await 후 호출).
    /// _flip=true면 좌우 오프셋 반전 + Y축 180도 회전(적 방향 공격용).</summary>
    public GameObject Spawn(Transform _anchor, bool _flip = false)
    {
        if (!this.use || _anchor == null) return null;
        GameObject t_prefab = CurrentPrefab();
        if (t_prefab == null) return null;

        Vector3    t_offset = _flip ? new Vector3(-this.localOffset.x, -this.localOffset.y, this.localOffset.z) : this.localOffset;
        Quaternion t_rot    = _flip ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;

        GameObject t_go = Object.Instantiate(t_prefab, _anchor.TransformPoint(t_offset), t_rot);
        if (this.follow) t_go.transform.SetParent(_anchor, worldPositionStays: true);
        t_go.transform.localScale = t_prefab.transform.localScale * this.scale;

        // 구매 에셋 VFX는 대개 sortingOrder 0 → 카드 스프라이트에 가려진다. 전부 위로 올린다.
        foreach (Renderer t_r in t_go.GetComponentsInChildren<Renderer>(true))
            t_r.sortingOrder = this.sortingOrder;

        Object.Destroy(t_go, Mathf.Max(0.05f, this.lifetime));
        return t_go;
    }
}
