using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 아웃게임 첫시작 튜토리얼의 스텝 시퀀스(에디터 저작, SO). 스텝 추가·순서변경을 코드 수정 없이 한다.
/// 진행도 영속은 OutgameTutorialProgress가, 실행은 러너가 맡는다 — 이 SO는 저작 데이터일 뿐.
/// </summary>
[CreateAssetMenu(fileName = "OutgameTutorial", menuName = "Card Battle/Outgame Tutorial")]
public class OutgameTutorialData : ScriptableObject
{
    /// <summary>스텝 종류. 완료 조건은 kind가 곧 정의한다(AutoPurchase=즉시, 나머지=앵커 클릭) — 별도 조건식 필드 없음.
    /// (SO는 int 직렬화 → 새 값은 반드시 끝에 추가.)</summary>
    public enum EStepKind
    {
        AutoPurchase = 0,   // 입력 없음. 팩 구매 → 캐리어 → 지정 씬 자동 전환
        WaitClick    = 1,   // 앵커 버튼 클릭 대기
        BattleEntry  = 2,   // 앵커 클릭 대기 + 진입 시 튜토리얼 시나리오 시작
    }

    /// <summary>튜토리얼 스텝 1개. 아래 필드는 kind별 전용이라 무관한 kind에서는 무시된다.</summary>
    [Serializable]
    public struct Step
    {
        public EStepKind kind;

        [Tooltip("안내 타깃 위젯. WaitClick / BattleEntry 전용 (AutoPurchase는 None)")]
        public EOutgameTutorialAnchor anchor;

        [Tooltip("게이트 배너 문구. 비우면 배너를 띄우지 않는다")]
        [TextArea] public string guideMessage;

        [Tooltip("구매할 카드팩. AutoPurchase 전용")]
        public CardPackData pack;

        [Tooltip("중복 카드 1장당 환급 골드. AutoPurchase 전용")]
        public long duplicateRefundGold;

        [Tooltip("개봉 연출 후 돌아올 씬 이름. AutoPurchase 전용")]
        public string nextScene;

        [Tooltip("전투에 넘길 튜토리얼 시나리오. BattleEntry 전용")]
        public TutorialScenarioData scenario;
    }

    [Header("스텝 시퀀스 (순서 = 진행 순서, 인덱스가 곧 세이브 진행도)")]
    public List<Step> steps = new List<Step>();
}
