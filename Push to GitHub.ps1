# Push to GitHub.ps1
# Usage: .\"Push to GitHub.ps1" "commit message"
# Writes the list of project files to FileList.txt, commits everything and pushes.

param(
    [Parameter(Mandatory = $true)]
    [string]$Message
)

Set-Location C:\Steambirds
Get-ChildItem -Recurse | Select-Object -ExpandProperty FullName | Out-File ".\FileList.txt" -Encoding UTF8
git add -A
git commit -m $Message
if ($LASTEXITCODE -ne 0) {
    Write-Host "Nothing new to send."
} else {
    git push
}

Read-Host "Press Enter to close"
