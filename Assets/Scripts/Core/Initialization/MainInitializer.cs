using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

public abstract class MainInitializer : MonoBehaviour
{
    [SerializeField] string initializerId;
    [SerializeField] bool required = true;
    [SerializeField] string[] requiredIds = System.Array.Empty<string>();
    [Tooltip("복구 화면의 재시도가 여기서부터 다시 돈다. 정확히 하나만 켠다.")]
    [SerializeField] bool retryEntry;

    public string InitializerId => string.IsNullOrWhiteSpace(initializerId)
        ? GetType().Name
        : initializerId;
    public bool Required => required;
    public IReadOnlyList<string> RequiredIds => requiredIds;
    public bool RetryEntry => retryEntry;

    public abstract UniTask Initialize(InitializationContext _context);

    /// <summary>초기화를 못 세운 사본을 접는다 — 복구 화면을 띄우고 루트를 걷어낸 뒤 남은 스텝을 멈춘다.
    /// 루트를 남기면 절반만 배선된 매니저가 씬을 따라다닌다.</summary>
    protected static void FailToRecovery(InitializationContext _context, System.Exception _exception)
    {
        GameInitialization.MarkRecoveryRequired();
        Debug.LogException(_exception);
        Destroy(_context.Root);
        _context.Abort();
    }
}
