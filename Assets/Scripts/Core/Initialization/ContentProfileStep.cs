using System;
using Cysharp.Threading.Tasks;

// 이번 부트가 쓸 콘텐츠 프로필을 확정하고 스펙시트 원본을 파싱한다.
// 프로필은 카탈로그·팩·클라우드 환경이 전부 읽는 전역 스위치라 제일 앞에서 한 번 세운다 —
// 미배선·손상이면 뒤 스텝의 낯선 예외가 아니라 이 스텝 이름으로 잡힌다.
public sealed class ContentProfileStep : MainInitializer
{
    public override UniTask Initialize(InitializationContext _context)
    {
        try
        {
            _context.SetProfile(ContentProfileConfig.Active);

            // 스펙시트 파싱은 프로필이 가리키는 환경(CloudEnvId)을 읽으므로 프로필 확정 뒤다.
            SpecSource.Init();
        }
        catch (Exception t_exception)
        {
            FailToRecovery(_context, t_exception);
        }

        return UniTask.CompletedTask;
    }
}
