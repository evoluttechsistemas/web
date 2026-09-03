@echo off
cd /d "%~dp0"

git config core.autocrlf true

echo ================================
echo        GIT - EVOLUT HELP
echo ================================
echo.

git diff --quiet
if not %errorlevel%==0 (
    echo ATENCAO: Voce tem alteracoes locais nao enviadas!
    echo Recomendado fazer push antes de pull.
    echo.
    set /p continuar="Deseja continuar mesmo assim? (S/N): "
    if /i "%continuar%"=="N" exit /b
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