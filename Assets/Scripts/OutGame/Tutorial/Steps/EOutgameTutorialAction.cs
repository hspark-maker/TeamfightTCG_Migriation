// 스텝 한 행이 "무엇을 하는가"(완료 조건·씬 이탈 여부·인스펙터 노출 필드가 여기서 파생된다)
// 시퀀스에 int로 직렬화 → 새 값은 끝에만 추가(재배치·삭제 시 저작된 행이 엉뚱한 액션이 된다)
public enum EOutgameTutorialAction
{
    WaitClick       = 0,
    Message         = 1,    // 화면 탭으로 넘기는 설명
    WaitPurchase    = 2,
    WaitPackOpen    = 3,
    DeckAutoEquip   = 4,
    BattleEntry     = 5,    // 앵커 클릭으로 전투 진입(showDeckGate를 켜면 덱 화면을 거친다)
    BattleStart     = 6,    // 덱 확인 화면의 전투 시작 버튼 클릭 대기
    AutoBattle      = 7,    // 입력 없이 곧장 전투로
    AutoPurchase    = 8,    // 입력 없이 팩을 구매해 개봉 오버레이를 연다
    DeckGrant       = 9,    // 완성된 덱을 유저 세이브에 미리 지급
    WaitAlbumInsert = 10,   // 도감 삽입 연출이 끝날 때까지 대기(연출이 스스로 안내한다)
    WaitEnhance     = 11,   // 강화 "성공"까지 대기(누르는 것만으로는 끝나지 않는다 — 실패하면 그 자리에서 다시 누른다)
    CloseCardDetail = 12,   // 입력 없이 카드 상세 오버레이를 닫는다(로비 위젯을 가리키는 다음 안내의 길을 튼다)
    EnterFirstRank  = 13,   // 입력 없이 첫 랭크 티어에 올린다(온보딩 전투가 끝난 자리 = 랭크 연출이 서던 그 자리)
    WaitLobbyReturn = 14,   // 유저가 열어 둔 오버레이를 스스로 닫고 로비 표면으로 돌아올 때까지 대기(화면에 아무것도 그리지 않는다)
}
