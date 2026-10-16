# ---------- CONFIGURACOES ----------
$projectPath  = 'C:\Users\User\Desktop\Desenvolvimento Web\EvolutCRM'
$publishPath  = 'C:\Users\User\Desktop\Desenvolvimento Web\publish'
$ftpHost      = 'ftp://gabriel.evoluttech@evoluttech.ddns.net/Site/Help'
$ftpUser      = 'gabriel.evoluttech'
$ftpPassword  = 'Gabriel@91401149'
$sqlServer    = '131.221.148.129,1435'
$sqlDatabase  = 'Evolut'
$sqlUser      = 'evolut_user'
$sqlPassword  = 'Evolut@91401149'
$versao       = (Get-Date -Format 'yyyy.MM.dd.HHmm')
# ------------------------------------

$ErrorActionPreference = 'Stop'

function Get-FtpFileSize($url, $credential) {
    try {
        $req = [System.Net.FtpWebRequest]::Create($url)
        $req.Credentials = $credential
        $req.Method = [System.Net.WebRequestMethods+Ftp]::GetFileSize
        $req.UseBinary = $true
        $response = $req.GetResponse()
        $size = $response.ContentLength
        $response.Close()
        return $size
    } catch {
        return -1
    }
}

function Send-FtpFile($url, $credential, $bytes) {
    $req = [System.Net.FtpWebRequest]::Create($url)
    $req.Credentials = $credential
    $req.Method = [System.Net.WebRequestMethods+Ftp]::UploadFile
    $req.UseBinary = $true
    $req.KeepAlive = $true
    $req.ContentLength = $bytes.Length
    $stream = $req.GetRequestStream()
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Close()
    $req.GetResponse().Close()
}

function Send-FtpFileWithRetry($url, $credential, $bytes, $maxTentativas = 3) {
    $tentativa = 0
    while ($tentativa -lt $maxTentativas) {
        try {
            Send-FtpFile $url $credential $bytes
            return $true
        } catch {
            $tentativa++
            if ($tentativa -lt $maxTentativas) {
                Start-Sleep -Seconds 2
            }
        }
    }
    return $false
}

function Read-Manifesto($caminho) {
    # Hash (SHA256) de cada arquivo ja enviado com sucesso ao servidor.
    # Necessario porque DLL nova pode ter o MESMO tamanho da antiga
    # (ex.: troca de versao de pacote NuGet) e a comparacao so por tamanho nao enviava.
    $mapa = @{}
    if (Test-Path $caminho) {
        try {
            $json = Get-Content $caminho -Raw | ConvertFrom-Json
            foreach ($p in $json.PSObject.Properties) { $mapa[$p.Name] = [string]$p.Value }
        } catch {
            Write-Host '    AVISO: publish-hashes.json invalido, todos os arquivos serao reenviados.' -ForegroundColor Yellow
        }
    }
    return $mapa
}

function Save-Manifesto($caminho, $mapa) {
    $ordenado = [ordered]@{}
    foreach ($k in ($mapa.Keys | Sort-Object)) { $ordenado[$k] = $mapa[$k] }
    ($ordenado | ConvertTo-Json -Depth 2) | Set-Content -Path $caminho -Encoding UTF8
}

function Invoke-Sql($query) {
    $conn = New-Object System.Data.SqlClient.SqlConnection
    $conn.ConnectionString = "Server=$sqlServer;Database=$sqlDatabase;User id=$sqlUser;Password=$sqlPassword;TrustServerCertificate=True"
    $conn.Open()
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $query
    $cmd.ExecuteNonQuery() | Out-Null
    $conn.Close()
}

# ============================================================
# PASSO 1: PUBLISH
# ============================================================
Write-Host ''
Write-Host '>>> Compilando e publicando o projeto...' -ForegroundColor Cyan

if (Test-Path $publishPath) { Remove-Item -Recurse -Force $publishPath }

$csproj = Get-ChildItem -Path $projectPath -Filter '*.csproj' -Recurse | Select-Object -First 1
if (-not $csproj) {
    Write-Host '    ERRO: Nenhum .csproj encontrado.' -ForegroundColor Red
    exit 1
}

Write-Host "    Projeto: $($csproj.FullName)" -ForegroundColor Gray
dotnet publish $csproj.FullName --configuration Release --output $publishPath --nologo -v quiet

