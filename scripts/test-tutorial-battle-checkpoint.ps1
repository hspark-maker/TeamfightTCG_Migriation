param([string]$UnityEditorData = 'C:/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Data')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$checkDir = Join-Path ([IO.Path]::GetTempPath()) ('TutorialBattleCheckpoint-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($checkDir) | Out-Null

function Read-Method([string]$source, [string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production method: $signature" }
    $body = $source.IndexOf('{', $start)
    $depth = 0
    for ($i = $body; $i -lt $source.Length; $i++) {
        if ($source[$i] -eq '{') { $depth++ }
        elseif ($source[$i] -eq '}') {
            $depth--
            if ($depth -eq 0) { return $source.Substring($start, $i - $start + 1) }
        }
    }
    throw "Unclosed production method: $signature"
}
$runner = [IO.File]::ReadAllText((Join-Path $repo 'Assets/Scripts/OutGame/Tutorial/OutgameTutorialRunner.cs'))
$config = [IO.File]::ReadAllText((Join-Path $repo 'Assets/Scripts/Battle/TutorialConfig.cs'))
$executor = [IO.File]::ReadAllText((Join-Path $repo 'Assets/Scripts/OutGame/Tutorial/Steps/TutorialStepExecutor.cs'))
$bridge = [IO.File]::ReadAllText((Join-Path $repo 'Assets/Scripts/UI/Tutorial/OutgameTutorialBridge.cs'))
$returnMethod = Read-Method $bridge '    public static async UniTask PrepareLobbyReturnAsync(CancellationToken _ct)'
$restoreAt = $returnMethod.IndexOf('OutgameTutorialRunner.RestorePendingBattleEntry();')
$cursorAt = $returnMethod.IndexOf('if (!CursorRunning) return;')
$saveAt = $returnMethod.IndexOf('await GuideResume.SaveConfirmedAsync(_ct)')
$applyAt = $returnMethod.IndexOf('t_owner.ApplyCurrentStep();')
if ($restoreAt -lt 0 -or $restoreAt -ge $cursorAt -or $saveAt -le $restoreAt -or $saveAt -ge $applyAt) {
    throw 'Lobby return must restore the pending battle and confirm its save before advancing the guide.'
}
$turn = [IO.File]::ReadAllText((Join-Path $repo 'Assets/Scripts/Battle/TurnRunner.cs'))
$resultMethod = Read-Method $turn '    void FinalizeResult(bool _won, EMatchEndReason _reason, bool _delayVoidExit = false)'
$voidAt = $resultMethod.IndexOf('if (_reason.IsVoid())')
$voidReturnAt = $resultMethod.IndexOf('return;', $voidAt)
$finishAt = $resultMethod.IndexOf('TutorialConfig.NotifyBattleFinished();')
if ($voidAt -lt 0 -or $voidReturnAt -lt $voidAt -or $finishAt -le $voidReturnAt) {
    throw 'A void battle must return without consuming its onboarding checkpoint.'
}
$markEnd = $executor.IndexOf('OutgameTutorialRunner.MarkPendingBattle(_step.StepId);', [StringComparison]::Ordinal)
if ($markEnd -lt 0) { throw 'Missing entry checkpoint branch.' }
$markStart = $executor.LastIndexOf('        if (', $markEnd, [StringComparison]::Ordinal)
$markBranch = $executor.Substring($markStart, $markEnd + 'OutgameTutorialRunner.MarkPendingBattle(_step.StepId);'.Length - $markStart)
$methods = @('    public static void MarkPendingBattle(int _stepId)', '    public static void RestorePendingBattleEntry()',
    '    public static void RewindToPendingBattleEntry()', '    static void NotifyScriptedBattleFinished()',
    '    static EOutgameTutorialStepResult CloseOrWarnOnMissingStep()') |
    ForEach-Object { Read-Method $runner $_ }
$source = @'
using System;
enum EOutgameTutorialAction { AutoBattle, BattleEntry, BattleStart }
enum EOutgameTutorialStepResult { Gated, Advanced, Failed }
sealed class TutorialStepDef { public object Scenario; public EOutgameTutorialAction Action; public int StepId; public bool ShowDeckGate; }
sealed class OnboardingExecutionSaveData { public int BattleEntryStepId; }
sealed class TutorialSaveData { public OnboardingExecutionSaveData Execution; public bool OutgameCompleted; }
sealed class Save { public TutorialSaveData Tutorial = new TutorialSaveData(); }
static class DataSaveManager { public static Save Data = new Save(); }
static class OutgameFeatureLock { public static void Refresh() {} public static void ClearStall() {} }
static class Debug { public static void LogWarning(string message) {} }
static class OutgameTutorialProgress {
    public static int ChapterIndex, StepIndex, StepId;
    public static bool IsCompleted => DataSaveManager.Data.Tutorial.OutgameCompleted;
    public static void Save() {} public static void ResetStallWatch() {}
    public static void CommitStep(int chapter, int step) { ChapterIndex=chapter; StepIndex=step; StepId=25; }
}
static class TutorialConfig {
    public static bool IsActive;
    public static event Action BattleFinished;
    /* FINISH_METHOD */
}
static class Entry {
    public static void Execute(TutorialStepDef _step) { /* ENTRY_BRANCH */ }
}
static class OutgameTutorialRunner {
    public static bool IsRunning = true;
    public const int ForcedChapterCount=4;
    const int TotalStepCount=23;
    static readonly (string name, int unused) s_data=("Test", 0);
    public static int Notifications;
    static event Action OnBattleEntryRestored = () => Notifications++;
    static bool TryFindStepId(int id, out int chapter, out int step) { chapter=3; step=2; return id==25; }
    static bool TryGetStepAt(int chapter, int step, out TutorialStepDef def) { def=null; return false; }
    static int StepCountOf(int chapter) => 3;
    static bool TryGetNext(int chapter, int step, out int nextChapter, out int nextStep) { nextChapter=4; nextStep=0; return false; }
    static void CompleteSequence() { DataSaveManager.Data.Tutorial.OutgameCompleted=true; }
    public static EOutgameTutorialStepResult RefreshMissingStep() => CloseOrWarnOnMissingStep();
    /* RUNNER_METHODS */
    public static void Hook() => TutorialConfig.BattleFinished += NotifyScriptedBattleFinished;
}
static class Program {
    static int assertions;
    static void Check(bool value, string label) { assertions++; if (!value) throw new Exception(label); }
    static int Pending => DataSaveManager.Data.Tutorial.Execution?.BattleEntryStepId ?? 0;
    static void Main() {
        OutgameTutorialRunner.Hook();
        var step=new TutorialStepDef { StepId=25, Action=EOutgameTutorialAction.BattleEntry };
        Entry.Execute(step);
        Check(Pending==25, "normal forced battle must checkpoint without scenario");
        OutgameTutorialProgress.StepId=0;
        OutgameTutorialProgress.ChapterIndex=4;
        Check(OutgameTutorialRunner.RefreshMissingStep()==EOutgameTutorialStepResult.Gated
            && !OutgameTutorialProgress.IsCompleted, "UI refresh during final battle handoff must not graduate");
        DataSaveManager.Data.Tutorial.OutgameCompleted=true;
        OutgameTutorialRunner.RestorePendingBattleEntry();
        Check(!OutgameTutorialProgress.IsCompleted && OutgameTutorialProgress.ChapterIndex==3
            && OutgameTutorialProgress.StepIndex==2, "cancelled entry must restore before graduation");
        Check(OutgameTutorialRunner.Notifications==1, "restored entry must wake bridge once");
        OutgameTutorialRunner.RestorePendingBattleEntry();
        Check(OutgameTutorialRunner.Notifications==1, "duplicate cancellation must not restart same step");
        OutgameTutorialProgress.StepId=0;
        OutgameTutorialProgress.ChapterIndex=4;
        TutorialConfig.IsActive=false;
        OutgameTutorialRunner.RewindToPendingBattleEntry();
        Check(OutgameTutorialProgress.ChapterIndex==3, "relaunch before normal battle result must rewind");
        TutorialConfig.NotifyBattleFinished();
        Check(Pending==0, "normal battle result must consume checkpoint with IsActive false");
        OutgameTutorialProgress.StepId=0;
        OutgameTutorialProgress.ChapterIndex=4;
        OutgameTutorialRunner.RestorePendingBattleEntry();
        OutgameTutorialRunner.RewindToPendingBattleEntry();
        Check(OutgameTutorialProgress.ChapterIndex==4, "finished battle must never rewind");
        Check(OutgameTutorialRunner.RefreshMissingStep()==EOutgameTutorialStepResult.Advanced
            && OutgameTutorialProgress.IsCompleted, "actual final battle result must allow graduation");
        OutgameTutorialRunner.IsRunning=false;
        Entry.Execute(step);
        Check(Pending==0, "ordinary guided battle must not create forced checkpoint");
        OutgameTutorialRunner.IsRunning=true;
        step.Action=EOutgameTutorialAction.AutoBattle;
        step.Scenario=new object();
        Entry.Execute(step);
        Check(Pending==25, "scripted automatic battle retains checkpoint");
        TutorialConfig.IsActive=true;
        TutorialConfig.NotifyBattleFinished();
        Check(Pending==0, "scripted result still consumes checkpoint");
        Console.WriteLine($"PASS: {assertions} tutorial battle checkpoint assertions");
    }
}
'@
$source = $source.Replace('/* RUNNER_METHODS */', ($methods -join "`n"))
$source = $source.Replace('/* FINISH_METHOD */', (Read-Method $config '    public static void NotifyBattleFinished()'))
$source = $source.Replace('/* ENTRY_BRANCH */', $markBranch)
[IO.File]::WriteAllText((Join-Path $checkDir 'Harness.cs'), $source)
$dotnet = Join-Path $UnityEditorData 'NetCoreRuntime/dotnet.exe'
$runtime = Get-ChildItem (Join-Path $UnityEditorData 'NetCoreRuntime/shared/Microsoft.NETCore.App') -Directory |
    Sort-Object { [Version]$_.Name } -Descending | Select-Object -First 1
$references = Get-ChildItem $runtime.FullName -Filter '*.dll' | Where-Object {
    try { [Reflection.AssemblyName]::GetAssemblyName($_.FullName) | Out-Null; $true } catch { $false }
} | ForEach-Object { '-r:"' + $_.FullName + '"' }
$arguments = @('-nologo', '-target:exe', '-langversion:9.0', '-nostdlib+',
    ('-out:"' + (Join-Path $checkDir 'Harness.dll') + '"')) + $references + ('"' + (Join-Path $checkDir 'Harness.cs') + '"')
$response = Join-Path $checkDir 'compile.rsp'
[IO.File]::WriteAllLines($response, $arguments)
& $dotnet (Join-Path $UnityEditorData 'DotNetSdkRoslyn/csc.dll') ('@' + $response)
if ($LASTEXITCODE -ne 0) { throw 'Checkpoint harness compilation failed.' }
$runtimeConfig = @{ runtimeOptions = @{ tfm = 'net6.0'; framework = @{ name = 'Microsoft.NETCore.App'; version = $runtime.Name } } }
[IO.File]::WriteAllText((Join-Path $checkDir 'Harness.runtimeconfig.json'), ($runtimeConfig | ConvertTo-Json -Depth 4))
& $dotnet (Join-Path $checkDir 'Harness.dll')
if ($LASTEXITCODE -ne 0) { throw 'Checkpoint regression failed.' }
Write-Output "Artifacts: $checkDir"
