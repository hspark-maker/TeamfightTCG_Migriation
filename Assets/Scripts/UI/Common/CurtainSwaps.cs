using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// 커튼이 덮은 동안 벌어지는 교체 한 건. CurtainView는 판을 여닫을 뿐 무엇이 갈리는지 모른다 —
// 씬 로드도 화면 교체도 여기 구현 하나로 표현된다.
//
// 네 박자로 나눈 이유는 씬 로드 때문이다. 로드는 커튼이 "닫히는 동안" 이미 돌아야 하고(Prepare),
// 다 덮은 뒤에도 덜 끝났으면 기다려야 하며(IsReady), 교체 자체가 여러 프레임에 걸친다(Commit).
// 화면 교체는 Commit 하나면 끝나므로 나머지는 비워 둔다.
public interface ICurtainSwap
{
    /// <summary>커튼이 닫히기 시작할 때 1회. 무거운 준비를 닫힘 연출로 가리는 자리다.</summary>
    void Prepare();

    /// <summary>덮인 채 더 기다려야 하는가. 준비할 것이 없으면 항상 true.</summary>
    bool IsReady { get; }

    /// <summary>완전히 덮인 순간 화면을 갈아치운다. 이게 끝나야 커튼이 열린다.</summary>
    IEnumerator Commit();

    /// <summary>연출이 어디서 잘리든 반드시 불린다. 붙잡은 것을 놓는 유일한 자리.</summary>
    void Abort();
}

// 씬 전환. 닫히는 동안 비동기로 미리 로드하고, 활성화만 붙잡아 뒀다가 다 덮인 뒤에 푼다.
//
// ⚠ 붙잡은 allowSceneActivation은 커튼의 수명과 무관하게 살아 있다 — 놓치면 씬이 영영 활성화되지 않고
//   이전 화면에 갇힌다. Abort가 그 유일한 안전장치다.
public class SceneLoadSwap : ICurtainSwap
{
    readonly string m_scene;
    readonly Action m_beforeLoad;

    AsyncOperation m_op;
    bool           m_committed;

    /// <param name="_onBeforeLoad">씬 교체 **직전** 1회 호출. 화면을 망가뜨리는 정리는 반드시 여기로 넘긴다
    /// — 씬 교체와 붙어 있어야 파괴된 오브젝트를 붙잡은 연출 체인이 깨어날 틈이 없다(LoadingCoverView와 같은 계약).</param>
    public SceneLoadSwap(string _scene, Action _onBeforeLoad = null)
    {
        m_scene      = _scene;
        m_beforeLoad = _onBeforeLoad;
    }

    public void Prepare()
    {
        m_op = SceneManager.LoadSceneAsync(m_scene);

        // 로드를 못 걸었으면 Commit이 동기 로드로 되돌아간다 — 연출 때문에 화면이 갇히는 일은 없어야 한다.
        if (m_op == null)
        {
            Debug.LogError($"[SceneLoadSwap] '{m_scene}' 를 비동기 로드할 수 없습니다 — 덮인 뒤 동기 로드로 넘깁니다.");

            return;
        }

        // 닫히는 동안 뒤에서 로드하고, 활성화는 다 닫힐 때까지 붙잡는다.
        m_op.allowSceneActivation = false;
    }

    // 활성화를 막아둔 동안 progress는 0.9에서 멈춘다 — 그게 이 경로의 "다 됐다"이다.
    public bool IsReady => m_op == null || m_op.progress >= 0.9f;

    public IEnumerator Commit()
    {
        m_committed = true;

        m_beforeLoad?.Invoke();

        if (m_op == null)
        {
            SceneManager.LoadScene(m_scene);
            yield break;
        }

        m_op.allowSceneActivation = true;
        yield return m_op;
        m_op = null;

        yield return null;   // 새 씬이 최소 한 번 그려지도록 한 프레임 양보
    }

    public void Abort()
    {
        if (m_op == null) return;

        // Commit도 못 간 채 잘렸다면 정리 훅조차 돌지 않았다 — 씬은 어차피 갈리므로 계약대로 돌려준다.
        if (!m_committed) m_beforeLoad?.Invoke();

        m_op.allowSceneActivation = true;
        m_op = null;
    }
}
