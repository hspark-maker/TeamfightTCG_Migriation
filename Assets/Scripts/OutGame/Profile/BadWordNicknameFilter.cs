using System;
using CookApps.BadWordFilter;
using UnityEngine;

/// <summary>사내 비속어 사전(com.cookapps.bad-word-filter)으로 닉네임을 판정하는 구현체.
///
/// <para>걸린 말을 별표로 갈아 저장하지 않고 막기만 한다 — 유저가 적은 이름과 다른 결과를 몰래 굳히지 않으려는 것이다.</para>
///
/// <para>사전이 아직 안 올라온 동안에는 예외도 없이 "깨끗함"으로 답하는 구간이 있는데, 패키지가 준비 여부
/// (BWFManager.isReady)를 internal 로 감춰 두어 판정 불가와 통과를 갈라낼 방법이 없다. 닉네임 편집은
/// 로비에 들어간 뒤에야 열려 시점상 여유가 있으므로 그대로 둔다.</para></summary>
public sealed class BadWordNicknameFilter : INicknameFilter
{
    // 판정기가 서지 못했다는 경고를 한 번만 남기려는 문지기. 편집 한 번에 여러 확정 경로가 겹쳐 들어와
    // 같은 경고가 콘솔을 덮는 것을 막는다.
    bool m_warned;

    public BadWordNicknameFilter()
    {
        // 패키지는 단말 언어(Application.systemLanguage)로 스스로 서기 때문에, 못 박지 않으면 해외 단말에서 한국어 사전이 빠진다.
        CookAppsBadWordFilter.SetLanguage(SystemLanguage.Korean);
    }

    public bool IsBlocked(string _nickname)
    {
        if (string.IsNullOrWhiteSpace(_nickname)) return false;

        try
        {
            // 사전 파싱이 비동기라 부팅 직후엔 아직 안 올라와 못 걸러낼 여지가 있다. 닉네임 편집은 로비에 들어간 뒤에야 열려 시점상 여유가 있다.
            return CookAppsBadWordFilter.Contains(_nickname);
        }
        catch (Exception t_exception)
        {
            // 판정기 자체가 서지 못한 경우다(내부 필터는 RuntimeInitializeOnLoadMethod로만 만들어져 에디트 모드에서는 없다).
            // 여기서 예외를 흘리면 개명 커밋 경로가 통째로 죽어 이름을 아예 못 바꾸게 되므로 통과시킨다.
            if (!this.m_warned)
            {
                this.m_warned = true;
                Debug.LogWarning($"[BadWordNicknameFilter] 비속어 판정기가 서지 못해 이름을 거르지 않는다. {t_exception.Message}");
            }
            return false;
        }
    }
}
