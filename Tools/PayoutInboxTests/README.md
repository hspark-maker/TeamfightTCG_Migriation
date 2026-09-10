# Payout inbox behavior tests

Run from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tools/PayoutInboxTests/run.ps1
```

The runner uses the installed Unity editor's Roslyn compiler and Mono runtime. It compiles the actual `Assets/Scripts/Network/PayoutInbox.cs` against dependency stubs; it does not start Unity, import assets, invoke Firebase, or write generated game data. Compiler output goes to a unique temporary directory. Override `-UnityData` when the editor installation differs.

The virtual clock and controlled network completions exercise empty-inbox cooldown, forced refresh coalescing, lifecycle flush waiting, retry invalidation, payout pagination, acknowledgement recovery, and session changes. The tests do not duplicate payout scheduling policy. They do not validate Unity PlayerLoop integration or the wallet adoption logic owned by `ServerSaveCommands`.

For a separate API compatibility compilation, `Stubs.cs` supports `REAL_UNITY`: this excludes Unity/UniTask stubs and uses the real UniTask task adapter. Compile the production file and `Stubs.cs` as a library with installed Unity/UniTask assemblies; exclude `Tests.cs`, which intentionally controls virtual time.
