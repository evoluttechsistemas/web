@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"
git config core.autocrlf true

echo ================================
echo        GIT - EVOLUT HELP
echo ================================
echo.

:: Busca informações do remoto sem aplicar
git fetch origin main >nul 2>&1

:: Verifica se há alterações no working tree (arquivos editados localmente)
git diff --quiet
set LOCAL_DIRTY=%errorlevel%

:: Verifica se o local está À FRENTE do remoto (commits não enviados)
git log origin/main..HEAD --oneline > "%TEMP%\git_ahead.txt" 2>nul
set /p AHEAD_LINE=<"%TEMP%\git_ahead.txt"
del "%TEMP%\git_ahead.txt" >nul 2>&1

if "!LOCAL_DIRTY!"=="1" (
    if defined AHEAD_LINE (
        :: Há arquivos alterados E commits locais não enviados — alterações reais desta máquina
        echo ATENCAO: Voce tem alteracoes locais nao enviadas!
        echo Recomendado fazer push antes de pull.
        echo.
        git diff --name-only
        echo.
        set /p continuar="Deseja continuar mesmo assim? (S/N): "
        if /i "!continuar!"=="N" exit /b
    ) else (
        :: Há diff no working tree mas SEM commits locais à frente — provavelmente lixo de CRLF/index
        echo Detectadas diferencas de formatacao local (CRLF/index). Normalizando...
        git checkout -- .
        echo.
    )
)

echo Baixando alteracoes do GitHub...
echo.
git pull origin main
if errorlevel 1 (
    echo.
    echo ERRO ao executar git pull.
    pause
    exit /b
)

echo.
echo ================================
echo      ATUALIZADO COM SUCESSO
echo ================================
pause