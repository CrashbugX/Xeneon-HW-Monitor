/**
 * Dashboard renderer.
 *
 * The bridge serves /api/view about once a second of host data, cached locally.
 * This file draws every animation frame so graphs, bars, and fan icons stay
 * smooth when the host poll is slow or briefly fails. Numbers are the latest
 * raw sample, not the interpolated graph point. Temperature colors come from
 * the bridge (thresholds.py). This file only fades them over 150ms.
 */
"use strict";

/** @type {object | null} */
let view = null;
/** Previous view JSON. DOM is rebuilt only when this changes, so 60 fps does not restart CSS spins. */
let painted = "";
/** Last raw logical percents, used only to ease bar height. */
let barShown = new Array(16).fill(0);

const stage = document.getElementById("stage");
const fit = document.getElementById("fit");
const banner = document.getElementById("banner");
const footer = document.getElementById("footer");

/**
 * Scale the 2560x720 stage to the area under the window bar.
 * Local layout does not wait on the network.
 */
function fitStage() {
  const narrow = window.innerWidth < 1100 || window.innerHeight < 500 || (window.innerHeight > window.innerWidth && window.innerWidth < 1400);
  stage.classList.toggle("compact", narrow);
  if (narrow) {
    stage.style.transform = "none";
    return;
  }
  const width = fit ? fit.clientWidth : window.innerWidth;
  const height = fit ? fit.clientHeight : window.innerHeight;
  const scale = Math.min(width / 2560, height / 720);
  stage.style.transform = "scale(" + scale + ")";
}

/**
 * Pull the latest view. A failed fetch leaves the previous frame on screen.
 * @returns {Promise<void>}
 */
async function refreshView() {
  try {
    const response = await fetch("/api/view", { cache: "no-store" });
    if (!response.ok) {
      return;
    }
    view = await response.json();
    if (view.moduleOrder) {
      moduleOrder = view.moduleOrder.slice();
      applyModuleOrder();
    }
    if (view.theme === "day" || view.theme === "night") {
      document.documentElement.dataset.theme = view.theme;
      const themeButton = document.getElementById("gear-theme");
      if (themeButton) {
        themeButton.textContent = view.theme === "day" ? "Use night mode" : "Use day mode";
      }
    }
    const next = JSON.stringify(view);
    if (next !== painted) {
      painted = next;
      paintAll();
    }
  } catch (_error) {
    /* The shell stays up. The banner already says connecting if we never got a view. */
  }
}

/**
 * Paint one frame from the cached view. Does not read the network.
 * @param {number} nowMs requestAnimationFrame timestamp
 */
function render(_nowMs) {
  fitStage();
  if (view) {
    redrawGraphs();
    easeBars();
  }
  requestAnimationFrame(render);
}

/** Rebuild cards after a new /api/view payload. Not called every frame. */
function paintAll() {
  paintBanner();
  paintWifi();
  paintTemp("cpu", "CPU");
  paintTemp("gpu", "GPU");
  paintLoad();
  paintStats();
  paintCases();
  paintSpotify();
  paintModules();
  paintFooter();
}

/** Hide a module without dropping it from the page silently. */
function paintModules() {
  const modules = (view && view.modules) || {};
  const hidden = [];
  [["cpu", "cpu"], ["gpu", "gpu"], ["load", "load"], ["stats", "memory"], ["cases", "fans"], ["spotify", "spotify"]].forEach((pair) => {
    const node = document.getElementById(pair[0]);
    const on = modules[pair[1]] !== false;
    if (node) {
      node.hidden = !on;
    }
    if (!on) {
      hidden.push(pair[1]);
    }
  });
  const note = document.getElementById("overflow");
  if (!note) {
    return;
  }
  if (hidden.length === 0 && !stage.classList.contains("compact")) {
    note.hidden = true;
    return;
  }
  note.hidden = false;
  note.textContent = hidden.length
    ? "Hidden modules: " + hidden.join(", ") + ". Open Setup, then Layout, to show them. This page scrolls instead of clipping them."
    : "This window is narrower than the 2560×720 stage. Cards stack and the page scrolls so the text stays readable.";
}

/** Set the header band from link or demo. Demo is never inferred from a failed poll. */
function paintBanner() {
  const preview = new URLSearchParams(location.search).get("preview");
  const link = preview === "disconnected" ? "disconnected" : view.link;
  if (view.demo && preview !== "disconnected") {
    banner.hidden = false;
    banner.textContent = view.banner;
    banner.className = "banner demo";
    return;
  }
  if (link === "live") {
    banner.hidden = true;
    return;
  }
  banner.hidden = false;
  banner.textContent = link === "unauthorized" ? "Disconnected from PC · the token was refused" : "Disconnected from PC";
  banner.className = "banner disconnected";
}

/** Wi-Fi block. Unavailable stays explicit. */
function paintWifi() {
  const wifi = view.wifi || {};
  document.getElementById("wifi-ssid").textContent = wifi.ssid || "Wi-Fi unavailable";
  document.getElementById("wifi-detail").textContent = wifi.detail || wifi.status || "unavailable";
  const bars = document.getElementById("wifi-bars");
  bars.replaceChildren();
  for (let i = 1; i <= 4; i += 1) {
    const tick = document.createElement("i");
    tick.style.height = (4 + i * 3) + "px";
    if (wifi.bars != null && i <= wifi.bars) {
      tick.className = "on";
    }
    bars.append(tick);
  }
}

/**
 * Draw one temperature card. The big number is the raw sample.
 * The canvas line may sit between samples; that interpolation is visual only.
 * @param {string} key cpu or gpu
 * @param {string} title hardware label, not a sensor reading
 */
