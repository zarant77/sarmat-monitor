# Sarmat Monitor

Монорепозиторій системи збору та перегляду телеметрії Sarmat.

## Структура

- [`sarmat-plugin`](./sarmat-plugin) — плагін для Mission Planner.
- [`telemetry-aggregator`](./telemetry-aggregator) — Node.js WebSocket-сервер, що зберігає
  актуальну телеметрію станцій у пам'яті.
- [`telemetry-monitor`](./telemetry-monitor) — WPF-клієнт для табличного перегляду актуальної
  телеметрії.

Усі три компоненти використовують компактний MessagePack-протокол через WebSocket.

## Релізи

Тег у форматі `vMAJOR.MINOR.PATCH` створює три окремі пакети:

- `SarmatPlugin-<version>.msi`;
- `TelemetryAggregator-<version>.zip`;
- `TelemetryMonitor-<version>-win-x64.msi`.

До пакетів додаються лише приклади конфігурації без робочих секретів.

## Робота з плагіном

Інструкції зі збирання, тестування та встановлення розміщені в
[`sarmat-plugin/README.md`](./sarmat-plugin/README.md).
