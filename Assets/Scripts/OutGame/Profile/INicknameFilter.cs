/// <summary>닉네임에 쓸 수 없는 말이 섞였는지 판정한다. 판정기의 사전·엔진을 ProfileManager에서 가리는 자리다.</summary>
public interface INicknameFilter
{
    /// <summary>이 이름을 막아야 하면 true. 왜 막혔는지는 알려주지 않는다 — 유저에게도 알리지 않기로 했다.</summary>
    bool IsBlocked(string _nickname);
}
