<#
.SYNOPSIS
  Compila o torify.exe a partir do codigo fonte C#.
#>

$ScriptDir = $PSScriptRoot
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

# Icon é opcional
$iconFile = "$ScriptDir\torify.ico"
$iconArg = if (Test-Path $iconFile) { "/win32icon:`"$iconFile`"" } else { "" }

& $csc /target:exe /reference:System.Windows.Forms.dll $iconArg "/out:$ScriptDir\torify.exe" "$ScriptDir\src\torify.cs" 2>&1

if (Test-Path "$ScriptDir\torify.exe") {
    $size = (Get-Item "$ScriptDir\torify.exe").Length / 1KB
    Write-Host "[+] torify.exe compilado! ($([math]::Round($size, 1)) KB)" -ForegroundColor Green
} else {
    Write-Host "[!] Erro na compilacao." -ForegroundColor Red
    exit 1
}