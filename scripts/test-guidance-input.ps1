param([string]$UnityEditorData = 'C:/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Data')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$checkDir = Join-Path ([IO.Path]::GetTempPath()) ('GuidanceInput-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($checkDir) | Out-Null

function Read-Member([string]$source, [string]$signature, [bool]$expression = $false) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    if ($expression) {
        $end = $source.IndexOf(';', $start)
        if ($end -lt 0) { throw "Unclosed expression: $signature" }
        return $source.Substring($start, $end - $start + 1)
    }
    $body = $source.IndexOf('{', $start)
    $depth = 0
    for ($index = $body; $index -lt $source.Length; $index++) {
        if ($source[$index] -eq '{') { $depth++ }
        elseif ($source[$index] -eq '}') {
            $depth--
            if ($depth -eq 0) { return $source.Substring($start, $index - $start + 1) }
        }
    }
    throw "Unclosed production member: $signature"
}

$missions = [IO.File]::ReadAllText((Join-Path $repo 'Assets/Scripts/UI/Tutorial/GuidanceCoordinator.Missions.cs'))
$recovery = [IO.File]::ReadAllText((Join-Path $repo 'Assets/Scripts/UI/Tutorial/GuidanceCoordinator.Recovery.cs'))
$members = @(
    '    public static bool IsInputLocked',
    '    public static bool IsRestoring',
    '    public static bool IsInternalNavigation',
    '    public static bool AllowsUserAction(',
    '    public static bool AllowsUserNavigation(',
    '    static bool AllowsFtueTabNavigation(',
    '    public static bool IsContentIntroBlockingNavigation',
    '    public static bool IsLobbyPresentationBlockingNavigation',
    '    public static IDisposable InternalNavigation('
) | ForEach-Object { Read-Member $missions $_ $true }
$members += Read-Member $missions '    sealed class NavigationScope'
$members += @(
    '    public static bool CanCloseCardDetail',
    '    public static bool CanCloseAlbum',
    '    static bool CanAcceptGuideReturn'
) | ForEach-Object { Read-Member $recovery $_ $true }
$members += Read-Member $recovery '    internal static async UniTask<bool> TryRestoreForcedSurfaceAsync('
$members += Read-Member $recovery '    async UniTask SelectFlowTabAsync('
# Only Unity's frame scheduler is substituted; tab selection, timeout and cancellation are production code.
$production = ($members -join "`n").Replace('await UniTask.Yield(_ct);', 'await TestLoop.Yield(_ct);').Replace('UniTask', 'Task')
$harness = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'tests/GuidanceInputHarness.cs'))
$harness = $harness.Replace('/* PRODUCTION_MEMBERS */', $production)
$detail = [IO.File]::ReadAllText((Join-Path $repo 'Assets/Scripts/UI/CardDetail/CardDetailOverlayView.cs'))
$harness = $harness.Replace('/* PRODUCTION_DETAIL_OPEN */', (Read-Member $detail '    public static void Open(IReadOnlyList<int>'))
$asset = [IO.File]::ReadAllText((Join-Path $repo 'Assets/SO/TutorialConfig/Outgame/OutgameTutorial.asset'))
$ftue = $asset.Substring($asset.IndexOf('  ftueChapters:'), $asset.IndexOf('  guide:') - $asset.IndexOf('  ftueChapters:'))
$steps = [regex]::Matches($ftue, '(?s)- stepId: (\d+)\s+action: (\d+)\s+anchor: (\d+).*?useDim: (\d+)')
if ($steps.Count -eq 0) { throw 'No authored FTUE steps found.' }
$rows = foreach ($step in $steps) {
    'new TutorialStepDef { StepId = ' + $step.Groups[1].Value + ', Action = (EOutgameTutorialAction)' +
        $step.Groups[2].Value + ', Anchor = (EOutgameTutorialAnchor)' + $step.Groups[3].Value +
        ', UseDim = ' + $(if ($step.Groups[4].Value -eq '0') { 'false' } else { 'true' }) + ' },'
}
$harness = $harness.Replace('/* AUTHORED_FTUE */', ($rows -join "`n"))
[IO.File]::WriteAllText((Join-Path $checkDir 'Harness.cs'), $harness)
$sources = @('Harness.cs')
foreach ($file in @('EOutgameTutorialAnchor.cs', 'EOutgameFeature.cs',
        'Steps/EOutgameTutorialAction.cs', 'Steps/EOutgameTutorialCompletion.cs', 'Steps/TutorialActionMeta.cs')) {
    $name = Split-Path $file -Leaf
    Copy-Item -LiteralPath (Join-Path $repo "Assets/Scripts/OutGame/Tutorial/$file") -Destination (Join-Path $checkDir $name)
    $sources += $name
}
$dotnet = Join-Path $UnityEditorData 'NetCoreRuntime/dotnet.exe'
$compiler = Join-Path $UnityEditorData 'DotNetSdkRoslyn/csc.dll'
$runtime = Get-ChildItem (Join-Path $UnityEditorData 'NetCoreRuntime/shared/Microsoft.NETCore.App') -Directory |
    Sort-Object { [Version]$_.Name } -Descending | Select-Object -First 1
$references = Get-ChildItem $runtime.FullName -Filter '*.dll' | Where-Object {
    try { [Reflection.AssemblyName]::GetAssemblyName($_.FullName) | Out-Null; $true } catch { $false }
} | ForEach-Object { '-r:"' + $_.FullName + '"' }
$arguments = @('-nologo', '-target:exe', '-langversion:9.0', '-nostdlib+',
    ('-out:"' + (Join-Path $checkDir 'Harness.dll') + '"')) + $references + @(
    $sources | ForEach-Object { '"' + (Join-Path $checkDir $_) + '"' })
$response = Join-Path $checkDir 'compile.rsp'
[IO.File]::WriteAllLines($response, $arguments)
& $dotnet $compiler ('@' + $response)
if ($LASTEXITCODE -ne 0) { throw 'Guidance input harness compilation failed.' }
$config = @{ runtimeOptions = @{ tfm = 'net6.0'; framework = @{ name = 'Microsoft.NETCore.App'; version = $runtime.Name } } }
[IO.File]::WriteAllText((Join-Path $checkDir 'Harness.runtimeconfig.json'), ($config | ConvertTo-Json -Depth 4))
& $dotnet (Join-Path $checkDir 'Harness.dll')
if ($LASTEXITCODE -ne 0) { throw 'Guidance input regression failed.' }
Write-Output "Artifacts: $checkDir"
