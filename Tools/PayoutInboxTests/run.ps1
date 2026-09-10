param(
    [string]$UnityData = 'C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Data'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$testOutput = Join-Path ([System.IO.Path]::GetTempPath()) ('payout-inbox-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testOutput | Out-Null
$refs = Join-Path $UnityData 'MonoBleedingEdge\lib\mono\4.8-api'
$testExe = Join-Path $testOutput 'PayoutInboxTests.exe'
& "$UnityData\NetCoreRuntime\dotnet.exe" "$UnityData\DotNetSdkRoslyn\csc.dll" `
    /nologo /langversion:latest /noconfig /nostdlib /target:exe `
    "/out:$testExe" "/r:$refs\mscorlib.dll" "/r:$refs\System.dll" "/r:$refs\System.Core.dll" `
    "$projectRoot\Assets\Scripts\Network\PayoutInbox.cs" "$PSScriptRoot\Stubs.cs" "$PSScriptRoot\Tests.cs"
if ($LASTEXITCODE -ne 0) { throw 'PayoutInbox test compilation failed.' }
& "$UnityData\MonoBleedingEdge\bin\mono.exe" $testExe
if ($LASTEXITCODE -ne 0) { throw 'PayoutInbox behavior tests failed.' }