if ($LASTEXITCODE -ne 0) {
    Write-Host '    ERRO: dotnet publish falhou.' -ForegroundColor Red
    exit 1
}

Write-Host '    Publish concluido!' -ForegroundColor Green

# ============================================================
# PASSO 2: COMPARAR ARQUIVOS
# ============================================================
Write-Host ''
Write-Host '>>> Comparando arquivos com o servidor...' -ForegroundColor Cyan

$credential = New-Object System.Net.NetworkCredential($ftpUser, $ftpPassword)
$files = Get-ChildItem -Path $publishPath -Recurse -File
$total = $files.Count
$count = 0
$arquivosAlterados = @()
$precisaReiniciar  = $false
$manifestoPath     = Join-Path $PSScriptRoot 'publish-hashes.json'
$manifesto         = Read-Manifesto $manifestoPath

foreach ($file in $files) {
    $count++
    $relative = $file.FullName.Substring($publishPath.Length).TrimStart([char]92).Replace([char]92, '/')
    $ftpUrl   = $ftpHost.TrimEnd('/') + '/' + $relative
    $percent  = [math]::Round(($count / $total) * 100)
    Write-Progress -Activity 'Comparando arquivos...' -Status "$count/$total - $relative" -PercentComplete $percent

    # Envia se o conteudo mudou desde o ultimo deploy (hash) OU se o tamanho no servidor difere/arquivo nao existe
    $hash       = (Get-FileHash -Path $file.FullName -Algorithm SHA256).Hash
    $hashMudou  = (-not $manifesto.ContainsKey($relative)) -or ($manifesto[$relative] -ne $hash)
    $remoteSize = if ($hashMudou) { -2 } else { Get-FtpFileSize $ftpUrl $credential }
    if ($hashMudou -or ($remoteSize -ne $file.Length)) {
        $arquivosAlterados += @{ File = $file; Url = $ftpUrl; Relative = $relative; Hash = $hash }

        # Qualquer DLL ou PDB exige parada do servico
        if ($relative -match '\.(dll|pdb|exe)$' -or $relative -match '\.(deps|runtimeconfig)\.json$' -or $relative -eq 'web.config') {
            $precisaReiniciar = $true
        }
    }
}

Write-Progress -Activity 'Comparando arquivos...' -Completed

$totalAlterados = $arquivosAlterados.Count
Write-Host "    $totalAlterados arquivo(s) para enviar. Requer reinicio: $precisaReiniciar" -ForegroundColor Gray

if ($totalAlterados -eq 0) {
    Write-Host ''
    Write-Host '============================================' -ForegroundColor Green
    Write-Host '  Nenhuma alteracao detectada. Deploy nao necessario!' -ForegroundColor Green
    Write-Host '============================================' -ForegroundColor Green
    exit 0
}

# ============================================================
# PASSO 3: FLUXO SEM REINICIO (so arquivos estaticos)
# ============================================================
if (-not $precisaReiniciar) {
    Write-Host ''
    Write-Host '>>> Apenas arquivos estaticos alterados. Enviando sem parar o servico...' -ForegroundColor Cyan

    $count    = 0
    $enviados = 0
    $falhas   = @()

    foreach ($item in $arquivosAlterados) {
        $count++
        $percent = [math]::Round(($count / $arquivosAlterados.Count) * 100)
        Write-Progress -Activity 'Enviando arquivos...' -Status "$count/$($arquivosAlterados.Count) - $($item.Relative)" -PercentComplete $percent

        $bytes = [System.IO.File]::ReadAllBytes($item.File.FullName)
        $ok    = Send-FtpFileWithRetry $item.Url $credential $bytes
        if ($ok) { $enviados++; $manifesto[$item.Relative] = $item.Hash } else { $falhas += $item.Relative }
    }

    Write-Progress -Activity 'Enviando arquivos...' -Completed
    Save-Manifesto $manifestoPath $manifesto

    if ($falhas.Count -gt 0) {
        Write-Host "    AVISO: $($falhas.Count) arquivo(s) nao enviado(s):" -ForegroundColor Yellow
        $falhas | ForEach-Object { Write-Host "      - $_" -ForegroundColor Yellow }
    }

    Write-Host "    $enviados arquivo(s) enviado(s)!" -ForegroundColor Green

    try {
        Invoke-Sql "INSERT INTO DeployNotificacao (Tipo, Versao, Mensagem) VALUES ('atualizacao', '$versao', 'Sistema atualizado! Recarregue a pagina para ver as novidades.')"
        Write-Host '    Notificacao enviada!' -ForegroundColor Green
    } catch {
        Write-Host '    AVISO: Nao foi possivel enviar notificacao SQL.' -ForegroundColor Yellow
    }

    Write-Host ''
    Write-Host '============================================' -ForegroundColor Green
    Write-Host "  Deploy $versao concluido com sucesso!" -ForegroundColor Green
    Write-Host '============================================' -ForegroundColor Green
    exit 0
}

