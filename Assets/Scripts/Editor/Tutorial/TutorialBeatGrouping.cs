using System;
using System.Collections.Generic;

/// <summary>비트 하나 — 유저가 겪는 사건 한 칸과, 그 사건에 매달린 무대 준비·뒤처리 행들.
/// 좌표는 전부 스텝 인덱스 그대로다(데이터도 세이브의 stepId도 접히지 않는다).</summary>
public readonly struct TutorialBeat
{
    public readonly int First;      // 이 비트가 차지하는 첫 행
    public readonly int Last;       // 마지막 행
    public readonly int BeatStep;   // 사건 행. -1 = 사건 없이 잡일만 남은 무리(저작이 덜 됐거나 편 머리·꼬리에 걸친 것)
    public readonly int PreCount;   // 사건 앞에 붙은 무대 준비 행 수
    public readonly int PostCount;  // 사건 뒤에 붙은 뒤처리 행 수

    public TutorialBeat(int _first, int _last, int _beatStep, int _preCount, int _postCount)
    {
        First     = _first;
        Last      = _last;
        BeatStep  = _beatStep;
        PreCount  = _preCount;
        PostCount = _postCount;
    }

    // 이 비트를 대표하는 행 — 목록 줄과 상세 머리가 가리키는 좌표다.
    public int PrimaryStep => BeatStep >= 0 ? BeatStep : First;

    public int Count => Last - First + 1;

    public bool Contains(int _step) => _step >= First && _step <= Last;
}

/// <summary>평평한 스텝 목록을 비트로 접는다.
///
/// 접는 규칙은 액션마다 정해진 <see cref="EBeatSlot"/> 하나뿐이다 — 사건(Beat) 하나에
/// 그 앞의 준비(Pre)와 뒤의 뒤처리(Post)가 매달린다. 33행짜리 온보딩이 22비트로 접히면
/// 목록이 "실행기가 처리하는 순서"가 아니라 "유저가 겪는 순서"로 읽힌다.
///
/// <b>파생일 뿐이다.</b> 저작 SO의 행도, 세이브가 붙잡는 stepId도, 실행 순서도 그대로다 —
/// 그래서 접기를 잘못 판정해도 게임이 달라지지 않는다(목록이 어색해질 뿐이다).</summary>
public static class TutorialBeatGrouping
{
    /// <summary>한 편(또는 트리거 묶음)의 스텝을 비트 목록으로 접는다.
    /// <paramref name="_at"/>는 빈 칸에 null을 돌려줘도 된다 — 빈 칸은 사건으로 보고 홀로 세운다(그래야 목록에서 사라지지 않는다).</summary>
    public static List<TutorialBeat> Build(int _stepCount, Func<int, TutorialStepDef> _at)
    {
        var t_beats = new List<TutorialBeat>();
        if (_stepCount <= 0 || _at == null) return t_beats;

        // 열려 있는 비트를 좌표 셋으로 들고 다닌다(구조체를 확정하는 것은 닫을 때다).
        int t_first = -1, t_last = -1, t_beatStep = -1, t_pre = 0, t_post = 0;

        void Flush()
        {
            if (t_first < 0) return;

            t_beats.Add(new TutorialBeat(t_first, t_last, t_beatStep, t_pre, t_post));
            t_first = t_last = t_beatStep = -1;
            t_pre   = t_post = 0;
        }

        void Open(int _index)
        {
            Flush();
            t_first = t_last = _index;
        }

        for (int t_i = 0; t_i < _stepCount; t_i++)
        {
            var t_def  = _at(t_i);
            var t_slot = SlotOf(t_def);

            switch (t_slot)
            {
                // 뒤처리는 앞선 사건에 붙는다. 붙을 사건이 없으면(편 머리의 정리 행) 자기들끼리 한 무리를 이룬다 —
                // 뒤에 올 사건에 매달면 "치우고 나서 시작한다"는 순서가 목록에서 뒤집힌다.
                case EBeatSlot.Post:
                    if (t_first < 0 || (t_beatStep < 0 && t_pre > 0)) Open(t_i);

                    t_last = t_i;
                    t_post++;
                    break;

                // 준비는 뒤따르는 사건에 붙는다. 이미 사건이 섰거나 뒤처리가 시작된 비트에는 낄 수 없다.
                case EBeatSlot.Pre:
                    if (t_first < 0 || t_beatStep >= 0 || t_post > 0) Open(t_i);

                    t_last = t_i;
                    t_pre++;
                    break;

                default:
                    if (t_first < 0 || t_beatStep >= 0 || t_post > 0) Open(t_i);

                    t_last     = t_i;
                    t_beatStep = t_i;
                    break;
            }
        }

        Flush();

        return t_beats;
    }

    /// <summary>이 스텝을 품은 비트의 인덱스(없으면 -1).</summary>
    public static int IndexOf(List<TutorialBeat> _beats, int _step)
    {
        if (_beats == null) return -1;

        for (int t_i = 0; t_i < _beats.Count; t_i++)
            if (_beats[t_i].Contains(_step)) return t_i;

        return -1;
    }

    // 빈 칸은 사건으로 본다 — 잡일로 접으면 목록에서 다른 줄 뒤에 숨어 저작 오류를 못 본다.
    static EBeatSlot SlotOf(TutorialStepDef _def)
        => _def == null ? EBeatSlot.Beat : TutorialActionMeta.Of(_def.Action).BeatSlot;
}
