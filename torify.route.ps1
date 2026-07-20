# torify.route.ps1 — aplica o proxy Torify.Route no terminal escolhido
$proxy = "http://127.0.0.1:8080"

Clear-Host
Write-Host ""
Write-Host "  ============================================" -ForegroundColor White
Write-Host "    Torify.Route  -  aplica proxy Tor" -ForegroundColor White
Write-Host "  ============================================" -ForegroundColor White
Write-Host ""
Write-Host "   1) CMD"
Write-Host "   2) PowerShell"
Write-Host "   3) Windows Terminal (PowerShell padrao)"
Write-Host ""
$opc = Read-Host "  escolha [1-3]"

switch ($opc) {
  "1" { Start-Process cmd -ArgumentList "/k","set HTTPS_PROXY=$proxy && echo [Torify.Route] proxy aplicado: $proxy && echo Pronto para iniciar sua aplicacao. && " }
  "2" { Start-Process powershell -ArgumentList "-NoExit","-Command","`$env:HTTPS_PROXY='$proxy'; Write-Host '[Torify.Route] proxy aplicado: $proxy'; Write-Host 'Pronto para iniciar sua aplicacao.'" }
  "3" { Start-Process wt -ArgumentList "new-tab","powershell","-NoExit","-Command","`$env:HTTPS_PROXY='$proxy'; Write-Host '[Torify.Route] proxy aplicado: $proxy'" }
  default { Write-Host "opcao invalida." }
}