# ============================================================
# PASSO 4: AVISO 5 MINUTOS
# ============================================================
Write-Host ''
Write-Host '>>> Notificando usuarios (aviso 5 minutos)...' -ForegroundColor Cyan

try {
    Invoke-Sql "INSERT INTO DeployNotificacao (Tipo, Versao, Mensagem) VALUES ('aviso5min', '$versao', 'O sistema sera reiniciado em 5 minutos devido a uma atualizacao.')"
    Write-Host '    Aviso de 5 minutos enviado!' -ForegroundColor Green
} catch {
    Write-Host '    AVISO: Nao foi possivel enviar aviso SQL.' -ForegroundColor Yellow
}

# ============================================================
# PASSO 5: AGUARDAR 4:30
# ============================================================
Write-Host ''
Write-Host '>>> Aguardando 4 minutos e 30 segundos...' -ForegroundColor Gray

$totalSecs = 270
for ($i = $totalSecs; $i -ge 1; $i--) {
    $mins = [math]::Floor($i / 60)
    $secs = $i % 60
    Write-Progress -Activity 'Aguardando aviso final...' -Status "Faltam $mins min $secs seg" -PercentComplete (($totalSecs - $i) / $totalSecs * 100)
    Start-Sleep -Seconds 1
}
Write-Progress -Activity 'Aguardando aviso final...' -Completed

# ============================================================
# PASSO 6: AVISO 30 SEGUNDOS
# ============================================================
Write-Host ''
Write-Host '>>> Notificando usuarios (aviso 30 segundos)...' -ForegroundColor Cyan

try {
    Invoke-Sql "INSERT INTO DeployNotificacao (Tipo, Versao, Mensagem) VALUES ('aviso30seg', '$versao', 'Atencao! O sistema sera fechado em 30 segundos devido a uma atualizacao.')"
    Write-Host '    Aviso de 30 segundos enviado!' -ForegroundColor Green
} catch {
    Write-Host '    AVISO: Nao foi possivel enviar aviso SQL.' -ForegroundColor Yellow
}

# ============================================================
# PASSO 7: AGUARDAR 30 SEGUNDOS
# ============================================================
Write-Host ''
Write-Host '>>> Aguardando 30 segundos...' -ForegroundColor Gray

for ($i = 30; $i -ge 1; $i--) {
    Write-Progress -Activity 'Aguardando reinicio...' -Status "Faltam $i seg" -PercentComplete ((30 - $i) / 30 * 100)
    Start-Sleep -Seconds 1
}
Write-Progress -Activity 'Aguardando reinicio...' -Completed

# ============================================================
# PASSO 8: COLOCAR SITE EM MANUTENCAO
# ============================================================
Write-Host ''
Write-Host '>>> Colocando site em manutencao...' -ForegroundColor Cyan

