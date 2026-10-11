"use strict";

const $ = (id) => document.getElementById(id);
let state = null;
let tab = "host";
let visibility = "private";
let formFilled = false;
let saveTimer = null;

async function api(path, body) {
  const res = await fetch(path, body === undefined ? {} : {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  const data = await res.json();
  if (!res.ok) throw new Error(data.error || res.statusText);
  return data;
}

function toast(text, isError = false) {
  const t = $("toast");
  t.textContent = text;
  t.className = "toast" + (isError ? " error" : "");
  t.hidden = false;
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => (t.hidden = true), isError ? 6000 : 2500);
}

function escapeHtml(s) {
  return String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}

// ---- settings ---------------------------------------------------------------

function currentSettings() {
  return {
    ...state.settings,
    name: $("name").value,
    character: document.querySelector(".hero.selected")?.dataset.char ?? state.settings.character,
    shareStars: $("opt-stars").checked,
    syncEnemies: $("opt-enemies").checked,
    syncSwitches: $("opt-switches").checked,
  };
}

function saveSoon() {
  clearTimeout(saveTimer);
  saveTimer = setTimeout(async () => {
    try {
      state = await api("/api/settings", currentSettings());
    } catch (e) {
      toast(e.message, true);
    }
  }, 300);
}

function fillForm() {
  const s = state.settings;
  $("name").value = s.name;
  selectHero(s.character);
  $("room-name").value = s.roomName;
  $("room-name").placeholder = `${s.name}'s galaxy`;
  $("max-players").value = String(s.maxPlayers);
  $("port").value = s.port;
  $("opt-stars").checked = s.shareStars;
  $("opt-enemies").checked = s.syncEnemies;
  $("opt-switches").checked = s.syncSwitches;
  $("join-address").value = s.lastJoinAddress;
  formFilled = true;
}

function selectHero(char) {
  document.querySelectorAll(".hero").forEach((h) => {
    const on = h.dataset.char === char;
    h.classList.toggle("selected", on);
    h.setAttribute("aria-checked", on);
  });
}

// ---- rendering --------------------------------------------------------------

function render() {
  if (!formFilled) fillForm();

  const problems = state.environment.problems;
  $("problems").hidden = problems.length === 0 || !state.settings.launchDolphin;
  $("problems").innerHTML = `<strong>Before you can play:</strong><ul>${problems.map((p) => `<li>${escapeHtml(p)}</li>`).join("")}</ul>`;

  const live = state.session;
  $("setup").hidden = !!live;
  $("live").hidden = !live;
  if (live) renderLive(live);
}

function renderLive(v) {
  const pill = $("status-pill");
  pill.className = "pill " + v.status;
  pill.textContent = { online: "Online", connecting: "Connecting", rejected: "Rejected" }[v.status] ?? v.status;

  $("live-title").textContent = v.role === "host" ? v.roomName : `Joined ${v.roomName}`;
  const bits = [v.statusText];
  if (v.role === "host") bits.push(v.visibility === "public" ? "Public room" : "Private room");
  if (v.hasPassword) bits.push("password protected");
  $("live-sub").textContent = bits.join(" · ");

  $("invite-card").hidden = v.role !== "host";
  if (v.role === "host") {
    $("invite-public").textContent = v.publicAddress
      ?? (v.visibility === "public" ? "waiting for the lobby to report it..." : "unknown: send your public IP (search \"what is my ip\") with :" + v.lanAddress.split(":").pop());
    $("invite-lan").textContent = v.lanAddress ?? "";
    $("port-note").textContent = v.portNote ?? "Checking whether the router can open the port...";
  }

  $("players").innerHTML = v.players.length === 0
    ? `<li class="empty">Nobody connected yet.</li>`
    : v.players.map((p) => {
        const cls = p.character === "Luigi" ? "luigi" : p.character === "Mario" ? "mario" : "unknown";
        const letter = p.character === "Luigi" ? "L" : p.character === "Mario" ? "M" : "?";
        return `<li class="player ${cls}">
          <span class="emblem">${letter}</span>
          <span class="player-name">${escapeHtml(p.name)}${p.isYou ? '<span class="tag">you</span>' : ""}${p.isHost ? '<span class="tag">host</span>' : ""}</span>
          <span class="player-where">${escapeHtml(p.character)} · ${escapeHtml(p.where || "not in a galaxy")}</span>
        </li>`;
      }).join("");

  $("fact-dolphin").innerHTML = v.dolphinRunning ? '<span class="yes">running</span>' : '<span class="no">not running</span>';
  $("fact-attached").innerHTML = v.gameAttached ? '<span class="yes">connected to the game</span>' : '<span class="no">waiting for the game</span>';
  $("fact-where").textContent = v.where || "not in a galaxy";

  const log = $("log");
  const atBottom = log.scrollTop + log.clientHeight >= log.scrollHeight - 8;
  log.innerHTML = v.log.map((l) => `<li><span class="t">${l.time}</span><span class="src">${escapeHtml(l.source)}</span>${escapeHtml(l.text)}</li>`).join("");
  if (atBottom) log.scrollTop = log.scrollHeight;
}

async function refresh() {
  try {
    state = await api("/api/state");
    render();
  } catch (e) {
    $("problems").hidden = false;
    $("problems").textContent = "The launcher is not running (start tools\\play.ps1 again).";
  }
}

// ---- rooms ------------------------------------------------------------------

async function loadRooms() {
  const box = $("rooms");
  box.innerHTML = `<p class="empty">Looking for rooms...</p>`;
  try {
    const r = await api("/api/rooms");
    if (r.error) {
      box.innerHTML = `<p class="empty error">${escapeHtml(r.error)}. Check the lobby server in Settings.</p>`;
      return;
    }
    if (r.rooms.length === 0) {
      box.innerHTML = `<p class="empty">No public rooms right now. Host one!</p>`;
      return;
    }
    box.innerHTML = r.rooms.map((room, i) => `
      <div class="room">
        <div>
          <div class="room-name">${room.hasPassword ? "🔒 " : ""}${escapeHtml(room.name)}</div>
          <div class="room-addr">${escapeHtml(room.address)}:${room.port}</div>
        </div>
        <span class="room-count">${room.players}/${room.maxPlayers} players</span>
        <button class="primary small" data-room="${i}">Join</button>
      </div>`).join("");
    box.querySelectorAll("[data-room]").forEach((b) => b.addEventListener("click", () => {
      const room = r.rooms[Number(b.dataset.room)];
      const password = room.hasPassword ? prompt(`Password for ${room.name}`) : "";
      if (password === null) return;
      join(`${room.address}:${room.port}`, password, room.name);
    }));
  } catch (e) {
    box.innerHTML = `<p class="empty error">${escapeHtml(e.message)}</p>`;
  }
}

// ---- actions ----------------------------------------------------------------

async function withBusy(button, fn) {
  button.disabled = true;
  try {
    await fn();
  } catch (e) {
    toast(e.message, true);
  } finally {
    button.disabled = false;
  }
}

async function host() {
  await withBusy($("host-btn"), async () => {
    state = await api("/api/settings", currentSettings());
    state = await api("/api/host", {
      visibility,
      roomName: $("room-name").value,
      password: $("host-password").value,
      maxPlayers: Number($("max-players").value),
      port: Number($("port").value) || state.defaults.port,
    });
    render();
  });
}

async function join(address, password, roomName = null) {
  if (!address.trim()) {
    toast("Enter the host's address, like 203.0.113.7:7777", true);
    return;
  }
  await withBusy($("join-btn"), async () => {
    state = await api("/api/settings", currentSettings());
    state = await api("/api/join", { address, password, roomName });
    render();
  });
}

function wire() {
  $("name").addEventListener("input", saveSoon);
  document.querySelectorAll(".hero").forEach((h) => h.addEventListener("click", () => {
    selectHero(h.dataset.char);
    saveSoon();
  }));
  ["opt-stars", "opt-enemies", "opt-switches"].forEach((id) => $(id).addEventListener("change", saveSoon));

  document.querySelectorAll(".tab").forEach((t) => t.addEventListener("click", () => {
    tab = t.dataset.tab;
    document.querySelectorAll(".tab").forEach((x) => x.classList.toggle("active", x === t));
    $("panel-host").hidden = tab !== "host";
    $("panel-join").hidden = tab !== "join";
    if (tab === "join") loadRooms();
  }));
  document.querySelectorAll(".vis").forEach((v) => v.addEventListener("click", () => {
    visibility = v.dataset.vis;
    document.querySelectorAll(".vis").forEach((x) => {
      x.classList.toggle("active", x === v);
      x.setAttribute("aria-checked", x === v);
    });
  }));

  $("host-btn").addEventListener("click", host);
  $("join-btn").addEventListener("click", () => join($("join-address").value, $("join-password").value));
  $("refresh-rooms").addEventListener("click", loadRooms);

  $("leave").addEventListener("click", () => withBusy($("leave"), async () => {
    state = await api("/api/stop", {});
    render();
  }));
  $("relaunch").addEventListener("click", () => withBusy($("relaunch"), async () => {
    state = await api("/api/dolphin", {});
    render();
  }));
  document.querySelectorAll(".copy").forEach((b) => b.addEventListener("click", async () => {
    const text = $(b.dataset.copy).textContent.trim();
    await navigator.clipboard.writeText(text);
    toast(`Copied ${text}`);
  }));

  $("open-settings").addEventListener("click", () => {
    const s = state.settings;
    const env = state.environment;
    $("set-dolphin").value = s.dolphinDir;
    $("set-dolphin").placeholder = env.dolphinDir ?? "D:\\Games\\Dolphin-x64";
    $("set-image").value = s.gameImage;
    $("set-image").placeholder = env.gameImage ?? "Super Mario Galaxy 2 (USA).iso";
    $("set-lobby").value = s.lobby;
    $("set-launch").checked = s.launchDolphin;
    $("env-facts").innerHTML = `
      <dt>Dolphin</dt><dd>${escapeHtml(env.dolphinDir ?? "not found")}</dd>
      <dt>Game</dt><dd>${escapeHtml(env.gameImage ?? "not found")}</dd>
      <dt>Dolphin data</dt><dd>${escapeHtml(env.userDir ?? "-")}</dd>
      <dt>Patch</dt><dd>${env.patchReady ? "built" : "not built"}</dd>`;
    $("settings").showModal();
  });
  $("settings").addEventListener("close", async () => {
    if ($("settings").returnValue !== "save") return;
    try {
      state = await api("/api/settings", {
        ...currentSettings(),
        dolphinDir: $("set-dolphin").value.trim(),
        gameImage: $("set-image").value.trim(),
        lobby: $("set-lobby").value.trim(),
        launchDolphin: $("set-launch").checked,
      });
      render();
      toast("Settings saved");
    } catch (e) {
      toast(e.message, true);
    }
  });
}

wire();
refresh();
setInterval(refresh, 1000);
