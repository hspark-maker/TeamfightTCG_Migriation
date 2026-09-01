using UnityEngine;

/// <summary>유산 시너지 연출. 엠블럼(베이스) + **왕관**.
///
/// 유산은 다른 시너지와 그림의 성격이 다르다 — 다른 것들은 상징 한 개가 한 번 뜨고 끝이지만
/// 유산은 <b>턴이 시작될 때마다 지금까지 쌓인 스택 수만큼 왕관이 떴다 사라지고,
/// 죽는 순간 그 왕관들이 회복받을 아군에게 날아간다</b>.
/// 개수가 곧 정보라 엠블럼 줄(SynergyEmblemEntry) 하나로는 표현할 수 없고 여기 전용 슬롯을 둔다
/// (낙인 투사체가 BrandSynergyVfxConfig에 있는 것과 같은 이유 — 그 시너지에서만 쓰는 연출).
///
/// ⚠ 왕관은 **남지 않는다**. 상시 표시로 두면 판이 길어질수록 보드가 왕관으로 덮이고,
///   "몇 개인가"를 세는 순간(턴 시작)이 사라져 개수 변화가 안 읽힌다.
///
/// 배선 지점만 여기다. 스폰·정렬·반납 규약은 BattleVfx, 순서·궤적은 <see cref="LegacyCrownVfx"/>.
/// 미배선(prefab 없음)이면 스택 연출 전체가 조용히 생략된다 — 규칙(스택 적립·회복)은 그대로 돈다.</summary>
[CreateAssetMenu(fileName = "LegacySynergyVfx", menuName = "Card Battle/Synergy Vfx/Legacy")]
public class LegacySynergyVfxConfig : SynergyVfxConfig
{
    [Header("왕관 한 개 (턴 시작마다 스택 수만큼 떴다 사라진다)")]
    // id는 쓰지 않는다 — BattleVfxId는 여러 곳이 공유하는 공용 축이고, 이건 유산 전용이다.
    public VfxEntry crown;

    [Tooltip("첫 왕관이 놓이는 자리(슬롯 중심 기준, 월드 단위).")]
    public Vector2 firstOffset = new Vector2(0f, 0.9f);

    [Tooltip("왕관 사이 간격. 줄은 항상 가운데 정렬이라 개수가 늘면 양옆으로 함께 벌어진다.")]
    public Vector2 step = new Vector2(0.62f, 0f);

    [Tooltip("간격이 '개수 축소 배율'을 따라가는 정도. 1이면 왕관이 작아진 만큼 간격도 좁아지고(빽빽), " +
             "0이면 작아져도 간격은 그대로다(넓게 퍼짐). 0.5면 절반만 따라간다.")]
    [Range(0f, 1f)] public float spacingFollowsScale = 0.4f;

    [Tooltip("줄을 호(弧)로 띄우는 높이. 양수면 가운데가 위로 솟고 음수면 가운데가 처진다. " +
             "0이면 일직선(step.y로 기울이는 것과는 별개 축이다). 왕관이 1개면 적용되지 않는다.")]
    public float arcHeight = 0.28f;

    [Tooltip("한 번에 띄우는 최대 개수(0이면 무제한). 넘는 스택은 숫자만 오르고 왕관은 안 는다 — " +
             "20턴짜리 판에서 왕관이 카드 밖으로 줄지어 나가지 않게.")]
    [Min(0)] public int maxVisible = 5;

    [Tooltip("왕관이 방출을 이어 가는 시간. 이 뒤에는 방출만 멈추고 이미 뜬 입자가 꺼지길 기다린다 " +
             "— 실제 화면 시간은 여기에 입자 수명이 더해진다. 프리팹이 다 피기 전에 끊기면 이 값을 올린다.")]
    [Min(0.05f)] public float showDuration = 2.2f;

    [Tooltip("방출을 멈춘 뒤 남은 입자를 기다리는 최대 시간. 상한을 넘기면 그냥 접는다 " +
             "(수명이 긴 입자 하나 때문에 왕관 하나가 영영 안 돌아오지 않게).")]
    [Min(0f)] public float fadeOutMaxWait = 2f;

    [Tooltip("왕관마다 등장을 어긋내는 간격. 0이면 전부 동시에 떠서 개수가 한 덩어리로 읽힌다.")]
    [Min(0f)] public float showStagger = 0.08f;

    [Tooltip("왕관이 뜰 때 튀는 배율(1이면 안 튄다).")]
    [Min(1f)] public float popScale = 1.3f;
    [Min(0.01f)] public float popDuration = 0.2f;

    [Header("개수에 따른 축소 (많아질수록 작게)")]
    [Tooltip("왕관이 한 개 늘 때마다 곱해지는 배율. 0.85면 1개=1.00 / 2개=0.85 / 3개=0.72… " +
             "간격(step)에도 같은 배율이 걸린다 — 크기만 줄이면 줄만 성기게 벌어진다.")]
    [Range(0.5f, 1f)] public float countScaleFalloff = 0.85f;

    [Tooltip("아무리 많아도 이 배율 아래로는 안 줄인다(점으로 뭉개지는 것 방지).")]
    [Range(0.1f, 1f)] public float minCountScale = 0.45f;

    [Header("이동 (파괴 시 아군에게 날아간다)")]
    // 죽는 순간엔 화면에 왕관이 떠 있지 않다(턴 시작 연출은 이미 끝났다) — 그래서 여기서 스택 수만큼
    // 새로 띄워 곧바로 날려 보낸다. "쌓여 있던 것이 간다"로 읽히게 등장 자리는 턴 시작 때와 같다.
    [Tooltip("날아가는 왕관에 따라붙는 궤적. 미배선이면 왕관만 날아간다.")]
    public VfxEntry trail;

    [Min(0.05f)] public float flyDuration = 0.55f;

    [Tooltip("왕관마다 출발을 어긋내는 간격. 0이면 한 덩어리로 움직여 개수가 안 읽힌다.")]
    [Min(0f)] public float flyStagger = 0.07f;

    [Tooltip("베지어 제어점을 직선에서 밀어내는 거리(0이면 직선). 부호는 인덱스 패리티로 갈려 부채꼴이 된다.")]
    public float curveHeight = 0.6f;

    [Tooltip("도착 후 왕관이 사라지기까지의 여운.")]
    [Min(0f)] public float arriveHold = 0.12f;
}
