const dash = "—";
const table = document.querySelector("#stations");
const connection = document.querySelector("#connection");
const connectionDot = document.querySelector("#connection-dot");
const login = document.querySelector("#login");
const dashboard = document.querySelector("#dashboard");
let secret = sessionStorage.getItem("sarmat-secret") ?? "";
let refreshTimer;

const authHeaders = () => ({ Authorization: `Bearer ${secret}` });
const number = (value, digits, suffix = "") => value == null ? dash : `${value.toFixed(digits)}${suffix}`;
function cell(value, className = "", label = "") {
  const element = document.createElement("td");
  element.textContent = value;
  if (className) element.className = className;
  if (label) element.dataset.label = label;
  return element;
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
    identity.dataset.label = "Станція";
    identity.style.color = station.color;
    identity.textContent = station.name.toUpperCase();
    row.append(identity);
    const snapshot = station.snapshot;
    if (!snapshot) {
      row.append(
        cell(dash, "", "Статус"), cell(dash, "", "Напруга"), cell(dash, "", "Струм"),
        cell(dash, "", "Sat"), cell(dash, "", "HDOP"), cell(dash, "", "Азимут"),
        cell(dash, "", "Висота"), cell(dash, "", "Зв'язок"), cell(dash, "", "OBS"),
      );
    } else {
      const [, , , voltage, current, satellites, hdop, heading, altitude, ruijie, flags] = snapshot;
      const vehicle = flags & 2 ? "Armed" : "Disarmed";
      const vehicleClass = flags & 2 ? "armed" : "disarmed";
      const recording = Boolean(flags & 1);
      const obsClass = flags & 2 ? (recording ? "good" : "bad") : (recording ? "normal" : "good");
      row.append(cell(vehicle, vehicleClass, "Статус"),
        cell(number(voltage, 1, " V"), minimumClass(voltage, thresholds.voltage), "Напруга"),
        cell(number(current, 1, " A"), maximumClass(current, thresholds.current), "Струм"),
        cell(satellites ?? dash, minimumClass(satellites, thresholds.satellites), "Sat"),
        cell(number(hdop, 2), maximumClass(hdop, thresholds.hdop), "HDOP"),
        cell(number(heading, 1, "°"), "", "Азимут"),
        cell(number(altitude, 1, " m"), "", "Висота"),
        cell(ruijie == null ? dash : `${ruijie} dBm`, minimumClass(ruijie, thresholds.linkRssi), "Зв'язок"),
        cell(recording ? "REC" : "NR", obsClass, "OBS"));
    }
    table.append(row);
  }
  if (!stations.length) table.innerHTML = '<tr><td colspan="10" class="empty">Немає підключених станцій</td></tr>';
}

function showLogin(message = "") {
  clearInterval(refreshTimer);
  dashboard.hidden = true;
  login.hidden = false;
  document.querySelector("#login-error").textContent = message;
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

if (secret) signIn(secret).catch(() => showLogin("Введіть актуальний секрет."));
else showLogin();
