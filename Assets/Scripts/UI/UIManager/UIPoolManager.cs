using System;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

// 팝업 인프라는 이걸 쓰는 UI들이 켜지기 전에 서 있어야 한다 —
// 기본 순서면 로비 탭 UI의 OnEnable이 Awake보다 먼저 돌아 instance가 아직 없다.
[DefaultExecutionOrder(-100)]
public class UIPoolManager : MonoBehaviour
{
    public static UIPoolManager instance;

    static bool s_nullWarned;

    /// <summary>외부 접근용. instance가 없으면(씬에 UIPoolManager 미배치) 에러 로그를 1회 남긴다.
    /// 예전엔 `instance?.`로 조용히 무시돼 팝업/오버레이가 안 떠도 원인을 알 수 없었다(튜토리얼 정보확인 버그).</summary>
    public static UIPoolManager Instance
    {
        get
        {
            if (instance == null && !s_nullWarned)
            {
                s_nullWarned = true;
                Debug.LogError("[UIPoolManager] instance 없음 — 현재 씬에 UIPoolManager가 배치되지 않았습니다. " +
                               "팝업/오버레이(카드 정보, 시너지 설명, YN 팝업 등)가 동작하지 않습니다.");
            }
            return instance;
        }
    }

    [SerializeField] Canvas canvas;
    [SerializeField] Transform uiRoot;

    readonly Dictionary<Type, PooledUIBase> activeUIs = new Dictionary<Type, PooledUIBase>();

    private void Awake()
    {
        if (!InitializeSingleton()) return;
        InitializeReferences();
    }

    bool InitializeSingleton()
    {
        if (instance != null)
        {
            Destroy(gameObject);
            return false;
        }

        instance = this;
        s_nullWarned = false;
        DontDestroyOnLoad(transform.root.gameObject);   // 부트 프리팹의 자식이라 루트 기준(단독 배치면 자기 자신)
        return true;
    }

    void InitializeReferences()
    {
        if (this.canvas == null)
            this.canvas = GetComponent<Canvas>();
        if (this.uiRoot == null)
            this.uiRoot = this.canvas.transform;
    }

    public void DestroyAllUI()
    {
        foreach (var ui in activeUIs)
        {
            Destroy(ui.Value.gameObject);
        }
        this.activeUIs.Clear();
    }

    public T GetUI<T>() where T : PooledUIBase
    {
        if (this.activeUIs.TryGetValue(typeof(T), out var t_ui))
            return t_ui as T;

        LogUtil.Log($"No Such UI {typeof(T).Name}");
        return null;
    }

    public T HideUI<T>() where T : PooledUIBase
    {
        if (this.activeUIs.TryGetValue(typeof(T), out var t_ui))
        {
            t_ui.Hide();
            return t_ui as T;
        }

        LogUtil.Log($"No Such UI {typeof(T).Name}");
        return null;
    }

    public T ShowUI<T>() where T : PooledUIBase
    {
        if (this.activeUIs.TryGetValue(typeof(T), out var t_ui))
        {
            t_ui.Show();
            return t_ui as T;
        }

        LogUtil.Log($"No Such UI {typeof(T).Name}");
        return null;
    }

    public T ToggleUI<T>() where T : PooledUIBase
    {
        if (this.activeUIs.TryGetValue(typeof(T), out var t_existingUI))
        {
            if (t_existingUI.isShow)
                t_existingUI.Hide();
            else
                t_existingUI.Show();

            return t_existingUI as T;
        }

        LogUtil.Log($"No Such UI {typeof(T).Name}");
        return null;
    }

