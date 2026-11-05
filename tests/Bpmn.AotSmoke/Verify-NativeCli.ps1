param(
    [Parameter(Mandatory)][string] $Executable,
    [Parameter(Mandatory)][string] $SampleDirectory
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$Executable = (Resolve-Path $Executable).Path
$SampleDirectory = (Resolve-Path $SampleDirectory).Path

$text = & $Executable validate (Join-Path $SampleDirectory 'order-process.bpmn') --json
if ($LASTEXITCODE -notin 0, 1) { throw 'Native validation failed.' }
$report = ($text -join "`n") | ConvertFrom-Json
if ($report.files.Count -ne 1 -or $null -eq $report.errors) { throw 'Validation JSON shape changed.' }

$start = [System.Diagnostics.ProcessStartInfo]::new($Executable)
$start.ArgumentList.Add('serve')
$start.ArgumentList.Add('--root')
$start.ArgumentList.Add($SampleDirectory)
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.UseShellExecute = $false
$process = [System.Diagnostics.Process]::Start($start)
$stderr = $process.StandardError.ReadToEndAsync()

function Read-Response {
    $line = $process.StandardOutput.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(15)).GetAwaiter().GetResult()
    if ($null -eq $line) { throw 'Native MCP server closed stdout before responding.' }
    return $line | ConvertFrom-Json
}

try {
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"native-smoke","version":"1"}}}')
    $initialized = Read-Response
    if ($null -eq $initialized.result.serverInfo) { throw 'MCP initialization failed.' }
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}')
    $tools = Read-Response
    if ('bpmn_list_documents' -notin $tools.result.tools.name) { throw 'Native MCP tools missing.' }
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"bpmn_list_documents","arguments":{}}}')
    $listing = Read-Response
    if (!$listing.result.structuredContent.ok) { throw 'Native MCP tool invocation failed.' }
}
finally {
    $process.StandardInput.Close()
    if (!$process.WaitForExit(10000)) {
        Stop-Process -Id $process.Id
        $process.WaitForExit()
    }
    $diagnostics = $stderr.GetAwaiter().GetResult()
    $exitCode = $process.ExitCode
    $process.Dispose()
    if ($exitCode -ne 0) { throw "Native MCP server failed ($exitCode). $diagnostics" }
}

Write-Output 'BPMN Native AOT CLI validation and stdio smoke passed.'
