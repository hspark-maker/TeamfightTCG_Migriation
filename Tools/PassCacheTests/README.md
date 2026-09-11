# Pass cache behavior tests

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tools/PassCacheTests/run.ps1
```

The runner compiles the actual `PassCommands.cs`, `PassManager.cs`, and `PassSnapshot.cs` with the installed Unity editor's Roslyn compiler and executes them with Mono. Dependency stubs control callable completion; no production cache/adoption policy is copied. Build output uses a unique temporary directory. Override the editor location with `-UnityData`.

The 12 behavior groups cover successful claim/mission reuse and panel entry query counts, display calculation, null/failing responses, missing/mismatched definitions, season expiry before/during a request, explicit invalidation, rank-dependent pack choices, stale reads, overlapping mutations, and mission fallback queries. Time boundaries use clearly future/past UTC timestamps, without sleeping.

No Unity process, asset importer, Firebase request, or game-data generation runs. This harness checks client cache/adoption behavior, not real Unity PlayerLoop or Firebase transactions. For API compatibility compilation define `REAL_UNITY`, compile the three production sources with `Stubs.cs` and actual Unity/UniTask references as a library, and exclude `Tests.cs`.
