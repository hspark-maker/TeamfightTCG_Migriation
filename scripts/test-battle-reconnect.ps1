param(
    [string]$UnityEditorData
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (!$UnityEditorData) {
    $versionFile = Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt'
    $version = [regex]::Match((Get-Content $versionFile -Raw), 'm_EditorVersion: (\S+)').Groups[1].Value
    $UnityEditorData = Join-Path ${env:ProgramFiles} "Unity/Hub/Editor/$version/Editor/Data"
}
$compiler = Join-Path $UnityEditorData 'DotNetSdkRoslyn/csc.dll'
$runtime = Join-Path $UnityEditorData 'NetCoreRuntime/dotnet.exe'
$mono = Join-Path $UnityEditorData 'MonoBleedingEdge/bin/mono.exe'
$corlib = Join-Path $UnityEditorData 'MonoBleedingEdge/lib/mono/4.5/mscorlib.dll'
foreach ($required in @($compiler, $runtime, $mono, $corlib)) {
    if (!(Test-Path -LiteralPath $required)) { throw "Unity compiler dependency missing: $required" }
}
$taskDir = Join-Path $projectRoot 'Temp/reconnect-protocol-check'
New-Item -ItemType Directory -Path $taskDir -Force | Out-Null
$source = Get-Content (Join-Path $projectRoot 'Assets/Scripts/Network/BattleReconnect.cs') -Raw -Encoding utf8
# Preserve production state-machine code. Replace only async adapters to run without Unity's player loop.
$source = $source.Replace('async UniTaskVoid', 'async System.Threading.Tasks.Task')
$source = $source.Replace('async UniTask', 'async System.Threading.Tasks.Task')
$source = $source.Replace('UniTask.Yield(', 'FakeLoop.Yield(')
$adaptedSource = Join-Path $taskDir 'BattleReconnect.cs'
[IO.File]::WriteAllText($adaptedSource, $source)
$output = Join-Path $taskDir 'ProtocolCheck.exe'
& $runtime $compiler -nologo -noconfig -nostdlib -langversion:9 -target:exe "-out:$output" "-r:$corlib" `
    (Join-Path $PSScriptRoot 'tests/BattleReconnectHarness.cs') $adaptedSource `
    (Join-Path $projectRoot 'Assets/Scripts/Network/ReliableBattleChannel.cs')
if ($LASTEXITCODE -ne 0) { throw 'Reconnect protocol harness compilation failed.' }
& $mono $output
if ($LASTEXITCODE -ne 0) { throw 'Reconnect protocol regression failed.' }
