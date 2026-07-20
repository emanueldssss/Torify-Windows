/* torify v1.5 — frontend client. autor: emanueldssss */
const $ = (s) => document.querySelector(s);
const $$ = (s) => document.querySelectorAll(s);
const API = "http://localhost:8899";

/* ---------- toast ---------- */
let toastT;
function toast(msg){
  const t = $("#toast"); t.textContent = msg; t.classList.add("show");
  clearTimeout(toastT); toastT = setTimeout(()=>t.classList.remove("show"), 2600);
}
function logEl(el, line, cls){
  const d = document.createElement("div");
  if(cls) d.className = cls; d.textContent = line;
  el.appendChild(d); el.scrollTop = el.scrollHeight;
}

/* ---------- typewriter ---------- */
function typewrite(el, txt, speed){
  speed = speed || 22; el.textContent = ""; let i = 0;
  const id = setInterval(()=>{
    el.textContent = txt.slice(0, ++i);
    if(i >= txt.length) clearInterval(id);
  }, speed);
}

/* ---------- particles ---------- */
const cv = $("#particles"); const ctx = cv.getContext("2d");
let parts = [];
function resizeCv(){
  cv.width = innerWidth; cv.height = innerHeight;
  parts = Array.from({length: Math.min(60, (innerWidth*innerHeight)/26000|0)}, ()=>({
    x: Math.random()*cv.width, y: Math.random()*cv.height,
    vx: (Math.random()-.5)*.3, vy: (Math.random()-.5)*.3,
    r: Math.random()*1.6+.4
  }));
}
addEventListener("resize", resizeCv); resizeCv();
(function loop(){
  ctx.clearRect(0,0,cv.width,cv.height);
  const acc = getComputedStyle(document.documentElement).getPropertyValue("--accent").trim();
  parts.forEach(p=>{
    p.x+=p.vx; p.y+=p.vy;
    if(p.x<0||p.x>cv.width) p.vx*=-1;
    if(p.y<0||p.y>cv.height) p.vy*=-1;
    ctx.beginPath(); ctx.arc(p.x,p.y,p.r,0,7); ctx.fillStyle = acc; ctx.globalAlpha=.5; ctx.fill();
  });
  ctx.globalAlpha=1; requestAnimationFrame(loop);
})();

/* ---------- status ---------- */
function setStatus(online){
  const s = $("#status");
  s.classList.toggle("online", online);
  $("#statusDot").style.background = online ? "var(--accent)" : "var(--text)";
  $("#statusLabel").textContent = online ? "connected" : "offline";
}

/* ---------- nav ---------- */
$$(".nav-item").forEach(b=>{
  b.addEventListener("click", ()=>{
    const v = b.dataset.view;
    if(v === "stop"){ stopTor(); return; }
    $$(".nav-item").forEach(n=>n.classList.remove("active"));
    b.classList.add("active");
    $$(".view").forEach(vw=>vw.hidden = vw.dataset.view !== v);
    if(v === "ip") checkIp(true);
    if(v === "apps") loadApps();
    if(v === "rotate") startRing();
  });
});

/* ---------- api calls ---------- */
async function api(path, opts){
  try{ return await fetch(API+path, opts); }
  catch(e){ return {ok:false, json:async()=>({err:"backend offline"})} };
}

async function startTor(){
  const btn = $("#startBtn"); btn.disabled = true;
  logEl($("#logHome"), "> iniciando tor…");
  let r = await api("/start", {method:"POST"});
  let j = await r.json().catch(()=>({}));
  if(j.ok){ logEl($("#logHome"), "✓ tor ativo", "ok"); setStatus(true); toast("tor online"); }
  else { logEl($("#logHome"), "✗ "+(j.err||"falha"), "err"); toast("falha ao iniciar"); }
  btn.disabled = false; await refreshStatus();
}

async function stopTor(){
  let r = await api("/stop", {method:"POST"});
  let j = await r.json().catch(()=>({}));
  if(j.ok){ setStatus(false); toast("tor parado"); logEl($("#logHome"), "✗ tor parado", "err"); }
  else toast("erro ao parar");
  await refreshStatus();
}

async function checkIp(silent){
  const ri = $("#realIp"), ti = $("#torIp");
  if(!silent) toast("verificando ip…");
  let r = await api("/ip"); let j = await r.json().catch(()=>({}));
  if(j.ok){
    typewrite(ri, j.real || "—");
    typewrite(ti, j.tor || "—");
    $("#realIp2") && typewrite($("#realIp2"), j.real||"—");
    $("#torIp2") && typewrite($("#torIp2"), j.tor||"—");
    const leaked = !j.tor || j.tor === "offline" || j.tor === j.real;
    $("#shieldNote") && ($("#shieldNote").textContent = leaked
      ? "⚠ atenção: tráfego pode não estar pelo tor" : "✓ roteado via tor — sem vazamento");
    if(j.tor && j.tor !== "offline"){ setStatus(true); }
  } else {
    ri.textContent = ti.textContent = "offline";
    $("#shieldNote") && ($("#shieldNote").textContent = "backend offline");
  }
}

