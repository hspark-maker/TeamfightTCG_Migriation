using System.Collections;
using DG.Tweening;
using UnityEngine;

// 이미 화면을 덮고 있는 매칭 화면을 그대로 다음 씬으로 데려가, 교체가 끝나면 그 화면의 배경 판을 열어
// 새 화면을 드러낸다.
//
// CurtainView의 대칭물이다. 그쪽은 "커튼을 세워 덮고 → 갈아치우고 → 연다"인데, 여기는 이미 덮고 있는 화면이
// 곧 커튼이라 덮는 박자가 없다. 그게 이 클래스가 있는 이유다 — 커튼을 새로 세우면 같은 판이 두 번 닫히고,
// 그 위에 서 있던 프로필과 배너가 한 프레임에 사라진다.
//
// 무엇이 갈리는지는 주입된 ICurtainSwap만 안다(커튼과 같은 계약). 셸도 여전히 씬을 모른다.
//
// ⚠ DontDestroyOnLoad로 씬을 넘어간다. 그 대가로 어떻게 빠져나가든 데려간 화면을 반드시 스스로 파괴해야 한다 —
//   남기면 커튼 층(UiSortingOrder.Curtain)에 선 레이캐스터가 이후 모든 씬을 영구 입력 불가로 잠근다.
//   CurtainView가 같은 대가를 같은 방식으로 치른다.
[DisallowMultipleComponent]
public class MatchSceneCarrier : MonoBehaviour
{
    // 씬을 넘어 사는 물건이라 가드도 씬 파괴에 묶이지 않아야 한다(CurtainView.s_busy와 같은 논리).
    static bool s_busy;

    // 교체가 준비되지 않아도 이 시간이 지나면 그대로 진행한다 — 무한 대기 방지(커튼과 같은 값).
    const float MaxWaitSeconds = 10f;

    MatchmakingShell m_shell;
    ICurtainSwap     m_swap;

    // 파괴를 한 번만 걸기 위한 표식. Finish는 코루틴 finally·OnDisable·OnDestroy 셋에서 들어온다.
    bool m_finished;

    /// <summary>_shell을 화면에 세워 둔 채로 _swap이 갈아치우고, 새 화면 위에서 그 배경 판이 갈라진다.
    /// 걸었으면 true — false면 호출부가 교체를 직접 책임진다(커튼으로 내려가면 된다).</summary>
    public static bool TryCarry(MatchmakingShell _shell, ICurtainSwap _swap)
    {
        if (_swap == null)
        {
            Debug.LogError("[MatchSceneCarrier] 갈아치울 것이 없습니다 — 화면을 데려가지 않습니다.");

            return false;
        }

        if (_shell == null || !_shell.CanCarryToScene) return false;

        // 두 번째 진입이 전환을 두 번 걸지 못하게. 커튼이 이미 돌고 있어도 물러난다 —
        // 두 전환이 겹치면 어느 쪽이 씬을 활성화할지가 순서에 달리고, 그 순서는 보장되지 않는다.
        if (s_busy || CurtainView.IsBusy) return false;

        s_busy = true;

        // 여기서부터는 무엇이 던져도 가드가 선 채로 남으면 안 된다 — 남기면 다음 전환이 통째로 막힌다.
        try
        {
            // 가드를 다 지난 뒤에야 화면을 건드린다 — 물러나는 길에서는 셸이 손대지 않은 채로 남아야
            // 호출부가 커튼으로 내려가도 화면이 어긋나지 않는다.
            _shell.PrepareForCarry();

            // 씬을 벗어나는 창구가 BGM 퇴장을 책임진다. 커튼을 안 쓰는 경로라 여기가 그 창구다 —
            // 길이는 커튼의 상수를 그대로 쓴다(값이 갈리면 전환마다 소리가 다른 박자로 빠진다).
            SoundManager.Instance?.FadeOutBGM(CurtainView.BgmFadeOutSeconds);

            var t_carrier = _shell.gameObject.AddComponent<MatchSceneCarrier>();
            t_carrier.m_shell = _shell;
            t_carrier.m_swap  = _swap;
            t_carrier.Begin();
        }
        catch
        {
            s_busy = false;

            throw;
        }

        return true;
    }

