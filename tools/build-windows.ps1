param(
    [string]$EditorPath = $env:SOCCER_UNITY_EDITOR
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'UnityProject'
$reportDir = Join-Path $repoRoot 'reports'
$reportPath = Join-Path $reportDir 'project-build-windows.json'
$logPath = Join-Path $reportDir 'project-build-windows.log'

if (-not $EditorPath -or -not (Test-Path -LiteralPath $EditorPath -PathType Leaf)) {
    throw 'Pass -EditorPath with the Tuanjie 1.10.4 (2022.3.62t16) editor exe, or set SOCCER_UNITY_EDITOR.'
}

New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
if (Test-Path -LiteralPath $reportPath) {
    Remove-Item -LiteralPath $reportPath
}

Write-Host 'Building Windows x64 with Tuanjie 1.10.4 (2022.3.62t16)...'
& $EditorPath -batchmode -quit -buildTarget Win64 -projectPath $projectPath `
    -executeMethod Soccer.Editor.ProjectSetup.BuildWindows -logFile $logPath | Out-Host

if ($LASTEXITCODE -ne 0) {
    throw "Editor exited with code $LASTEXITCODE. See $logPath"
}
if (-not (Test-Path -LiteralPath $reportPath)) {
    throw "No build report was produced. See $logPath"
}
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
if ($report.result -ne 'Succeeded' -or $report.errors -ne 0 -or $report.target -ne 'StandaloneWindows64') {
    throw "Windows build failed. See $logPath"
}
if ($report.editor -ne '2022.3.62t16') {
    throw "Editor version mismatch: $($report.editor). Expected 2022.3.62t16."
}
Write-Host "Build ready: $(Join-Path $projectPath 'Builds\Windows\SkillSoccer.exe')"
