@echo off
:: ============================================================
::  publicar.bat — Deploy do HELP para producao via FTP
::  Duplo clique para executar
:: ============================================================
powershell -ExecutionPolicy Bypass -File "%~dp0publicar.ps1"
pause
