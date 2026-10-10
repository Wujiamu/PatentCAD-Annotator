[CmdletBinding()]
param([string]$ScriptRoot)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ScriptRoot)) { $ScriptRoot = $PSScriptRoot }
foreach ($script in Get-ChildItem -LiteralPath $ScriptRoot -File |
    Where-Object { $_.Extension -in @('.ps1', '.psm1') }) {
    $tokens = $null
    $errors = $null
    [Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count -gt 0) { throw "PowerShell syntax failed: $($script.Name): $($errors[0].Message)" }
}
Write-Output "PASS|OFFICE_POWERSHELL_SYNTAX|$($PSVersionTable.PSVersion)"
