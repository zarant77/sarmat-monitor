import assert from "node:assert/strict";
import { test } from "node:test";
import { decode, encode } from "@msgpack/msgpack";
import WebSocket from "ws";
import { createTelemetryServer } from "../src/app.js";
import { validateConfig } from "../src/config.js";

const silentLogger = { info() {} };

function testConfig() {
  return validateConfig({
    server: {
      host: "127.0.0.1",
      port: 0,
      snapshotIntervalMs: 1000,
      staleAfterMs: 3000,
      offlineAfterMs: 10000,
      maxMessageBytes: 4096,
    },
    stations: [{ secret: "station-secret", name: "Red", color: "#ff0000" }],
    clients: [{ secret: "monitor-secret", name: "Test monitor" }],
  });
}

function connect(url, secret) {
  return new Promise((resolve, reject) => {
    const socket = new WebSocket(url, { headers: { Authorization: `Bearer ${secret}` } });
    socket.messageQueue = [];
    socket.messageWaiters = [];
    socket.on("message", (data, isBinary) => {
      const message = { data, isBinary };
      const waiter = socket.messageWaiters.shift();
      if (waiter) waiter(message);
      else socket.messageQueue.push(message);
    });
    socket.once("open", () => resolve(socket));
    socket.once("error", reject);
  });
}

async function nextBinaryMessage(socket) {
  const message = socket.messageQueue.shift() ??
    (await new Promise((resolve) => socket.messageWaiters.push(resolve)));
  assert.equal(message.isBinary, true);
  return decode(message.data);
}

test("authenticates a station and broadcasts its latest telemetry", async (t) => {
  const server = createTelemetryServer(testConfig(), silentLogger);
  const address = await server.listen();
  t.after(() => server.close());
  const baseUrl = `ws://127.0.0.1:${address.port}`;

  const monitor = await connect(`${baseUrl}/ws/monitor`, "monitor-secret");
  t.after(() => monitor.terminate());
  const configuration = await nextBinaryMessage(monitor);
  assert.deepEqual(configuration, [1, [["Red", "#FF0000"]]]);
  assert.deepEqual(await nextBinaryMessage(monitor), [null]);

  const station = await connect(`${baseUrl}/ws/station`, "station-secret");
  t.after(() => station.terminate());
  const nextSnapshot = nextBinaryMessage(monitor);
  station.send(encode([7, 22.7, 14.3, 18, 0.8, 274.5, 123.4, 86, 3]));

  const snapshot = await nextSnapshot;
  assert.equal(snapshot[0][0], 0);
  assert.equal(snapshot[0][2], 7);
  assert.deepEqual(snapshot[0].slice(3), [22.7, 14.3, 18, 0.8, 274.5, 123.4, 86, 3]);
  assert.deepEqual(server.stationStates[0].telemetry, [7, 22.7, 14.3, 18, 0.8, 274.5, 123.4, 86, 3]);
});

test("rejects an unknown station secret", async (t) => {
  const server = createTelemetryServer(testConfig(), silentLogger);
  const address = await server.listen();
  t.after(() => server.close());

  await assert.rejects(
    connect(`ws://127.0.0.1:${address.port}/ws/station`, "wrong-secret"),
    /Unexpected server response: 401/,
  );
});
