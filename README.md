# Torify

Roteia qualquer aplicativo Windows pelo Tor. Usa Tor Expert Bundle + proxychains-windows.

Feito pra contornar limites diários de CLIs e serviços que restringem por IP. Cada sessão usa um IP Tor diferente, e a rotação automática troca o IP em intervalos definidos.

---

## Instalação

### Requisitos

- Windows 10 ou 11 (64-bit)
- .NET Framework 4.x (vem instalado no Windows)

### Método 1: download do .exe (recomendado)

Baixa o `torify.exe` da [release](https://github.com/emanueldssss/Torify-Windows/releases), coloca em qualquer pasta e executa.

Na primeira execução ele baixa Tor (~30 MB) e proxychains automaticamente pra `%LOCALAPPDATA%\Torify\`. O .exe é portátil — pode mover pra qualquer lugar.

### Método 2: compilar do código

```powershell
git clone https://github.com/emanueldssss/Torify-Windows.git
cd Torify-Windows
powershell -ExecutionPolicy Bypass -File setup.ps1
```

### Atalho no desktop (opcional)

```powershell
$ws = New-Object -ComObject WScript.Shell
$sc = $ws.CreateShortcut("$env:USERPROFILE\Desktop\Torify.lnk")
$sc.TargetPath = "C:\caminho\completo\até\torify.exe"
$sc.WorkingDirectory = "%LOCALAPPDATA%\Torify"
$sc.Save()
```

---

## Como usar

```
  ========================
    Torify v1.3
  ========================

  [1] Iniciar Tor + Verificar Conexão
  [2] Conferir IP
  [3] Configurar
  [4] Adicionar App
  [5] Abrir App com Tor
  [6] Parar Tor
  [7] Auto-Rotate (torsocks mode)
  [0] Sair
```

### Primeiro uso

Opção **4 — Adicionar App**. Abre um seletor de arquivo. Escolha o .exe que quer rotear pelo Tor. O programa:
- Salva o app na lista (`apps.txt`)
- Inicia o Tor (se não estiver rodando)
- Abre o app via proxychains

Repetir pra cada app que quiser adicionar.

### Abrir app salvo

Opção **5 — Abrir App com Tor**. Lista os apps salvos. Escolhe um, o Torify:
1. Rotaciona o IP (SIGNAL NEWNYM)
2. Abre o app via proxychains

### Verificar IP

Opção **2 — Conferir IP**. Mostra o IP real (sem proxy) e o IP do Tor lado a lado. Se forem diferentes, o proxy está funcionando.

Opção **1 — Iniciar Tor**. Sobe o Tor, rotaciona IP, e mostra a comparação.

### Auto-Rotate

Opção **7 — Auto-Rotate**. Pergunta um intervalo em segundos. Abre o app configurado via Tor e fica num loop:
- Rotaciona IP (NEWNYM)
- Verifica se o IP mudou
- Espera N segundos
- Repete

Aperta **Q** pra parar.

---

## Segurança — v1.3

### strict_chain

A configuração do proxychains agora usa `strict_chain` ao invés de `dynamic_chain`.

| Modo | Comportamento | Risco |
|------|--------------|-------|
| `dynamic_chain` | Tenta conectar pelo proxy. Se falhar, cai em conexão direta | **Vazamento de IP** se o Tor cair |
| `strict_chain` | Toda conexão passa pelo proxy. Se falhar, a conexão falha | Zero vazamento |

Com `strict_chain`, se o Tor cair ou o proxy ficar indisponível, o app simplesmente perde conexão — não cai em rota direta.

### Verificação de IP via SOCKS5 puro

A verificação de IP do Tor agora é feita com conexão SOCKS5 direta em C#, sem depender de `curl.exe`. Se o SOCKS5 falhar, retorna "falhou" — **nunca** faz fallback pra conexão direta (como acontecia na v1.2).

### proxy_dns

DNS também passa pelo proxy Tor. Sem DNS leak.

### Anti-vazamento geral

- `proxy_dns` ativado no proxychains.conf
- `strict_chain` evita fallback não-proxificado
- Verificação de IP sempre passa pelo Tor (SOCKS5 direto)
- Removido o fallback via WebClient sem proxy que existia na v1.2

---

## Estrutura

```
Torify-Windows/                  # repositório
├── src/torify.cs                # código fonte (C#)
├── setup.ps1                    # setup + compilação
├── build.ps1                    # compilação manual
├── README.md
└── torify.ico

%LOCALAPPDATA%/Torify/           # runtime (criado automaticamente)
├── torify.exe
├── apps.txt                     # lista de apps adicionados
├── target-app.txt               # app configurado (opção 3)
├── .setup-complete              # marcador de setup
│
├── tor/                         # Tor Expert Bundle
│   ├── tor.exe
│   └── Data/Tor/torrc
│
└── proxychains/                 # proxychains-windows
    ├── proxychains_win32_x64.exe
    └── proxychains.conf
```

---

## Compilar manualmente

```powershell
.\build.ps1
```

Ou direto:

```powershell
& "$env:windir\Microsoft.NET\Framework\v4.0.30319\csc.exe" `
    /target:exe `
    /reference:System.Windows.Forms.dll `
    /out:torify.exe `
    src\torify.cs
```

---

## Dependências

- [Tor Expert Bundle](https://www.torproject.org/)
- [proxychains-windows](https://github.com/shunf4/proxychains-windows) (shunf4)

---

## Linux

Versão Linux (mesmo conceito, Python):

```
https://github.com/emanueldssss/Torify-Linux
```
