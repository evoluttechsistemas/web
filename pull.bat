@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"
git config core.autocrlf true

echo ================================
echo        GIT - EVOLUT HELP
echo ================================
echo.

git fetch origin main >nul 2>&1

git diff --quiet
set LOCAL_DIRTY=%errorlevel%

set AHEAD_LINE=
git log origin/main..HEAD --oneline > "%TEMP%\git_ahead.txt" 2>nul
set /p AHEAD_LINE=<"%TEMP%\git_ahead.txt"
del "%TEMP%\git_ahead.txt" >nul 2>&1

if "!LOCAL_DIRTY!"=="1" goto :check_ahead
goto :do_pull

:check_ahead
if defined AHEAD_LINE goto :warn_ahead
echo Detectadas diferencas de formatacao local (CRLF/index). Normalizando...
git checkout -- .
echo.
goto :do_pull

:warn_ahead
echo ATENCAO: Voce tem alteracoes locais nao enviadas!
echo Recomendado fazer push antes de pull.
echo.
git diff --name-only
echo.
set "continuar=S"
set /p continuar="Deseja continuar mesmo assim? (S/N): "
if /i "!continuar!"=="N" goto :cancelar
goto :do_pull

:cancelar
echo Operacao cancelada.
pause
exit /b

:do_pull
echo Baixando alteracoes do GitHub...
echo.
git pull --no-edit origin main
if errorlevel 1 goto :erro
echo.
echo ================================
echo      ATUALIZADO COM SUCESSO
echo ================================
pause
exit /b

:erro
echo.
echo ERRO ao executar git pull.
pause
exit /b