function paintTemp(key, title) {
  const card = document.getElementById(key);
  const data = view[key];
  const level = data.level || "";
  card.className = "temp-card" + (level ? " level-" + level : "");
  const text = data.temp == null ? "—" : String(Math.round(data.temp));
  card.innerHTML = "";
  card.append(el("div", "kicker", title));
  const hero = el("div", "hero", text);
  const unit = el("span", "unit", "°C");
  hero.append(unit);
  card.append(hero);
  card.append(el("div", "pill", level ? "STATE · " + level.toUpperCase() : "NO SAMPLE"));
  const stats = historyStats(data.history);
  card.append(el("div", "sub", stats));
  const canvas = document.createElement("canvas");
  canvas.className = "graph";
  canvas.width = 500;
  canvas.height = 210;
  card.append(canvas);
  drawGraph(canvas, data.history, levelColor(level));
  const fans = el("div", "fans");
  const assigned = ((view && view.fanBoard) || []).filter(function (fan) { return fan.role === key; });
  const list = assigned.length
    ? assigned.map(function (fan) { return { name: fan.label || fan.sensorName || key, rpm: fan.rpm }; })
    : (data.fans && data.fans.length ? data.fans : [{ name: key === "cpu" ? "CPU FAN" : "GPU FAN", rpm: null }]);
  for (const fan of list) {
    fans.append(fanNode(fan));
  }
  card.append(fans);
}

/**
 * @param {number[]} history raw Celsius samples, oldest first
 * @returns {string}
 */
function historyStats(history) {
  if (!history || history.length === 0) {
    return "no fresh samples";
  }
  const min = Math.min(...history);
  const max = Math.max(...history);
  const avg = history.reduce((sum, value) => sum + value, 0) / history.length;
  return "min " + Math.round(min) + " °C · avg " + Math.round(avg) + " °C · max " + Math.round(max) + " °C";
}

/**
 * Stroke the temperature history. Does not invent samples to fill 60 seconds.
 * @param {HTMLCanvasElement} canvas
 * @param {number[]} history
 * @param {string} color
 */
function drawGraph(canvas, history, color) {
  const ctx = canvas.getContext("2d");
  const low = 30;
  const high = 95;
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  const day = document.documentElement.dataset.theme === "day";
  ctx.strokeStyle = day ? "#8ea0b8" : "#9aa6b4";
  ctx.fillStyle = day ? "#10141c" : "#eef3f8";
  ctx.lineWidth = 1;
  ctx.font = "bold 15px sans-serif";
  [95, 60, 30].forEach(function (mark) {
    const y = canvas.height - 18 - ((mark - low) / (high - low)) * (canvas.height - 36);
    ctx.beginPath();
    ctx.moveTo(42, y);
    ctx.lineTo(canvas.width - 8, y);
    ctx.stroke();
    ctx.fillText(String(mark) + "°", 2, y + 5);
  });
  if (!history || history.length < 2) {
    return;
  }
  ctx.beginPath();
  ctx.strokeStyle = color;
  ctx.lineWidth = document.documentElement.dataset.theme === "day" ? 4 : 3;
  history.forEach((value, index) => {
    const clamped = Math.max(low, Math.min(high, value));
    const x = 42 + (index / (history.length - 1)) * (canvas.width - 54);
    const y = canvas.height - 18 - ((clamped - low) / (high - low)) * (canvas.height - 36);
    if (index === 0) {
      ctx.moveTo(x, y);
    } else {
      ctx.lineTo(x, y);
    }
  });
  ctx.stroke();
}

/** @param {string} level */
function levelColor(level) {
  /* Day uses deeper saturated inks so the line stays vivid on a white card. */
  if (document.documentElement.dataset.theme === "day") {
    return { green: "#00a63e", yellow: "#e6a100", orange: "#f25c00", red: "#e10600" }[level] || "#2c3a4d";
  }
  return { green: "#54de7c", yellow: "#f6d434", orange: "#ff8c1a", red: "#ff3a3a" }[level] || "#808996";
}

/**
 * Fan row. rpm null or 0 does not spin. Spin rate is a visual fraction of RPM,
 * not the mechanical revolutions per second.
 * @param {{name: string, rpm: number | null}} fan
 */
function fanNode(fan) {
  const row = el("div", "fan");
  const spinning = typeof fan.rpm === "number" && fan.rpm > 0;
  const seconds = spinning ? Math.max(0.35, 2.2 - (fan.rpm / 2000) * 1.6) : 0;
  row.innerHTML = fanSvg(spinning, seconds);
  row.append(el("span", "", fan.name));
  row.append(el("b", "", spinning || fan.rpm === 0 ? String(Math.round(fan.rpm)) : "—"));
  row.append(el("span", "", spinning || fan.rpm === 0 ? "RPM" : "n/a"));
  return row;
}

/** @param {boolean} spinning @param {number} seconds */
function fanSvg(spinning, seconds) {
  const cls = spinning ? "spin" : "stopped";
  const style = spinning ? " style=\"--spin:" + seconds.toFixed(2) + "s\"" : "";
  return "<svg class=\"" + cls + "\"" + style + " viewBox=\"0 0 24 24\" aria-hidden=\"true\">" +
    "<circle cx=\"12\" cy=\"12\" r=\"9\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.4\"/>" +
    "<path d=\"M12 12 L12 4 A8 8 0 0 1 18 10 Z M12 12 L20 12 A8 8 0 0 1 14 19 Z M12 12 L12 20 A8 8 0 0 1 6 14 Z M12 12 L4 12 A8 8 0 0 1 10 5 Z\" fill=\"currentColor\"/>" +
    "<circle cx=\"12\" cy=\"12\" r=\"2\" fill=\"#13161c\"/></svg>";
}

