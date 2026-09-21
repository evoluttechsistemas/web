@echo off
:: ============================================================
::  publicar-rapido.bat — Deploy IMEDIATO do HELP (sem aviso aos usuarios)
::  Use apenas em horario de baixo uso ou para testar fixes urgentes
:: ============================================================
powershell -ExecutionPolicy Bypass -File "%~dp0publicar-rapido.ps1"
pause