$offlineContent = [System.Text.Encoding]::UTF8.GetBytes(@'
<!DOCTYPE html><html lang="pt-br"><head><meta charset="utf-8">
<meta http-equiv="refresh" content="10"><title>Atualizando o HELP</title></head>
<body style="font-family:sans-serif;display:grid;place-items:center;height:100vh;margin:0;background:#0f172a;color:#e2e8f0">
<div style="text-align:center"><h2>Atualizando o HELP...</h2><p>A tela volta sozinha em instantes.</p></div>
</body></html>
'@)
$offlineUrl     = $ftpHost.TrimEnd('/') + '/app_offline.htm'

Send-FtpFile $offlineUrl $credential $offlineContent
Write-Host '    app_offline.htm criado. Aguardando processo encerrar...' -ForegroundColor Gray

# Aguarda o processo dotnet terminar de vez (era 2s, agora 12s)
Start-Sleep -Seconds 12

# ============================================================
# PASSO 9: ENVIAR TODOS OS ARQUIVOS ALTERADOS
# ============================================================
Write-Host ''
Write-Host '>>> Enviando todos os arquivos alterados...' -ForegroundColor Cyan

$count    = 0
$enviados = 0
$falhas   = @()

foreach ($item in $arquivosAlterados) {
    $count++
    $percent = [math]::Round(($count / $arquivosAlterados.Count) * 100)
    Write-Progress -Activity 'Enviando arquivos...' -Status "$count/$($arquivosAlterados.Count) - $($item.Relative)" -PercentComplete $percent

    $bytes = [System.IO.File]::ReadAllBytes($item.File.FullName)
    $ok    = Send-FtpFileWithRetry $item.Url $credential $bytes
    if ($ok) {
        $enviados++
        $manifesto[$item.Relative] = $item.Hash
    } else {
        $falhas += $item.Relative
        Write-Host "    FALHA: $($item.Relative)" -ForegroundColor Red
    }
}

Write-Progress -Activity 'Enviando arquivos...' -Completed
Save-Manifesto $manifestoPath $manifesto
Write-Host "    $enviados/$($arquivosAlterados.Count) arquivo(s) enviado(s)." -ForegroundColor Green

# ============================================================
# PASSO 10: REATIVAR SITE
# ============================================================
Write-Host ''
Write-Host '>>> Reativando site...' -ForegroundColor Cyan

try {
    $delReq = [System.Net.FtpWebRequest]::Create($offlineUrl)
    $delReq.Credentials = $credential
    $delReq.Method = [System.Net.WebRequestMethods+Ftp]::DeleteFile
    $delReq.GetResponse().Close()
    Write-Host '    app_offline.htm removido. Site reativado!' -ForegroundColor Green
} catch {
    Write-Host '    AVISO: Nao foi possivel remover app_offline.htm automaticamente.' -ForegroundColor Yellow
    Write-Host '    Remova manualmente via FileZilla para o site voltar.' -ForegroundColor Yellow
}

# ============================================================
# PASSO EXTRA: ACORDAR O HELP (forca o IIS a subir o app agora)
# ============================================================
Write-Host ''
Write-Host '>>> Acordando o HELP...' -ForegroundColor Cyan

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$helpNoAr = $false
for ($i = 1; $i -le 12; $i++) {
    try {
        $r = Invoke-WebRequest 'https://help.evoluttech.com/' -UseBasicParsing -TimeoutSec 30
        if ($r.StatusCode -eq 200) { $helpNoAr = $true; break }
    } catch {
        Write-Host "    Tentativa $i/12: HELP ainda iniciando..." -ForegroundColor Gray
        Start-Sleep -Seconds 5
    }
}
if ($helpNoAr) {
    Write-Host '    HELP no ar!' -ForegroundColor Green
} else {
    Write-Host '    AVISO: o HELP nao respondeu em ~1 minuto. Verifique o site no IIS.' -ForegroundColor Yellow
}

# ============================================================
# PASSO 11: NOTIFICAR CONCLUSAO
# ============================================================
if ($falhas.Count -eq 0) {
    try {
        Invoke-Sql "INSERT INTO DeployNotificacao (Tipo, Versao, Mensagem) VALUES ('atualizacao', '$versao', 'Sistema atualizado para a versao $versao! Recarregue a pagina para ver as novidades.')"
        Write-Host '    Notificacao de conclusao enviada!' -ForegroundColor Green
    } catch {
        Write-Host '    AVISO: Nao foi possivel enviar notificacao SQL.' -ForegroundColor Yellow
    }
} else {
    Write-Host ''
    Write-Host "    ATENCAO: $($falhas.Count) arquivo(s) nao foram enviados:" -ForegroundColor Red
    $falhas | ForEach-Object { Write-Host "      - $_" -ForegroundColor Red }
    Write-Host '    Verifique e reenvie manualmente se necessario.' -ForegroundColor Red
}

# ============================================================
# RESULTADO
# ============================================================
Write-Host ''
if ($falhas.Count -eq 0) {
    Write-Host '============================================' -ForegroundColor Green
    Write-Host "  Deploy $versao concluido com sucesso!" -ForegroundColor Green
    Write-Host '============================================' -ForegroundColor Green
} else {
    Write-Host '============================================' -ForegroundColor Yellow
    Write-Host "  Deploy $versao concluido COM AVISOS." -ForegroundColor Yellow
    Write-Host '============================================' -ForegroundColor Yellow
}