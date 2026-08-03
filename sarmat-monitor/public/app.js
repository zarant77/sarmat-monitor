const dash = "—";
const table = document.querySelector("#stations");
const connection = document.querySelector("#connection");
const connectionDot = document.querySelector("#connection-dot");
const login = document.querySelector("#login");
const dashboard = document.querySelector("#dashboard");
let secret = sessionStorage.getItem("sarmat-secret") ?? "";
let refreshTimer;
const dashboardHeader = document.querySelector("#dashboard-header");
const showHeaderButton = document.querySelector("#show-header");
const columnsButton = document.querySelector("#columns-button");
const columnsMenu = document.querySelector("#columns-menu");
const columnsList = document.querySelector("#columns-list");
const columns = [
  ["station", "Станція"], ["status", "Статус"], ["voltage", "Напруга"],
  ["current", "Струм"], ["satellites", "Sat"], ["hdop", "HDOP"],
  ["heading", "Азимут"], ["altitude", "Висота"], ["link", "Зв'язок"], ["obs", "OBS"],
];
const columnNames = new Map(columns);
let columnPreferences = loadColumnPreferences();
let draggedColumn = null;

const authHeaders = () => ({ Authorization: `Bearer ${secret}` });
const number = (value, digits, suffix = "") => value == null ? dash : `${value.toFixed(digits)}${suffix}`;
function cell(value, className = "", column = "") {
  const element = document.createElement("td");
  element.textContent = value;
  if (className) element.className = className;
  if (column) {
    element.dataset.column = column;
    element.dataset.label = columnNames.get(column);
  }
  return element;
}

function loadColumnPreferences() {
  let saved = {};
  try { saved = JSON.parse(localStorage.getItem("sarmat-columns") ?? "{}"); } catch { /* use defaults */ }
  const valid = new Set(columns.map(([key]) => key));
  const savedOrder = Array.isArray(saved.order) ? saved.order.filter((key) => valid.has(key) && key !== "station") : [];
  const order = ["station", ...savedOrder, ...columns.map(([key]) => key).filter((key) => key !== "station" && !savedOrder.includes(key))];
  const hidden = Array.isArray(saved.hidden) ? saved.hidden.filter((key) => valid.has(key) && key !== "station") : [];
  return { order, hidden };
}

function saveColumnPreferences() {
  localStorage.setItem("sarmat-columns", JSON.stringify(columnPreferences));
}

function moveColumn(draggedKey, targetKey, after) {
  if (!draggedKey || draggedKey === targetKey || draggedKey === "station" || targetKey === "station") return false;
  const order = columnPreferences.order.filter((value) => value !== draggedKey);
  const targetIndex = order.indexOf(targetKey);
  order.splice(targetIndex + (after ? 1 : 0), 0, draggedKey);
  columnPreferences.order = order;
  return true;
}

function applyColumnPreferences() {
  const hidden = new Set(columnPreferences.hidden);
  const headerRow = document.querySelector("thead tr");
  const headers = new Map([...headerRow.children].map((element) => [element.dataset.column, element]));
  for (const key of columnPreferences.order) {
    const header = headers.get(key);
    if (header) { header.hidden = hidden.has(key); headerRow.append(header); }
  }
  for (const row of table.rows) {
    if (row.querySelector(".empty")) continue;
    const cells = new Map([...row.cells].map((element) => [element.dataset.column, element]));
    for (const key of columnPreferences.order) {
      const element = cells.get(key);
      if (element) { element.hidden = hidden.has(key); row.append(element); }
    }
  }
}

