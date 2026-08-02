import { createServer } from "node:http";
import { timingSafeEqual } from "node:crypto";
import { readFile } from "node:fs/promises";
import { decode } from "@msgpack/msgpack";
import { WebSocketServer } from "ws";
import { createSnapshot, validateTelemetry } from "./protocol.js";

const publicDirectory = new URL("../public/", import.meta.url);
const publicFiles = new Map([
  ["/", ["index.html", "text/html; charset=utf-8"]],
  ["/app.css", ["app.css", "text/css; charset=utf-8"]],
  ["/app.js", ["app.js", "text/javascript; charset=utf-8"]],
]);

function getBearerToken(request) {
  const header = request.headers.authorization;
  if (typeof header !== "string") return null;
  return /^Bearer\s+(.+)$/i.exec(header)?.[1] ?? null;
}

function secretsEqual(left, right) {
  const leftBuffer = Buffer.from(left ?? "", "utf8");
  const rightBuffer = Buffer.from(right ?? "", "utf8");
  return leftBuffer.length === rightBuffer.length && timingSafeEqual(leftBuffer, rightBuffer);
}

function authorized(request, config) {
  return secretsEqual(getBearerToken(request), config.secret);
}

function rejectUpgrade(socket, statusCode, message) {
  const body = `${message}\n`;
  socket.end(`HTTP/1.1 ${statusCode} ${message}\r\nConnection: close\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: ${Buffer.byteLength(body)}\r\n\r\n${body}`);
}

function parseStationMetadata(data, isBinary) {
  if (isBinary) return null;
  try {
    const value = JSON.parse(data.toString("utf8"));
    if (typeof value?.name !== "string" || value.name.trim().length === 0 || value.name.trim().length > 100) return null;
    if (typeof value.color !== "string" || !/^#[0-9a-f]{6}$/i.test(value.color)) return null;
    return { name: value.name.trim(), color: value.color.toUpperCase() };
  } catch {
    return null;
  }
}

export function createTelemetryServer(config, logger = console) {
  const stationStates = [];

  const httpServer = createServer(async (request, response) => {
    const path = new URL(request.url, "http://localhost").pathname;
    if (request.method === "GET" && path === "/health") {
      response.writeHead(200, { "content-type": "application/json" });
      response.end(JSON.stringify({ status: "ok" }));
      return;
    }
    if ((request.method === "POST" && path === "/api/login") ||
        (request.method === "GET" && path === "/api/stations")) {
      if (!authorized(request, config)) {
        response.writeHead(401, { "content-type": "application/json; charset=utf-8", "cache-control": "no-store" });
        response.end(JSON.stringify({ error: "Unauthorized" }));
        return;
      }
      if (path === "/api/login") {
        response.writeHead(204, { "cache-control": "no-store" });
        response.end();
        return;
      }
      const snapshots = createSnapshot(stationStates, Date.now(), config.server.staleAfterMs, config.server.offlineAfterMs);
      response.writeHead(200, { "content-type": "application/json; charset=utf-8", "cache-control": "no-store" });
      response.end(JSON.stringify({ thresholds: config.thresholds, stations: stationStates.map((state, index) => ({
        name: state.name, color: state.color, snapshot: snapshots[index],
      })) }));
      return;
    }
    const publicFile = request.method === "GET" ? publicFiles.get(path) : null;
    if (publicFile) {
      try {
        const [fileName, contentType] = publicFile;
        response.writeHead(200, { "content-type": contentType });
        response.end(await readFile(new URL(fileName, publicDirectory)));
      } catch (error) {
        logger.error?.(`Cannot serve web interface: ${error.message}`);
        response.writeHead(500, { "content-type": "text/plain; charset=utf-8" });
        response.end("Web interface is unavailable\n");
      }
      return;
    }
    response.writeHead(404, { "content-type": "text/plain; charset=utf-8" });
    response.end("Not found\n");
  });

  const stationServer = new WebSocketServer({ noServer: true, maxPayload: config.server.maxMessageBytes });
  stationServer.on("connection", (socket) => {
    let state = null;
    socket.on("message", (data, isBinary) => {
      if (!state) {
        const metadata = parseStationMetadata(data, isBinary);
        if (!metadata) {
          socket.close(1007, "First frame must contain station name and color");
          return;
        }
        state = { socket, connected: true, telemetry: null, receivedAt: null, ...metadata };
        stationStates.push(state);
        logger.info(`Station connected: ${state.name}`);
        return;
      }
      if (!isBinary) {
        socket.close(1003, "Binary MessagePack telemetry frames are required");
        return;
      }
      let packet;
      try { packet = decode(data); }
      catch { socket.close(1007, "Invalid MessagePack payload"); return; }
      const validationError = validateTelemetry(packet);
      if (validationError) { socket.close(1007, validationError.slice(0, 123)); return; }
      state.telemetry = [...packet];
      state.receivedAt = Date.now();
    });
    socket.on("close", () => {
      if (!state) return;
      const index = stationStates.indexOf(state);
      if (index >= 0) stationStates.splice(index, 1);
      logger.info(`Station disconnected: ${state.name}`);
    });
  });

  httpServer.on("upgrade", (request, socket, head) => {
    const path = new URL(request.url, "http://localhost").pathname;
    if (path !== "/ws/station") return rejectUpgrade(socket, 404, "Not Found");
    if (!authorized(request, config)) return rejectUpgrade(socket, 401, "Unauthorized");
    stationServer.handleUpgrade(request, socket, head, (webSocket) => stationServer.emit("connection", webSocket, request));
  });

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
      for (const state of [...stationStates]) state.socket.terminate();
      stationServer.close();
      await new Promise((resolve, reject) => httpServer.close((error) => error ? reject(error) : resolve()));
    },
  };
}
