# ====================================================================
# Script de Conversão de Documentação Markdown para PDF
# ====================================================================
# Este script converte o arquivo DOCUMENTACAO.md para PDF
# usando Pandoc com formatação profissional
# ====================================================================

param(
    [string]$InputFile = "DOCUMENTACAO.md",
    [string]$OutputFile = "DOCUMENTACAO.pdf"
)

# Cores para output
function Write-ColorOutput($ForegroundColor) {
    $fc = $host.UI.RawUI.ForegroundColor
    $host.UI.RawUI.ForegroundColor = $ForegroundColor
    if ($args) {
        Write-Output $args
    }
    $host.UI.RawUI.ForegroundColor = $fc
}

Write-Host ""
Write-ColorOutput Green "╔════════════════════════════════════════════════════════════╗"
Write-ColorOutput Green "║     Conversor de Documentação Markdown para PDF           ║"
Write-ColorOutput Green "╚════════════════════════════════════════════════════════════╝"
Write-Host ""

# Verificar se o arquivo de entrada existe
if (-not (Test-Path $InputFile)) {
    Write-ColorOutput Red "❌ Erro: Arquivo '$InputFile' não encontrado!"
    Write-Host ""
    exit 1
}

Write-ColorOutput Cyan "📄 Arquivo de entrada: $InputFile"
Write-ColorOutput Cyan "📄 Arquivo de saída: $OutputFile"
Write-Host ""

# ====================================================================
# Verificar se Pandoc está instalado
# ====================================================================
Write-ColorOutput Yellow "🔍 Verificando se Pandoc está instalado..."

$pandocInstalled = $false
try {
    $pandocVersion = pandoc --version 2>$null
    if ($LASTEXITCODE -eq 0) {
        $pandocInstalled = $true
        $version = ($pandocVersion | Select-Object -First 1) -replace 'pandoc ', ''
        Write-ColorOutput Green "✅ Pandoc encontrado! Versão: $version"
    }
} catch {
    $pandocInstalled = $false
}

# ====================================================================
# Instalar Pandoc se não estiver instalado
# ====================================================================
if (-not $pandocInstalled) {
    Write-ColorOutput Yellow "⚠️  Pandoc não está instalado."
    Write-Host ""
    Write-Host "Pandoc é necessário para conversão. Opções de instalação:"
    Write-Host ""
    Write-Host "1️⃣  Instalar via winget (recomendado - automático)"
    Write-Host "2️⃣  Instalar manualmente"
    Write-Host "3️⃣  Cancelar"
    Write-Host ""
    
    $choice = Read-Host "Escolha uma opção (1-3)"
    
    switch ($choice) {
        "1" {
            Write-ColorOutput Cyan "📦 Instalando Pandoc via winget..."
            Write-Host ""
            
            try {
                winget install --id JohnMacFarlane.Pandoc -e --silent
                
                if ($LASTEXITCODE -eq 0) {
                    Write-ColorOutput Green "✅ Pandoc instalado com sucesso!"
                    Write-ColorOutput Yellow "⚠️  Por favor, reinicie o PowerShell e execute o script novamente."
                    Write-Host ""
                    exit 0
                } else {
                    Write-ColorOutput Red "❌ Erro ao instalar Pandoc."
                    exit 1
                }
            } catch {
                Write-ColorOutput Red "❌ Erro ao instalar Pandoc: $_"
                exit 1
            }
        }
        "2" {
            Write-Host ""
            Write-ColorOutput Cyan "📖 Instruções de instalação manual:"
            Write-Host ""
            Write-Host "1. Acesse: https://pandoc.org/installing.html"
            Write-Host "2. Baixe e instale o Pandoc para Windows"
            Write-Host "3. Reinicie o PowerShell"
            Write-Host "4. Execute este script novamente"
            Write-Host ""
            exit 0
        }
        "3" {
            Write-ColorOutput Yellow "Operação cancelada."
            exit 0
        }
        default {
            Write-ColorOutput Red "Opção inválida. Operação cancelada."
            exit 1
        }
    }
}

# ====================================================================
# Criar arquivo de estilo CSS temporário
# ====================================================================
Write-ColorOutput Yellow "🎨 Criando arquivo de estilo..."

$cssContent = @"
body {
    font-family: 'Segoe UI', Arial, sans-serif;
    font-size: 11pt;
    line-height: 1.6;
    color: #333;
    max-width: 800px;
    margin: 0 auto;
    padding: 20px;
}

h1 {
    color: #2c3e50;
    border-bottom: 3px solid #3498db;
    padding-bottom: 10px;
    font-size: 28pt;
    margin-top: 30px;
}

h2 {
    color: #34495e;
    border-bottom: 2px solid #95a5a6;
    padding-bottom: 8px;
    font-size: 20pt;
    margin-top: 25px;
}

h3 {
    color: #2c3e50;
    font-size: 16pt;
    margin-top: 20px;
}

h4 {
    color: #34495e;
    font-size: 14pt;
    margin-top: 15px;
}

code {
    background-color: #f4f4f4;
    padding: 2px 6px;
    border-radius: 3px;
    font-family: 'Courier New', monospace;
    font-size: 10pt;
    color: #c7254e;
}

