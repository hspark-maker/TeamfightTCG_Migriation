using CookApps.SpecData.Generator;

// 시트 원본(외부 SpecData 생성기)에 아직 등록되지 않은 표를 손으로 선언해 두는 자리.
// SpecDatas.cs는 생성기가 통째로 덮어쓰므로 거기에 적으면 다음 재생성에서 사라진다 — 실제로 한 번 사라졌다.
//
// [GeneratorSpecData]는 소스 제너레이터 마커라 파일 위치와 무관하게 스캔된다.
// 즉 여기 선언만 해도 SpecDataManager에 컨테이너(Manager.AIDeck)가 생긴다.
//
// ★ 시트 원본에 AIDeck이 등록되는 순간 이 파일을 지워라. 안 지우면 SpecDatas.cs의 생성본과
//   같은 partial에 같은 필드가 두 번 선언돼 CS0102로 죽는다 — 조용히 갈리지 않고 컴파일이 막힌다.
[GeneratorSpecData]
public partial class AIDeck
{
    /// 행 고유 번호(부여 후 변경 금지)
    [GeneratorId(nameof(id), typeof(int))]
    public int id;
    /// 덱 안정 키
    public string deckId;
    /// 검증·로그용 표시 이름
    public string deckName;
    /// 등장 시작 티어 인덱스
    public int fromTier;
    /// 등장 종료 티어(포함). 0은 제한 없음
    public int toTier;
    /// 같은 티어 안 등장 가중치. 0 이하는 1
    public int weight;
    /// 이 덱 카드가 쓸 레벨의 하한. 0은 미저작(바닥 레벨 고정)
    public int fromLevel;
    /// 이 덱 카드가 쓸 레벨의 상한(포함). 0은 미저작(바닥 레벨 고정)
    public int toLevel;
    /// 덱 1~6번 칸의 Card.id. 덱 크기가 DeckSaveManager.DECK_SIZE로 고정이라 자식 표 대신 칸을 컬럼으로 편다
    public int card1;
    public int card2;
    public int card3;
    public int card4;
    public int card5;
    public int card6;
}
