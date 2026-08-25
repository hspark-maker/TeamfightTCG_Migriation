param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'

$cardCsvPath = Join-Path $ProjectRoot 'docs/SpecData/Card_sheet.csv'
$specPath = Join-Path $ProjectRoot 'Assets/Resources/SpecData.bytes'
$utf8Bom = New-Object System.Text.UTF8Encoding($true)
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Read-SpecCsv([string]$Path) {
    $lines = [IO.File]::ReadAllLines($Path)
    $csv = @($lines[1]) + @($lines | Select-Object -Skip 3)
    return @((($csv -join "`n") | ConvertFrom-Csv))
}

function Decode-Utf8([string]$Base64) {
    return [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Base64))
}

function Grade4([int]$Id, [string]$Grade) {
    if ($Id -in @(39, 40)) { return 'Mythic' }
    switch ($Grade) {
        'Silver' { return 'Common' }
        'Gold' { return 'Rare' }
        'Prism' { return 'Arcane' }
        'Common' { return 'Common' }
        'Rare' { return 'Rare' }
        'Arcane' { return 'Arcane' }
        'Mythic' { return 'Mythic' }
        default { throw "Unknown card grade '$Grade' for card $Id" }
    }
}

function CardRow([object]$Row) {
    $id = [int]$Row.id
    return [ordered]@{
        id = $id
        name = [string]$Row.name
        displayName = [string]$Row.displayName
        channel = [string]$Row.channel
        maxHp = [int]$Row.maxHp
        keywords = [string]$Row.keywords
        keywordUnlockLevel = [int]$Row.keywordUnlockLevel
        synergies = [string]$Row.synergies
        defaultEvolutionStage = [int]$Row.defaultEvolutionStage
        hp2 = [int]$Row.hp2
        hp3 = [int]$Row.hp3
        hp4 = [int]$Row.hp4
        cardExplain = [string]$Row.cardExplain
        grade = Grade4 $id ([string]$Row.grade)
    }
}

function Crypt([byte[]]$InputBytes, [bool]$Encrypt) {
    $key = [Text.Encoding]::UTF8.GetBytes('cRM1fuNZDwvqnjzY')
    $iv = [byte[]]$key.Clone()
    [Array]::Reverse($iv)
    $aes = New-Object Security.Cryptography.RijndaelManaged
    try {
        $aes.Mode = [Security.Cryptography.CipherMode]::CBC
        $aes.Padding = [Security.Cryptography.PaddingMode]::PKCS7
        $aes.KeySize = 128
        $aes.BlockSize = 128
        $aes.Key = $key
        $aes.IV = $iv
        $transform = $(if ($Encrypt) { $aes.CreateEncryptor() } else { $aes.CreateDecryptor() })
        try { return $transform.TransformFinalBlock($InputBytes, 0, $InputBytes.Length) }
        finally { $transform.Dispose() }
    }
    finally { $aes.Dispose() }
}

function Merge-CardRows([object[]]$ExistingRows, [object[]]$FallbackRows) {
    $merged = New-Object System.Collections.Generic.List[object]
    $seen = @{}
    foreach ($row in @($ExistingRows)) {
        $id = [int]$row.id
        $row.grade = Grade4 $id ([string]$row.grade)
        $merged.Add($row)
        $seen[$id] = $true
    }
    foreach ($row in $FallbackRows) {
        $id = [int]$row.id
        if (!$seen.ContainsKey($id)) { $merged.Add($row) }
    }
    return @($merged | Sort-Object { [int]$_.id })
}

$rows = @(Read-SpecCsv $cardCsvPath | ForEach-Object { CardRow $_ })
if ($rows.Count -ne 40) { throw "Card work copy must contain 40 rows, got $($rows.Count)" }

$ids = @($rows | ForEach-Object { [int]$_.id })
if (@($ids | Sort-Object -Unique).Count -ne 40 -or ($ids | Measure-Object -Minimum).Minimum -ne 1 -or ($ids | Measure-Object -Maximum).Maximum -ne 40) {
    throw 'Card ids must be exactly 1..40'
}

$csvLines = [IO.File]::ReadAllLines($cardCsvPath)
$csvLines[0] = $csvLines[0].Replace('Silver/Gold/Prism', 'Common/Rare/Arcane/Mythic')
$body = foreach ($row in $rows) {
    $values = @(
        $row.id, $row.name, $row.displayName, $row.channel, $row.maxHp, $row.keywords,
        $row.keywordUnlockLevel, $row.synergies, $row.defaultEvolutionStage,
        $row.hp2, $row.hp3, $row.hp4, $row.cardExplain, $row.grade
    )
    ($values | ForEach-Object {
        $value = [string]$_
        if ($value.Contains(',') -or $value.Contains('"') -or $value.Contains("`n") -or $value.Contains("`r")) {
            '"' + $value.Replace('"', '""') + '"'
        } else { $value }
    }) -join ','
}
[IO.File]::WriteAllText($cardCsvPath, (@($csvLines[0..2]) + $body -join "`r`n") + "`r`n", $utf8Bom)

$plainBytes = Crypt ([IO.File]::ReadAllBytes($specPath)) $false
$json = $utf8NoBom.GetString($plainBytes)
$root = $json | ConvertFrom-Json
$root.Card = @(Merge-CardRows @($root.Card) $rows)
$root.Card_Test = @(Merge-CardRows @($root.Card_Test) $rows)
$root | Add-Member -Force NoteProperty CardGrade @(
    [ordered]@{ id=1; gradeKey='Common'; displayName=(Decode-Utf8 '7J2867CY'); sortOrder=1; colorHex='C9D1DA'; playsAppearCinematic=0; memo='Base grade' },
    [ordered]@{ id=2; gradeKey='Rare'; displayName=(Decode-Utf8 '7Z2s6reA'); sortOrder=2; colorHex='F2B33D'; playsAppearCinematic=0; memo='Mid grade' },
    [ordered]@{ id=3; gradeKey='Arcane'; displayName=(Decode-Utf8 '7Iug67mE'); sortOrder=3; colorHex='9B6BF5'; playsAppearCinematic=0; memo='High grade' },
    [ordered]@{ id=4; gradeKey='Mythic'; displayName=(Decode-Utf8 '7Iug7ZmU'); sortOrder=4; colorHex='FF5A36'; playsAppearCinematic=1; memo='Top grade' }
)

$newJson = $root | ConvertTo-Json -Depth 20 -Compress
$encrypted = Crypt ($utf8NoBom.GetBytes($newJson)) $true
[IO.File]::WriteAllBytes($specPath, $encrypted)

$distribution = $root.Card | Group-Object grade | ForEach-Object { "$($_.Name)=$($_.Count)" }
Write-Host "Card/Card_Test=40; CardGrade=4; $($distribution -join ', ')"