    // 화면을 들어 올려 씬 밖으로 옮기고 연출을 시작한다. Start를 기다리지 않는 이유는 그 한 프레임만큼
    // 씬 로드가 늦어지기 때문이다 — 로비는 이 시점에 이미 덮여 있어 미룰 이유가 없다.
    void Begin()
    {
        // 부모에서 떼면 셸의 Canvas가 루트가 되어 화면 전체를 잡는다. 로비 캔버스도 같은 스케일러 값이라
        // 배율이 그대로 이어지고, 셸은 그 캔버스 직속이었으므로 rect도 바뀌지 않는다 — 부품이 튀지 않는 근거다.
        transform.SetParent(null, false);

        // 데려가는 동안 이 화면이 하는 일이 정확히 커튼이다. 배틀 씬의 어떤 캔버스보다 위이고,
        // 초기화 커버(LoadingCover)보다는 아래다.
        UiSortingOrder.Stamp(GetComponent<Canvas>(), UiSortingOrder.Curtain);

        DontDestroyOnLoad(gameObject);

        StartCoroutine(CoRun());
    }

    void OnDisable()
    {
        // 오브젝트가 꺼지면 Unity가 코루틴을 finally 없이 버린다 — 여기서 걷지 않으면
        // 데려가던 화면이 커튼 층 그대로 DontDestroyOnLoad에 남아 이후 모든 씬의 입력을 먹는다.
        Finish();
    }

    void OnDestroy()
    {
        Finish();
    }

    // 세 박자뿐이다 — 덮는 박자가 없는 것이 커튼과의 유일한 차이다.
    IEnumerator CoRun()
    {
        try
        {
            // Prepare도 try 안이다 — 빌드셋에 없는 씬처럼 여기서 던지는 길이 있고,
            // 그때 밖으로 새면 가드가 선 채 화면이 커튼 층에 남는다.
            m_swap.Prepare();

            float t_waited = 0f;
            while (!m_swap.IsReady)
            {
                if (t_waited >= MaxWaitSeconds)
                {
                    Debug.LogWarning($"[MatchSceneCarrier] 교체 준비가 {MaxWaitSeconds}초 안에 끝나지 않아 그대로 진행합니다.");

                    break;
                }

                t_waited += Time.unscaledDeltaTime;   // 화면 전환을 덮는 물건이라 timeScale을 신뢰하지 않는다
                yield return null;
            }

            yield return m_swap.Commit();

            // 씬이 갈린 뒤에야 연다. 갈리기 전에 열면 판 뒤에서 사라지는 로비가 그대로 보인다.
            Sequence t_part = m_shell != null ? m_shell.PlayCarryPart() : null;

            if (t_part != null) yield return t_part.WaitForCompletion();
        }
        finally
        {
            Finish();
        }
    }

    // 어떻게 빠져나가든 여기를 지난다. 교체가 붙잡은 것을 놓고, 가드를 되돌리고, 데려온 화면을 걷는다 —
    // 셋 중 하나라도 놓치면 씬 활성화가 영영 안 풀리거나(붙잡힌 allowSceneActivation),
    // 다음 전환이 통째로 막히거나, 커튼 층에 선 화면이 이후 모든 씬의 입력을 먹는다.
    //
    // 파괴까지 여기서 하는 이유: 코루틴의 finally는 오브젝트가 꺼지는 길에서 오지 않는다.
    void Finish()
    {
        if (m_swap != null)
        {
            m_swap.Abort();
            m_swap = null;
        }

        s_busy = false;

        // OnDestroy가 부른 경우에는 이미 파괴 중이라 다시 부르지 않는다.
        if (this != null && gameObject != null && !m_finished)
        {
            m_finished = true;
            Destroy(gameObject);
        }
    }
}
