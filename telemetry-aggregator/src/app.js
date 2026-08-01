import { createServer } from "node:http";
import { timingSafeEqual } from "node:crypto";
import { decode } from "@msgpack/msgpack";
import { WebSocket, WebSocketServer } from "ws";
import { encodeMonitorConfiguration, encodeSnapshot, validateTelemetry } from "./protocol.js";

function getBearerToken(request) {
  const header = request.headers.authorization;
  if (typeof header !== "string") return null;
  const match = /^Bearer\s+(.+)$/i.exec(header);
  return match?.[1] ?? null;
}

function secretsEqual(left, right) {
  const leftBuffer = Buffer.from(left ?? "", "utf8");
  const rightBuffer = Buffer.from(right ?? "", "utf8");
  return leftBuffer.length === rightBuffer.length && timingSafeEqual(leftBuffer, rightBuffer);
}

function findBySecret(entries, secret) {
  if (!secret) return -1;
  return entries.findIndex((entry) => secretsEqual(entry.secret, secret));
}

function rejectUpgrade(socket, statusCode, message) {
  const body = `${message}\n`;
  socket.end(
    `HTTP/1.1 ${statusCode} ${message}\r\n` +
      "Connection: close\r\n" +
      "Content-Type: text/plain; charset=utf-8\r\n" +
      `Content-Length: ${Buffer.byteLength(body)}\r\n` +
      "\r\n" +
      body,
  );
}

export function createTelemetryServer(config, logger = console) {
  const stationStates = config.stations.map(() => ({
    socket: null,
    connected: false,
    telemetry: null,
    receivedAt: null,
  }));
  const monitors = new Set();

  const httpServer = createServer((request, response) => {
    if (request.method === "GET" && request.url === "/health") {
      response.writeHead(200, { "content-type": "application/json" });
      response.end(JSON.stringify({ status: "ok" }));
      return;
    }
    response.writeHead(404, { "content-type": "text/plain; charset=utf-8" });
    response.end("Not found\n");
  });

  const stationServer = new WebSocketServer({ noServer: true, maxPayload: config.server.maxMessageBytes });
  const monitorServer = new WebSocketServer({ noServer: true, maxPayload: config.server.maxMessageBytes });

  function sendSnapshot(socket, now = Date.now()) {
    if (socket.readyState !== WebSocket.OPEN) return;
    socket.send(
      encodeSnapshot(stationStates, now, config.server.staleAfterMs, config.server.offlineAfterMs),
      { binary: true },
    );
  }

  function broadcastSnapshot() {
    const now = Date.now();
    for (const monitor of monitors) sendSnapshot(monitor, now);
  }

  stationServer.on("connection", (socket, request, stationIndex) => {
    const station = config.stations[stationIndex];
    const state = stationStates[stationIndex];
    const previousSocket = state.socket;

    state.socket = socket;
    state.connected = true;
    if (previousSocket && previousSocket !== socket) {
      previousSocket.close(4001, "Replaced by a newer connection");
    }
    logger.info(`Station connected: ${station.name}`);

    socket.on("message", (data, isBinary) => {
      if (!isBinary) {
        socket.close(1003, "Binary MessagePack frames are required");
        return;
      }

      let packet;
      try {
        packet = decode(data);
      } catch {
        socket.close(1007, "Invalid MessagePack payload");
        return;
      }

      const validationError = validateTelemetry(packet);
      if (validationError) {
        socket.close(1007, validationError.slice(0, 123));
        return;
      }

      state.telemetry = [...packet];
      state.receivedAt = Date.now();
      broadcastSnapshot();
    });

    socket.on("close", () => {
      if (state.socket !== socket) return;
      state.socket = null;
      state.connected = false;
      logger.info(`Station disconnected: ${station.name}`);
      broadcastSnapshot();
    });
  });

  monitorServer.on("connection", (socket, request, clientIndex) => {
    const client = config.clients[clientIndex];
    monitors.add(socket);
    logger.info(`Monitor connected: ${client.name}`);
    socket.send(encodeMonitorConfiguration(config.stations), { binary: true });
    sendSnapshot(socket);

    socket.on("message", () => socket.close(1008, "Monitor connections are read-only"));
    socket.on("close", () => {
      monitors.delete(socket);
      logger.info(`Monitor disconnected: ${client.name}`);
    });
  });

  httpServer.on("upgrade", (request, socket, head) => {
    const path = new URL(request.url, "http://localhost").pathname;
    const secret = getBearerToken(request);

    if (path === "/ws/station") {
      const stationIndex = findBySecret(config.stations, secret);
      if (stationIndex < 0) return rejectUpgrade(socket, 401, "Unauthorized");
      stationServer.handleUpgrade(request, socket, head, (webSocket) => {
        stationServer.emit("connection", webSocket, request, stationIndex);
      });
      return;
    }

    if (path === "/ws/monitor") {
      const clientIndex = findBySecret(config.clients, secret);
      if (clientIndex < 0) return rejectUpgrade(socket, 401, "Unauthorized");
      monitorServer.handleUpgrade(request, socket, head, (webSocket) => {
        monitorServer.emit("connection", webSocket, request, clientIndex);
      });
      return;
    }

    rejectUpgrade(socket, 404, "Not Found");
  });

  const snapshotTimer = setInterval(broadcastSnapshot, config.server.snapshotIntervalMs);
  snapshotTimer.unref();

  return {
    stationStates,
    async listen() {
      await new Promise((resolve, reject) => {
        httpServer.once("error", reject);
        httpServer.listen(config.server.port, config.server.host, resolve);
      });
      return httpServer.address();
    },
    async close() {
      clearInterval(snapshotTimer);
      for (const state of stationStates) state.socket?.terminate();
      for (const monitor of monitors) monitor.terminate();
      stationServer.close();
      monitorServer.close();
      await new Promise((resolve, reject) => {
        httpServer.close((error) => (error ? reject(error) : resolve()));
      });
    },
  };
}
