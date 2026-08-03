import assert from "node:assert/strict";
import { test } from "node:test";
import { encode } from "@msgpack/msgpack";
import WebSocket from "ws";
import { createTelemetryServer } from "../src/app.js";
import { validateConfig } from "../src/config.js";

const silentLogger = { info() {}, error() {} };
const auth = { Authorization: "Bearer admin-secret" };

function testConfig() {
  return validateConfig({
    server: { host: "127.0.0.1", port: 0, snapshotIntervalMs: 1000,
      staleAfterMs: 3000, offlineAfterMs: 10000, maxMessageBytes: 4096 },
    admins: ["admin-secret"],
    stations: [
      { title: "Red Station", color: "#ff0000", secret: "red-secret" },
      { title: "Green Station", color: "#00ff00", secret: "green-secret" },
    ],
  });
}

function connect(url, secret = "red-secret") {
  return new Promise((resolve, reject) => {
    const socket = new WebSocket(url, { headers: { Authorization: `Bearer ${secret}` } });
    socket.once("open", () => resolve(socket));
    socket.once("error", reject);
  });
}

function waitFor(predicate) {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error("Timed out")), 1000);
    const interval = setInterval(() => {
      if (!predicate()) return;
      clearTimeout(timeout); clearInterval(interval); resolve();
    }, 5);
  });
}

test("uses configured station presentation and exposes telemetry to admins", async (t) => {
  const server = createTelemetryServer(testConfig(), silentLogger);
  const address = await server.listen();
  t.after(() => server.close());
  const httpUrl = `http://127.0.0.1:${address.port}`;
  const initialResponse = await fetch(`${httpUrl}/api/stations`, { headers: auth });
  const initialBody = await initialResponse.json();
  assert.equal(initialBody.stations.length, 2);
  assert.equal(initialBody.stations[0].name, "Red Station");
  assert.equal(initialBody.stations[0].snapshot, null);
  assert.equal(initialBody.stations[1].name, "Green Station");
  assert.equal(initialBody.stations[1].snapshot, null);

  const station = await connect(`ws://127.0.0.1:${address.port}/ws/station`);
  t.after(() => station.terminate());

  station.send(encode([7, 22.7, 14.3, 18, 0.8, 274.5, 123.4, -68, 3]));
  await waitFor(() => server.stationStates[0]?.telemetry);

  const response = await fetch(`${httpUrl}/api/stations`, { headers: auth });
  assert.equal(response.status, 200);
  const body = await response.json();
  assert.equal(body.stations[0].name, "Red Station");
  assert.equal(body.stations[0].color, "#FF0000");
  assert.equal(body.stations[0].snapshot[2], 7);
  assert.equal(body.stations[0].snapshot[9], -68);
  assert.equal(body.thresholds.linkRssi.goodMin, -70);

  station.close();
  await waitFor(() => !server.stationStates[0].connected);
  assert.equal(server.stationStates.length, 2);
  assert.equal(server.stationStates[0].telemetry, null);
});

test("keeps admin and station authorization separate", async (t) => {
  const server = createTelemetryServer(testConfig(), silentLogger);
  const address = await server.listen();
  t.after(() => server.close());
  const httpUrl = `http://127.0.0.1:${address.port}`;

  assert.equal((await fetch(`${httpUrl}/api/login`, { method: "POST" })).status, 401);
  assert.equal((await fetch(`${httpUrl}/api/login`, { method: "POST", headers: auth })).status, 204);
  assert.equal((await fetch(`${httpUrl}/api/stations`)).status, 401);
  assert.equal((await fetch(`${httpUrl}/api/login`, {
    method: "POST", headers: { Authorization: "Bearer red-secret" },
  })).status, 401);
  await assert.rejects(connect(`ws://127.0.0.1:${address.port}/ws/station`, "admin-secret"),
    /Unexpected server response: 401/);
  await assert.rejects(connect(`ws://127.0.0.1:${address.port}/ws/station`, "wrong"),
    /Unexpected server response: 401/);
});

test("validates configured station presentation and identity", () => {
  const base = {
    admins: ["admin-secret"],
    stations: [{ title: "Red", color: "#ff0000", secret: "station-secret" }],
  };
  assert.throws(() => validateConfig({ ...base, stations: [
    { title: "Red", color: "red", secret: "station-secret" },
  ] }), /#RRGGBB/);
  assert.throws(() => validateConfig({ ...base, stations: [
    ...base.stations, { title: "Green", color: "#00ff00", secret: "station-secret" },
  ] }), /unique/);
});

test("serves the login page", async (t) => {
  const server = createTelemetryServer(testConfig(), silentLogger);
  const address = await server.listen();
  t.after(() => server.close());
  const page = await fetch(`http://127.0.0.1:${address.port}`);
  assert.equal(page.status, 200);
  assert.match(await page.text(), /aria-label="SARMAT"/);
});
