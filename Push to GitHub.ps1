param(
    [Parameter(Mandatory = $true)]
    [string]$Message
)

# Always run in the folder where this script is saved
Set-Location -Path $PSScriptRoot

Write-Host "Adding changes..." -ForegroundColor Cyan
git add -A

# Check that there is something to commit before trying to commit
$status = git status --porcelain
if ($status) {
    Write-Host "Creating commit: '$Message'..." -ForegroundColor Cyan
    git commit -m "$Message"
} else {
    Write-Host "No new changes to commit. Going on to the sync..." -ForegroundColor Yellow
}

Write-Host "Syncing with the remote repository (pull --rebase)..." -ForegroundColor Cyan
git pull --rebase origin main

if ($LASTEXITCODE -eq 0) {
    Write-Host "Pushing to GitHub..." -ForegroundColor Cyan
    git push origin main
} else {
    Write-Host "Error while syncing (possible conflict). Resolve the conflicts by hand before pushing." -ForegroundColor Red
}

Read-Host -Prompt "Press Enter to close"
