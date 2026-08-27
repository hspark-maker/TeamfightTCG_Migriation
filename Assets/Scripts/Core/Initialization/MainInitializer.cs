using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

public abstract class MainInitializer : MonoBehaviour
{
    [SerializeField] string initializerId;
    [SerializeField] bool required = true;
    [SerializeField] string[] requiredIds = System.Array.Empty<string>();

    public string InitializerId => string.IsNullOrWhiteSpace(initializerId)
        ? GetType().Name
        : initializerId;
    public bool Required => required;
    public IReadOnlyList<string> RequiredIds => requiredIds;

    public abstract UniTask Initialize(InitializationContext _context);
}
