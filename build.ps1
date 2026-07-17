<#
.SYNOPSIS
  Compila o torify.exe a partir do codigo fonte C#.
#>

$Base = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = "${env:windir}\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if (!(Test-Path $csc)) {
    $csc = Get-ChildItem "${env:windir}\Microsoft.NET\Framework" -Recurse -Filter "csc.exe" |
           Select-Object -First 1 -ExpandProperty FullName
}

if (!($csc) -or !(Test-Path $csc)) {
    Write-Host "[!] C# compiler (csc.exe) nao encontrado." -ForegroundColor Red
    exit 1
}

Write-Host "[*] Compilando..." -ForegroundColor Cyan
& $csc /target:exe /reference:System.Windows.Forms.dll /out:"$Base\torify.exe" "$Base\src\torify.cs" 2>&1

if (Test-Path "$Base\torify.exe") {
    $size = (Get-Item "$Base\torify.exe").Length / 1KB
    Write-Host "[+] torify.exe compilado! ($([math]::Round($size, 1)) KB)" -ForegroundColor Green
} else {
    Write-Host "[!] Erro na compilacao." -ForegroundColor Red
    exit 1
}
