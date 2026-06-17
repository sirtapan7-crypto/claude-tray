@echo off
REM ==========================================================================
REM build-installer.cmd - Gera o instalador do ClaudeTray com Inno Setup
REM Faz o build (dotnet publish) e depois compila installer.iss
REM Saida: dist\ClaudeTray-Setup.exe
REM ==========================================================================
setlocal

cd /d "%~dp0"

REM --- 1) Build / publish -------------------------------------------------
call "%~dp0build.cmd"
if errorlevel 1 (
    echo *** ERRO: build falhou, instalador nao foi gerado. ***
    exit /b 1
)

REM --- 2) Localiza o compilador do Inno Setup (ISCC.exe) -------------------
set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=C:\Program Files\Inno Setup 6\ISCC.exe"

if not exist "%ISCC%" (
    echo.
    echo *** ERRO: ISCC.exe nao encontrado. ***
    echo Instale o Inno Setup 6: https://jrsoftware.org/isdl.php
    exit /b 1
)

REM --- 3) Compila o instalador --------------------------------------------
echo.
echo === Gerando instalador com Inno Setup ===
echo.

"%ISCC%" "%~dp0installer.iss"
if errorlevel 1 (
    echo.
    echo *** ERRO: falha ao compilar o instalador. ***
    exit /b 1
)

echo.
echo === Instalador gerado com sucesso ===
echo Arquivo: dist\ClaudeTray-Setup.exe
echo.

endlocal
