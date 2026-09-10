# Mission query behavior tests

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tools/MissionQueryTests/run.ps1
```

The runner compiles the actual `MissionCommands.cs`, `MissionManager.cs`, and `MissionSnapshot.cs` with the installed Unity editor's Roslyn compiler, then executes the standalone tests with Mono. Only dependency stubs are supplied; mission cache and adoption policy are not copied into tests. Build output goes to a unique temporary directory. Use `-UnityData` to override the editor installation path.

Tests control request completion and realtime to check shared queries, 30-second cache expiry, force refresh, daily/weekly reset boundaries on the next refresh, mutation overlap, save revision changes, pending uploads, bounded invalidation retries, failed claims/queries, and session/debug reset guards. Reset timestamps use clearly future/past UTC values; no wall-clock sleeps are needed.

No Unity process, asset importer, Firebase request, or game-data generation runs. These tests cover the client scheduling and mission adoption logic, not Firebase transaction correctness or real Unity PlayerLoop integration. The unrelated server command and JSON dependencies are mocked.

For API compatibility compilation, define `REAL_UNITY`, compile the three production sources and `Stubs.cs` as a library with installed Unity/UniTask assemblies, and exclude `Tests.cs`. This removes the Unity/UniTask runtime stubs.
