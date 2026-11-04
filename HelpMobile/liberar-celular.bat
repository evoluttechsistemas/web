@echo off
chcp 65001 >nul
REM =====================================================================
REM  HELP Mobile - libera o acesso pelo celular na rede local (Wi-Fi)
REM  Clique com o botao direito > Executar como administrador.
REM  Precisa rodar so uma vez.
REM =====================================================================

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo  Execute este arquivo como ADMINISTRADOR
    echo  ^(botao direito ^> Executar como administrador^).
    echo.
    pause
    exit /b
)

netsh advfirewall firewall delete rule name="HELP (desenvolvimento) 5031" >nul 2>&1
netsh advfirewall firewall delete rule name="HELP Mobile (desenvolvimento) 5114" >nul 2>&1

netsh advfirewall firewall add rule name="HELP (desenvolvimento) 5031" dir=in action=allow protocol=TCP localport=5031 profile=private,domain >nul
netsh advfirewall firewall add rule name="HELP Mobile (desenvolvimento) 5114" dir=in action=allow protocol=TCP localport=5114 profile=private,domain >nul

echo.
echo  Portas 5031 (HELP) e 5114 (HELP Mobile) liberadas no firewall.
echo.
echo  Enderecos deste computador na rede:
setlocal enabledelayedexpansion
for /f "tokens=2 delims=:" %%a in ('ipconfig ^| findstr /c:"IPv4"') do (
    set ip=%%a
    echo     http://!ip: =!:5114
)
echo.
echo  Abra um desses enderecos no navegador do celular (mesmo Wi-Fi).
echo  Se nao abrir: confira se a rede do Windows esta como "Privada".
echo.
pause
