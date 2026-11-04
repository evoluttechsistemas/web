@echo off
:: ============================================================
::  publicar-mobile.bat - Deploy do HELP Mobile (helpmobile.evoluttech.com) via FTP
::  Duplo clique para executar
:: ============================================================
powershell -ExecutionPolicy Bypass -File "%~dp0publicar-mobile.ps1"
pause
