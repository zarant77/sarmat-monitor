# Sarmat Monitor

Монорепозиторій системи збору та перегляду телеметрії з Mission Planner.

## Структура

- [`telemetry-plugin`](./telemetry-plugin) — плагін для Mission Planner, який надсилає телеметрію.
- [`monitor`](./monitor) — Node.js сервер, який збирає актуальну телеметрію станцій і показує її у вебінтерфейсі.
- [`vision-hold-plugin`](./vision-hold-plugin) — плагін утримання позиції за даними комп'ютерного зору.

Обидва компоненти використовують компактний MessagePack-протокол через WebSocket. Вебпанель доступна на корені HTTP-сервера Sarmat Monitor, типово `http://localhost:8080/`.

## Релізи

Тег у форматі `vMAJOR.MINOR.PATCH` створює два окремі пакети:

- `SarmatTelemetry-<version>.msi`;
- `SarmatMonitor-<version>.zip`.

До пакетів додаються лише приклади конфігурації без робочих секретів.
