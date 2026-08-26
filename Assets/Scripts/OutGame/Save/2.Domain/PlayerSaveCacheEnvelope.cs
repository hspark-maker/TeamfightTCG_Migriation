// 로컬 캐시 봉투. 캐시는 진실원이 아니라 오프라인 폴백이라, 담긴 세이브가 어느 revision 위에서
// 놀았는지와 그것이 원격에 올라갔는지를 함께 들고 있어야 온라인 복귀 시 원격을 안전하게 덮을 수 있다.
public class PlayerSaveCacheEnvelope
{
    // UserSaveData.VERSION과 다르면 캐시를 버린다(세이브 리셋 전제라 변환 코드가 없다).
    public int SchemaVersion { get; set; }

    // 마지막으로 업로드에 성공한 revision. 오프라인 중에는 오르지 않는다.
    public long Revision { get; set; }

    public UserSaveData Data { get; set; }
}
