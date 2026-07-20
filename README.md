# torify v1.5

roteamento anonimo via Tor + Proxychains, com interface web.

**autor:** Emanuel Domingues  ·  **nick:** eds / emanueldssss
**licenca:** codigo protegido. copia nao autorizada e roubo de propriedade.

## como usar
1. baixe `torify.exe` (launcher) e a pasta `webui` (deixe ambos juntos).
2. execute `torify.exe` — ele sobe o servidor local e abre o app no seu navegador (http://localhost:8899).
3. clique em **start tor** para iniciar. verifique o ip em **check ip**.

## requisitos
- windows
- tor expert bundle e proxychains-windows em %LOCALAPPDATA%\Torify\ (o app detecta e usa)
- .NET Framework 4.x (ja vem no windows)

## estrutura
- `torify.exe` — launcher (sobe backend + abre navegador)
- `webui/` — frontend (index.html, styles.css, app.js)
- `webui/backend/TorifyWeb.cs` — servidor C#

todos os direitos reservados a Emanuel Domingues.
