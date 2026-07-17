# Torproxy-win

Windows privacy tool — Tor + Proxychains automation.  
Inicia o Tor daemon, configura proxychains e roda aplicativos atraves do proxy com rotacao automatica de IP.

---

## Como funciona

- **Tor daemon** — SOCKS5 em `127.0.0.1:9050`
- **Proxychains-Windows** — hookeia Winsock e redireciona conexoes de qualquer aplicacao
- **torify.exe** — menu em C# que coordena: inicia Tor, rotaciona IP, abre o app configurado

O terminal do menu **nao fecha** — o app abre em janela separada.

---

## Instalacao

### Requisitos
- Windows 10/11 64-bit
- .NET Framework 4.x (ja vem instalado)
- O aplicativo que voce quer rotear (ex: opencode, navegador, etc.)

### Setup

```powershell
git clone https://github.com/emanueldssss/Torproxy-win.git
cd Torproxy-win
powershell -ExecutionPolicy Bypass -File setup.ps1
```

O setup baixa Tor Expert Bundle + Proxychains-Windows, cria as configs e compila o menu.

---

## Uso

Execute `torify.exe`. Menu:

```
  [1] Rodar TorProxy
  [2] Conferir IP
  [3] Configurar
  [0] Sair
```

### Opcao 1
1. Inicia Tor (se necessario)
2. Rotaciona IP via SIGNAL NEWNYM
3. Mostra IP real vs IP do Tor
4. Abre o aplicativo configurado em nova janela

### Opcao 2
Verifica se o proxy esta funcionando comparando IP real vs IP do Tor.

### Opcao 3
Define qual aplicativo sera roteado pelo Tor.  
Digite o caminho completo do .exe ou `auto` para detectar automaticamente.

---

## Estrutura

```
Torproxy-win/
├── src/torify.cs          # codigo fonte (C#)
├── setup.ps1              # instalacao completa
├── build.ps1              # compila o exe
├── .gitignore
├── README.md
├── torify.exe             # compilado (gitignored)
├── tor/                   # Tor Expert Bundle (gitignored)
└── proxychains/           # proxychains-windows (gitignored)
```

Paths sao detectados automaticamente — funciona em qualquer maquina sem configuracao.

---

## Compilar manualmente

```powershell
.\build.ps1
```

Ou direto com o compilador C#:

```powershell
& "$env:windir\Microsoft.NET\Framework\v4.0.30319\csc.exe" /target:exe /reference:System.Windows.Forms.dll /out:torify.exe src\torify.cs
```

---

## Notas

- O proxy funciona com qualquer aplicacao que use conexoes de rede padrao (Winsock)
- Por padrao o menu tenta detectar o opencode, mas voce pode configurar qualquer .exe
- Para usar com outros aplicativos, configure o caminho na opcao 3 do menu
