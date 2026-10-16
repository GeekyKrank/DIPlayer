param([string]$Report='evidence/app-selftest.json',[string]$Output='evidence/measurement-summary.json')
$ErrorActionPreference='Stop'
$r=Get-Content -LiteralPath $Report -Raw|ConvertFrom-Json
$samples=@($r.samples)
if(!$samples.Count){throw 'No measurement samples'}
$ram=$samples|Measure-Object workingSetMiB -Minimum -Maximum -Average
$private=$samples|Measure-Object privateMiB -Minimum -Maximum -Average
$streak=0;$longest=0
for($i=1;$i -lt $samples.Count;$i++) {
    if([math]::Abs($samples[$i].position-$samples[$i-1].position) -lt 0.05){$streak++;$longest=[math]::Max($longest,$streak)}else{$streak=0}
}
$bufferValues=@($samples|ForEach-Object {if($_.PSObject.Properties.Name -contains 'pcmBufferedSeconds'){$_.pcmBufferedSeconds}else{$_.buffer}})
$summary=[ordered]@{
    source=$Report;utc=$r.utc;seconds=$r.seconds;samples=$samples.Count
    workingSetMiB=@{min=$ram.Minimum;max=$ram.Maximum;mean=$ram.Average}
    privateMiB=@{min=$private.Minimum;max=$private.Maximum;mean=$private.Average}
    cpuMachinePercent=$r.cpuMachinePercent;cpuOneCorePercent=$r.cpuOneCorePercent
    logicalProcessors=$r.logicalProcessors
    pcmBufferMaxSeconds=($bufferValues|Measure-Object -Maximum).Maximum
    firstPosition=$samples[0].position;lastPosition=$samples[-1].position
    longestStationarySampleStreak=$longest
    checks=$r.checks
}
$summary|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $Output
$summary|ConvertTo-Json -Depth 8
