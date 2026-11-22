param(
    [Parameter(Mandatory = $true)]
    [string]$Message
)

# Always run in the folder where this script is saved
Set-Location -Path $PSScriptRoot

# Works on the branch you are on (main, or a release branch such as release-0.4.0), never on another one by accident.
$branch = ((git rev-parse --abbrev-ref HEAD) | Out-String).Trim()
if (-not $branch -or $branch -eq "HEAD") {
    Write-Host "ERROR: no branch is checked out (detached HEAD). Switch to a branch and run this again." -ForegroundColor Red
    Read-Host -Prompt "Press Enter to close"
    exit 1
}
Write-Host "Branch: $branch" -ForegroundColor Cyan

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

# A branch that was never pushed does not exist on the remote yet: there is nothing to pull, it is created by the push.
$remoteHasBranch = $false
git ls-remote --exit-code --heads origin $branch | Out-Null
if ($LASTEXITCODE -eq 0) { $remoteHasBranch = $true }

$synced = $true
if ($remoteHasBranch) {
    Write-Host "Syncing with the remote repository (pull --rebase origin $branch)..." -ForegroundColor Cyan
    git pull --rebase origin $branch
    $synced = ($LASTEXITCODE -eq 0)
} else {
    Write-Host "Branch $branch is not on GitHub yet: it will be created by the push." -ForegroundColor Yellow
}

if ($synced) {
    Write-Host "Pushing $branch to GitHub..." -ForegroundColor Cyan
    git push -u origin $branch
} else {
    Write-Host "Error while syncing (possible conflict). Resolve the conflicts by hand before pushing." -ForegroundColor Red
}

Read-Host -Prompt "Press Enter to close"
