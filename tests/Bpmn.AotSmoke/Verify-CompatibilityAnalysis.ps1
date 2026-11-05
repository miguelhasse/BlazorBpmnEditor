param([string] $Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$output = & dotnet build (Join-Path $root 'BlazorBpmnEditor.slnx') -c $Configuration -t:Rebuild `
    -p:BpmnCompatibilityAnalysis=true -p:TreatWarningsAsErrors=false --verbosity quiet 2>&1
$exitCode = $LASTEXITCODE
if ($exitCode -ne 0) {
    $output | Write-Output
    throw "Compatibility analysis build failed ($exitCode)."
}

$diagnostics = @($output | ForEach-Object { "$_" } |
    Where-Object { $_ -match '(warning|error) IL\d+' } | Sort-Object -Unique)
$unexpected = @($diagnostics | Where-Object {
    $_ -notmatch 'BpmnFeelSharpField_razor\.g\.cs.*warning IL2111:.*Microsoft\.AspNetCore\.Components\.DynamicComponent\.Type\.set'
})
if ($unexpected.Count -gt 0) {
    $unexpected | Write-Output
    throw 'New trimming/AOT diagnostics require investigation; do not extend the exception list without qualification.'
}
if ($diagnostics.Count -gt 0) {
    $diagnostics | Write-Output
    Write-Output 'Known unqualified FEEL runtime-Type boundary remains. All other owned-source compatibility checks passed.'
}
else {
    Write-Output 'Owned-source compatibility analysis passed. Dependency/native publishing qualification remains separate.'
}
