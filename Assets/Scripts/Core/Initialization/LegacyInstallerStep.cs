using Cysharp.Threading.Tasks;
using UnityEngine;

// 이관 과도기 전용 스텝. 기존 InitializationInstaller 전체를 스텝 하나로 감싼다 —
// 목적은 동작 변경이 아니라 "실행 주체를 InitializationRunner 하나로 모으는 것"이다.
// 표의 15개 스텝을 하나씩 떼어낼 때마다 그만큼 Installer가 얇아지고, 다 떼면 이 파일도 함께 사라진다.
[RequireComponent(typeof(InitializationInstaller))]
public sealed class LegacyInstallerStep : MainInitializer
{
    [SerializeField] InitializationInstaller installer;

    void Reset() => installer = GetComponent<InitializationInstaller>();

    public override async UniTask Initialize(InitializationContext _context)
    {
        if (installer == null) installer = GetComponent<InitializationInstaller>();
        if (installer == null)
            throw new MissingComponentException("[LegacyInstallerStep] InitializationInstaller가 배선되지 않았다.");

        installer.InstallImmediate();

        // 원래 Start 코루틴이 서던 자리(형제 매니저의 Awake가 전부 끝난 뒤)를 유지한다.
        // 러너가 Awake(-210)에서 도는 탓에 곧바로 이어 돌리면 DataLibrary.Awake보다 앞서 검사하게 된다.
        await UniTask.Yield(PlayerLoopTiming.Update);
        if (installer == null) return;

        await installer.RunDeferred().ToUniTask(installer);
    }
}
