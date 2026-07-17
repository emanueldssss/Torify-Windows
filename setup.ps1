<#
.SYNOPSIS
  Setup script for Torify — Tor + Proxychains wrapper for opencode.
.DESCRIPTION
  Downloads Tor Expert Bundle + Proxychains-Windows, creates configs,
  and compiles the torify.exe menu launcher.
#>

$ErrorActionPreference = "Stop"
$Base = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "`n  ========================" -ForegroundColor Magenta
Write-Host "     TORIFY - Setup" -ForegroundColor Magenta
Write-Host "  ========================" -ForegroundColor Magenta
Write-Host "`n"

# ─── 1. Download Tor Expert Bundle ───────────────────────────────────
$TorDir    = "$Base\tor"
$TorTarball = "$Base\tor-expert.tar.gz"
$TorVer    = "15.0.18"
$TorUrl    = "https://www.torproject.org/dist/torbrowser/$TorVer/tor-expert-bundle-windows-x86_64-$TorVer.tar.gz"
$TorUrlMir = "https://archive.torproject.org/tor-package-archive/torbrowser/$TorVer/tor-expert-bundle-windows-x86_64-$TorVer.tar.gz"

if (!(Test-Path "$TorDir\tor.exe")) {
    Write-Host "  [*] Baixando Tor Expert Bundle $TorVer...`n" -ForegroundColor Cyan

    $downloaded = $false
    foreach ($url in @($TorUrl, $TorUrlMir)) {
        try {
            Write-Host "      $url" -ForegroundColor DarkGray
            Invoke-WebRequest -Uri $url -OutFile $TorTarball -UseBasicParsing -TimeoutSec 120
            $downloaded = $true
            break
        } catch {
            Write-Host "      [!] falhou, tentando mirror..." -ForegroundColor Yellow
        }
    }

    if (-not $downloaded) {
        Write-Host "`n  [!] Erro ao baixar Tor. Baixe manualmente:" -ForegroundColor Red
        Write-Host "      $TorUrl" -ForegroundColor Red
        Write-Host "      Extraia o conteudo para: $TorDir`n" -ForegroundColor Red
        Write-Host "      Apos baixar, extraia com:" -ForegroundColor Yellow
        Write-Host "      tar -xzf tor-expert-bundle-windows-x86_64-$TorVer.tar.gz -C `"$Base`"" -ForegroundColor Yellow
        exit 1
    }

    Write-Host "  [*] Extraindo Tor..." -ForegroundColor Cyan
    # Use tar.exe (built-in on Win10/11)
    tar -xzf $TorTarball -C "$Base"
    if ($LASTEXITCODE -eq 0) {
        # The tarball extracts to tor-expert-bundle-windows-x86_64-15.0.18/
        $extracted = Get-ChildItem "$Base\tor-expert-bundle-windows-*" -Directory | Select-Object -First 1
        if ($extracted) {
            # Rename to just "tor"
            if (Test-Path $TorDir) { Remove-Item $TorDir -Recurse -Force }
            Move-Item $extracted.FullName $TorDir -Force
            Write-Host "  [+] Tor extraido em $TorDir" -ForegroundColor Green
        } else {
            Write-Host "  [!] Extraido mas pasta nao encontrada. Verifique manualmente." -ForegroundColor Red
        }
    } else {
        Write-Host "  [!] Erro na extracao. Tente manualmente." -ForegroundColor Red
        exit 1
    }
    Remove-Item $TorTarball -Force -ErrorAction SilentlyContinue
} else {
    Write-Host "  [+] Tor ja baixado." -ForegroundColor Green
}

# ─── 2. Create torrc ─────────────────────────────────────────────────
$TorData = "$TorDir\Data\Tor"
if (!(Test-Path $TorData)) { New-Item -ItemType Directory -Path $TorData -Force | Out-Null }

$torrc = @"
SOCKSPort 127.0.0.1:9050
ControlPort 127.0.0.1:9051
CookieAuthentication 0
DataDirectory $TorData
GeoIPFile $TorDir\Data\Tor\geoip
GeoIPv6File $TorDir\Data\Tor\geoip6
Log notice stdout
"@

$torrc | Out-File -FilePath "$TorData\torrc" -Encoding ASCII -Force
Write-Host "  [+] torrc criado." -ForegroundColor Green

# ─── 3. Download Proxychains-Windows ─────────────────────────────────
$PcDir    = "$Base\proxychains"
$PcZip    = "$Base\proxychains.zip"
$PcVer    = "0.6.8"
$PcUrl    = "https://github.com/shunf4/proxychains-windows/releases/download/$PcVer/proxychains_${PcVer}_win32_x64.zip"
$PcUrlMir = "https://github.com/shunf4/proxychains-windows/releases/download/$PcVer/proxychains_${PcVer}_win32_x64_debug.zip"

if (!(Test-Path "$PcDir\proxychains_win32_x64.exe")) {
    Write-Host "`n  [*] Baixando Proxychains-Windows $PcVer..." -ForegroundColor Cyan

    $downloaded = $false
    foreach ($url in @($PcUrl, $PcUrlMir)) {
        try {
            Write-Host "      $url" -ForegroundColor DarkGray
            Invoke-WebRequest -Uri $url -OutFile $PcZip -UseBasicParsing -TimeoutSec 60
            $downloaded = $true
            break
        } catch {
            Write-Host "      [!] falhou, tentando mirror..." -ForegroundColor Yellow
        }
    }

    if (-not $downloaded) {
        Write-Host "`n  [!] Erro ao baixar proxychains. Baixe manualmente:" -ForegroundColor Red
        Write-Host "      https://github.com/shunf4/proxychains-windows/releases" -ForegroundColor Red
        Write-Host "      Extraia para: $PcDir`n" -ForegroundColor Red
        exit 1
    }

    Write-Host "  [*] Extraindo Proxychains..." -ForegroundColor Cyan
    Expand-Archive -Path $PcZip -DestinationPath $PcDir -Force
    Remove-Item $PcZip -Force -ErrorAction SilentlyContinue
    Write-Host "  [+] Proxychains extraido em $PcDir" -ForegroundColor Green
} else {
    Write-Host "  [+] Proxychains ja baixado." -ForegroundColor Green
}

# ─── 4. Create proxychains.conf ──────────────────────────────────────
$pcConf = @"
strict_chain
proxy_dns
tcp_read_time_out 15000
tcp_connect_time_out 8000
[ProxyList]
socks5 127.0.0.1 9050
"@

$pcConf | Out-File -FilePath "$PcDir\proxychains.conf" -Encoding ASCII -Force
Write-Host "  [+] proxychains.conf criado." -ForegroundColor Green

# ─── 5. Compile torify.exe ───────────────────────────────────────────
Write-Host "`n  [*] Compilando torify.exe..." -ForegroundColor Cyan

$csc = "${env:windir}\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if (!(Test-Path $csc)) {
    # Try other versions
    $csc = Get-ChildItem "${env:windir}\Microsoft.NET\Framework" -Recurse -Filter "csc.exe" | Select-Object -First 1 -ExpandProperty FullName
}
if (!($csc) -or !(Test-Path $csc)) {
    Write-Host "  [!] C# compiler (csc.exe) nao encontrado." -ForegroundColor Red
    Write-Host "  [!] Instale .NET Framework 4.x ou compile manualmente:" -ForegroundColor Red
    Write-Host "      ${env:windir}\Microsoft.NET\Framework\v4.0.30319\csc.exe src\torify.cs /out:torify.exe" -ForegroundColor Red
    exit 1
}

& $csc /target:exe /reference:System.Windows.Forms.dll /out:"$Base\torify.exe" "$Base\src\torify.cs" 2>&1 | Out-Null
if (Test-Path "$Base\torify.exe") {
    Write-Host "  [+] torify.exe compilado! ($((Get-Item "$Base\torify.exe").Length / 1KB) KB)" -ForegroundColor Green
} else {
    Write-Host "  [!] Erro na compilacao." -ForegroundColor Red
    exit 1
}

# ─── 6. Cleanup ──────────────────────────────────────────────────────
# Remove old scripts folder if it exists (legacy)
if (Test-Path "$Base\scripts") {
    Remove-Item "$Base\scripts" -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "`n  ========================" -ForegroundColor Magenta
Write-Host "     Setup concluido!" -ForegroundColor Magenta
Write-Host "  ========================" -ForegroundColor Magenta
Write-Host "`n  Execute torify.exe para iniciar o menu.`n" -ForegroundColor Cyan
