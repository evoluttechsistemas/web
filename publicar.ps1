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
$arquivosNormais = @()
$arquivosTravados = @()

foreach ($file in $files) {
    $count++
    $relative = $file.FullName.Substring($publishPath.Length).TrimStart([char]92).Replace([char]92, '/')
    $ftpUrl   = $ftpHost.TrimEnd('/') + '/' + $relative
    $percent  = [math]::Round(($count / $total) * 100)
    Write-Progress -Activity 'Comparando arquivos...' -Status "$count/$total - $relative" -PercentComplete $percent

    $remoteSize = Get-FtpFileSize $ftpUrl $credential
    if ($remoteSize -ne $file.Length) {
        # DLLs e PDBs principais sao enviados por ultimo (podem estar travados)
        if ($relative -match '^EvolutCRM\.(dll|pdb)$') {
            $arquivosTravados += @{ File = $file; Url = $ftpUrl; Relative = $relative }
        } else {
            $arquivosNormais += @{ File = $file; Url = $ftpUrl; Relative = $relative }
        }
    }
}

Write-Progress -Activity 'Comparando arquivos...' -Completed

$totalAlterados = $arquivosNormais.Count + $arquivosTravados.Count
Write-Host "    $totalAlterados arquivo(s) para enviar ($($arquivosNormais.Count) normais, $($arquivosTravados.Count) DLL principal)." -ForegroundColor Gray

if ($totalAlterados -eq 0) {
    Write-Host ''
    Write-Host '============================================' -ForegroundColor Green
    Write-Host '  Nenhuma alteracao detectada. Deploy nao necessario!' -ForegroundColor Green
    Write-Host '============================================' -ForegroundColor Green
    exit 0
}

# ============================================================
# PASSO 3: UPLOAD DOS ARQUIVOS NORMAIS
# ============================================================
if ($arquivosNormais.Count -gt 0) {
    Write-Host ''
    Write-Host '>>> Enviando arquivos normais...' -ForegroundColor Cyan

    $count = 0
    $enviados = 0

    foreach ($item in $arquivosNormais) {
        $count++
        $percent = [math]::Round(($count / $arquivosNormais.Count) * 100)
        Write-Progress -Activity 'Enviando arquivos...' -Status "$count/$($arquivosNormais.Count) - $($item.Relative)" -PercentComplete $percent

        try {
            $bytes = [System.IO.File]::ReadAllBytes($item.File.FullName)
            Send-FtpFile $item.Url $credential $bytes
            $enviados++
        } catch {
            Write-Host "    AVISO: Falha ao enviar $($item.Relative)" -ForegroundColor Yellow
        }
    }

    Write-Progress -Activity 'Enviando arquivos...' -Completed
    Write-Host "    $enviados arquivo(s) enviado(s)!" -ForegroundColor Green
}

# ============================================================
# PASSO 4: AVISO 5 MINUTOS (so se houver DLLs travadas)
# ============================================================
if ($arquivosTravados.Count -gt 0) {
    Write-Host ''
    Write-Host '>>> Notificando usuarios (aviso 5 minutos)...' -ForegroundColor Cyan

    Invoke-Sql "INSERT INTO DeployNotificacao (Tipo, Versao, Mensagem) VALUES ('aviso5min', '$versao', 'O sistema sera reiniciado em 5 minutos devido a uma atualizacao.')"

    Write-Host '    Aviso de 5 minutos enviado!' -ForegroundColor Green

    # ============================================================
    # PASSO 5: AGUARDAR 4:30
    # ============================================================
    Write-Host ''
    Write-Host '>>> Aguardando 4 minutos e 30 segundos...' -ForegroundColor Gray

    $totalSecs = 270
    for ($i = $totalSecs; $i -ge 1; $i--) {
        $mins = [math]::Floor($i / 60)
        $secs = $i % 60
        Write-Progress -Activity 'Aguardando para aviso final...' -Status "Faltam $mins min $secs seg" -PercentComplete (($totalSecs - $i) / $totalSecs * 100)
        Start-Sleep -Seconds 1
    }
    Write-Progress -Activity 'Aguardando para aviso final...' -Completed

    # ============================================================
    # PASSO 6: AVISO 30 SEGUNDOS
    # ============================================================
    Write-Host ''
    Write-Host '>>> Notificando usuarios (aviso 30 segundos)...' -ForegroundColor Cyan

    Invoke-Sql "INSERT INTO DeployNotificacao (Tipo, Versao, Mensagem) VALUES ('aviso30seg', '$versao', 'Atencao! O sistema sera fechado em 30 segundos devido a uma atualizacao.')"

    Write-Host '    Aviso de 30 segundos enviado!' -ForegroundColor Green

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
    # PASSO 8: COLOCAR SITE EM MANUTENCAO E ENVIAR DLLs
    # ============================================================
    Write-Host ''
    Write-Host '>>> Reiniciando site e enviando DLLs...' -ForegroundColor Cyan

    $offlineContent = [System.Text.Encoding]::UTF8.GetBytes('<html><body><h2>Atualizando sistema...</h2></body></html>')
    $offlineUrl = $ftpHost.TrimEnd('/') + '/app_offline.htm'

    Send-FtpFile $offlineUrl $credential $offlineContent
    Start-Sleep -Seconds 2

    $enviados = 0
    foreach ($item in $arquivosTravados) {
        try {
            $bytes = [System.IO.File]::ReadAllBytes($item.File.FullName)
            Send-FtpFile $item.Url $credential $bytes
            $enviados++
            Write-Host "    Enviado: $($item.Relative)" -ForegroundColor Green
        } catch {
            Write-Host "    AVISO: Falha ao enviar $($item.Relative)" -ForegroundColor Yellow
        }
    }

    # Remove manutencao — site volta
    $delReq = [System.Net.FtpWebRequest]::Create($offlineUrl)
    $delReq.Credentials = $credential
    $delReq.Method = [System.Net.WebRequestMethods+Ftp]::DeleteFile
    $delReq.GetResponse().Close()

    Write-Host '    Site reativado!' -ForegroundColor Green
} else {
    # Sem DLLs travadas — so notifica conclusao simples
    Invoke-Sql "INSERT INTO DeployNotificacao (Tipo, Versao, Mensagem) VALUES ('aviso5min', '$versao', 'Sistema atualizado! Recarregue a pagina para ver as novidades.')"
    Write-Host ''
    Write-Host '    Notificacao de atualizacao enviada!' -ForegroundColor Green
}

# ============================================================
# RESULTADO
# ============================================================
Write-Host ''
Write-Host '============================================' -ForegroundColor Green
Write-Host "  Deploy $versao concluido com sucesso!" -ForegroundColor Green
Write-Host '============================================' -ForegroundColor Green