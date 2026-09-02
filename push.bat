@echo off
cd /d "%~dp0"

echo ================================
echo        GIT - EVOLUT HELP
echo ================================
echo.

git status
echo.

set /p mensagem="Mensagem do commit: "

git add .
git commit -m "%mensagem%"

if errorlevel 1 (
    echo.
    echo ERRO ao criar commit.
    pause
    exit /b
)

git push

if errorlevel 1 (
    echo.
    echo ERRO ao enviar para o GitHub.
    pause
    exit /b
)

echo.
echo ================================
echo      ENVIADO COM SUCESSO
echo ================================
pause