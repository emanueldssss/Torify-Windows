$base = "C:\Users\emanuel\Torify-Windows\webui"
$backend = "$base\backend"

function Make-CS($rel, $var){
    $raw = Get-Content -Raw -Encoding UTF8 (Join-Path $base $rel)
    # escape backslash, double-quote, then newlines
    $e = $raw.Replace("\", "\\").Replace('"', '\"').Replace("`r`n", "\n").Replace("`n", "\n")
    return "    public static readonly string $var = ""$e"";"
}

$html = Make-CS "index.html" "HTML"
$css  = Make-CS "styles.css" "CSS"
$js   = Make-CS "app.js" "JS"

$out = @"
// gerado automaticamente — assets embutidos (torify v1.5, by emanueldssss)
using System;
class Assets
{
$html

$css

$js
}
"@

Set-Content -Path (Join-Path $backend "Assets.cs") -Value $out -Encoding UTF8
Write-Host "Assets.cs gerado: $((Get-Item (Join-Path $backend 'Assets.cs')).Length) bytes"
