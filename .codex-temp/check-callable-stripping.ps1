param([string]$StrippedPath = 'Library/Bee/artifacts/Android/ManagedStripped/Assembly-CSharp.dll')
$ErrorActionPreference = 'Stop'
Add-Type -Path 'C:/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Data/Managed/Unity.Cecil.dll'
function Get-TaskTypes($types) {
    foreach ($type in $types) {
        $type
        Get-TaskTypes $type.NestedTypes
    }
}
$source = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path (Get-Location) 'Library/ScriptAssemblies/Assembly-CSharp.dll'))
$stripped = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path (Get-Location) $StrippedPath))
try {
    $actualTypes = @{}
    Get-TaskTypes $stripped.MainModule.Types | ForEach-Object { $actualTypes[$_.FullName] = $_ }
    $failures = [System.Collections.Generic.List[string]]::new()
    $checked = 0
    foreach ($type in (Get-TaskTypes $source.MainModule.Types)) {
        $actual = $actualTypes[$type.FullName]
        if ($null -eq $actual) { continue } # Editor-only types are absent from the player.
        foreach ($property in $type.Properties) {
            if (-not ($property.CustomAttributes.AttributeType.FullName -match 'Newtonsoft.Json.JsonPropertyAttribute|Firebase.Firestore.FirestorePropertyAttribute')) { continue }
            $checked++
            $found = $actual.Properties | Where-Object { $_.Name -eq $property.Name }
            if ($null -eq $found -or ($property.GetMethod -and -not $found.GetMethod) -or ($property.SetMethod -and -not $found.SetMethod)) {
                $failures.Add($type.FullName + '.' + $property.Name)
            }
        }
    }
    $anonymousCount = 0
    foreach ($type in $stripped.MainModule.Types) {
        if ($type.Name -notlike '*AnonymousType*') { continue }
        $anonymousCount++
        foreach ($field in $type.Fields) {
            if ($field.Name -notmatch '^<(.+)>i__Field$') { continue }
            $name = $Matches[1]
            $found = $type.Properties | Where-Object { $_.Name -eq $name -and $_.GetMethod }
            if (-not $found) { $failures.Add($type.FullName + '.' + $name) }
        }
    }
    $codec = Get-Content 'Assets/Scripts/OutGame/Spec/SpecPayloadCodec.cs' -Raw
    $tableBlock = [regex]::Match($codec, 'string\[\] TableNames\s*=\s*\{([^}]+)\}').Groups[1].Value
    $tableNames = [regex]::Matches($tableBlock, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
    $sourceTypes = @{}
    Get-TaskTypes $source.MainModule.Types | ForEach-Object { $sourceTypes[$_.FullName] = $_ }
    foreach ($name in $tableNames) {
        $expectedFields = @($sourceTypes[$name].Fields | Where-Object { $_.IsPublic -and -not $_.IsStatic } | ForEach-Object { $_.Name + ':' + $_.FieldType.FullName })
        $actualFields = @($actualTypes[$name].Fields | Where-Object { $_.IsPublic -and -not $_.IsStatic } | ForEach-Object { $_.Name + ':' + $_.FieldType.FullName })
        if (($expectedFields -join '|') -ne ($actualFields -join '|')) { $failures.Add('Table fields/order: ' + $name) }
        $property = $actualTypes['SpecDataManager'].Properties | Where-Object { $_.Name -eq $name }
        if (-not $property.GetMethod) { $failures.Add('Table getter: ' + $name) }
        $all = $actualTypes['SpecDataManager/InnerData' + $name].Properties | Where-Object { $_.Name -eq 'All' }
        if (-not $all.GetMethod) { $failures.Add('Table All getter: ' + $name) }
    }
    Write-Output "Checked $checked attributed properties, $anonymousCount anonymous request types, $($tableNames.Count) spec schemas/accessors; missing=$($failures.Count)"
    $failures | Write-Output
    if ($failures.Count -gt 0) { exit 1 }
} finally {
    $source.Dispose()
    $stripped.Dispose()
}