/** Sixteen thread slots. Missing host rows stay blank. Printed percent is the raw sample. */
function paintLoad() {
  const root = document.getElementById("load");
  root.innerHTML = "";
  root.className = "panel pad";
  const head = el("div", "kicker", "CPU LOAD · 8 CORES / 16 THREADS");
  const total = view.cpu.loadTotal == null ? "—" : Math.round(view.cpu.loadTotal) + " %";
  head.append(el("span", "", "   TOTAL " + total));
  root.append(head);
  const grid = el("div", "threads");
  view.cpu.logical.forEach((value, index) => {
    const cell = el("div", "thread");
    cell.append(el("em", "", "T" + String(index).padStart(2, "0")));
    cell.append(el("strong", "", value == null ? "—" : Math.round(value) + "%"));
    const bar = el("div", "bar");
    bar.style.height = "8px";
    cell.append(bar);
    grid.append(cell);
  });
  root.append(grid);
}

/**
 * Ease bar pixels toward the raw percent. The printed number stays on the raw sample
 * because paintLoad only runs when the payload changes.
 */
function easeBars() {
  document.querySelectorAll("#load .bar").forEach((bar, index) => {
    const value = view.cpu.logical[index];
    const target = value == null ? 0 : Math.max(8, value * 0.7);
    barShown[index] = barShown[index] + (target - barShown[index]) * 0.15;
    bar.style.height = barShown[index] + "px";
  });
}

/** Redraw both temperature lines every frame without touching the hero numbers. */
function redrawGraphs() {
  ["cpu", "gpu"].forEach((key) => {
    const canvas = document.querySelector("#" + key + " canvas");
    if (canvas) {
      drawGraph(canvas, view[key].history, levelColor(view[key].level || ""));
    }
  });
}

/** GPU load, RAM, and VRAM. Denominators come from telemetry, not from a hardcoded 32 or 8. */
function paintStats() {
  const root = document.getElementById("stats");
  root.replaceChildren(
    statCard("GPU LOAD", view.gpu.load, "%", null),
    byteCard("RAM", view.memory),
    byteCard("VRAM", view.vram)
  );
}

/**
 * @param {string} label
 * @param {number | null} value
 * @param {string} unit
 * @param {number | null} fraction
 */
function statCard(label, value, unit, fraction) {
  const card = el("div", "stat");
  card.append(el("div", "kicker", label));
  const big = el("div", "big", value == null ? "—" : String(Math.round(value)));
  big.append(el("span", "of", unit));
  card.append(big);
  const meter = el("div", "meter");
  const fill = document.createElement("span");
  fill.style.width = (fraction == null ? (value == null ? 0 : value) : fraction * 100) + "%";
  meter.append(fill);
  card.append(meter);
  card.append(el("small", "", value == null ? "unavailable" : (view.demo ? "example value" : "from host")));
  return card;
}

/** @param {string} label @param {{usedBytes: number | null, totalBytes: number | null}} pair */
function byteCard(label, pair) {
  const used = pair && pair.usedBytes != null ? pair.usedBytes / (1024 ** 3) : null;
  const total = pair && pair.totalBytes != null ? pair.totalBytes / (1024 ** 3) : null;
  const card = el("div", "stat");
  card.append(el("div", "kicker", label));
  const big = el("div", "big", used == null ? "—" : used.toFixed(1));
  big.append(el("span", "of", total == null ? "/ —" : "/ " + total.toFixed(0) + " GB"));
  card.append(big);
  const meter = el("div", "meter");
  const fill = document.createElement("span");
  fill.style.width = used != null && total ? Math.min(100, (used / total) * 100) + "%" : "0%";
  meter.append(fill);
  card.append(meter);
  const pct = used != null && total ? Math.round((used / total) * 100) + "% used" : "unavailable";
  card.append(el("small", "", view.demo ? pct + " · example" : pct));
  return card;
}

/** Case-fan strip. Only channels the view lists. A null rpm is n/a and does not spin. */
function paintCases() {
  const root = document.getElementById("cases");
  root.innerHTML = "";
  root.className = "panel pad";
  root.append(el("div", "kicker", "CASE FANS"));
  const row = el("div", "fans");
  const fans = view.caseFans || [];
  if (fans.length === 0) {
    row.append(el("span", "", "no case-fan tach mapped"));
  }
  for (const fan of fans) {
    row.append(fanNode(fan));
  }
  root.append(row);
}

/**
 * Spotify column. Live mode keeps every control disabled.
 * Demo pause is offered only while playing, and play only while paused.
 */
