# torify v1.5

Roteamento anônimo via Tor, com interface web e proxy local para outros aplicativos.

**autor:** Emanuel D. · **nick:** eds / emanueldssss
**licença:** código protegido. cópia não autorizada é roubo de propriedade.

---

## 1. Instalação

1. Baixe apenas o `torify.exe` (disponível na release v1.5).
2. Coloque o `torify.exe` em qualquer pasta do seu computador (ele é autossuficiente, não precisa de nenhuma pasta extra).
3. Dê um duplo clique no `torify.exe`.

Pronto. Na primeira vez ele baixa o Tor Expert Bundle sozinho e extrai para
`%LOCALAPPDATA%\Torify\`. Não é preciso instalar nada manualmente.

> Requisitos: Windows 10/11 e .NET Framework 4.x (já vem no Windows).
> Se uma versão antiga do torify estiver rodando, mate os processos
> `torify.exe` e `tor.exe` no Gerenciador de Tarefas antes de abrir o novo.

---

## 2. Uso básico (interface web)

Ao abrir o `torify.exe`, ele sobe um servidor local e abre o app no seu navegador
em `http://localhost:8899/`.

1. Clique em **start tor**. Na primeira vez demora alguns segundos (baixa o Tor).
2. O status no topo fica `connected` quando o Tor está ativo.
3. Vá em **check ip** para ver seu IP real vs. o IP do Tor (devem ser diferentes).
4. **new identity** troca seu IP do Tor na hora.
5. **auto-rotate** (toggle) troca o IP sozinho a cada intervalo (padrão 60s).
6. **⏻ sair** encerra o Tor, libera a porta e fecha tudo (você volta ao IP normal).

---

## 3. Para CLIs (burlar rate-limit por IP)

O torify sobe um **proxy HTTP local** que ponteia todo o tráfego pelo Tor.
Qualquer programa que respeite a variável de ambiente `HTTPS_PROXY` vai sair
com o IP do Tor em vez do seu IP real.

### PowerShell
```powershell
$env:HTTPS_PROXY = "http://127.0.0.1:8080"
# depois rode o CLI normalmente, exemplo:
opencode
```

### CMD
```cmd
set HTTPS_PROXY=http://127.0.0.1:8080
opencode
```

### Como confirmar
Com o torify aberto e o Tor ativo, rode:
```powershell
(Invoke-WebRequest -Uri https://api.ipify.org -Proxy http://127.0.0.1:8080).Content
```
O IP retornado deve ser diferente do seu IP real.

### Trocar o IP no meio da sessão
Clique em **new identity** no torify (ou ligue o **auto-rotate**). O proxy passa
a usar o novo circuito Tor automaticamente.

---

## 4. Configuração

Na aba **configure** do app você vê o comando `HTTPS_PROXY` já montado com a
porta correta. Se você mudar o campo "porta http (proxy)", o comando é
atualizado automaticamente. Exemplo:

- porta http (proxy) = `8080` → `HTTPS_PROXY=http://127.0.0.1:8080`
- porta http (proxy) = `9020` → `HTTPS_PROXY=http://127.0.0.1:9020`

Os outros campos:
- **intervalo de rotação (s):** tempo do auto-rotate.
- **porta socks (tor):** porta SOCKS do Tor (padrão 9050).
- **porta http (proxy):** porta do proxy local para CLIs (padrão 8080).

---

## 5. Créditos e proteção

Criado e mantido por Emanuel D. (eds / emanueldssss).
Todos os direitos reservados. O binário é protegido contra engenharia reversa
básica; cópia não autorizada é roubo.
