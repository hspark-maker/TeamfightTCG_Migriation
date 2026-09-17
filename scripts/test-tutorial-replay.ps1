param([string]$UnityEditorData = 'C:/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Data')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$checkDir = Join-Path ([IO.Path]::GetTempPath()) ('TutorialReplay-' + [Guid]::NewGuid().ToString('N'))
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

$bridge = [IO.File]::ReadAllText((Join-Path $repo 'Assets/Scripts/UI/Tutorial/OutgameTutorialBridge.cs'))
$methods = @(
    '    void ApplyCurrentStep()',
    '    async UniTask ApplyCurrentStepAsync()',
    '    void OnGateSatisfied()',
    '    async UniTask CompleteStepAsync(TutorialStepDef _step)'
) | ForEach-Object { Read-Method $bridge $_ }
# The UI/remote boundaries are stubbed; production control flow and session logic are compiled.
# Task replaces UniTask only to run outside Unity's player loop.
$harness = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'tests/TutorialReplayHarness.cs'))
$harness = $harness.Replace('/* PRODUCTION_BRIDGE_METHODS */', ($methods -join "`n")).Replace('UniTask', 'Task')
[IO.File]::WriteAllText((Join-Path $checkDir 'Harness.cs'), $harness)
$session = [IO.File]::ReadAllText((Join-Path $repo 'Assets/Scripts/OutGame/Tutorial/OnboardingSession.cs'))
$session = $session.Replace('using Cysharp.Threading.Tasks;', 'using System.Threading.Tasks;').Replace('UniTask', 'Task')
[IO.File]::WriteAllText((Join-Path $checkDir 'OnboardingSession.cs'), $session)
$dotnet = Join-Path $UnityEditorData 'NetCoreRuntime/dotnet.exe'
$runtime = Get-ChildItem (Join-Path $UnityEditorData 'NetCoreRuntime/shared/Microsoft.NETCore.App') -Directory |
    Sort-Object { [Version]$_.Name } -Descending | Select-Object -First 1
$compiler = Join-Path $UnityEditorData 'DotNetSdkRoslyn/csc.dll'
$references = Get-ChildItem $runtime.FullName -Filter '*.dll' | Where-Object {
    try { [Reflection.AssemblyName]::GetAssemblyName($_.FullName) | Out-Null; $true } catch { $false }
} | ForEach-Object { '-r:"' + $_.FullName + '"' }
$arguments = @('-nologo', '-target:exe', '-langversion:9.0', '-nostdlib+',
    ('-out:"' + (Join-Path $checkDir 'Harness.dll') + '"')) + $references + @(
    ('"' + (Join-Path $checkDir 'Harness.cs') + '"'),
    ('"' + (Join-Path $checkDir 'OnboardingSession.cs') + '"'))
$response = Join-Path $checkDir 'compile.rsp'
[IO.File]::WriteAllLines($response, $arguments)
& $dotnet $compiler ('@' + $response)
if ($LASTEXITCODE -ne 0) { throw 'Tutorial replay harness compilation failed.' }
$config = @{ runtimeOptions = @{ tfm = 'net6.0'; framework = @{ name = 'Microsoft.NETCore.App'; version = $runtime.Name } } }
[IO.File]::WriteAllText((Join-Path $checkDir 'Harness.runtimeconfig.json'), ($config | ConvertTo-Json -Depth 4))
& $dotnet (Join-Path $checkDir 'Harness.dll')
if ($LASTEXITCODE -ne 0) { throw 'Tutorial replay regression failed.' }
Write-Output "Artifacts: $checkDir"
