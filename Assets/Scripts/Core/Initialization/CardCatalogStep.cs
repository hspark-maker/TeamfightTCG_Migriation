using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 카드 마스터 단일 창구 주입 — 도감·소유권·덱 등 아웃게임 소비자가 안정 키로 조회한다.
// 여기까지 성공해야 이 사본이 초기화를 선점한 것으로 본다(실패하면 루트가 걷히고 다른 사본이 다시 든다).
public sealed class CardCatalogStep : MainInitializer
{
    // 카드 목록은 SpecData가 단일 진실원이며 CardCatalog가 초기화 시 구성한다. 시너지 표만 저작물로 받는다.
    [SerializeField] SynergyRegistry synergyRegistry;

    public override UniTask Initialize(InitializationContext _context)
    {
        try
        {
            ContentProfileConfig t_profile = _context.Profile;
            if (t_profile == null)
                throw new InvalidOperationException("[CardCatalogStep] ContentProfileStep이 먼저 서야 한다.");

            CardCatalog.SetSource(synergyRegistry, t_profile.RunMode, t_profile.IncludeTestCards);
            InitializationRunner.MarkBootClaimed();
        }
        catch (Exception t_exception)
        {
            FailToRecovery(_context, t_exception);
        }

        return UniTask.CompletedTask;
    }
}
