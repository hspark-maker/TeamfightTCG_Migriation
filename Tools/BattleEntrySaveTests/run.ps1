param([string]$UnityData = 'C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Data')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$testOutput = Join-Path ([System.IO.Path]::GetTempPath()) ('battle-entry-save-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testOutput | Out-Null
$saveRoot = Join-Path $projectRoot 'Assets\Scripts\OutGame\Save\4.Cloud'
$source = [System.IO.File]::ReadAllText((Join-Path $saveRoot 'PlayerSaveCloud.cs'))
$pattern = '(?ms)^    internal static async UniTask<bool> FlushForBattleEntryAsync\(CancellationToken _ct\).*?^    }(?=\r?\n\r?\n    /// <summary>.*?\r?\n    internal static async UniTask SuspendUploadsAsync\(\))'
$matches = [regex]::Matches($source, $pattern)
if ($matches.Count -ne 1) { throw 'Expected exactly one battle entry wait bounded by SuspendUploadsAsync; update the extraction boundary if methods moved.' }
$extracted = "using System.Threading;`nusing Cysharp.Threading.Tasks;`nstatic partial class PlayerSaveCloud`n{`n" + $matches[0].Value + "`n}`n"
$extractedPath = Join-Path $testOutput 'PlayerSaveCloud.EntryWait.cs'
[System.IO.File]::WriteAllText($extractedPath, $extracted, (New-Object System.Text.UTF8Encoding($false)))

$caller = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'Assets\Scripts\Network\DeckLockSubmission.cs'))
if ($caller -match 'FirebaseManager\.FlushPendingAsync\(') { throw 'Deck entry still waits for all Firebase modules.' }
if ($caller -notmatch 'if \(!await PlayerSaveCloud\.FlushForBattleEntryAsync\(t_flushTimeout\.Token\)\)') { throw 'Deck entry must check the dedicated wait result.' }
if ($caller -notmatch 'FlushFallbackTimeout = TimeSpan\.FromSeconds\(15\)' -or $caller -notmatch 't_flushTimeout\.CancelAfter\(FlushFallbackTimeout\)') { throw 'The 15-second save wait cap is missing.' }
if ($matches[0].Value -match 'MatchResultSubmission|PayoutInbox|FirebaseManager\.FlushPendingAsync') { throw 'Dedicated save wait starts unrelated settlement work.' }
Write-Output 'PASS caller integration source checks (dedicated wait, result guard, 15-second cap)'

$refs = Join-Path $UnityData 'MonoBleedingEdge\lib\mono\4.8-api'
$testExe = Join-Path $testOutput 'BattleEntrySaveTests.exe'
& "$UnityData\NetCoreRuntime\dotnet.exe" "$UnityData\DotNetSdkRoslyn\csc.dll" `
    /nologo /langversion:latest /noconfig /nostdlib /target:exe /nowarn:0414 `
    "/out:$testExe" "/r:$refs\mscorlib.dll" "/r:$refs\System.dll" "/r:$refs\System.Core.dll" `
    $extractedPath "$saveRoot\EPlayerSaveCloudState.cs" "$PSScriptRoot\Dependencies.cs" `
    "$PSScriptRoot\UniTaskStubs.cs" "$PSScriptRoot\Tests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Battle entry save test compilation failed.' }
& "$UnityData\MonoBleedingEdge\bin\mono.exe" $testExe
if ($LASTEXITCODE -ne 0) { throw 'Battle entry save behavior tests failed.' }

$unityRefs = Join-Path $UnityData 'UnityReferenceAssemblies\unity-4.8-api'
$checkDll = Join-Path $testOutput 'BattleEntrySave.UnityApiCheck.dll'
& "$UnityData\NetCoreRuntime\dotnet.exe" "$UnityData\DotNetSdkRoslyn\csc.dll" `
    /nologo /langversion:latest /noconfig /nostdlib /target:library /nowarn:0414 `
    "/out:$checkDll" "/r:$unityRefs\mscorlib.dll" "/r:$unityRefs\System.dll" `
    "/r:$unityRefs\System.Core.dll" "/r:$unityRefs\Facades\netstandard.dll" `
    "/r:$UnityData\Managed\UnityEngine\UnityEngine.CoreModule.dll" `
    "/r:$projectRoot\Library\ScriptAssemblies\UniTask.dll" `
    $extractedPath "$saveRoot\EPlayerSaveCloudState.cs" "$PSScriptRoot\Dependencies.cs"
if ($LASTEXITCODE -ne 0) { throw 'Battle entry save Unity/UniTask API compilation failed.' }
Write-Output 'PASS extracted entry wait compiled against installed Unity/UniTask assemblies'
