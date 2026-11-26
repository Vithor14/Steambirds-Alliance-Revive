param(
    [Parameter(Mandatory = $true)]
    [string]$Message
)

# Always run in the folder where this script is saved.
# One maintainer: every commit and every push is on main. This script never creates or pushes another branch.
Set-Location -Path $PSScriptRoot

$branch = ((git rev-parse --abbrev-ref HEAD) | Out-String).Trim()
if (-not $branch -or $branch -eq "HEAD") {
    Write-Host "ERROR: no branch is checked out (detached HEAD). Check out main and run this again." -ForegroundColor Red
    Read-Host -Prompt "Press Enter to close"
    exit 1
}

if ($branch -ne "main") {
    $leftBehind = ((git rev-list --count "main..$branch") | Out-String).Trim()
    if ($leftBehind -and $leftBehind -ne "0") {
        Write-Host "ERROR: $branch has $leftBehind commit(s) that are not on main." -ForegroundColor Red
        Write-Host "Move them onto main first (git checkout main; git merge $branch), then run this again." -ForegroundColor Red
        Read-Host -Prompt "Press Enter to close"
        exit 1
    }
    Write-Host "Switching from $branch to main. This script only commits and pushes main." -ForegroundColor Yellow
    git checkout main
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: could not switch to main. The push was not done." -ForegroundColor Red
        Read-Host -Prompt "Press Enter to close"
        exit 1
    }
}

Write-Host "Branch: main" -ForegroundColor Cyan

Write-Host "Adding changes..." -ForegroundColor Cyan
git add -A

$status = git status --porcelain
if ($status) {
    Write-Host "Creating commit: '$Message'..." -ForegroundColor Cyan
    git commit -m "$Message"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: the commit failed. Nothing was pushed." -ForegroundColor Red
        Read-Host -Prompt "Press Enter to close"
        exit 1
    }
} else {
    Write-Host "No new changes to commit. Going on to the sync..." -ForegroundColor Yellow
}

Write-Host "Syncing with the remote repository (pull --rebase origin main)..." -ForegroundColor Cyan
git pull --rebase origin main
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error while syncing (possible conflict). Resolve the conflicts by hand before pushing." -ForegroundColor Red
    Read-Host -Prompt "Press Enter to close"
    exit 1
}

Write-Host "Pushing main to GitHub..." -ForegroundColor Cyan
git push origin main
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: the push of main failed." -ForegroundColor Red
}

Read-Host -Prompt "Press Enter to close"