function renderColumnsMenu() {
  columnsList.replaceChildren();
  for (const key of columnPreferences.order) {
    const item = document.createElement("li");
    item.dataset.column = key;
    item.draggable = key !== "station";
    const handle = document.createElement("span");
    handle.className = "drag-handle";
    handle.textContent = key === "station" ? "•" : "⋮⋮";
    const checkbox = document.createElement("input");
    checkbox.type = "checkbox";
    checkbox.checked = !columnPreferences.hidden.includes(key);
    checkbox.disabled = key === "station";
    checkbox.addEventListener("change", () => {
      columnPreferences.hidden = checkbox.checked
        ? columnPreferences.hidden.filter((value) => value !== key)
        : [...columnPreferences.hidden, key];
      saveColumnPreferences(); applyColumnPreferences();
    });
    const label = document.createElement("span");
    label.textContent = columnNames.get(key);
    item.append(handle, checkbox, label);
    handle.addEventListener("pointerdown", (event) => {
      if (key === "station" || event.pointerType === "mouse") return;
      event.preventDefault();
      draggedColumn = key;
      item.classList.add("dragging");
      handle.setPointerCapture(event.pointerId);
      const move = (pointerEvent) => {
        const target = document.elementFromPoint(pointerEvent.clientX, pointerEvent.clientY)?.closest("#columns-list li");
        const targetKey = target?.dataset.column;
        if (!targetKey || !moveColumn(key, targetKey,
          pointerEvent.clientY > target.getBoundingClientRect().top + target.offsetHeight / 2)) return;
        const after = columnPreferences.order.indexOf(key) > columnPreferences.order.indexOf(targetKey);
        columnsList.insertBefore(item, after ? target.nextSibling : target);
        applyColumnPreferences();
      };
      const end = () => {
        item.classList.remove("dragging"); draggedColumn = null; saveColumnPreferences();
        handle.removeEventListener("pointermove", move); handle.removeEventListener("pointerup", end);
        handle.removeEventListener("pointercancel", end);
      };
      handle.addEventListener("pointermove", move);
      handle.addEventListener("pointerup", end);
      handle.addEventListener("pointercancel", end);
    });
    item.addEventListener("dragstart", (event) => {
      item.classList.add("dragging");
      draggedColumn = key;
      event.dataTransfer.setData("text/plain", key);
      event.dataTransfer.effectAllowed = "move";
    });
    item.addEventListener("dragend", () => { item.classList.remove("dragging"); draggedColumn = null; });
    item.addEventListener("dragover", (event) => {
      if (key === "station") return;
      event.preventDefault();
      event.dataTransfer.dropEffect = "move";
    });
    item.addEventListener("drop", (event) => {
      event.preventDefault();
      const draggedKey = draggedColumn || event.dataTransfer.getData("text/plain");
      if (!draggedKey || draggedKey === key || draggedKey === "station") return;
      const after = event.clientY > item.getBoundingClientRect().top + item.offsetHeight / 2;
      if (!moveColumn(draggedKey, key, after)) return;
      saveColumnPreferences(); applyColumnPreferences(); renderColumnsMenu();
    });
    columnsList.append(item);
  }
}

function minimumClass(value, { goodMin, normalMin }) {
  if (value == null) return "";
  return value >= goodMin ? "good" : value >= normalMin ? "normal" : "bad";
}

function maximumClass(value, { goodMax, normalMax }) {
  if (value == null) return "";
  return value <= goodMax ? "good" : value <= normalMax ? "normal" : "bad";
}

