export const STATION_STATUS = Object.freeze({
  ONLINE: 0,
  STALE: 1,
  OFFLINE: 2,
});

function isNullableNumber(value) {
  return value === null || (typeof value === "number" && Number.isFinite(value));
}

function isNullableInteger(value, minimum, maximum) {
  return value === null || (Number.isInteger(value) && value >= minimum && value <= maximum);
}

export function validateTelemetry(packet) {
  if (!Array.isArray(packet) || packet.length !== 9) {
    return "telemetry must be a 9-element MessagePack array";
  }

  const [sequence, voltage, current, satellites, hdop, heading, altitude, linkRssi, flags] = packet;
  if (!Number.isInteger(sequence) || sequence < 0 || sequence > 0xffffffff) {
    return "sequence must be an unsigned 32-bit integer";
  }
  if (!isNullableNumber(voltage)) return "voltage must be a finite number or nil";
  if (!isNullableNumber(current)) return "current must be a finite number or nil";
  if (!isNullableInteger(satellites, 0, 255)) return "satellites must be 0..255 or nil";
  if (!isNullableNumber(hdop) || (hdop !== null && hdop < 0)) return "hdop must be non-negative or nil";
  if (!isNullableNumber(heading) || (heading !== null && (heading < 0 || heading >= 360))) {
    return "heading must be in the range 0..<360 or nil";
  }
  if (!isNullableNumber(altitude)) return "altitude must be a finite number or nil";
  if (!isNullableInteger(linkRssi, -127, 0)) return "link RSSI must be -127..0 dBm or nil";
  if (!Number.isInteger(flags) || flags < 0 || flags > 255) return "flags must be 0..255";
  return null;
}

export function createSnapshot(stationStates, now, staleAfterMs, offlineAfterMs) {
  return stationStates.map((state) => {
    if (!state.telemetry || state.receivedAt === null) return null;

    const ageMs = Math.max(0, now - state.receivedAt);
    let status = STATION_STATUS.ONLINE;
    if (!state.connected || ageMs >= offlineAfterMs) status = STATION_STATUS.OFFLINE;
    else if (ageMs >= staleAfterMs) status = STATION_STATUS.STALE;

    return [status, ageMs, ...state.telemetry];
  });
}
