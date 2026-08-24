param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'

$packPath = Join-Path $ProjectRoot 'docs/SpecData/CardPack_sheet.csv'
$dropPath = Join-Path $ProjectRoot 'docs/SpecData/CardPackDrop_sheet.csv'
$cardPath = Join-Path $ProjectRoot 'docs/SpecData/Card_sheet.csv'
$utf8Bom = New-Object System.Text.UTF8Encoding($true)

function Decode-Utf8([string]$Base64) {
    return [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Base64))
}

function Read-SpecCsv([string]$Path) {
    $lines = [IO.File]::ReadAllLines($Path)
    $csv = @($lines[1]) + @($lines | Select-Object -Skip 3)
    $text = $csv -join "`n"
    return @($text | ConvertFrom-Csv)
}

function Write-SpecCsv([string]$Path, [string[]]$Header, [object[]]$Rows, [string[]]$Columns) {
    $body = foreach ($row in $Rows) {
        ($Columns | ForEach-Object { [string]$row.$_ }) -join ','
    }
    [IO.File]::WriteAllText($Path, (@($Header) + @($body) -join "`r`n") + "`r`n", $utf8Bom)
}

$cards = Read-SpecCsv $cardPath
$cardById = @{}
foreach ($card in $cards) { $cardById[[int]$card.id] = $card }

$packLines = [IO.File]::ReadAllLines($packPath)
$packRows = @(Read-SpecCsv $packPath | Where-Object { $_.packId -notin @(
    'HuntingBrandPack', 'ImmortalLegacyPack', 'IronArmorPack', 'GiantsGardenPack', 'ElementalFlowPack'
) })
$packRows += @(
    [pscustomobject]@{ id=7; packId='HuntingBrandPack'; displayName=(Decode-Utf8 '7IKs64Ol7J2YIOuCmeyduA=='); channel='Live'; priceType='Diamond'; price=30; drawCount=6; uniqueDraw=0; refundType='Shard'; refundAmount=8 },
    [pscustomobject]@{ id=8; packId='ImmortalLegacyPack'; displayName=(Decode-Utf8 '67aI66m47J2YIOycoOyCsA=='); channel='Live'; priceType='Diamond'; price=30; drawCount=6; uniqueDraw=0; refundType='Shard'; refundAmount=8 },
    [pscustomobject]@{ id=9; packId='IronArmorPack'; displayName=(Decode-Utf8 '7LKg67K97J2YIOqwkeyjvA=='); channel='Live'; priceType='Diamond'; price=30; drawCount=6; uniqueDraw=0; refundType='Shard'; refundAmount=8 },
    [pscustomobject]@{ id=10; packId='GiantsGardenPack'; displayName=(Decode-Utf8 '6rGw7J247J2YIOygleybkA=='); channel='Live'; priceType='Diamond'; price=30; drawCount=6; uniqueDraw=0; refundType='Shard'; refundAmount=8 },
    [pscustomobject]@{ id=11; packId='ElementalFlowPack'; displayName=(Decode-Utf8 '7JuQ7IaM7J2YIO2dkOumhA=='); channel='Live'; priceType='Diamond'; price=30; drawCount=6; uniqueDraw=0; refundType='Shard'; refundAmount=8 }
)
Write-SpecCsv $packPath $packLines[0..2] $packRows @('id','packId','displayName','channel','priceType','price','drawCount','uniqueDraw','refundType','refundAmount')

$dropLines = [IO.File]::ReadAllLines($dropPath)
$allDropRows = @(Read-SpecCsv $dropPath)
$managedPackIds = @('NormalPack_TEST','SpecialPack','HuntingBrandPack','ImmortalLegacyPack','IronArmorPack','GiantsGardenPack','ElementalFlowPack')
$existingIds = @{}
$maxId = 0
foreach ($row in $allDropRows) {
    $rowId = [int]$row.id
    if ($rowId -gt $maxId) { $maxId = $rowId }
    if ($row.packId -in $managedPackIds) {
        $existingIds["$($row.packId)|$($row.minGrade)|$($row.cardId)"] = $rowId
    }
}
$keptRows = @($allDropRows | Where-Object { $_.packId -notin $managedPackIds })
$nextId = $maxId + 1
$generated = New-Object System.Collections.Generic.List[object]

function Add-Drop([string]$PackId, [string]$Grade, [int]$CardId, [int]$Weight) {
    $key = "$PackId|$Grade|$CardId"
    if ($existingIds.ContainsKey($key)) { $id = $existingIds[$key] }
    else { $id = $script:nextId; $script:nextId++ }
    $generated.Add([pscustomobject]@{
        id=$id; packId=$PackId; minGrade=$Grade; cardId=$CardId; weight=$Weight; '#cardName'=$cardById[$CardId].displayName
    })
}