async function refreshStatus(){
  let r = await api("/status"); let j = await r.json().catch(()=>({}));
  setStatus(!!(j && j.online));
}

async function loadApps(){
  const list = $("#appsList"); list.innerHTML = "";
  let r = await api("/apps"); let j = await r.json().catch(()=>({apps:[]}));
  (j.apps||[]).forEach(a=>{
    const row = document.createElement("div"); row.className = "app-row";
    row.innerHTML = `<div class="app-name"><span class="icon">▸</span>${a}</div>
      <div style="display:flex;gap:8px;align-items:center">
      <span class="app-state">idle</span>
      <button class="app-del" data-app="${a}">✕</button></div>`;
    list.appendChild(row);
  });
  $$(".app-del").forEach(b=>b.addEventListener("click", async ()=>{
    await api("/apps?name="+encodeURIComponent(b.dataset.app), {method:"DELETE"});
    loadApps();
  }));
}
$("#addApp") && $("#addApp").addEventListener("click", async ()=>{
  const inp = $("#appInput"); const v = inp.value.trim(); if(!v) return;
  await api("/apps", {method:"POST", headers:{"Content-Type":"application/json"},
    body: JSON.stringify({name:v})});
  inp.value = ""; loadApps(); toast("app adicionado");
});

/* ---------- rotate ring ---------- */
let ringTimer, ringLeft;
function startRing(){
  clearInterval(ringTimer);
  let r = api("/rotate/status");
  r.then(x=>x.json()).then(j=>{
    let interval = (j.interval)||60; let on = j.on;
    ringLeft = interval;
    drawRing(interval, ringLeft, on);
    $("#rotateSub").textContent = on ? `próxima em ${ringLeft}s · intervalo ${interval}s`
                                     : `pausado · intervalo ${interval}s`;
    if(on) tickRing(interval);
  });
}
function drawRing(interval, left, on){
  const C = 2*Math.PI*52;
  const fg = $("#ringFg"); fg.style.strokeDasharray = C;
  fg.style.strokeDashoffset = on ? C*(1 - left/interval) : C;
  $("#ringNum").textContent = left;
  $("#rotateToggle").classList.toggle("on", on);
  $("#rotateToggle").setAttribute("aria-checked", on);
}
function tickRing(interval){
  clearInterval(ringTimer);
  ringTimer = setInterval(()=>{
    ringLeft--; if(ringLeft<=0) ringLeft = interval;
    drawRing(interval, ringLeft, true);
    $("#rotateSub").textContent = `próxima em ${ringLeft}s · intervalo ${interval}s`;
  }, 1000);
}
$("#rotateToggle") && $("#rotateToggle").addEventListener("click", async ()=>{
  const on = !$("#rotateToggle").classList.contains("on");
  await api("/rotate", {method: on?"POST":"DELETE"});
  toast(on ? "rotação ativada" : "rotação pausada");
  startRing();
});

/* ---------- config ---------- */
$("#saveConfig") && $("#saveConfig").addEventListener("click", async ()=>{
  const body = {interval:+$("#intervalInput").value, socks:+$("#socksInput").value,
    http:+$("#httpInput").value};
  let r = await api("/config", {method:"POST",
    headers:{"Content-Type":"application/json"}, body:JSON.stringify(body)});
  let j = await r.json().catch(()=>({}));
  logEl($("#logConfig"), j.ok?"✓ salvo":"✗ erro", j.ok?"ok":"err");
  toast("configurado");
});
async function loadConfig(){
  let r = await api("/config"); let j = await r.json().catch(()=>({}));
  if(j.interval) $("#intervalInput").value = j.interval;
  if(j.socks) $("#socksInput").value = j.socks;
  if(j.http) $("#httpInput").value = j.http;
}

/* ---------- theme ---------- */
$("#themeBtn").addEventListener("click", ()=>{
  const cur = document.documentElement.dataset.theme;
  document.documentElement.dataset.theme = cur === "dark" ? "light" : "dark";
});

/* ---------- bindings ---------- */
$("#startBtn").addEventListener("click", startTor);
$("#checkBtn").addEventListener("click", ()=>checkIp(false));
$("#checkBtn2") && $("#checkBtn2").addEventListener("click", ()=>checkIp(false));

/* ---------- sair : mata tudo (tor + servidor + pagina) ---------- */
async function doExit(){
  try { await fetch(API+"/exit", {method:"POST"}); } catch(e){}
  // tenta fechar a aba/janela do browser
  setTimeout(()=>{ window.open("", "_self"); window.close();
    // fallback: some o conteudo
    document.body.style.opacity = "0";
    document.body.innerHTML = "<div style='color:#fff;font-family:monospace;padding:40px'>torify encerrado. voce voltou ao seu ip normal.</div>";
  }, 400);
}
$("#exitBtn") && $("#exitBtn").addEventListener("click", doExit);
$("#exitBtn2") && $("#exitBtn2").addEventListener("click", doExit);

/* ---------- boot ---------- */
(async ()=>{
  await loadConfig();
  await refreshStatus();
  await checkIp(true);
})();
