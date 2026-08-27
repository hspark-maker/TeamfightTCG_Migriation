param(
    [ValidateSet('on', 'off', 'status')]
    [string]$Action = 'status',

    # 추가로 차단할 실행 파일(스탠드얼론 빌드 등). 여러 개 지정 가능.
    [string[]]$Program = @()
)

$ErrorActionPreference = 'Stop'

# 규칙 이름이 곧 해제 키다 — 이 이름의 규칙을 전부 지우는 것으로 off 가 성립한다.
$RuleName = 'TFTCG-offline-test'
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Test-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# 실행 중인 에디터를 최우선으로 본다 — Hub에 여러 버전이 깔려 있어도 지금 쓰는 것이 확실하다.
function Get-UnityExePaths {
    $paths = New-Object System.Collections.Generic.List[string]

    Get-Process Unity -ErrorAction SilentlyContinue | ForEach-Object {
        if ($_.Path) { $paths.Add($_.Path) }
    }

    if ($paths.Count -eq 0) {
        $hub = 'C:\Program Files\Unity\Hub\Editor'
        if (Test-Path $hub) {
            Get-ChildItem $hub -Directory | Sort-Object Name -Descending | ForEach-Object {
                $exe = Join-Path $_.FullName 'Editor\Unity.exe'
                if (Test-Path $exe) { $paths.Add($exe) }
            }
        }
    }

    foreach ($p in $Program) {
        if (Test-Path $p) { $paths.Add((Resolve-Path $p).Path) }
        else { Write-Warning "실행 파일을 찾지 못했습니다: $p" }
    }

    return $paths | Select-Object -Unique
}

# 세이브 캐시는 폐기됐다 — 이 파일의 수정 시각이 그대로여야 캐시 쓰기 경로가 없다는 뜻이다.
function Show-SaveCache {
    $settings = Join-Path $ProjectRoot 'ProjectSettings\ProjectSettings.asset'
    if (-not (Test-Path $settings)) { return }

    $text = Get-Content $settings -Raw
    $company = if ($text -match 'companyName:\s*(.+)') { $Matches[1].Trim() } else { $null }
    $product = if ($text -match 'productName:\s*(.+)') { $Matches[1].Trim() } else { $null }
    if (-not $company -or -not $product) { return }

    $base = Join-Path $env:USERPROFILE "AppData\LocalLow\$company\$product"
    if (-not (Test-Path $base)) { return }

    Write-Host ''
    Write-Host '[세이브 캐시 파일 — 부트 후 수정 시각이 그대로여야 정상]' -ForegroundColor Cyan
    $found = Get-ChildItem $base -Recurse -Filter 'outgame_save.json' -ErrorAction SilentlyContinue
    if (-not $found) {
        Write-Host '  (없음) 캐시가 생성되지 않은 상태입니다 — 기대한 동작입니다.'
        return
    }
    foreach ($f in $found) {
        Write-Host ("  {0}  <-  {1}" -f $f.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'), $f.FullName)
    }
}

function Show-Status {
    $rules = @(Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue)

    Write-Host ''
    if ($rules.Count -eq 0) {
        Write-Host '[상태] 온라인 — 차단 규칙 없음' -ForegroundColor Green
    }
    else {
        Write-Host ("[상태] 오프라인 모의 중 — 차단 규칙 {0}건" -f $rules.Count) -ForegroundColor Yellow
        foreach ($rule in $rules) {
            $filter = $rule | Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue
            $enabled = if ($rule.Enabled -eq 'True') { '켜짐' } else { '꺼짐' }
            Write-Host ("  [{0}] {1}" -f $enabled, $filter.Program)
        }
    }

    Show-SaveCache
}

function Enable-Block {
    $paths = @(Get-UnityExePaths)
    if ($paths.Count -eq 0) {
        throw 'Unity.exe 를 찾지 못했습니다. -Program 으로 경로를 직접 지정하세요.'
    }

    # 먼저 지운다 — 같은 이름의 낡은 규칙이 남아 있으면 어느 경로가 막혔는지 알 수 없게 된다.
    Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule

    foreach ($exe in $paths) {
        New-NetFirewallRule -DisplayName $RuleName `
            -Description 'TeamfightTCG 온라인 전용 부트 검증용 임시 아웃바운드 차단' `
            -Direction Outbound -Program $exe -Action Block -Profile Any | Out-Null
        Write-Host ("  차단 추가: {0}" -f $exe)
    }

    Write-Host ''
    Write-Host '[오프라인 모의 켜짐] PC 자체는 온라인입니다.' -ForegroundColor Yellow
    Write-Host '규칙은 새 연결부터 먹습니다. 물지 않으면 Unity 에디터를 재시작하세요.'
    Write-Host ''
    Write-Host '확인할 것:' -ForegroundColor Cyan
    Write-Host '  1. 로비로 넘어가지 않고 복구 화면에서 멈춘다 (이번 변경의 핵심)'
    Write-Host '  2. 복구 화면이 5초 안팎에 뜬다 (15초를 기다리면 CoFillBar 탈출이 안 먹은 것)'
    Write-Host '  3. 버튼 라벨이 "종료", 누르면 Play 모드가 꺼진다'
    Write-Host '  4. 콘솔: "Firebase authentication is unavailable." 또는 "Remote save read failed"'
    Write-Host '  5. "Offline boot — adopted the local cache" 가 없어야 한다 (코드에서 삭제된 문구)'
    Write-Host '  6. outgame_save.json 수정 시각이 그대로여야 한다'
}

function Disable-Block {
    $rules = @(Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue)
    if ($rules.Count -eq 0) {
        Write-Host '[해제] 차단 규칙이 이미 없습니다.' -ForegroundColor Green
        return
    }

    $rules | Remove-NetFirewallRule
    Write-Host ("[해제] 차단 규칙 {0}건을 제거했습니다. 온라인으로 돌아왔습니다." -f $rules.Count) -ForegroundColor Green
    Write-Host '다시 Play 해서 정상 부팅되는지 확인하세요 — 규칙이 덜 지워진 상태와 구분됩니다.'
}

if ($Action -ne 'status' -and -not (Test-Admin)) {
    throw '방화벽 규칙 변경에는 관리자 권한이 필요합니다. OfflineBootTest.bat 또는 Unity 메뉴 Tools/Card Battle/오프라인 부트 검증 으로 실행하세요.'
}

switch ($Action) {
    'on' { Enable-Block; Show-SaveCache }
    'off' { Disable-Block }
    'status' { Show-Status }
}