function paintSpotify() {
  const root = document.getElementById("spotify");
  let spot = view.spotify || {};
  if (new URLSearchParams(location.search).get("preview") === "paused") {
    spot = Object.assign({}, spot, { playing: false, shuffle: "off", repeat: "off" });
  }
  const playing = !!spot.playing && (view.demo || spot.connected);
  root.innerHTML = "";
  root.className = "panel aa";
  const bg = el("div", "aa-bg");
  const shade = el("div", "aa-shade");
  if (playing && spot.art) {
    const src = String(spot.art).charAt(0) === "/" ? spot.art : ("/art?u=" + encodeURIComponent(spot.art));
    bg.style.backgroundImage = "url(\"" + src + "\")";
    tintFromArt(root, src);
  } else {
    bg.style.background = "linear-gradient(165deg, #2a3544 0%, #151b24 55%, #10141a 100%)";
  }
  root.append(bg);
  root.append(shade);
  const body = el("div", "aa-body");
  body.append(spotifyMark());
  if (!playing) {
    body.append(el("div", "aa-title", view.demo || spot.connected ? "Nothing playing" : "Not connected"));
    body.append(el("div", "aa-artist", view.demo || spot.connected ? "Spotify is idle" : (spot.detail || "Connect Spotify in Setup.")));
  } else {
    body.append(el("div", "aa-title", spot.title || "Unknown title"));
    body.append(el("div", "aa-artist", spot.artist || ""));
    const progress = el("div", "aa-progress");
    const fill = document.createElement("span");
    const ratio = spot.duration ? Math.max(0, Math.min(1, (spot.position || 0) / spot.duration)) : 0;
    fill.style.width = (ratio * 100) + "%";
    progress.append(fill);
    body.append(progress);
  }
  const live = !view.demo && spot.connected;
  const skipOn = view.demo || (live && !!spot.canSkip);
  const main = el("div", "aa-main");
  main.append(aaButton("Previous track", skipOn, live && spot.canSkip ? "previous" : null, iconPrevious(), false, "aa-skip"));
  const showPause = (view.demo && playing) || (live && spot.canPause);
  main.append(aaButton(showPause ? "Pause" : "Play", view.demo || (live && (spot.canPause || spot.canPlay)), showPause ? "pause" : ((view.demo || (live && spot.canPlay)) ? "play" : null), showPause ? iconPause() : iconPlay(), false, "aa-play"));
  main.append(aaButton("Next track", skipOn, live && spot.canSkip ? "next" : null, iconNext(), false, "aa-skip"));
  body.append(main);
  const side = el("div", "aa-side");
  const shuffleOn = spot.shuffle === "on";
  side.append(aaButton(shuffleOn ? "Shuffle on" : "Shuffle off", view.demo || (live && !!spot.canShuffle), (view.demo || live) ? "shuffle" : null, iconShuffle(), shuffleOn, ""));
  const repeat = spot.repeat || "off";
  const repeatLabel = repeat === "one" ? "Repeat one" : (repeat === "all" ? "Repeat all" : "Repeat off");
  side.append(aaButton(repeatLabel, view.demo || (live && !!spot.canRepeat), (view.demo || live) ? "repeat" : null, iconRepeat(repeat), repeat !== "off", ""));
  body.append(side);
  root.append(body);
}

function aaButton(label, enabled, action, svg, active, extra) {
  const button = iconButton(label, enabled, action, svg, active);
  if (extra) {
    button.className += " " + extra;
  }
  return button;
}

function spotifyMark() {
  const mark = el("div", "aa-logo");
  mark.innerHTML = "<svg viewBox=\"0 0 24 24\" role=\"img\" aria-label=\"Spotify\"><circle cx=\"12\" cy=\"12\" r=\"12\" fill=\"#1DB954\"/>" +
    "<path fill=\"#191414\" d=\"M17.4 16.3a.55.55 0 0 1-.76.18c-2.1-1.28-4.75-1.57-7.86-.86a.55.55 0 1 1-.24-1.07c3.4-.78 6.32-.44 8.68.97.24.14.32.45.18.78zm1-2.4a.7.7 0 0 1-.96.23c-2.4-1.48-6.06-1.91-8.9-1.04a.7.7 0 0 1-.4-1.34c3.26-1 7.36-.5 10.03 1.2.3.18.4.58.23.95zm.1-2.5c-2.9-1.72-7.7-1.88-10.47-1.04a.84.84 0 1 1-.48-1.61c3.2-.96 8.52-.77 11.88 1.2a.84.84 0 0 1-.93 1.45z\"/></svg>";
  return mark;
}

function tintFromArt(root, src) {
  const image = new Image();
  image.onload = function () {
    try {
      const sample = document.createElement("canvas");
      sample.width = 4;
      sample.height = 4;
      const ctx = sample.getContext("2d");
      ctx.drawImage(image, 0, 0, 4, 4);
      const data = ctx.getImageData(0, 0, 4, 4).data;
      let r = 0;
      let g = 0;
      let b = 0;
      const pixels = data.length / 4;
      for (let i = 0; i < data.length; i += 4) {
        r += data[i];
        g += data[i + 1];
        b += data[i + 2];
      }
      root.style.background = "rgb(" + Math.round(r / pixels * 0.45) + "," + Math.round(g / pixels * 0.45) + "," + Math.round(b / pixels * 0.45) + ")";
    } catch (_error) {
      /* A tainted canvas leaves the darkened fallback. */
    }
  };
  image.src = src;
}

function iconButton(label, enabled, action, svg, active) {
  const button = document.createElement("button");
  button.className = "transport" + (enabled ? " primary" : "") + (active ? " on" : "");
  button.type = "button";
  button.setAttribute("aria-label", label);
  button.title = label;
  button.innerHTML = svg;
  button.disabled = !enabled;
  if (action && enabled) {
    button.addEventListener("click", () => postAction(action));
  }
  return button;
}

function iconPrevious() { return '<svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M6 6h2v12H6zm3.5 6 8.5 6V6z"/></svg>'; }
function iconNext() { return '<svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M16 6h2v12h-2zM6 18l8.5-6L6 6z"/></svg>'; }
function iconPlay() { return '<svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M8 5v14l11-7z"/></svg>'; }
function iconPause() { return '<svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M6 5h4v14H6zm8 0h4v14h-4z"/></svg>'; }
function iconShuffle() { return '<svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M14 4h6v6h-2V7.4l-3.3 3.3-1.4-1.4L16.6 6H14V4zM4 6h4.6l3.2 3.2-1.4 1.4L7.2 8H4V6zm8.8 7.4 1.4 1.4L11 18h2.6v2H8v-6h2v2.6l2.8-2.8zM16.6 16l3.3-3.3 1.4 1.4L18 17.4V20h-2v-6h2v2.6z"/></svg>'; }
function iconRepeat(mode) {
  const one = mode === "one" ? '<text x="9" y="16" font-size="8" fill="currentColor">1</text>' : "";
  return '<svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M7 7h10V4l4 4-4 4V9H7v3L3 8l4-4v3zm10 10H7v3l-4-4 4-4v3h10v-3l4 4-4 4v-3z"/>' + one + "</svg>";
}