    public T AddOrUpdateUI<T>(UIData _data = null) where T : PooledUIBase
    {
        if (this.activeUIs.TryGetValue(typeof(T), out var existingUI))
        {
            existingUI.transform.SetAsLastSibling();
            existingUI.Initialization(_data);
            existingUI.Show();
            return existingUI as T;
        }

        GameObject uiPrefab = DataLibrary.instance.GetUI<T>();
        if (uiPrefab == null)
        {
            Debug.LogError($"UI Prefab Not Exist: {typeof(T).Name}");
            return null;
        }

        DumpPrefabSource(uiPrefab);   // TODO 임시 진단용 — 폰트 이슈 확인 끝나면 제거할 것.

        T uiInstance = Instantiate(uiPrefab, uiRoot).GetComponent<T>();
        if (uiInstance == null)
        {
            Debug.LogError($"UI Component Not Exist: {typeof(T).Name}");
            return null;
        }

        if (_data == null || _data.order == -1)
            uiInstance.transform.SetAsLastSibling();
        else
            uiInstance.transform.SetSiblingIndex(_data.order);

        this.activeUIs[typeof(T)] = uiInstance;
        uiInstance.Initialization(_data);
        uiInstance.Show();

        return uiInstance;
    }

    /// <summary>TODO 임시 진단용 — Addressables가 넘겨준 "프리팹 원본"이 실제로 어떤 에셋이고,
    /// 그 안의 TMP 폰트가 무엇인지 인스턴스화 전에 찍는다. 폰트 이슈 확인 끝나면 제거할 것.</summary>
    static void DumpPrefabSource(GameObject _prefab)
    {
        string t_path = "(에디터 아님)";
#if UNITY_EDITOR
        t_path = UnityEditor.AssetDatabase.GetAssetPath(_prefab);
        if (string.IsNullOrEmpty(t_path)) t_path = "(빈 경로 = 번들에서 로드됨)";
#endif
        LogUtil.Log($"[프리팹출처] {_prefab.name} ← {t_path}");

        foreach (var t_text in _prefab.GetComponentsInChildren<TMPro.TMP_Text>(true))
        {
            string t_font = t_text.font != null ? t_text.font.name : "null";
            LogUtil.Log($"[프리팹원본] {t_text.name} font={t_font}");
        }
    }

    public void RegisterUI(PooledUIBase _ui)
    {
        Type t_type = _ui.GetType();

        // 같은 타입이 둘 이상이면 나중 Awake가 이긴다 — 어느 쪽이 답이 될지는 씬 로드 순서에 달렸고,
        // 조용히 넘어가면 "다른 인스턴스가 열리는" 증상으로만 드러난다(진단 불가).
        // 풀드 UI는 타입당 하나가 계약이다. 마이그레이션 중간 상태(프리팹 인스턴스가 아직 씬에 남음)를 잡는 그물.
        if (this.activeUIs.TryGetValue(t_type, out PooledUIBase t_existing) &&
            t_existing != null && t_existing != _ui)
        {
            Debug.LogWarning(
                $"[UIPoolManager] {t_type.Name}이 둘 이상 등록됐다 — 풀드 UI는 타입당 하나여야 한다.\n"
              + $"  기존: {Path(t_existing)} (scene='{t_existing.gameObject.scene.name}')\n"
              + $"  신규: {Path(_ui)} (scene='{_ui.gameObject.scene.name}')  ← 이쪽이 이긴다\n"
              + "  씬/프리팹에 저작된 사본을 지우고 프리팹만 남길 것.", _ui);
        }

        this.activeUIs[t_type] = _ui;
    }

    static string Path(PooledUIBase _ui)
    {
        var t_path = new System.Text.StringBuilder(_ui.name);
        for (Transform t_p = _ui.transform.parent; t_p != null; t_p = t_p.parent)
            t_path.Insert(0, t_p.name + "/");

        return t_path.ToString();
    }

    public void UnregisterUI(PooledUIBase _ui)
    {
        Type t_type = _ui.GetType();
        if (this.activeUIs.TryGetValue(t_type, out PooledUIBase t_existing) && t_existing == _ui)
            this.activeUIs.Remove(t_type);
    }

    public void CleanupInactiveUIs()
    {
        var t_inactiveUIs = new List<Type>();
        foreach (var t_kvp in activeUIs)
        {
            if (!t_kvp.Value.isShow)
                t_inactiveUIs.Add(t_kvp.Key);
        }

        foreach (var key in t_inactiveUIs)
        {
            if (activeUIs.TryGetValue(key, out var ui))
            {
                Destroy(ui.gameObject);
                activeUIs.Remove(key);
            }
        }
    }
}
