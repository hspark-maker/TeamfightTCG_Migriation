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
    [pscustomobject]@{ id=7; packId='HuntingBrandPack'; displayName=(Decode-Utf8 '64KZ7J247J2YIOu5hOuKmA=='); channel='Live'; priceType='Diamond'; price=30; drawCount=6; uniqueDraw=0; refundType='Shard'; refundAmount=8; minRankGrade='Platinum' },
    [pscustomobject]@{ id=8; packId='ImmortalLegacyPack'; displayName=(Decode-Utf8 '67aI66m47J2YIOycoOyCsA=='); channel='Live'; priceType='Diamond'; price=30; drawCount=6; uniqueDraw=0; refundType='Shard'; refundAmount=8; minRankGrade='Diamond' },
    [pscustomobject]@{ id=9; packId='IronArmorPack'; displayName=(Decode-Utf8 '7IiY7Zi47J2YIOygleybkA=='); channel='Live'; priceType='Diamond'; price=30; drawCount=6; uniqueDraw=0; refundType='Shard'; refundAmount=8; minRankGrade='Silver' },
    [pscustomobject]@{ id=10; packId='GiantsGardenPack'; displayName=(Decode-Utf8 '6rGw7J247J2YIOyCrOuDpQ=='); channel='Live'; priceType='Diamond'; price=30; drawCount=6; uniqueDraw=0; refundType='Shard'; refundAmount=8; minRankGrade='Gold' },
    [pscustomobject]@{ id=11; packId='ElementalFlowPack'; displayName=(Decode-Utf8 '7JuQ7IaM7J2YIO2dkOumhA=='); channel='Live'; priceType='Diamond'; price=30; drawCount=6; uniqueDraw=0; refundType='Shard'; refundAmount=8; minRankGrade='Bronze' }
)
Write-SpecCsv $packPath $packLines[0..2] $packRows @('id','packId','displayName','channel','priceType','price','drawCount','uniqueDraw','refundType','refundAmount','minRankGrade')

$dropLines = [IO.File]::ReadAllLines($dropPath)
$allDropRows = @(Read-SpecCsv $dropPath)
$managedPackIds = @('NormalPack_TEST','SpecialPack','UltraPack','HuntingBrandPack','ImmortalLegacyPack','IronArmorPack','GiantsGardenPack','ElementalFlowPack')
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
    Gold    = @(1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,22,25,29,30)
    Platinum = @(1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,28,29,30,31,32,33)
    Diamond = @(1..38)
}

function Add-WeightedPool([string]$PackId, [string]$RankGrade, [int[]]$CardIds, [hashtable]$GradeMasses) {
    foreach ($gradeName in @('Common','Rare','Arcane','Mythic')) {
        $ids = @($CardIds | Where-Object { $cardById[$_].grade -eq $gradeName } | Sort-Object)
        $budget = [int]$GradeMasses[$gradeName]
        if ($ids.Count -eq 0) { continue }
        if ($budget -le 0) { throw "$PackId/$RankGrade contains $gradeName cards with no weight budget" }
        $base = [math]::Floor($budget / $ids.Count)
        $remainder = $budget - ($base * $ids.Count)
        for ($i = 0; $i -lt $ids.Count; $i++) {
            Add-Drop $PackId $RankGrade $ids[$i] ([int]$base + $(if ($i -lt $remainder) { 1 } else { 0 }))
        }
    }
}

foreach ($entry in $rankPools.GetEnumerator()) {
    $masses = $(if ($entry.Key -in @('Bronze','Silver')) {
        @{ Common=8000; Rare=2000; Arcane=0; Mythic=0 }
    } else {
        @{ Common=7960; Rare=1990; Arcane=50; Mythic=0 }
    })
    Add-WeightedPool 'NormalPack_TEST' $entry.Key $entry.Value $masses
}
foreach ($entry in $rankPools.GetEnumerator()) {
    $masses = $(if ($entry.Key -in @('Bronze','Silver')) {
        @{ Common=6000; Rare=4000; Arcane=0; Mythic=0 }
    } else {
        @{ Common=5970; Rare=3980; Arcane=50; Mythic=0 }
    })
    Add-WeightedPool 'SpecialPack' $entry.Key $entry.Value $masses
}

$themes = [ordered]@{
    HuntingBrandPack = @(17,18,19,20,21,22,31,37,39,40)
    IronArmorPack = @(1,2,3,4,5,6,7,32,39,40)
    GiantsGardenPack = @(13,14,23,24,30,34,35,38,39,40)
    ImmortalLegacyPack = @(8,9,10,11,12,29,33,39,40)
    ElementalFlowPack = @(15,16,25,26,27,28,36,39,40)
}
foreach ($pack in $themes.GetEnumerator()) {
    Add-WeightedPool $pack.Key 'Bronze' $pack.Value @{ Common=6631; Rare=3109; Arcane=200; Mythic=60 }
}

