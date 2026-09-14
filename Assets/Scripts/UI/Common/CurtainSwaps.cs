using System;
using System.Collections;

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

    GameSceneLoadOperation m_op;
    bool m_handedToRecovery;

    /// <param name="_onBeforeLoad">씬 교체 **직전** 1회 호출. 화면을 망가뜨리는 정리는 반드시 여기로 넘긴다
    /// — 씬 교체와 붙어 있어야 파괴된 오브젝트를 붙잡은 연출 체인이 깨어날 틈이 없다(LoadingCoverView와 같은 계약).</param>
    public SceneLoadSwap(string _scene, Action _onBeforeLoad = null)
    {
        m_scene      = _scene;
        m_beforeLoad = _onBeforeLoad;
    }

    public void Prepare()
    {
        m_op = new GameSceneLoadOperation(m_scene);
    }

    public bool IsReady => m_op == null || m_op.IsReady;

    public IEnumerator Commit()
    {
        if (m_op == null) Prepare();
        yield return m_op.Commit(m_beforeLoad);
        if (!m_op.Succeeded)
        {
            m_handedToRecovery = true;
            LoadingCoverView.ShowSceneLoadFailure(m_scene, m_beforeLoad);
        }

        yield return null;   // 새 씬이 최소 한 번 그려지도록 한 프레임 양보
    }

    public void Abort()
    {
        if (m_op == null || m_handedToRecovery) return;
        m_op.FinishWithoutCover(m_beforeLoad);
    }
}
