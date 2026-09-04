using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 룰렛의 static 창구. 저작(RouletteConfig)과 판정(IRouletteSpinSource)을 갈라 쥔다.
// 1단계는 티켓 차감도 재화 지급도 하지 않는다 — 잔액의 진실원이 서버 지갑 문서라 클라에 더하는 경로가 없다.
public static class RouletteManager
{
    static RouletteConfig s_config;
    static IRouletteSpinSource s_source;
    static bool s_spinning;

    // SetSource로 명시적으로 꽂힌 소스가 있는가. 개발용 로컬 소스는 이 자리를 덮지 않는다 —
    // 초기화가 한 번 더 돌면(재시도) 2단계 서버 소스가 조용히 로컬로 되돌아간다.
    static bool s_sourceInjected;

    /// <summary>회전을 실제로 돌릴 수 있는가. 설정과 소스가 둘 다 서야 true다 — 로비 버튼 노출 판정이 이 값이다.</summary>
    public static bool IsAvailable => s_config != null && s_source != null;

    // 판 이름·비용은 프리팹 저작이 그린다. 화면이 문구를 덮지 않으므로 여기서 내주는 표면도 두지 않는다
    // — 2단계에 비용 표시가 필요해지면 그때 되살린다(저작값은 RouletteConfig 에 그대로 있다).

    public static IReadOnlyList<RouletteSlotDef> Slots
        => s_config != null ? s_config.Slots : Array.Empty<RouletteSlotDef>();

    public static bool TryGetSlot(int _index, out RouletteSlotDef _slot)
    {
        if (s_config != null) return s_config.TryGetSlot(_index, out _slot);

        _slot = default;
        return false;
    }

    /// <summary>회전 가능 여부의 낙관 검사. 티켓 잔액은 보지 않는다 — 1단계는 티켓 0장으로 무제한 회전이 기획값이다
    /// (2단계에 CanAfford 한 줄이 붙고 <see cref="ERouletteSpinResult.InsufficientTicket"/> 로 떨어진다).</summary>
    public static ERouletteSpinResult Precheck()
    {
        if (!IsAvailable) return ERouletteSpinResult.NotConfigured;

        // 앞선 회전이 아직 안 끝났다 — 화면이 "지금은 돌릴 수 없다"로 접는 갈래라 서버 거절과 같은 코드를 쓴다.
        if (s_spinning) return ERouletteSpinResult.Rejected;

        return ERouletteSpinResult.Success;
    }

    /// <summary>회전 1회. 실패·거절·취소는 전부 결과값으로 돌아온다.</summary>
    public static async UniTask<RouletteSpinOutcome> SpinAsync(CancellationToken _ct)
    {
        ERouletteSpinResult t_precheck = Precheck();
        if (t_precheck != ERouletteSpinResult.Success) return RouletteSpinOutcome.CreateFailure(t_precheck);

        s_spinning = true;
        try
        {
            RouletteSpinOutcome t_outcome = await s_source.SpinAsync(_ct);
            if (!t_outcome.Success) return t_outcome;

            // 그릴 수 없는 칸을 성공으로 넘기면 화면이 그 인덱스로 판을 돌리다 터진다.
            if (!TryGetSlot(t_outcome.SlotIndex, out _))
            {
                Debug.LogError($"[RouletteManager] 결과 칸 {t_outcome.SlotIndex}이(가) 판에 없다 — 저작과 판정이 어긋났다.");
                return RouletteSpinOutcome.CreateFailure(ERouletteSpinResult.Rejected);
            }

            return t_outcome;
        }
        catch (OperationCanceledException)
        {
            // 소스는 예외를 던지지 않는 계약이지만, 취소된 대기를 await 하는 것만은 이 자리에서 결과값으로 접는다.
            return RouletteSpinOutcome.CreateFailure(ERouletteSpinResult.Canceled);
        }
        finally
        {
            s_spinning = false;
        }
    }

    /// <summary>초기화에서 1회 주입. 결함이 있으면 설정 전체를 버린다 —
    /// <see cref="IsAvailable"/> 가 false로 남아 로비 버튼이 뜨지 않으므로 저작 실수가 즉시 드러난다.</summary>
    public static void SetConfig(RouletteConfig _config)
    {
        s_config = null;

        // 명시적으로 꽂힌 소스는 건드리지 않는다 — 그 자리를 비우면 초기화가 다시 돌 때마다
        // 2단계 서버 소스가 개발용 로컬 소스로 갈린다.
        if (!s_sourceInjected) s_source = null;

        if (_config == null) return;

        var t_faults = new List<string>();
        var t_warnings = new List<string>();
        int t_faultCount = _config.Validate(t_faults, t_warnings);

        for (int t_i = 0; t_i < t_warnings.Count; t_i++) Debug.LogWarning($"[RouletteManager] {_config.name}: {t_warnings[t_i]}");

        if (t_faultCount > 0)
        {
            for (int t_i = 0; t_i < t_faults.Count; t_i++) Debug.LogError($"[RouletteManager] {_config.name}: {t_faults[t_i]}");
            return;
        }

        s_config = _config;
        BuildSource();
    }

    /// <summary>추첨 소스 교체 창구(2단계 서버 소스·테스트 대역). null이면 <see cref="IsAvailable"/> 가 false로 떨어진다.
    /// 한 번 꽂으면 이후 <see cref="SetConfig"/> 가 덮지 않는다.</summary>
    public static void SetSource(IRouletteSpinSource _source)
    {
        s_source = _source;
        s_sourceInjected = true;
    }

    // 도메인 리로드를 끈 에디터에서 이전 플레이의 s_spinning이 살아남으면 두 번째 플레이의 첫 회전이 잠긴다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        s_config = null;
        s_source = null;
        s_spinning = false;
        s_sourceInjected = false;
    }

    // #if 밖에서는 소스가 null로 남는다. 폴백이 없는 것이 설계다 — 출시 빌드에서 룰렛 진입 자체가 닫힌다.
    static void BuildSource()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 누가 소스를 이미 꽂았으면 개발용으로 갈아치우지 않는다.
        if (s_sourceInjected) return;

        // DEVELOPMENT_BUILD 만으로는 Live 프로파일로 켠 개발 빌드를 못 막는다.
        // Active는 프로파일 애셋이 없으면 던지는 게터라, 룰렛 하나 때문에 초기화가 죽지 않게 감싼다.
        EContentRunMode t_runMode;
        try
        {
            t_runMode = ContentProfileConfig.Active.RunMode;
        }
        catch (Exception t_exception)
        {
            Debug.LogWarning($"[RouletteManager] 실행 프로파일을 못 읽어 로컬 추첨을 꽂지 않는다 — {t_exception.Message}");
            return;
        }

        if (t_runMode != EContentRunMode.Test) return;

        s_source = new LocalRouletteSpinSource(s_config);
#endif
    }
}