/**
 * @param {string} label
 * @param {boolean} enabled
 * @param {string | null} action
 */
function actionButton(label, enabled, action) {
  const button = document.createElement("button");
  button.className = "transport" + (enabled ? " primary" : "");
  button.type = "button";
  button.textContent = label;
  button.disabled = !enabled;
  if (!enabled) {
    button.title = "Not available in this playback state.";
  }
  if (action && enabled) {
    button.addEventListener("click", () => postAction(action));
  }
  return button;
}

/**
 * Ask the local bridge to change demo-only transport state.
 * @param {string} action play, pause, shuffle, or repeat
 * @returns {Promise<void>}
 */
let setupToken = "";

async function postAction(action) {
  const path = view && view.demo ? "/api/demo/spotify" : "/api/spotify/action";
  await postSetup(path, { action });
  await refreshView();
}

async function postSetup(path, body) {
  if (!setupToken) {
    await loadSetup();
  }
  await fetch(path, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Xeneon-Setup": setupToken },
    body: JSON.stringify(body)
  });
}

async function loadSetup() {
  const response = await fetch("/api/setup", { cache: "no-store" });
  if (!response.ok) {
    return null;
  }
  const payload = await response.json();
  setupToken = payload.csrf || "";
  return payload;
}

function openSetup() {
  document.getElementById("setup").hidden = false;
  paintSetup();
}

async function paintSetup() {
  const payload = await loadSetup();
  if (!payload) {
    return;
  }
  document.getElementById("setup-redirect").textContent = payload.spotifyRedirectUri || "";
  const clientId = document.getElementById("setup-client-id");
  if (clientId && payload.spotifyClientId) {
    clientId.value = payload.spotifyClientId;
  }
  document.documentElement.dataset.theme = payload.theme === "day" ? "day" : "night";
  const themeButton = document.getElementById("gear-theme");
  if (themeButton) {
    themeButton.textContent = payload.theme === "day" ? "Use night mode" : "Use day mode";
  }
  paintFans(payload.fans || []);
  const account = document.getElementById("spotify-account");
  const connect = document.getElementById("setup-spotify");
  const logout = document.getElementById("setup-spotify-out");
  if (account && connect && logout) {
    if (payload.spotifyConnected) {
      account.textContent = payload.spotifyAccount ? ("Connected as " + payload.spotifyAccount) : "Connected.";
      connect.hidden = true;
      logout.hidden = false;
    } else {
      account.textContent = "Not connected.";
      connect.hidden = false;
      logout.hidden = true;
    }
  }
  paintLayout(payload.modules || {}, payload.moduleOrder || []);
  document.getElementById("setup-status").textContent = payload.demo
    ? "Demo is on. Pairing and Spotify login stay available, and demo numbers stay labelled."
    : (payload.paired ? "Paired with " + (payload.hostUrl || "the host") + "." : "Not paired yet.");
  const hosts = document.getElementById("setup-hosts");
  hosts.replaceChildren();
  (payload.hosts || []).forEach((host) => {
    const button = el("button", "transport", (host.name || "Host") + " · " + (host.baseUrl || ""));
    button.type = "button";
    button.addEventListener("click", () => {
      document.getElementById("setup-address").value = host.baseUrl || "";
    });
    hosts.append(button);
  });
  ["cpu", "gpu", "load", "memory", "fans", "spotify"].forEach((name) => {
    const box = document.getElementById("mod-" + name);
    if (box) {
      box.checked = !payload.modules || payload.modules[name] !== false;
    }
  });
}

async function discoverHosts() {
  await postSetup("/api/setup/discover", {
    address: document.getElementById("setup-manual").value,
    port: Number(document.getElementById("setup-port").value || 8765)
  });
  await paintSetup();
}

async function requestPairing() {
  document.getElementById("setup-code").value = "";
  await sendPair("");
}

async function pairHost() {
  await sendPair(document.getElementById("setup-code").value);
}

// Request code posts the selected host only. The manual address is not read here.
async function sendPair(code) {
  if (!setupToken) {
    await loadSetup();
  }
  const result = await fetch("/api/setup/pair", {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Xeneon-Setup": setupToken },
    body: JSON.stringify({
      baseUrl: document.getElementById("setup-address").value,
      code: code
    })
  });
  const payload = await result.json();
  document.getElementById("setup-status").textContent = payload.detail || payload.error || (payload.step === "paired" ? "Paired. The token stays on this Pi." : "Approve the code on Windows, then submit it.");
}

async function saveLayout() {
  const modules = {};
  ["cpu", "gpu", "load", "memory", "fans", "spotify"].forEach((name) => {
    modules[name] = document.getElementById("mod-" + name).checked;
  });
  await postSetup("/api/setup/layout", { modules });
  await refreshView();
}

async function saveSpotifyId() {
  await postSetup("/api/setup/spotify-id", { clientId: document.getElementById("setup-client-id").value });
  await paintSetup();
}

async function connectSpotify() {
  if (!setupToken) {
    await loadSetup();
  }
  const response = await fetch("/api/setup/spotify-begin", {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Xeneon-Setup": setupToken },
    body: "{}"
  });
  const payload = await response.json();
  if (payload.url) {
    window.location.href = payload.url;
    return;
  }
  document.getElementById("setup-status").textContent = payload.detail || "Add the Client ID first. Do not paste a client secret.";
}

