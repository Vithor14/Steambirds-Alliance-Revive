param(
    [Parameter(Mandatory = $true)]
    [string]$Message
)

# Garante que os comandos rodem na pasta onde o script está salvo
Set-Location -Path $PSScriptRoot

Write-Host "Adicionando alterações..." -ForegroundColor Cyan
git add -A

# Verifica se há algo para commitar antes de tentar o commit
$status = git status --porcelain
if ($status) {
    Write-Host "Criando commit: '$Message'..." -ForegroundColor Cyan
    git commit -m "$Message"
} else {
    Write-Host "Nenhuma alteração nova para commitar. Seguindo para sincronização..." -ForegroundColor Yellow
}

Write-Host "Sincronizando com o repositório remoto (pull --rebase)..." -ForegroundColor Cyan
git pull --rebase origin main

if ($LASTEXITCODE -eq 0) {
    Write-Host "Enviando para o GitHub..." -ForegroundColor Cyan
    git push origin main
} else {
    Write-Host "Erro ao sincronizar (possível conflito). Resolva os conflitos manualmente antes de dar push." -ForegroundColor Red
}

Read-Host -Prompt "Press Enter to close"