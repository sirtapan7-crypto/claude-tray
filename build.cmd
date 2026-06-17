@echo off
REM ==========================================================================
REM build.cmd - Compila e publica o ClaudeTray como .exe self-contained
REM Saida: bin\Release\net10.0-windows\win-x64\publish\ClaudeTray.exe
REM ==========================================================================
setlocal

cd /d "%~dp0"

echo.
echo === Publicando ClaudeTray (Release, win-x64, self-contained) ===
echo.

dotnet publish -c Release
if errorlevel 1 (
    echo.
    echo *** ERRO: falha no dotnet publish. ***
    exit /b 1
)

echo.
echo === Build concluido com sucesso ===
echo Executavel: bin\Release\net10.0-windows\win-x64\publish\ClaudeTray.exe
echo.

endlocal