async function logoutSpotify() {
  await postSetup("/api/setup/spotify-logout", {});
  await paintSetup();
  await refreshView();
}

/** Footer explains that colors are display thresholds, not safety limits. */
function paintFooter() {
  /* The bottom-left status line was removed. Thresholds stay in the code, not on the stage. */
}

/**
 * @param {string} tag
 * @param {string} className
 * @param {string} text
 */
function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) {
    node.className = className;
  }
  if (text) {
    node.append(document.createTextNode(text));
  }
  return node;
}

/** Launcher-owned window state. Not document.fullscreenElement. */
let windowState = {
  enabled: false,
  closeEnabled: false,
  pending: true,
  label: "Fullscreen",
  action: null,
  detail: ""
};
/** True from the click until a later read shows the launcher finished. */
let controlsLocked = true;
/** True while the POST is still on the wire. A poll must not unlock during it. */
let postInFlight = false;

function paintWindowControls() {
  const locked = controlsLocked || windowState.pending;
  const toggle = document.getElementById("gear-window");
  if (toggle) {
    const full = !!document.fullscreenElement;
    toggle.textContent = locked ? "Switching…" : (full ? "Exit fullscreen" : "Fullscreen");
    toggle.disabled = locked;
    toggle.dataset.action = full ? "windowed" : "fullscreen";
  }
  const close = document.getElementById("gear-close");
  if (close) {
    close.disabled = locked || !windowState.closeEnabled;
  }
  const note = document.getElementById("win-note");
  if (note && windowState.detail) {
    note.hidden = false;
    note.textContent = windowState.detail;
  }
}

/** Read the launcher ack. A POST response is not enough to unlock the buttons. */
async function refreshWindow() {
  try {
    const response = await fetch("/api/window", { cache: "no-store" });
    if (!response.ok) {
      return;
    }
    windowState = await response.json();
    if (!windowState.pending && !postInFlight) {
      controlsLocked = false;
    }
  } catch (_error) {
    windowState.detail = "The launcher state could not be read. The buttons stay off until it answers.";
  }
  paintWindowControls();
}

async function postWindow(action) {
  if (!setupToken) {
    await loadSetup();
  }
  const response = await fetch("/api/window", {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Xeneon-Setup": setupToken },
    body: JSON.stringify({ action: action })
  });
  return response.json();
}

async function waitForLauncher() {
  const deadline = Date.now() + 12000;
  while (Date.now() < deadline) {
    await refreshWindow();
    if (!windowState.pending) {
      return;
    }
    await new Promise(function (resolve) { setTimeout(resolve, 300); });
  }
  await refreshWindow();
  if (windowState.pending) {
    windowState.detail = "The window change did not finish. The buttons stay off until the launcher reports the mode it actually started.";
    paintWindowControls();
  }
}

async function windowAction(action) {
  if (controlsLocked || windowState.pending || postInFlight) {
    return;
  }
  if (action === "close") {
    if (!windowState.closeEnabled) {
      return;
    }
    postInFlight = true;
    controlsLocked = true;
    windowState.pending = true;
    windowState.label = "Switching…";
    paintWindowControls();
    try {
      const payload = await postWindow(action);
      if (payload && payload.detail) {
        windowState.detail = payload.detail;
        paintWindowControls();
      }
    } catch (_error) {
      windowState.detail = "The launcher did not answer. The buttons stay off until the current change finishes or times out.";
      paintWindowControls();
    } finally {
      postInFlight = false;
    }
    await waitForLauncher();
    return;
  }
  controlsLocked = true;
  windowState.label = "Switching…";
  paintWindowControls();
  const wantFull = action === "fullscreen";
  let changed = false;
  try {
    if (wantFull) {
      await document.documentElement.requestFullscreen();
    } else if (document.fullscreenElement) {
      await document.exitFullscreen();
    }
  } catch (_error) {
    changed = false;
  }
  await new Promise(function (resolve) { setTimeout(resolve, 400); });
  changed = !!document.fullscreenElement === wantFull;
  if (!changed) {
    try {
      const payload = await postWindowFallback(action);
      if (payload && payload.detail) {
        windowState.detail = payload.detail;
      }
    } catch (_error) {
      windowState.detail = "The fullscreen fallback did not answer.";
    }
    await new Promise(function (resolve) { setTimeout(resolve, 800); });
    changed = !!document.fullscreenElement === wantFull;
  } else {
    postWindowFallback(action, false);
  }
  controlsLocked = false;
  windowState.pending = false;
  windowState.label = wantFull && changed ? "Exit fullscreen" : "Fullscreen";
  windowState.action = wantFull && changed ? "windowed" : "fullscreen";
  windowState.enabled = true;
  if (!changed) {
    windowState.detail = (windowState.detail || "") + " The window did not change. Chromium was not relaunched. See window.log on the Pi.";
  }
  paintWindowControls();
}

async function postWindowFallback(action, needsFallback) {
  if (!setupToken) {
    await loadSetup();
  }
  const response = await fetch("/api/window", {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Xeneon-Setup": setupToken },
    body: JSON.stringify({ action: action, needsFallback: needsFallback !== false })
  });
  return response.json();
}

const MODULES = [["cpu", "CPU"], ["gpu", "GPU"], ["load", "Load"], ["memory", "Memory"], ["fans", "Fans"], ["spotify", "Spotify"]];
let moduleOrder = MODULES.map(function (item) { return item[0]; });

