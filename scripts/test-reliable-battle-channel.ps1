$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Add-Type -Path @(
    (Join-Path $projectRoot 'Assets/Scripts/Network/ReliableBattleChannel.cs'),
    (Join-Path $PSScriptRoot 'tests/ReliableBattleChannelTests.cs')
)
[ReliableBattleChannelTests]::Run()
