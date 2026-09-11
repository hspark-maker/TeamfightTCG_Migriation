param([string]$UnityData = 'C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Data')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$testOutput = Join-Path ([System.IO.Path]::GetTempPath()) ('content-unlock-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testOutput | Out-Null
$refs = Join-Path $UnityData 'MonoBleedingEdge\lib\mono\4.8-api'
$testExe = Join-Path $testOutput 'ContentUnlockTests.exe'
$unlockRoot = Join-Path $projectRoot 'Assets\Scripts\OutGame\ContentUnlock'
$saveRoot = Join-Path $projectRoot 'Assets\Scripts\OutGame\Save\2.Domain'
$json = Get-ChildItem -Path "$projectRoot\Library\PackageCache\com.unity.nuget.newtonsoft-json*\Runtime\Newtonsoft.Json.dll" | Select-Object -First 1
if (!$json) { throw 'Unity Newtonsoft.Json package not found.' }
Copy-Item -LiteralPath $json.FullName -Destination $testOutput
& "$UnityData\NetCoreRuntime\dotnet.exe" "$UnityData\DotNetSdkRoslyn\csc.dll" `
    /nologo /langversion:latest /noconfig /nostdlib /target:exe `
    "/out:$testExe" "/r:$refs\mscorlib.dll" "/r:$refs\System.dll" "/r:$refs\System.Core.dll" `
    "/r:$refs\Facades\netstandard.dll" "/r:$($json.FullName)" `
    "$unlockRoot\ContentUnlockManager.cs" "$unlockRoot\ContentUnlockRules.cs" `
    "$saveRoot\ContentUnlockSaveData.cs" "$saveRoot\ProfileSaveData.cs" `
    "$PSScriptRoot\Stubs.cs" "$PSScriptRoot\Tests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Content unlock test compilation failed.' }
& "$UnityData\MonoBleedingEdge\bin\mono.exe" $testExe
if ($LASTEXITCODE -ne 0) { throw 'Content unlock behavior tests failed.' }
