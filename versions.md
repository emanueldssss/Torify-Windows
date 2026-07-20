# Histórico de Versões — Torify.Route

Registro do que existia em cada versão e o que foi adicionado, alterado ou
removido de uma para a outra. A versão funcional e estável é a **v1.5**.

---

## v1.0 — nascimento
- App em C#, modo console (`torify.cs`), com menu interativo.
- Baixava o Tor Expert Bundle e o proxychains-windows para
  `%LOCALAPPDATA%\Torify\`.
- Objetivo: rotear apps pela rede Tor de forma simples.

## v1.1 / v1.2 — ajustes de setup
- Correções na extração do Tor (caminho do binário após o `tar`).
- Tratamento de falhas no download e na escrita do `torrc`.
- Adição de logs de diagnóstico no console.

## v1.3 — menu minimalista
- Menu console reescrito, mais limpo.
- Corrigido `FindPcExe` (não retornava nulo).
- Parser SOCKS5/HTTP revisado para checar IP real x Tor.
- Mantido 100% em console (sem interface gráfica).

## v1.4 — release console
- Empacotado como release público (menu console).
- Pequenos ajustes de robustez na verificação de IP.

---

## v1.5 — Torify.Route (atual, a que funciona)
Reescrita completa. O foco mudou de "menu console" para "app web + proxy".

### Removido (vs v1.0–v1.4)
- Menu/CLI em console.
- GUI Windows Forms (janela pesada) que existiu numa fase intermediária.
- Dependência de pasta `webui/` solta ao lado do exe (assets embutidos).
- `setup.ps1` / `build.ps1` legados.
- A "mentira" de status: antes dizia "tor ativo" mesmo sem Tor rodando.

### Adicionado / Colocado
- **Launcher `torify.exe`** (winexe) que sobe o servidor e abre o app no
  navegador sozinho (`http://localhost:8899/`).
- **Interface web** minimalista preto e branco, animada (typewriter no IP,
  ring de rotação, partículas, fade-in).
- **Botão SAÍR** que mata o Tor, libera a porta e encerra tudo.
- **Download + extração automática do Tor** no primeiro "start tor".
- **TLS 1.2** ligado no backend (real ip funcionando de verdade).
- **Túnel SOCKS5 + TLS manual em C#** → o "tor ip" mostra o IP real do circuito.
- **NEWNYM corrigido** (protocolo Tor Control puro) → rotação de IP real:
  - toggle **auto-rotate**
  - botão **new identity**
  - IP diferente a cada abertura do app
- **Proxy HTTP local** (porta 8080) que ponteia TODO o tráfego pelo Tor
  (CONNECT tunnel + DNS remoto, sem vazar). Usado por CLIs para burlar
  rate-limit por IP via `HTTPS_PROXY=http://127.0.0.1:8080`.
- **Proteção anti-debug** e créditos do autor em todo lugar.
- **Renomeado para Torify.Route** (com "Route" em fonte cursiva elegante).
- **Configure dinâmico**: o comando `HTTPS_PROXY` atualiza sozinho ao mudar
  a porta do proxy, e fica destacado.
- `versions.md` (este arquivo).

### Alterado
- Marca "torify" → **Torify.Route**.
- Crédito "Emanuel Domingues" → **Emanuel D.**
- As outras releases (v1.0–v1.4) foram marcadas como prerelease e removidas
  do "latest"; apenas a v1.5 é a release oficial.

---

Observação: a partir da v1.5, apenas o `torify.exe` é distribuído na release
(o código-fonte não é exposto). O binário é autossuficiente.
