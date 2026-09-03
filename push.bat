@echo off
cd /d "%~dp0"

git config core.autocrlf true

echo ================================
echo        GIT - EVOLUT HELP
echo ================================
echo.

git config user.email >nul 2>&1
if errorlevel 1 (
    set /p gitemail="Seu email do GitHub: "
    git config --global user.email "%gitemail%"
    set /p gitname="Seu nome: "
    git config --global user.name "%gitname%"
)

git status
echo.

git add .
git diff --cached --quiet

if %errorlevel%==0 (
    echo Nenhuma alteracao para enviar.
    pause
    exit /b
)

set /p mensagem="Mensagem do commit: "

git commit -m "%mensagem%"

if errorlevel 1 (
    echo ERRO ao criar commit.
    pause
    exit /b
)

git push --set-upstream origin main

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