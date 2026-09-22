param([string]$UnityEditorData = 'C:/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Data')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$checkDir = Join-Path ([IO.Path]::GetTempPath()) ('TutorialGrantRecovery-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($checkDir) | Out-Null

function Read-Method([string]$source, [string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production method: $signature" }
    $body = $source.IndexOf('{', $start)
    $depth = 0
    for ($index = $body; $index -lt $source.Length; $index++) {
        if ($source[$index] -eq '{') { $depth++ }
        elseif ($source[$index] -eq '}') {
            $depth--
            if ($depth -eq 0) { return $source.Substring($start, $index - $start + 1) }
        }
    }
    throw "Unclosed production method: $signature"
}

$methods = foreach ($manager in @('CardGrowthManager', 'KeywordGrowthManager')) {
    $source = [IO.File]::ReadAllText((Join-Path $repo "Assets/Scripts/OutGame/Growth/$manager.cs"))
    $method = Read-Method $source '    static async UniTask RefreshFreeShotMarkIfRejectedAsync('
    # Compile the production branch with Task in place of Unity's player-loop task.
    $method = $method.Replace('static async UniTask', 'public static async Task')
    "public static class $manager { $method }"
}

$harness = @'
using System;
using System.Threading.Tasks;
public enum EEnhanceOutcome { Success, Failed, NotAffordable, MaxLevel, NotReady }
public struct EnhanceCommandResult { public bool RejectedByServer; public EEnhanceOutcome Outcome; }
public static class OutgameTutorialGuide
{
    public static int Reads;
    public static bool Spent, ReadSucceeds;
    public static Task RefreshFreeShotSpentAsync()
    {
        Reads++;
        if (ReadSucceeds) Spent = true;
        return Task.CompletedTask;
    }
}
public static class Program
{
    public static async Task Main()
    {
        int checks = 0;
        foreach (var execute in new Func<bool, EnhanceCommandResult, Task>[] {
            CardGrowthManager.RefreshFreeShotMarkIfRejectedAsync,
            KeywordGrowthManager.RefreshFreeShotMarkIfRejectedAsync })
        foreach (bool free in new[] { false, true })
        foreach (bool rejected in new[] { false, true })
        foreach (bool readSucceeds in new[] { false, true })
        foreach (EEnhanceOutcome outcome in Enum.GetValues(typeof(EEnhanceOutcome)))
        {
            OutgameTutorialGuide.Reads = 0;
            OutgameTutorialGuide.Spent = false;
            OutgameTutorialGuide.ReadSucceeds = readSucceeds;
            await execute(free, new EnhanceCommandResult { RejectedByServer = rejected, Outcome = outcome });
            bool shouldRead = free && rejected && (outcome == EEnhanceOutcome.NotReady
                || outcome == EEnhanceOutcome.NotAffordable || outcome == EEnhanceOutcome.MaxLevel);
            if (OutgameTutorialGuide.Reads != (shouldRead ? 1 : 0))
                throw new Exception($"Wrong read count: {execute.Method.DeclaringType}, free={free}, rejected={rejected}, outcome={outcome}");
            if (OutgameTutorialGuide.Spent != (shouldRead && readSucceeds))
                throw new Exception("Failed read must not consume the grant.");
            checks++;
        }
        Console.WriteLine($"PASS {checks} grant recovery cases: one read on rejected free enhancement; failed reads remain unconfirmed; paid and nonserver failures unchanged.");
    }
}
'@
[IO.File]::WriteAllText((Join-Path $checkDir 'Harness.cs'), $harness + "`n" + ($methods -join "`n"))
$dotnet = Join-Path $UnityEditorData 'NetCoreRuntime/dotnet.exe'
$compiler = Join-Path $UnityEditorData 'DotNetSdkRoslyn/csc.dll'
$runtime = Get-ChildItem (Join-Path $UnityEditorData 'NetCoreRuntime/shared/Microsoft.NETCore.App') -Directory |
    Sort-Object { [Version]$_.Name } -Descending | Select-Object -First 1
$references = Get-ChildItem $runtime.FullName -Filter '*.dll' | Where-Object {
    try { [Reflection.AssemblyName]::GetAssemblyName($_.FullName) | Out-Null; $true } catch { $false }
} | ForEach-Object { '-r:"' + $_.FullName + '"' }
$arguments = @('-nologo', '-target:exe', '-langversion:9.0', '-nostdlib+',
    ('-out:"' + (Join-Path $checkDir 'Harness.dll') + '"')) + $references + @(
    ('"' + (Join-Path $checkDir 'Harness.cs') + '"'))
$response = Join-Path $checkDir 'compile.rsp'
[IO.File]::WriteAllLines($response, $arguments)
& $dotnet $compiler ('@' + $response)
if ($LASTEXITCODE -ne 0) { throw 'Tutorial grant recovery harness compilation failed.' }
$config = @{ runtimeOptions = @{ tfm = 'net6.0'; framework = @{ name = 'Microsoft.NETCore.App'; version = $runtime.Name } } }
[IO.File]::WriteAllText((Join-Path $checkDir 'Harness.runtimeconfig.json'), ($config | ConvertTo-Json -Depth 4))
& $dotnet (Join-Path $checkDir 'Harness.dll')
if ($LASTEXITCODE -ne 0) { throw 'Tutorial grant recovery regression failed.' }
Write-Output "Artifacts: $checkDir"