function paintLayout(modules, order) {
  const board = document.getElementById("layout-board");
  if (!board) {
    return;
  }
  const known = MODULES.map(function (item) { return item[0]; });
  moduleOrder = (order || []).filter(function (id) { return known.indexOf(id) >= 0; });
  known.forEach(function (id) {
    if (moduleOrder.indexOf(id) < 0 && (!modules || modules[id] !== false)) {
      moduleOrder.push(id);
    }
  });
  board.replaceChildren();
  moduleOrder.forEach(function (id) {
    if (modules && modules[id] === false) {
      return;
    }
    const row = el("div", "layout-item");
    row.dataset.id = id;
    const named = MODULES.filter(function (item) { return item[0] === id; })[0];
    const label = el("span", "");
    label.append(el("span", "grip", "⋮⋮"));
    label.append(document.createTextNode(named ? named[1] : id));
    row.append(label);
    const remove = document.createElement("button");
    remove.type = "button";
    remove.textContent = "Remove";
    remove.addEventListener("click", function (event) {
      event.stopPropagation();
      setModule(id, false);
    });
    row.append(remove);
    row.addEventListener("pointerdown", layoutPointerDown);
    board.append(row);
  });
  const hidden = document.getElementById("layout-hidden");
  const hiddenPairs = MODULES.filter(function (pair) { return modules && modules[pair[0]] === false; });
  if (hidden) {
    hidden.replaceChildren();
    hiddenPairs.forEach(function (pair) {
      const add = document.createElement("button");
      add.type = "button";
      add.textContent = "Add " + pair[1];
      add.addEventListener("click", function () { setModule(pair[0], true); });
      hidden.append(add);
    });
  }
  const addButton = document.getElementById("layout-add");
  if (addButton) {
    addButton.disabled = hiddenPairs.length === 0;
    addButton.textContent = hiddenPairs.length ? "Add module" : "All modules are on the board";
    addButton.onclick = function () {
      if (hiddenPairs.length === 1) {
        setModule(hiddenPairs[0][0], true);
      } else if (hidden) {
        hidden.scrollIntoView({ block: "nearest" });
      }
    };
  }
  applyModuleOrder();
}

function layoutPointerDown(event) {
  if (event.target.closest("button")) {
    return;
  }
  const row = event.currentTarget;
  row.setPointerCapture(event.pointerId);
  row.classList.add("drag");
  function move(ev) {
    const board = row.parentElement;
    const items = Array.prototype.slice.call(board.children);
    const index = items.indexOf(row);
    const previous = items[index - 1];
    const next = items[index + 1];
    if (previous && ev.clientY < previous.getBoundingClientRect().top + previous.offsetHeight / 2) {
      board.insertBefore(row, previous);
    } else if (next && ev.clientY > next.getBoundingClientRect().top + next.offsetHeight / 2) {
      board.insertBefore(next, row);
    }
  }
  function up() {
    row.classList.remove("drag");
    row.removeEventListener("pointermove", move);
    row.removeEventListener("pointerup", up);
    moduleOrder = Array.prototype.map.call(row.parentElement.children, function (item) { return item.dataset.id; });
    applyModuleOrder();
    postSetup("/api/setup/layout", { order: moduleOrder, modules: currentModules() });
  }
  row.addEventListener("pointermove", move);
  row.addEventListener("pointerup", up);
}

function currentModules() {
  const modules = {};
  MODULES.forEach(function (pair) {
    modules[pair[0]] = moduleOrder.indexOf(pair[0]) >= 0;
  });
  return modules;
}

function setModule(id, on) {
  if (on && moduleOrder.indexOf(id) < 0) {
    moduleOrder.push(id);
  }
  if (!on) {
    moduleOrder = moduleOrder.filter(function (item) { return item !== id; });
  }
  const modules = currentModules();
  if (!on) {
    modules[id] = false;
  }
  postSetup("/api/setup/layout", { order: moduleOrder, modules: modules }).then(paintSetup);
}

function applyModuleOrder() {
  const defaults = MODULES.map(function (item) { return item[0]; });
  stage.classList.toggle("custom-order", moduleOrder.join() !== defaults.join());
  const map = { memory: "stats", fans: "cases", load: "load" };
  moduleOrder.forEach(function (id, index) {
    const node = document.getElementById(map[id] || id);
    if (node) {
      node.style.order = String(index);
      node.hidden = false;
    }
  });
  MODULES.forEach(function (pair) {
    if (moduleOrder.indexOf(pair[0]) < 0) {
      const node = document.getElementById(map[pair[0]] || pair[0]);
      if (node) {
        node.hidden = true;
      }
    }
  });
}