pre {
    background-color: #f8f8f8;
    border: 1px solid #ddd;
    border-radius: 5px;
    padding: 15px;
    overflow-x: auto;
    font-size: 9pt;
}

pre code {
    background-color: transparent;
    padding: 0;
    color: #333;
}

blockquote {
    border-left: 4px solid #3498db;
    padding-left: 15px;
    margin-left: 0;
    color: #555;
    font-style: italic;
}

table {
    border-collapse: collapse;
    width: 100%;
    margin: 20px 0;
}

th, td {
    border: 1px solid #ddd;
    padding: 10px;
    text-align: left;
}

th {
    background-color: #3498db;
    color: white;
    font-weight: bold;
}

tr:nth-child(even) {
    background-color: #f9f9f9;
}

a {
    color: #3498db;
    text-decoration: none;
}

a:hover {
    text-decoration: underline;
}

ul, ol {
    margin: 10px 0;
    padding-left: 30px;
}

li {
    margin: 5px 0;
}

.page-break {
    page-break-after: always;
}

@media print {
    body {
        max-width: 100%;
    }
}
"@

$cssFile = "temp_style.css"
$cssContent | Out-File -FilePath $cssFile -Encoding UTF8

Write-ColorOutput Green "✅ Arquivo de estilo criado"
Write-Host ""

# ====================================================================
# Converter MD para PDF
# ====================================================================
Write-ColorOutput Yellow "🔄 Convertendo Markdown para PDF..."
Write-Host ""

try {
    # Comando Pandoc com todas as opções de formatação
    $pandocArgs = @(
        $InputFile,
        "-o", $OutputFile,
        "--pdf-engine=wkhtmltopdf",
        "--css=$cssFile",
        "--toc",
        "--toc-depth=3",
        "--metadata", "title=EvolutDelivery - Documentação Completa",
        "--metadata", "author=Evolut Tech",
        "--metadata", "date=$(Get-Date -Format 'dd/MM/yyyy')",
        "-V", "geometry:margin=2cm",
        "-V", "linkcolor=blue",
        "--highlight-style=tango"
    )
    
    Write-ColorOutput Cyan "Executando Pandoc..."
    & pandoc $pandocArgs 2>&1 | Out-String | Write-Host
    
    if ($LASTEXITCODE -eq 0 -and (Test-Path $OutputFile)) {
        Write-Host ""
        Write-ColorOutput Green "╔════════════════════════════════════════════════════════════╗"
        Write-ColorOutput Green "║              ✅ CONVERSÃO CONCLUÍDA COM SUCESSO!           ║"
        Write-ColorOutput Green "╚════════════════════════════════════════════════════════════╝"
        Write-Host ""
        
        $fileInfo = Get-Item $OutputFile
        $fileSizeKB = [math]::Round($fileInfo.Length / 1KB, 2)
        
        Write-ColorOutput Cyan "📄 Arquivo gerado: $OutputFile"
        Write-ColorOutput Cyan "📊 Tamanho: $fileSizeKB KB"
        Write-ColorOutput Cyan "📅 Data: $(Get-Date -Format 'dd/MM/yyyy HH:mm:ss')"
        Write-Host ""
        
        # Perguntar se deseja abrir o arquivo
        $openFile = Read-Host "Deseja abrir o arquivo PDF agora? (S/N)"
        if ($openFile -eq "S" -or $openFile -eq "s") {
            Start-Process $OutputFile
        }
        
    } else {
        Write-ColorOutput Red "❌ Erro: Falha na conversão do arquivo"
        Write-Host ""
        
        if (-not (Get-Command wkhtmltopdf -ErrorAction SilentlyContinue)) {
            Write-ColorOutput Yellow "⚠️  wkhtmltopdf não encontrado!"
            Write-Host ""
            Write-Host "Para melhor formatação, instale wkhtmltopdf:"
            Write-Host "https://wkhtmltopdf.org/downloads.html"
            Write-Host ""
            Write-Host "Ou tente conversão alternativa sem wkhtmltopdf:"
            Write-Host ""
            
            $useAlternative = Read-Host "Tentar conversão alternativa? (S/N)"
            if ($useAlternative -eq "S" -or $useAlternative -eq "s") {
                Write-ColorOutput Cyan "Tentando conversão alternativa..."
                
                # Conversão alternativa (sem CSS avançado)
                pandoc $InputFile -o $OutputFile --toc --metadata title="EvolutDelivery - Documentação"
                
                if (Test-Path $OutputFile) {
                    Write-ColorOutput Green "✅ Conversão alternativa bem-sucedida!"
                    Write-Host ""
                } else {
                    Write-ColorOutput Red "❌ Conversão alternativa falhou"
                }
            }
        }
    }
    
} catch {
    Write-ColorOutput Red "❌ Erro durante a conversão: $_"
    Write-Host ""
} finally {
    # Limpar arquivo CSS temporário
    if (Test-Path $cssFile) {
        Remove-Item $cssFile -Force
        Write-ColorOutput Cyan "🧹 Arquivos temporários removidos"
    }
}

Write-Host ""
Write-ColorOutput Cyan "Script finalizado."
Write-Host ""
