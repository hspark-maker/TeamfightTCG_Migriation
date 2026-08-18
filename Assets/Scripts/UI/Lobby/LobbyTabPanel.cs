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

    public virtual void OnLeave() { }
}