function paintFans(fans) {
  const list = document.getElementById("fan-list");
  if (!list) {
    return;
  }
  list.replaceChildren();
  if (!fans.length) {
    list.append(el("p", "", "No fan tachometers in the latest host inventory."));
    return;
  }
  fans.forEach(function (fan) {
    const row = el("div", "fan-row");
    const title = fan.label || fan.sensorName || "Fan";
    row.append(el("strong", "", title));
    row.append(el("div", "sub", (fan.hardwareName || "Unknown hardware") + " · " + fan.id));
    const rpm = fan.rpm == null ? "no sample" : Math.round(fan.rpm) + " RPM";
    const range = fan.samples ? ("seen " + (fan.minRpm == null ? "—" : Math.round(fan.minRpm)) + "–" + (fan.maxRpm == null ? "—" : Math.round(fan.maxRpm)) + " RPM over " + fan.samples + " samples") : "no observed range yet";
    row.append(el("div", "", rpm + " · " + range + ". Not a physical limit."));
    const role = document.createElement("select");
    ["unknown", "cpu", "gpu", "case", "other"].forEach(function (name) {
      const option = document.createElement("option");
      option.value = name;
      option.textContent = name;
      if (fan.role === name) {
        option.selected = true;
      }
      role.append(option);
    });
    const label = document.createElement("input");
    label.value = fan.label || "";
    label.maxLength = 40;
    label.setAttribute("aria-label", "Name for " + fan.id);
    const save = document.createElement("button");
    save.type = "button";
    save.textContent = "Save";
    save.addEventListener("click", function () {
      postSetup("/api/setup/fan", { id: fan.id, op: "save", label: label.value, role: role.value });
    });
    const reset = document.createElement("button");
    reset.type = "button";
    reset.textContent = "Reset range";
    reset.addEventListener("click", function () { postSetup("/api/setup/fan", { id: fan.id, op: "reset" }).then(paintSetup); });
    const clear = document.createElement("button");
    clear.type = "button";
    clear.textContent = "Clear range";
    clear.addEventListener("click", function () { postSetup("/api/setup/fan", { id: fan.id, op: "clear" }).then(paintSetup); });
    row.append(label);
    row.append(role);
    row.append(save);
    row.append(reset);
    row.append(clear);
    list.append(row);
  });
}

async function toggleTheme() {
  const next = document.documentElement.dataset.theme === "day" ? "night" : "day";
  await postSetup("/api/setup/theme", { theme: next });
  await paintSetup();
}

async function toggleKeyboard() {
  if (!setupToken) {
    await loadSetup();
  }
  const response = await fetch("/api/keyboard", {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Xeneon-Setup": setupToken },
    body: JSON.stringify({ action: "toggle" })
  });
  const payload = await response.json();
  const note = document.getElementById("win-note");
  const menu = document.getElementById("gear-menu");
  if (menu) {
    menu.hidden = false;
  }
  if (note) {
    note.hidden = false;
    note.textContent = payload.detail || "The keyboard did not change.";
  }
  document.getElementById("osk").setAttribute("aria-expanded", payload.visible ? "true" : "false");
}

document.getElementById("gear").addEventListener("click", function () {
  const menu = document.getElementById("gear-menu");
  menu.hidden = !menu.hidden;
  document.getElementById("gear").setAttribute("aria-expanded", menu.hidden ? "false" : "true");
});
document.getElementById("gear-window").addEventListener("click", function () {
  const toggle = document.getElementById("gear-window");
  windowAction(toggle.dataset.action || "fullscreen");
});
document.getElementById("gear-close").addEventListener("click", function () { windowAction("close"); });
document.getElementById("gear-setup").addEventListener("click", function () {
  document.getElementById("gear-menu").hidden = true;
  openSetup();
});
document.getElementById("gear-theme").addEventListener("click", toggleTheme);
document.getElementById("osk").addEventListener("click", toggleKeyboard);
refreshWindow();
setInterval(refreshWindow, 1000);
document.getElementById("setup").addEventListener("click", function (event) {
  if (event.target.id === "setup") {
    document.getElementById("setup").hidden = true;
  }
});
document.getElementById("setup-use-manual").addEventListener("click", function () {
  const address = document.getElementById("setup-manual").value.trim();
  const port = document.getElementById("setup-port").value.trim() || "8765";
  if (!address) {
    return;
  }
  document.getElementById("setup-address").value = "http://" + address + ":" + port;
});
document.querySelectorAll("[data-host-tab]").forEach(function (button) {
  button.addEventListener("click", function () {
    const tab = button.getAttribute("data-host-tab");
    document.querySelectorAll("[data-host-tab]").forEach(function (item) {
      item.setAttribute("aria-selected", item === button ? "true" : "false");
    });
    document.querySelectorAll("[data-host-panel]").forEach(function (panel) {
      panel.hidden = panel.getAttribute("data-host-panel") !== tab;
    });
  });
});
document.getElementById("setup-discover").addEventListener("click", discoverHosts);
document.getElementById("setup-request").addEventListener("click", requestPairing);
document.getElementById("setup-pair").addEventListener("click", pairHost);
document.getElementById("setup-spotify").addEventListener("click", connectSpotify);
const saveClientId = document.getElementById("setup-save-id");
if (saveClientId) {
  saveClientId.addEventListener("click", saveSpotifyId);
}
document.getElementById("setup-spotify-out").addEventListener("click", logoutSpotify);
document.querySelectorAll("#setup nav button").forEach((button) => {
  button.addEventListener("click", () => {
    const tab = button.getAttribute("data-tab");
    document.querySelectorAll("#setup nav button").forEach(function (item) {
      item.setAttribute("aria-selected", item === button ? "true" : "false");
    });
    document.querySelectorAll("#setup [data-panel]").forEach((panel) => {
      panel.hidden = panel.getAttribute("data-panel") !== tab;
    });
  });
});
refreshView();
setInterval(refreshView, 250);
requestAnimationFrame(render);
if (location.search.indexOf("gear=1") >= 0) {
  document.getElementById("gear-menu").hidden = false;
}
if (location.search.indexOf("setup=1") >= 0) {
  openSetup();
  const tab = new URLSearchParams(location.search).get("tab");
  if (tab) {
    document.querySelectorAll("#setup nav button").forEach(function (item) {
      item.setAttribute("aria-selected", item.getAttribute("data-tab") === tab ? "true" : "false");
    });
    document.querySelectorAll("#setup [data-panel]").forEach((panel) => {
      panel.hidden = panel.getAttribute("data-panel") !== tab;
    });
  }
  const hostTab = new URLSearchParams(location.search).get("host");
  if (hostTab) {
    const button = document.querySelector("[data-host-tab=\"" + hostTab + "\"]");
    if (button) {
      button.click();
    }
  }
}
