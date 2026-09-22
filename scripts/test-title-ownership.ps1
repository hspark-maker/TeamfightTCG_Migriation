param([string]$UnityEditorData = 'C:/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Data')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$checkDir = Join-Path ([IO.Path]::GetTempPath()) ('TitleOwnership-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($checkDir) | Out-Null
$dotnet = Join-Path $UnityEditorData 'NetCoreRuntime/dotnet.exe'
$runtime = Get-ChildItem (Join-Path $UnityEditorData 'NetCoreRuntime/shared/Microsoft.NETCore.App') -Directory |
    Sort-Object { [Version]$_.Name } -Descending | Select-Object -First 1
$compiler = Join-Path $UnityEditorData 'DotNetSdkRoslyn/csc.dll'
$references = Get-ChildItem $runtime.FullName -Filter '*.dll' | Where-Object {
    try { [Reflection.AssemblyName]::GetAssemblyName($_.FullName) | Out-Null; $true } catch { $false }
} | ForEach-Object { '-r:"' + $_.FullName + '"' }
$arguments = @('-nologo', '-target:exe', '-langversion:9.0', '-nostdlib+',
    ('-out:"' + (Join-Path $checkDir 'Harness.dll') + '"')) + $references + @(
    ('"' + (Join-Path $PSScriptRoot 'tests/TitleOwnershipHarness.cs') + '"'),
    ('"' + (Join-Path $repo 'Assets/Scripts/OutGame/Title/TitleManager.cs') + '"'),
    ('"' + (Join-Path $repo 'Assets/Scripts/OutGame/Title/TitleUnlocks.cs') + '"'),
    ('"' + (Join-Path $repo 'Assets/Scripts/OutGame/Account/AccountRewardHandoff.cs') + '"'),
    ('"' + (Join-Path $repo 'Assets/Scripts/Editor/SpecFirestoreUploader.TitleCsv.cs') + '"'))
$response = Join-Path $checkDir 'compile.rsp'
[IO.File]::WriteAllLines($response, $arguments)
& $dotnet $compiler ('@' + $response)
if ($LASTEXITCODE -ne 0) { throw 'Title harness compilation failed.' }
$config = @{ runtimeOptions = @{ tfm = 'net6.0'; framework = @{ name = 'Microsoft.NETCore.App'; version = $runtime.Name } } }
[IO.File]::WriteAllText((Join-Path $checkDir 'Harness.runtimeconfig.json'), ($config | ConvertTo-Json -Depth 4))
& $dotnet (Join-Path $checkDir 'Harness.dll')
if ($LASTEXITCODE -ne 0) { throw 'Title ownership regression failed.' }
