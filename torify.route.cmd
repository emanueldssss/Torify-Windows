@echo off
chcp 65001 >nul
setlocal
set PROXY=http://127.0.0.1:8080

echo.
echo  ============================================
echo   Torify.Route  —  aplica proxy Tor no terminal
echo  ============================================
echo.
echo   1) CMD
echo   2) PowerShell
echo   3) Windows Terminal (PowerShell padrao)
echo.
set /p OPC=  escolha [1-3]: 

if "%OPC%"=="1" (
  start cmd /k "set HTTPS_PROXY=%PROXY% && echo [Torify.Route] proxy aplicado: %PROXY% && echo Pronto para iniciar sua aplicacao (CLI ou outra). && "
) else if "%OPC%"=="2" (
  start powershell -NoExit -Command "$env:HTTPS_PROXY='%PROXY%'; Write-Host '[Torify.Route] proxy aplicado: %PROXY%'; Write-Host 'Pronto para iniciar sua aplicacao.'"
) else if "%OPC%"=="3" (
  start wt new-tab powershell -NoExit -Command "$env:HTTPS_PROXY='%PROXY%'; Write-Host '[Torify.Route] proxy aplicado: %PROXY%'"
) else (
  echo opcao invalida.
)
endlocal
