param([string]$UnityEditorData = 'C:/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Data')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$source = [IO.File]::ReadAllText((Join-Path $repo 'Assets/Scripts/UI/Pass/PassPanel.Progress.cs'))
function Read-Method([string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing method: $signature" }
    $body = $source.IndexOf('{', $start)
    $depth = 0
    for ($i = $body; $i -lt $source.Length; $i++) {
        if ($source[$i] -eq '{') { $depth++ }
        elseif ($source[$i] -eq '}') { $depth--; if ($depth -eq 0) { break } }
    }
    return $source.Substring($start, $i - $start + 1)
}
$methods = (Read-Method 'readonly struct ProgressRange') + (Read-Method 'ProgressRange ProgressRangeOf(') + (Read-Method 'List<UiGaugeSequence.Step> BuildProgressSteps(')
$harness = @'
using System;
using System.Collections.Generic;
public class PassLevelDefinition { public long RequiredExp; public int Level; }
public class PassRepeatDefinition { public long RequiredExp; }
public static class UiGaugeSequence {
    public struct Step {
        public double From, To; public float Fraction; public bool ReachesBoundary;
        public Step(double from, double to, float fraction, bool reachesBoundary) {
            From=from; To=to; Fraction=fraction; ReachesBoundary=reachesBoundary;
        }
    }
}
public static class PassManager {
    public static List<PassLevelDefinition> Levels = new List<PassLevelDefinition>();
    public static PassRepeatDefinition Repeat = new PassRepeatDefinition();
    public static bool HasRepeatReward;
    public static long MaxRequiredExp = 300;
}
public class PassHeaderRangeChecks {
    public static void Main() { Console.WriteLine(Run()); }
    /* METHODS */
    int checks;
    void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
    void Range(double exp, bool before, double floor, double ceiling, int level) {
        var actual = ProgressRangeOf(exp, before);
        Check(actual.Floor == floor && actual.Ceiling == ceiling && actual.Level == level, "Range " + exp + " before=" + before);
    }
    public static string Run() {
        var test = new PassHeaderRangeChecks();
        PassManager.Levels.Clear(); PassManager.HasRepeatReward = false;
        for (int i=0; i<4; i++) PassManager.Levels.Add(new PassLevelDefinition { Level=i+1, RequiredExp=i*100 });
        test.Range(0,false,0,100,1);
        test.Range(99.5,false,0,100,1);
        test.Range(100,true,0,100,1);
        test.Range(100,false,100,200,2);
        test.Range(300,true,200,300,3);
        test.Range(300,false,300,300,4);
        test.Check(test.ProgressRangeOf(250).Floor==200, "First entry starts at current floor");
        test.Check(test.ProgressRangeOf(300).Floor==300, "MAX entry stays full");
        var steps = test.BuildProgressSteps(25, 250);
        test.Check(steps.Count==3 && steps[0].To==100 && steps[1].To==200 && steps[2].To==250, "Cross every regular level");
        test.Check(steps[0].ReachesBoundary && steps[1].ReachesBoundary && !steps[2].ReachesBoundary, "Partial target never triggers arrival");
        test.Check(Math.Abs(steps[0].Fraction-0.75f)<0.001 && Math.Abs(steps[2].Fraction-0.5f)<0.001, "Fraction uses shared range");
        test.Check(test.BuildProgressSteps(200, 200).Count==0, "Unchanged progress does not replay");
        test.Check(test.BuildProgressSteps(250, 100).Count==0, "Decreasing progress has no forward steps");
        PassManager.HasRepeatReward=true; PassManager.Repeat.RequiredExp=50;
        test.Range(300,false,300,350,4);
        test.Range(350,true,300,350,4);
        test.Range(350,false,350,400,4);
        test.Range(375,false,350,400,4);
        test.Check(test.ProgressRangeOf(375).Floor==350, "Repeat entry starts at current floor");
        steps = test.BuildProgressSteps(275, 425);
        test.Check(steps.Count==4 && steps[0].To==300 && steps[1].To==350 && steps[2].To==400 && steps[3].To==425, "Regular to repeat handoff");
        steps = test.BuildProgressSteps(325, 100000025);
        test.Check(steps.Count==3 && steps[0].To==350 && steps[1].To==100000000 && steps[2].To==100000025, "Large repeat gain keeps first and last boundary");
        test.Check(steps[1].From==99999950 && steps[1].ReachesBoundary && !steps[2].ReachesBoundary, "Compressed repeat charges only final cycle");
        steps = test.BuildProgressSteps(350, 100000000);
        test.Check(steps.Count==2 && steps[1].To==100000000 && steps[1].ReachesBoundary, "Exact repeat target holds boundary");
        test.Range(100000000,false,100000000,100000050,4);
        PassManager.Levels.Clear(); PassManager.HasRepeatReward=false;
        test.Range(0,false,0,0,0);
        test.Check(test.BuildProgressSteps(0, 100).Count==0, "Missing range cannot loop forever");
        return test.checks + " production range checks passed";
    }
}
'@
$checkDir = Join-Path ([IO.Path]::GetTempPath()) ('PassHeaderRange-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($checkDir) | Out-Null
[IO.File]::WriteAllText((Join-Path $checkDir 'Harness.cs'), $harness.Replace('/* METHODS */', $methods))
$dotnet = Join-Path $UnityEditorData 'NetCoreRuntime/dotnet.exe'
$compiler = Join-Path $UnityEditorData 'DotNetSdkRoslyn/csc.dll'
$runtime = Get-ChildItem (Join-Path $UnityEditorData 'NetCoreRuntime/shared/Microsoft.NETCore.App') -Directory |
    Sort-Object { [Version]$_.Name } -Descending | Select-Object -First 1
$references = Get-ChildItem $runtime.FullName -Filter '*.dll' | Where-Object {
    try { [Reflection.AssemblyName]::GetAssemblyName($_.FullName) | Out-Null; $true } catch { $false }
} | ForEach-Object { '-r:"' + $_.FullName + '"' }
$arguments = @('-nologo', '-target:exe', '-langversion:9.0', '-nostdlib+',
    ('-out:"' + (Join-Path $checkDir 'Harness.dll') + '"')) + $references + @('"' + (Join-Path $checkDir 'Harness.cs') + '"')
$response = Join-Path $checkDir 'compile.rsp'
[IO.File]::WriteAllLines($response, $arguments)
& $dotnet $compiler ('@' + $response)
if ($LASTEXITCODE -ne 0) { throw 'Pass header harness compilation failed.' }
$config = @{ runtimeOptions = @{ tfm = 'net6.0'; framework = @{ name = 'Microsoft.NETCore.App'; version = $runtime.Name } } }
[IO.File]::WriteAllText((Join-Path $checkDir 'Harness.runtimeconfig.json'), ($config | ConvertTo-Json -Depth 4))
& $dotnet (Join-Path $checkDir 'Harness.dll')
if ($LASTEXITCODE -ne 0) { throw 'Pass header regression failed.' }