$rankPools = [ordered]@{
    Bronze  = @(1,2,3,5,6,17,18,19,29,30)
    Silver  = @(1,2,3,4,5,6,8,9,10,13,14,17,18,19,29,30)
    Gold    = @(1,2,3,4,5,6,8,9,10,11,12,13,14,15,16,17,18,19,22,25,29,30)
    Platinum = @(1,2,3,4,5,6,8,9,10,11,12,13,14,15,16,17,18,19,20,22,25,28,29,30,31,32,33)
    Diamond = @(1,2,3,4,5,6,8,9,10,11,12,13,14,15,16,17,18,19,20,22,25,28,29,30,31,32,33,34,35,36,37,38)
}

function Add-RankPool([string]$PackId, [string]$Grade, [int[]]$CardIds, [int]$CommonMass, [int]$RareMass) {
    $byGrade = @{
        Common = @($CardIds | Where-Object { $cardById[$_].grade -eq 'Common' } | Sort-Object)
        Rare = @($CardIds | Where-Object { $cardById[$_].grade -eq 'Rare' } | Sort-Object)
    }
    foreach ($gradeName in @('Common','Rare')) {
        $ids = $byGrade[$gradeName]
        if ($ids.Count -eq 0) { continue }
        $budget = $(if ($gradeName -eq 'Common') { $CommonMass * 100 } else { $RareMass * 100 })
        $base = [math]::Floor($budget / $ids.Count)
        $remainder = $budget - ($base * $ids.Count)
        for ($i = 0; $i -lt $ids.Count; $i++) {
            Add-Drop $PackId $Grade $ids[$i] ([int]$base + $(if ($i -lt $remainder) { 1 } else { 0 }))
        }
    }
}

foreach ($entry in $rankPools.GetEnumerator()) {
    Add-RankPool 'NormalPack_TEST' $entry.Key $entry.Value 80 20
}
foreach ($entry in $rankPools.GetEnumerator()) {
    Add-RankPool 'SpecialPack' $entry.Key $entry.Value 60 40
}

$themes = [ordered]@{
    HuntingBrandPack = [ordered]@{ 13=11; 14=30; 17=11; 18=11; 19=11; 20=10; 35=10; 39=6 }
    ImmortalLegacyPack = [ordered]@{ 8=6; 9=32; 10=6; 11=6; 12=6; 28=6; 33=32; 40=6 }
    IronArmorPack = [ordered]@{ 5=8; 6=8; 7=3; 21=3; 22=7; 29=32; 31=7; 37=32 }
    GiantsGardenPack = [ordered]@{ 1=13; 2=13; 3=13; 4=13; 23=3; 24=3; 32=12; 34=15; 38=15 }
    ElementalFlowPack = [ordered]@{ 15=16; 16=16; 25=30; 26=3; 27=3; 30=16; 36=16 }
}
foreach ($pack in $themes.GetEnumerator()) {
    foreach ($card in $pack.Value.GetEnumerator()) { Add-Drop $pack.Key 'Bronze' ([int]$card.Key) ([int]$card.Value) }
}

$rows = @($keptRows) + @($generated | Sort-Object id)
Write-SpecCsv $dropPath $dropLines[0..2] $rows @('id','packId','minGrade','cardId','weight','#cardName')

$errors = New-Object System.Collections.Generic.List[string]
if (@($packRows.id | Group-Object | Where-Object Count -gt 1).Count -gt 0) { $errors.Add('CardPack id duplicate') }
if (@($packRows.packId | Group-Object | Where-Object Count -gt 1).Count -gt 0) { $errors.Add('CardPack packId duplicate') }
if (@($rows.id | Group-Object | Where-Object Count -gt 1).Count -gt 0) { $errors.Add('CardPackDrop id duplicate') }

$previous = @()
foreach ($entry in $rankPools.GetEnumerator()) {
    $current = @($entry.Value)
    if (@($previous | Where-Object { $_ -notin $current }).Count -gt 0) { $errors.Add("rank pool is not cumulative: $($entry.Key)") }
    if (@($current | Where-Object { $cardById[$_].grade -in @('Arcane','Mythic') }).Count -gt 0) { $errors.Add("Arcane/Mythic in gold pack: $($entry.Key)") }
    $previous = $current
}
if ((@($rankPools.Values | ForEach-Object Count) -join ',') -ne '10,16,22,27,32') { $errors.Add('unexpected rank pool counts') }

$themeIds = @($themes.Values | ForEach-Object { $_.Keys } | ForEach-Object { [int]$_ })
if ($themeIds.Count -ne 40 -or @($themeIds | Sort-Object -Unique).Count -ne 40 -or ($themeIds | Measure-Object -Minimum).Minimum -ne 1 -or ($themeIds | Measure-Object -Maximum).Maximum -ne 40) {
    $errors.Add('theme packs must cover card ids 1..40 exactly once')
}
foreach ($pack in $themes.GetEnumerator()) {
    if (($pack.Value.Values | Measure-Object -Sum).Sum -ne 100) { $errors.Add("theme weight must total 100: $($pack.Key)") }
}
if ($errors.Count -gt 0) { throw ($errors -join '; ') }

Write-Host "CardPack rows: $($packRows.Count); CardPackDrop rows: $($rows.Count); rank pools: 10/16/22/27/32; themes: 40 unique"
