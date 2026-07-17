# Torify

Roteie qualquer aplicativo Windows pelo Tor com um clique.

Inicia o Tor daemon, configura proxychains e abre programas selecionados passando pelo proxy — com rotação automática de IP a cada sessão. O terminal do menu nunca fecha; cada app abre em janela separada.

---

## Instalação

### Requisitos

- Windows 10 ou 11 (64-bit)
- .NET Framework 4.x (já vem instalado no Windows)

### Opção 1: download direto do .exe (recomendado)

Baixe o `torify.exe` da [release](https://github.com/emanueldssss/Torify/releases), coloque em uma pasta e dê 2 cliques.

Na primeira execução ele baixa automaticamente o Tor + Proxychains (cerca de 30 MB) e extrai tudo. Depois disso, abre direto o menu.

### Opção 2: usando o repositório completo

```powershell
git clone https://github.com/emanueldssss/Torify.git
cd Torify
powershell -ExecutionPolicy Bypass -File setup.ps1
```

O setup baixa as dependências e compila o `torify.exe`.

### Opcional: atalho no desktop

```powershell
$ws = New-Object -ComObject WScript.Shell
$sc = $ws.CreateShortcut("$env:USERPROFILE\Desktop\Torify.lnk")
$sc.TargetPath = "C:\caminho\completo\até\Torify\torify.exe"
$sc.WorkingDirectory = "C:\caminho\completo\até\Torify"
$sc.Save()
```

---

## Como usar

Execute `torify.exe`. O menu:

```
  ========================
    Torify v1.0
  ========================
  Tor + Proxychains for Windows
  ========================

  [1] Rodar Torify
  [2] Conferir IP
  [3] Configurar
  [4] Adicionar App
  [5] Abrir App com Tor
  [0] Sair

  ========================
```

### Primeiro uso: adicionar um aplicativo

**1. Menu > opção 4 — Adicionar App**

Uma janela do Windows vai abrir para você selecionar um arquivo `.exe`. Escolha o programa que você quer rotear pelo Tor (navegador, cliente de chat, qualquer coisa).

Assim que selecionar, o programa:
- Salva o app numa lista (arquivo `apps.txt` na pasta do Torify)
- Inicia o Tor (se ainda não tiver rodando)
- Abre o app via proxychains — o tráfego dele vai passar pelo Tor

O terminal do menu continua aberto. Você pode adicionar quantos apps quiser.

### Abrir um app salvo

**2. Menu > opção 5 — Abrir App com Tor**

O menu mostra todos os apps que você já adicionou:

```
  Apps salvos:

  [1] Firefox
      C:\Program Files\Mozilla Firefox\firefox.exe
  [2] Discord
      C:\Users\você\AppData\Local\Discord\Discord.exe
  [3] opencode
      C:\Users\você\AppData\Roaming\npm\node_modules\opencode-ai\bin\opencode.exe

  [0] Voltar

  Escolha:
```

Digite o número do app. O Torproxy:
1. Rotaciona o IP (SIGNAL NEWNYM)
2. Abre o app em janela separada com o tráfego passando pelo Tor

### Verificar se o proxy está funcionando

**3. Menu > opção 2 — Conferir IP**

Mostra seu IP real (sem proxy) e o IP do Tor lado a lado. Se forem diferentes, está roteando corretamente.

```
  IP real: 201.95.xx.xx
  IP Tor:  185.220.xxx.xxx

  [+] IPs DIFERENTES — Tor funcionando!
```

---

## Estrutura de arquivos

```
Torproxy-win/
├── src/torify.cs            # código fonte (C#)
├── setup.ps1                # instalação completa
├── build.ps1                # compila o .exe manualmente
├── .gitignore
├── README.md
│
├── torify.exe               # menu compilado (gerado pelo setup)
├── apps.txt                 # lista de apps que você adicionou
├── target-app.txt           # caminho do app padrão (opção 3)
│
├── tor/                     # Tor Expert Bundle (baixado pelo setup)
│   ├── tor.exe
│   └── Data/Tor/torrc
│
└── proxychains/             # proxychains-windows (baixado pelo setup)
    ├── proxychains_win32_x64.exe
    └── proxychains.conf
```

Tudo portátil. Nada registra no sistema. Copie a pasta inteira para outro PC que funciona — só rodar `setup.ps1` de novo para baixar as dependências.

---

## Recompilar manualmente

Se quiser modificar o código e recompilar:

```powershell
.\build.ps1
```

Ou direto com o compilador C#:

```powershell
& "$env:windir\Microsoft.NET\Framework\v4.0.30319\csc.exe" `
    /target:exe `
    /reference:System.Windows.Forms.dll `
    /out:torify.exe `
    src\torify.cs
```

---

## Sobre

Ferramenta gratuita e open-source. Usa o [Tor Expert Bundle](https://www.torproject.org/) da comunidade Tor Project e o [proxychains-windows](https://github.com/shunf4/proxychains-windows) mantido por shunf4.
