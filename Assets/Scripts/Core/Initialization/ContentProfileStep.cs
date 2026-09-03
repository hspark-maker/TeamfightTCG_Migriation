using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 이번 초기화가 쓸 콘텐츠 프로필을 확정하고 스펙시트 원본을 파싱한다.
// 프로필은 카탈로그·팩·클라우드 환경이 전부 읽는 전역 스위치라 제일 앞에서 한 번 세운다 —
// 미배선·손상이면 뒤 스텝의 낯선 예외가 아니라 이 스텝 이름으로 잡힌다.
public sealed class ContentProfileStep : MainInitializer
{
    public override UniTask Initialize(InitializationContext _context)
    {
        try
        {
            ContentProfileConfig t_profile = ContentProfileConfig.Active;
            _context.SetProfile(t_profile);

            // 빌드 종류와 환경을 항상 한 줄 남긴다 — 어떤 빌드가 어느 환경을 보는지는
            // 화면만 보고는 알 수 없어서, 매번 추측으로 시간을 버리는 자리였다.
            Debug.Log($"[초기화] dev={Debug.isDebugBuild} mode={t_profile.RunMode} env={t_profile.CloudEnvId} " +
                      $"app={Application.version} tableGen={ContentVersion.Major}");

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
