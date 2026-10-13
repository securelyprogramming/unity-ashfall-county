$ErrorActionPreference = 'Stop'
$frontierRunner = Join-Path $PSScriptRoot 'tools/playtest/run_current.py'
if ($env:PYTHON) {
    & $env:PYTHON $frontierRunner @args
    exit $LASTEXITCODE
}
$frontierPython = Get-Command python3, python, py -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $frontierPython) {
    throw 'Python 3 is required. Set PYTHON to the path of your existing Python executable.'
}
if ($frontierPython.Name -match '^py(\.exe)?$') {
    & $frontierPython.Source -3 $frontierRunner @args
} else {
    & $frontierPython.Source $frontierRunner @args
}
exit $LASTEXITCODE
