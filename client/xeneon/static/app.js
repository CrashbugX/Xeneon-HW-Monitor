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
const banner = document.getElementById("banner");
const footer = document.getElementById("footer");

/**
 * Scale the 2560x720 stage to the window. Local layout does not wait on the network.
 */
function fitStage() {
  const scale = Math.min(window.innerWidth / 2560, window.innerHeight / 720);
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
  paintTemp("cpu", "CPU · RYZEN 7 5800X3D");
  paintTemp("gpu", "GPU · RTX 2080 SUPER");
  paintLoad();
  paintStats();
  paintCases();
  paintSpotify();
  paintFooter();
}

/** Set the header band from link or demo. Demo is never inferred from a failed poll. */
function paintBanner() {
  banner.textContent = view.banner;
  banner.className = "banner " + (view.demo ? "demo" : view.link);
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
  const list = data.fans && data.fans.length ? data.fans : [{ name: key === "cpu" ? "CPU FAN" : "GPU FAN", rpm: null }];
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
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  ctx.strokeStyle = "#242932";
  ctx.lineWidth = 1;
  for (const tick of [40, 105, 170]) {
    ctx.beginPath();
    ctx.moveTo(0, tick);
    ctx.lineTo(canvas.width - 36, tick);
    ctx.stroke();
  }
  if (!history || history.length < 2) {
    return;
  }
  const low = 50;
  const high = 90;
  ctx.beginPath();
  ctx.strokeStyle = color;
  ctx.lineWidth = 3;
  history.forEach((value, index) => {
    const x = (index / (history.length - 1)) * (canvas.width - 48);
    const y = canvas.height - 20 - ((value - low) / (high - low)) * (canvas.height - 40);
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
  const spot = view.spotify;
  root.innerHTML = "";
  root.className = "panel pad";
  const head = el("div", "spot-head");
  head.append(el("div", "kicker", "SPOTIFY / NOW PLAYING"));
  const macros = el("button", "macros", "MACROS ›");
  macros.disabled = true;
  macros.title = "Macros are not available in this version.";
  head.append(macros);
  root.append(head);
  if (!view.demo) {
    const box = el("div", "paused-box");
    box.append(el("div", "track", "Not connected"));
    box.append(el("div", "sub", "Spotify controls stay off until an account is added. Nothing here is live playback."));
    root.append(box);
  } else if (spot.playing) {
    const now = el("div", "now");
    now.append(el("div", "art", ""));
    const meta = el("div", "");
    meta.append(el("div", "sub", "EXAMPLE TRACK"));
    meta.append(el("div", "track", spot.title || ""));
    meta.append(el("div", "sub", spot.artist || ""));
    meta.append(el("div", "sub", "not connected to Spotify"));
    now.append(meta);
    root.append(now);
  } else {
    const box = el("div", "paused-box");
    box.append(el("div", "track", "Nothing playing"));
    box.append(el("div", "sub", "Paused"));
    root.append(box);
  }
  const controls = el("div", "controls");
  controls.append(actionButton("Previous", false, null));
  if (view.demo && spot.playing) {
    controls.append(actionButton("Pause", true, "pause"));
  } else if (view.demo && !spot.playing) {
    controls.append(actionButton("Play", true, "play"));
  } else {
    controls.append(actionButton("Play", false, null));
  }
  controls.append(actionButton("Next", false, null));
  root.append(controls);
  const toggles = el("div", "toggles");
  toggles.append(actionButton("Shuffle " + spot.shuffle, view.demo, "shuffle"));
  toggles.append(actionButton("Repeat " + spot.repeat, view.demo, "repeat"));
  root.append(toggles);
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
    button.title = "Not available. This build does not send Spotify or macro commands.";
  }
  if (action) {
    button.addEventListener("click", () => postDemo(action));
  }
  return button;
}

/**
 * Ask the local bridge to change demo-only transport state.
 * @param {string} action play, pause, shuffle, or repeat
 * @returns {Promise<void>}
 */
async function postDemo(action) {
  await fetch("/api/demo/spotify", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ action })
  });
  await refreshView();
}

/** Footer explains that colors are display thresholds, not safety limits. */
function paintFooter() {
  const rise = (view.thresholds && view.thresholds.rise || []).join(" / ");
  const fall = (view.thresholds && view.thresholds.fall || []).join(" / ");
  footer.textContent = (view.demo ? "DEMO · " : "") +
    "60 fps drawing · host samples near 1 Hz · display thresholds rise " + rise +
    " °C, cool " + fall + " °C · not safety limits · missing fans stay blank";
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

refreshView();
setInterval(refreshView, 250);
requestAnimationFrame(render);
