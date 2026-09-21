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
            if ($tentativa -lt $maxTentativas) { Start-Sleep -Seconds 2 }
        }
    }
    return $false
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

foreach ($file in $files) {
    $count++
    $relative = $file.FullName.Substring($publishPath.Length).TrimStart([char]92).Replace([char]92, '/')
    $ftpUrl   = $ftpHost.TrimEnd('/') + '/' + $relative
    $percent  = [math]::Round(($count / $total) * 100)
    Write-Progress -Activity 'Comparando arquivos...' -Status "$count/$total - $relative" -PercentComplete $percent

    $remoteSize = Get-FtpFileSize $ftpUrl $credential
    if ($remoteSize -ne $file.Length) {
        $arquivosAlterados += @{ File = $file; Url = $ftpUrl; Relative = $relative }
        if ($relative -match '\.(dll|pdb)$') { $precisaReiniciar = $true }
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

    $count = 0; $enviados = 0; $falhas = @()

    foreach ($item in $arquivosAlterados) {
        $count++
        $percent = [math]::Round(($count / $arquivosAlterados.Count) * 100)
        Write-Progress -Activity 'Enviando arquivos...' -Status "$count/$($arquivosAlterados.Count) - $($item.Relative)" -PercentComplete $percent

        $bytes = [System.IO.File]::ReadAllBytes($item.File.FullName)
        $ok    = Send-FtpFileWithRetry $item.Url $credential $bytes
        if ($ok) { $enviados++ } else { $falhas += $item.Relative }
    }

    Write-Progress -Activity 'Enviando arquivos...' -Completed

    try {
        Invoke-Sql "INSERT INTO DeployNotificacao (Tipo, Versao, Mensagem) VALUES ('atualizacao', '$versao', 'Sistema atualizado! Recarregue a pagina para ver as novidades.')"
    } catch {
        Write-Host '    AVISO: Nao foi possivel enviar notificacao SQL.' -ForegroundColor Yellow
    }

    Write-Host "    $enviados arquivo(s) enviado(s)!" -ForegroundColor Green
    Write-Host ''
    Write-Host '============================================' -ForegroundColor Green
    Write-Host "  Deploy $versao concluido com sucesso!" -ForegroundColor Green
    Write-Host '============================================' -ForegroundColor Green
    exit 0
}

# ============================================================
# PASSO 4: COLOCAR SITE EM MANUTENCAO IMEDIATAMENTE
# ============================================================
Write-Host ''
Write-Host '>>> Colocando site em manutencao agora...' -ForegroundColor Yellow

$offlineContent = [System.Text.Encoding]::UTF8.GetBytes('<html><body><h2>Atualizando sistema, aguarde...</h2></body></html>')
$offlineUrl     = $ftpHost.TrimEnd('/') + '/app_offline.htm'

Send-FtpFile $offlineUrl $credential $offlineContent
Write-Host '    app_offline.htm criado. Aguardando processo encerrar...' -ForegroundColor Gray
Start-Sleep -Seconds 12

# ============================================================
# PASSO 5: ENVIAR TODOS OS ARQUIVOS ALTERADOS
# ============================================================
Write-Host ''
Write-Host '>>> Enviando todos os arquivos alterados...' -ForegroundColor Cyan

$count = 0; $enviados = 0; $falhas = @()

foreach ($item in $arquivosAlterados) {
    $count++
    $percent = [math]::Round(($count / $arquivosAlterados.Count) * 100)
    Write-Progress -Activity 'Enviando arquivos...' -Status "$count/$($arquivosAlterados.Count) - $($item.Relative)" -PercentComplete $percent

    $bytes = [System.IO.File]::ReadAllBytes($item.File.FullName)
    $ok    = Send-FtpFileWithRetry $item.Url $credential $bytes
    if ($ok) {
        $enviados++
    } else {
        $falhas += $item.Relative
        Write-Host "    FALHA: $($item.Relative)" -ForegroundColor Red
    }
}

Write-Progress -Activity 'Enviando arquivos...' -Completed
Write-Host "    $enviados/$($arquivosAlterados.Count) arquivo(s) enviado(s)." -ForegroundColor Green

# ============================================================
# PASSO 6: REATIVAR SITE
# ============================================================
Write-Host ''
Write-Host '>>> Reativando site...' -ForegroundColor Cyan

try {
    $delReq = [System.Net.FtpWebRequest]::Create($offlineUrl)
    $delReq.Credentials = $credential
    $delReq.Method = [System.Net.WebRequestMethods+Ftp]::DeleteFile
    $delReq.GetResponse().Close()
    Write-Host '    Site reativado!' -ForegroundColor Green
} catch {
    Write-Host '    AVISO: Nao foi possivel remover app_offline.htm automaticamente.' -ForegroundColor Yellow
    Write-Host '    Remova manualmente via FileZilla para o site voltar.' -ForegroundColor Yellow
}

# ============================================================
# PASSO 7: NOTIFICAR CONCLUSAO
# ============================================================
try {
    Invoke-Sql "INSERT INTO DeployNotificacao (Tipo, Versao, Mensagem) VALUES ('atualizacao', '$versao', 'Sistema atualizado para a versao $versao! Recarregue a pagina para ver as novidades.')"
    Write-Host '    Notificacao de conclusao enviada!' -ForegroundColor Green
} catch {
    Write-Host '    AVISO: Nao foi possivel enviar notificacao SQL.' -ForegroundColor Yellow
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
    Write-Host "  $($falhas.Count) arquivo(s) nao enviado(s) - verifique acima." -ForegroundColor Yellow
    Write-Host '============================================' -ForegroundColor Yellow
}