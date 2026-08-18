/// <summary>인게임 전투 UX 임시 스위치. "기본 UX만 남긴다"는 결정의 단일 지점 —
/// 되살릴 때 여기만 true로 바꾼다(호출부는 손대지 않는다). 삭제가 아니라 게이팅인 이유도 그것.
/// const가 아니라 static readonly인 이유: 도달불가 코드 경고(CS0162) 회피.</summary>
public static class BattleUxFlags
{
    public static readonly bool DragAimAttack      = false;  // 드래그 조준 공격(탭 공격만 남김)
    // 치사 예고 = 잡히는 카드의 HP 점멸. 같이 있던 카드 흐려짐 오버레이(DieOverlay)는 배선째 삭제됐다.
    public static readonly bool DeathPreview       = false;
    public static readonly bool EffectNotifyBanner = false;  // 화면 우측 슬라이드 설명 배너
    // 처형 재공격의 대상을 무작위로 자동 선택(false면 예전처럼 플레이어가 다시 고른다).
    // 규칙 자체는 ExecutionRule 한 곳 — 여기선 "어느 경로를 쓸지"만 정한다.
    public static readonly bool ExecutionRandomTarget = true;
}