function render({ stations, thresholds }) {
  table.replaceChildren();
  for (const station of stations) {
    const row = document.createElement("tr");
    const identity = document.createElement("td");
    identity.className = "station-name";
    identity.dataset.column = "station";
    identity.dataset.label = columnNames.get("station");
    identity.style.color = station.color;
    identity.textContent = station.name.toUpperCase();
    row.append(identity);
    const snapshot = station.snapshot;
    if (!snapshot) {
      row.append(
        cell(dash, "", "status"), cell(dash, "", "voltage"), cell(dash, "", "current"),
        cell(dash, "", "satellites"), cell(dash, "", "hdop"), cell(dash, "", "heading"),
        cell(dash, "", "altitude"), cell(dash, "", "link"), cell(dash, "", "obs"),
      );
    } else {
      const [, , , voltage, current, satellites, hdop, heading, altitude, ruijie, flags] = snapshot;
      const vehicle = flags & 2 ? "Armed" : "Disarmed";
      const vehicleClass = flags & 2 ? "armed" : "disarmed";
      const recording = Boolean(flags & 1);
      const obsClass = flags & 2 ? (recording ? "good" : "bad") : (recording ? "normal" : "good");
      row.append(cell(vehicle, vehicleClass, "status"),
        cell(number(voltage, 1, " V"), minimumClass(voltage, thresholds.voltage), "voltage"),
        cell(number(current, 1, " A"), maximumClass(current, thresholds.current), "current"),
        cell(satellites ?? dash, minimumClass(satellites, thresholds.satellites), "satellites"),
        cell(number(hdop, 2), maximumClass(hdop, thresholds.hdop), "hdop"),
        cell(number(heading, 0, "°"), "", "heading"),
        cell(number(altitude, 0, " m"), "", "altitude"),
        cell(ruijie == null ? dash : `${ruijie} dBm`, minimumClass(ruijie, thresholds.linkRssi), "link"),
        cell(recording ? "REC" : "NR", obsClass, "obs"));
    }
    table.append(row);
  }
  if (!stations.length) table.innerHTML = '<tr><td colspan="10" class="empty">Немає підключених станцій</td></tr>';
  applyColumnPreferences();
}

function showLogin(message = "") {
  clearInterval(refreshTimer);
  dashboard.hidden = true;
  login.hidden = false;
  document.querySelector("#login-error").textContent = message;
}

function setHeaderHidden(hidden) {
  dashboardHeader.hidden = hidden;
  showHeaderButton.hidden = !hidden;
  localStorage.setItem("sarmat-header-hidden", hidden ? "true" : "false");
}

async function refresh() {
  try {
    const response = await fetch("/api/stations", { cache: "no-store", headers: authHeaders() });
    if (response.status === 401) return showLogin("Сесія завершилась. Введіть секрет знову.");
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    render(await response.json());
    connection.textContent = "Дані оновлюються";
    connectionDot.className = "connected";
  } catch {
    connection.textContent = "Немає зв’язку";
    connectionDot.className = "disconnected";
  }
}

async function signIn(candidate) {
  const response = await fetch("/api/login", { method: "POST", headers: { Authorization: `Bearer ${candidate}` } });
  if (!response.ok) throw new Error("Невірний секрет");
  secret = candidate;
  sessionStorage.setItem("sarmat-secret", secret);
  login.hidden = true;
  dashboard.hidden = false;
  await refresh();
  refreshTimer = setInterval(refresh, 1000);
}

document.querySelector("#login-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  try { await signIn(document.querySelector("#secret").value); }
  catch (error) { document.querySelector("#login-error").textContent = error.message; }
});
document.querySelector("#logout").addEventListener("click", () => {
  secret = "";
  sessionStorage.removeItem("sarmat-secret");
  showLogin();
});
document.querySelector("#hide-header").addEventListener("click", () => setHeaderHidden(true));
showHeaderButton.addEventListener("click", () => setHeaderHidden(false));
columnsButton.addEventListener("click", (event) => {
  event.stopPropagation();
  columnsMenu.hidden = !columnsMenu.hidden;
  columnsButton.setAttribute("aria-expanded", String(!columnsMenu.hidden));
  if (!columnsMenu.hidden) renderColumnsMenu();
});
columnsMenu.addEventListener("click", (event) => event.stopPropagation());
document.addEventListener("click", () => {
  columnsMenu.hidden = true;
  columnsButton.setAttribute("aria-expanded", "false");
});

setHeaderHidden(localStorage.getItem("sarmat-header-hidden") === "true");
applyColumnPreferences();

if (secret) signIn(secret).catch(() => showLogin("Введіть актуальний секрет."));
else showLogin();
