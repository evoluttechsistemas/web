@echo off
cd /d "%~dp0"

echo ================================
echo        GIT - EVOLUT HELP
echo ================================
echo.

git status
echo.

git diff --cached --quiet
git diff --quiet

if %errorlevel%==0 (
    echo Nenhuma alteracao para enviar.
    pause
    exit /b
)

set /p mensagem="Mensagem do commit: "

git add .
git commit -m "%mensagem%"

if errorlevel 1 (
    echo ERRO ao criar commit.
    pause
    exit /b
)

git push

if errorlevel 1 (
    echo ERRO ao enviar para o GitHub.
    pause
    exit /b
)

echo.
echo ================================
echo      ENVIADO COM SUCESSO
echo ================================
pause