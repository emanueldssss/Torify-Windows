# Torify Web UI

Frontend web (HTML/CSS/JS) + backend local (C# `HttpListener`) that replaces the
desktop window with a browser-driven interface. Same Tor + proxychains core.

## Run

1. Build the backend:
   ```
   csc /target:exe /reference:System.Web.dll /out:torify-web.exe backend\TorifyWeb.cs
   ```
2. Start the backend (first launch downloads Tor ~30 MB + proxychains to `%LOCALAPPDATA%\Torify\`):
   ```
   torify-web.exe
   ```
3. Open `index.html` in a browser (or serve the folder with any static server).
4. The UI talks to `http://localhost:8899`.

## Aesthetic

"Nocturne Terminal" — near-black canvas, mint accent (`#00e08f`), Syne display +
IBM Plex Mono, subtle grid + grain, staggered load motion, pulsing status dot,
sliding auto-rotate toggle, glow-on-hover IP cards. Dark/light toggle.

## Endpoints (backend)

`/status` · `/ip` · `/start` (POST) · `/stop` (POST) · `/config` (POST) ·
`/config/auto` (POST) · `/apps` · `/apps/add` (POST) · `/apps/open` (POST) ·
`/rotate/on` (POST) · `/rotate/off` (POST) · `/rotate/status`
