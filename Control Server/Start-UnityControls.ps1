$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$pythonPath = Join-Path (Split-Path -Parent $projectRoot) '.venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $pythonPath)) {
    throw "Python environment not found at $pythonPath. Create it outside the repository first."
}
$env:UNITY_UDP_HOST = '127.0.0.1'
$env:UNITY_UDP_PORT = '15050'
& $pythonPath (Join-Path $PSScriptRoot 'server.py')
