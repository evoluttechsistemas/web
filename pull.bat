@echo off
cd /d "%~dp0"

echo ================================
echo        GIT - EVOLUT HELP
echo ================================
echo.
echo Baixando alteracoes do GitHub...
echo.

git pull

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