/* ════════════════════════════════════════════════
   TORIFY WEB UI — client
   talks to local backend at http://localhost:8899
   ════════════════════════════════════════════════ */
const API = "http://localhost:8899";

const $ = (s) => document.querySelector(s);
const $$ = (s) => document.querySelectorAll(s);

async function api(path, opts) {
  try {
    const r = await fetch(API + path, opts);
    if (!r.ok) throw new Error("http " + r.status);
    return await r.json();
  } catch (e) {
    return { ok: false, error: e.message };
  }
}

/* ── theme ── */
const root = document.documentElement;
$("#themeToggle").addEventListener("click", () => {
  root.dataset.theme = root.dataset.theme === "dark" ? "light" : "dark";
});

/* ── nav ── */
$$(".nav-item").forEach((b) => {
  b.addEventListener("click", () => {
    $$(".nav-item").forEach((n) => n.classList.remove("active"));
    b.classList.add("active");
    const v = b.dataset.view;
    $$(".view").forEach((vw) => vw.classList.remove("active"));
    $(`.view[data-view="${v}"]`).classList.add("active");
  });
});

/* ── status ── */
async function refreshStatus() {
  const r = await api("/status");
  const el = $("#status");
  if (r.ok && r.online) {
    el.classList.add("online");
    $(".status-label").textContent = "connected";
  } else {
    el.classList.remove("online");
    $(".status-label").textContent = "offline";
  }
}

/* ── start ── */
$("#startBtn").addEventListener("click", async () => {
  const btn = $("#startBtn");
  btn.textContent = "starting…";
  btn.disabled = true;
  const r = await api("/start", { method: "POST" });
  btn.disabled = false;
  btn.textContent = "start tor";
  $("#startHint").textContent = r.ok ? "tor booted. route is live." : "failed: " + (r.error || "unknown");
  refreshStatus();
  showCheck();
});

/* ── check ip ── */
function setIp(elId, val) {
  const el = $(elId);
  el.textContent = val;
  el.classList.remove("flash");
  void el.offsetWidth;
  el.classList.add("flash");
}
async function showCheck() {
  const r = await api("/ip");
  if (r.ok) {
    setIp("#realIp", r.real || "failed");
    setIp("#torIp", r.tor || "offline");
  }
}
$("#refreshBtn").addEventListener("click", showCheck);

/* ── config ── */
$("#saveBtn").addEventListener("click", async () => {
  const p = $("#pathField").value.trim();
  const r = await api("/config", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ path: p }) });
  $("#configHint").textContent = r.ok ? "saved: " + p : "not found or invalid";
});
$("#autoBtn").addEventListener("click", async () => {
  const r = await api("/config/auto", { method: "POST" });
  if (r.ok) { $("#pathField").value = r.path; $("#configHint").textContent = "detected: " + r.path; }
  else $("#configHint").textContent = "nothing detected";
});

/* ── apps ── */
async function refreshApps() {
  const r = await api("/apps");
  const list = $("#appList");
  list.innerHTML = "";
  if (!r.ok || !r.apps || r.apps.length === 0) {
    list.innerHTML = '<li class="app-row empty">no apps saved</li>';
    return;
  }
  r.apps.forEach((a, i) => {
    const li = document.createElement("li");
    li.className = "app-row" + (a.active ? " active" : "");
    li.innerHTML = `<span>${a.name}</span><span class="state">${a.active ? "active" : "idle"}</span>`;
    li.dataset.idx = i;
    li.addEventListener("click", () => { $$(".app-row").forEach((x) => x.style.background = ""); li.style.background = "var(--glow)"; selectedApp = i; });
    list.appendChild(li);
  });
}
let selectedApp = -1;
$("#addBtn").addEventListener("click", async () => {
  const p = prompt("executable path to route via tor:");
  if (!p) return;
  const r = await api("/apps/add", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ path: p }) });
  if (r.ok) { await api("/apps/open", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ path: p }) }); refreshApps(); }
});
$("#openBtn").addEventListener("click", async () => {
  const r = await api("/apps");
  if (!r.ok || !r.apps || r.apps.length === 0) return;
  const idx = selectedApp >= 0 ? selectedApp : 0;
  await api("/apps/open", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ path: r.apps[idx].path }) });
  refreshApps();
});

/* ── rotate ── */
let rotating = false;
$("#rotateToggle").addEventListener("click", async () => {
  rotating = !rotating;
  $("#rotateToggle").classList.toggle("on", rotating);
  $("#rotateToggle").setAttribute("aria-checked", rotating);
  if (rotating) {
    await api("/rotate/on", { method: "POST" });
    rotateTick();
  } else {
    await api("/rotate/off", { method: "POST" });
    $("#rotateSub").textContent = "next in — · interval 60s";
    $("#rotateHint").textContent = "off.";
  }
});
async function rotateTick() {
  if (!rotating) return;
  const r = await api("/rotate/status");
  if (r.ok) $("#rotateSub").textContent = `next in ${r.countdown}s · interval ${r.interval}s`;
  setTimeout(rotateTick, 1000);
}

/* ── stop ── */
$("#stopBtn").addEventListener("click", async () => {
  const r = await api("/stop", { method: "POST" });
  $("#stopHint").textContent = r.ok ? "tor stopped." : "nothing to stop.";
  refreshStatus();
});

/* ── boot ── */
refreshStatus();
showCheck();
refreshApps();
setInterval(refreshStatus, 4000);
setInterval(() => { if ($('.view[data-view="check"]').classList.contains("active")) showCheck(); }, 5000);