$ultraCardIds = @(10,12,14,15,16,25,13,19,3,1,24,7,21,23,26,27,39,40)
Add-WeightedPool 'UltraPack' 'Bronze' $ultraCardIds @{ Common=6631; Rare=3109; Arcane=200; Mythic=60 }

$rows = @($keptRows) + @($generated | Sort-Object id)
Write-SpecCsv $dropPath $dropLines[0..2] $rows @('id','packId','minGrade','cardId','weight','#cardName')

$errors = New-Object System.Collections.Generic.List[string]
if (@($packRows.id | Group-Object | Where-Object Count -gt 1).Count -gt 0) { $errors.Add('CardPack id duplicate') }
if (@($packRows.packId | Group-Object | Where-Object Count -gt 1).Count -gt 0) { $errors.Add('CardPack packId duplicate') }
if (@($rows.id | Group-Object | Where-Object Count -gt 1).Count -gt 0) { $errors.Add('CardPackDrop id duplicate') }
if (@($rows | Group-Object packId,minGrade,cardId | Where-Object Count -gt 1).Count -gt 0) { $errors.Add('CardPackDrop tuple duplicate') }
if (@($rows | Where-Object { -not $cardById.ContainsKey([int]$_.cardId) }).Count -gt 0) { $errors.Add('CardPackDrop references missing card') }

$previous = @()
$expectedRankCounts = @(10,16,23,31,38)
$rankIndex = 0
foreach ($entry in $rankPools.GetEnumerator()) {
    $current = @($entry.Value)
    if (@($previous | Where-Object { $_ -notin $current }).Count -gt 0) { $errors.Add("rank pool is not cumulative: $($entry.Key)") }
    if ($current.Count -ne $expectedRankCounts[$rankIndex]) { $errors.Add("unexpected rank pool count: $($entry.Key)") }
    if (@($current | Where-Object { $cardById[$_].grade -eq 'Mythic' }).Count -gt 0) { $errors.Add("Mythic in gold pack: $($entry.Key)") }
    $previous = $current
    $rankIndex++
}

foreach ($packId in @('NormalPack_TEST','SpecialPack')) {
    foreach ($entry in $rankPools.GetEnumerator()) {
        $block = @($rows | Where-Object { $_.packId -eq $packId -and $_.minGrade -eq $entry.Key })
        $mass = @{}
        foreach ($gradeName in @('Common','Rare','Arcane','Mythic')) {
            $mass[$gradeName] = (@($block | Where-Object { $cardById[[int]$_.cardId].grade -eq $gradeName }).weight | Measure-Object -Sum).Sum
            if ($null -eq $mass[$gradeName]) { $mass[$gradeName] = 0 }
        }
        $expectedArcane = $(if ($entry.Key -in @('Bronze','Silver')) { 0 } else { 50 })
        if ($mass.Arcane -ne $expectedArcane -or $mass.Mythic -ne 0) { $errors.Add("bad gold high-grade mass: $packId/$($entry.Key)") }
    }
}

$diamondPaid = @('UltraPack') + @($themes.Keys)
foreach ($packId in $diamondPaid) {
    $block = @($rows | Where-Object { $_.packId -eq $packId -and $_.minGrade -eq 'Bronze' })
    $actual = @{}
    foreach ($gradeName in @('Common','Rare','Arcane','Mythic')) {
        $actual[$gradeName] = (@($block | Where-Object { $cardById[[int]$_.cardId].grade -eq $gradeName }).weight | Measure-Object -Sum).Sum
    }
    if ($actual.Common -ne 6631 -or $actual.Rare -ne 3109 -or $actual.Arcane -ne 200 -or $actual.Mythic -ne 60) {
        $errors.Add("bad diamond grade mass: $packId")
    }
}

$themeNonMythicIds = @($themes.Values | ForEach-Object { $_ | Where-Object { $_ -le 38 } })
if ($themeNonMythicIds.Count -ne 38 -or @($themeNonMythicIds | Sort-Object -Unique).Count -ne 38 -or ($themeNonMythicIds | Measure-Object -Minimum).Minimum -ne 1 -or ($themeNonMythicIds | Measure-Object -Maximum).Maximum -ne 38) {
    $errors.Add('theme packs must cover non-Mythic card ids 1..38 exactly once')
}
foreach ($mythicId in @(39,40)) {
    $count = @($themes.Values | Where-Object { $mythicId -in $_ }).Count
    if ($count -ne 5) { $errors.Add("Mythic card $mythicId must appear in all five themes") }
}
if ($errors.Count -gt 0) { throw ($errors -join '; ') }

Write-Host "CardPack rows: $($packRows.Count); CardPackDrop rows: $($rows.Count); rank pools: 10/16/23/31/38; themes: 1..38 unique + shared Mythics"
