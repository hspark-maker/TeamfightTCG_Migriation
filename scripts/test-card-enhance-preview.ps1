param([string]$UnityEditorData = 'C:/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Data')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$checkDir = Join-Path ([IO.Path]::GetTempPath()) ('CardEnhancePreview-' + [Guid]::NewGuid().ToString('N'))
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

$manager = [IO.File]::ReadAllText((Join-Path $repo 'Assets/Scripts/OutGame/Growth/CardGrowthManager.cs'))
$start = $manager.IndexOf('    public static CardGrowth GrowthOf(int _id)', [StringComparison]::Ordinal)
$end = $manager.IndexOf('    public static bool TryGetEvolutionStep(', $start, [StringComparison]::Ordinal)
if ($start -lt 0 -or $end -lt 0) { throw 'Missing production preview methods' }
$methods = @($manager.Substring($start, $end - $start))
$methods += @('    public static int LevelOf(int _id)', '    static int ResolveEnhanceAmount(',
    '    static CardGrowth Snapshot(') | ForEach-Object { Read-Method $manager $_ }
$harness = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'tests/CardEnhancePreviewHarness.cs'))
$harness = $harness.Replace('/* PRODUCTION_METHODS */', ($methods -join "`n"))
[IO.File]::WriteAllText((Join-Path $checkDir 'Harness.cs'), $harness)
$dotnet = Join-Path $UnityEditorData 'NetCoreRuntime/dotnet.exe'
$runtime = Get-ChildItem (Join-Path $UnityEditorData 'NetCoreRuntime/shared/Microsoft.NETCore.App') -Directory |
    Sort-Object { [Version]$_.Name } -Descending | Select-Object -First 1
$compiler = Join-Path $UnityEditorData 'DotNetSdkRoslyn/csc.dll'
$references = Get-ChildItem $runtime.FullName -Filter '*.dll' | Where-Object {
    try { [Reflection.AssemblyName]::GetAssemblyName($_.FullName) | Out-Null; $true } catch { $false }
} | ForEach-Object { '-r:"' + $_.FullName + '"' }
$sources = @((Join-Path $checkDir 'Harness.cs'),
    (Join-Path $repo 'Assets/Scripts/OutGame/Growth/GrowthRules.cs'),
    (Join-Path $repo 'Assets/Scripts/BattleCore/CardGrowth.cs')) | ForEach-Object { '"' + $_ + '"' }
$arguments = @('-nologo', '-target:exe', '-langversion:9.0', '-nostdlib+',
    ('-out:"' + (Join-Path $checkDir 'Harness.dll') + '"')) + $references + $sources
$response = Join-Path $checkDir 'compile.rsp'
[IO.File]::WriteAllLines($response, $arguments)
& $dotnet $compiler ('@' + $response)
if ($LASTEXITCODE -ne 0) { throw 'Card enhancement preview harness compilation failed.' }
$config = @{ runtimeOptions = @{ tfm = 'net6.0'; framework = @{ name = 'Microsoft.NETCore.App'; version = $runtime.Name } } }
[IO.File]::WriteAllText((Join-Path $checkDir 'Harness.runtimeconfig.json'), ($config | ConvertTo-Json -Depth 4))
& $dotnet (Join-Path $checkDir 'Harness.dll')
if ($LASTEXITCODE -ne 0) { throw 'Card enhancement preview regression failed.' }
Write-Output "Artifacts: $checkDir"
