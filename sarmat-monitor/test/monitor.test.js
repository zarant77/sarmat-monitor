import assert from "node:assert/strict";
import { test } from "node:test";
import { encode } from "@msgpack/msgpack";
import WebSocket from "ws";
import { createTelemetryServer } from "../src/app.js";
import { validateConfig } from "../src/config.js";

const silentLogger = { info() {}, error() {} };
const auth = { Authorization: "Bearer shared-secret" };

function testConfig() {
  return validateConfig({
    server: { host: "127.0.0.1", port: 0, snapshotIntervalMs: 1000,
      staleAfterMs: 3000, offlineAfterMs: 10000, maxMessageBytes: 4096 },
    secret: "shared-secret",
  });
}

function connect(url, secret = "shared-secret") {
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

test("registers station metadata and exposes its telemetry to authenticated web users", async (t) => {
  const server = createTelemetryServer(testConfig(), silentLogger);
  const address = await server.listen();
  t.after(() => server.close());
  const httpUrl = `http://127.0.0.1:${address.port}`;
  const station = await connect(`ws://127.0.0.1:${address.port}/ws/station`);
  t.after(() => station.terminate());

  station.send(JSON.stringify({ name: "Red", color: "#ff0000" }));
  station.send(encode([7, 22.7, 14.3, 18, 0.8, 274.5, 123.4, -68, 3]));
  await waitFor(() => server.stationStates[0]?.telemetry);

  const response = await fetch(`${httpUrl}/api/stations`, { headers: auth });
  assert.equal(response.status, 200);
  const body = await response.json();
  assert.equal(body.stations[0].name, "Red");
  assert.equal(body.stations[0].color, "#FF0000");
  assert.equal(body.stations[0].snapshot[2], 7);
  assert.equal(body.stations[0].snapshot[9], -68);
  assert.equal(body.thresholds.linkRssi.goodMin, -70);

  station.close();
  await waitFor(() => server.stationStates.length === 0);
});

test("uses the shared secret for stations and web login", async (t) => {
  const server = createTelemetryServer(testConfig(), silentLogger);
  const address = await server.listen();
  t.after(() => server.close());
  const httpUrl = `http://127.0.0.1:${address.port}`;

  assert.equal((await fetch(`${httpUrl}/api/login`, { method: "POST" })).status, 401);
  assert.equal((await fetch(`${httpUrl}/api/login`, { method: "POST", headers: auth })).status, 204);
  assert.equal((await fetch(`${httpUrl}/api/stations`)).status, 401);
  await assert.rejects(connect(`ws://127.0.0.1:${address.port}/ws/station`, "wrong"),
    /Unexpected server response: 401/);
});

test("rejects invalid station presentation data", async (t) => {
  const server = createTelemetryServer(testConfig(), silentLogger);
  const address = await server.listen();
  t.after(() => server.close());
  const station = await connect(`ws://127.0.0.1:${address.port}/ws/station`);
  station.send(JSON.stringify({ name: "Red", color: "red" }));
  const closeCode = await new Promise((resolve) => station.once("close", resolve));
  assert.equal(closeCode, 1007);
  assert.equal(server.stationStates.length, 0);
});

test("serves the login page", async (t) => {
  const server = createTelemetryServer(testConfig(), silentLogger);
  const address = await server.listen();
  t.after(() => server.close());
  const page = await fetch(`http://127.0.0.1:${address.port}`);
  assert.equal(page.status, 200);
  assert.match(await page.text(), /Вхід у монітор/);
});
