# ====================================================================
# Versão Simplificada - Conversão Markdown para PDF
# ====================================================================
# Esta é uma versão mais simples que usa apenas Pandoc básico
# ====================================================================

Write-Host ""
Write-Host "=================================" -ForegroundColor Cyan
Write-Host "  Conversor MD → PDF (Simples)  " -ForegroundColor Cyan
Write-Host "=================================" -ForegroundColor Cyan
Write-Host ""

# Verificar Pandoc
if (-not (Get-Command pandoc -ErrorAction SilentlyContinue)) {
    Write-Host "❌ Pandoc não instalado!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Instale com: winget install --id JohnMacFarlane.Pandoc" -ForegroundColor Yellow
    Write-Host ""
    exit 1
}

# Converter
Write-Host "🔄 Convertendo..." -ForegroundColor Yellow

pandoc DOCUMENTACAO.md `
    -o DOCUMENTACAO.pdf `
    --toc `
    --metadata title="EvolutDelivery - Documentação" `
    --metadata author="Evolut Tech" `
    --metadata date="$(Get-Date -Format 'dd/MM/yyyy')"

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "✅ PDF gerado com sucesso!" -ForegroundColor Green
    Write-Host "📄 Arquivo: DOCUMENTACAO.pdf" -ForegroundColor Cyan
    Write-Host ""
    
    # Abrir arquivo
    $open = Read-Host "Abrir PDF? (S/N)"
    if ($open -eq "S" -or $open -eq "s") {
        Start-Process "DOCUMENTACAO.pdf"
    }
} else {
    Write-Host ""
    Write-Host "❌ Erro na conversão" -ForegroundColor Red
    Write-Host ""
}
