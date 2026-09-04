using System;
using UnityEngine;

/// <summary>Lobby tab content owns its enter/leave lifecycle and leave decision.</summary>
public class LobbyTabPanel : MonoBehaviour
{
    public RectTransform Root => transform as RectTransform;

    /// <summary>로비가 캔버스 레벨 서비스를 넘긴다. 첫 <see cref="OnEnter"/>보다 먼저, 한 번만 불린다.
    /// 인스펙터로 탭 안쪽을 배선하지 않기 위한 유일한 창구다 — 그래야 탭 인스턴스에 오버라이드가 안 남는다.</summary>
    public virtual void Initialize(LobbyTabServices _services) { }

    public virtual void RequestLeave(Action _proceed) => _proceed?.Invoke();

    public virtual void OnEnter() { }

    /// <summary>탭 전환 연출까지 끝나 제자리에 선 뒤에 불린다(연출이 없으면 <see cref="OnEnter"/> 직후).
    /// 화면 좌표를 재는 일은 OnEnter가 아니라 여기서 한다 — 그때는 패널이 아직 화면 밖이다.</summary>
    public virtual void OnSettled() { }

    public virtual void OnLeave() { }
}
