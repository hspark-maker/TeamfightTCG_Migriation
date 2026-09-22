param([string]$UnityEditorData = 'C:/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Data')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$checkDir = Join-Path ([IO.Path]::GetTempPath()) ('ProfileCosmetics-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($checkDir) | Out-Null
$source = [IO.File]::ReadAllText((Join-Path $repo 'Assets/Scripts/OutGame/Save/3.Manager/DataSaveManager.cs'))
$start = $source.IndexOf('    internal static ESaveSlot AdoptServerSlots(', [StringComparison]::Ordinal)
if ($start -lt 0) { throw 'Production adoption method missing.' }
$body = $source.IndexOf('{', $start)
$depth = 0
for ($i = $body; $i -lt $source.Length; $i++) {
    if ($source[$i] -eq '{') { $depth++ }
    elseif ($source[$i] -eq '}') { $depth--; if ($depth -eq 0) { break } }
}
$method = $source.Substring($start, $i - $start + 1)
$harness = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'tests/ProfileCosmeticsHarness.cs')).Replace('/* PRODUCTION_ADOPT */', $method)
[IO.File]::WriteAllText((Join-Path $checkDir 'Harness.cs'), $harness)
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
    ('"' + (Join-Path $repo 'Assets/Scripts/OutGame/Profile/ProfileManager.cs') + '"'),
    ('"' + (Join-Path $repo 'Assets/Scripts/OutGame/Save/4.Cloud/PlayerSaveDocument.cs') + '"'),
    ('"' + (Join-Path $repo 'Assets/Scripts/OutGame/Save/2.Domain/ProfileSaveData.cs') + '"'))
$response = Join-Path $checkDir 'compile.rsp'
[IO.File]::WriteAllLines($response, $arguments)
& $dotnet $compiler ('@' + $response)
if ($LASTEXITCODE -ne 0) { throw 'Profile cosmetics harness compilation failed.' }
$config = @{ runtimeOptions = @{ tfm = 'net6.0'; framework = @{ name = 'Microsoft.NETCore.App'; version = $runtime.Name } } }
[IO.File]::WriteAllText((Join-Path $checkDir 'Harness.runtimeconfig.json'), ($config | ConvertTo-Json -Depth 4))
& $dotnet (Join-Path $checkDir 'Harness.dll')
if ($LASTEXITCODE -ne 0) { throw 'Profile cosmetics regression failed.' }
Write-Output "Artifacts: $checkDir"
