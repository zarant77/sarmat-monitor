# Sarmat Monitor

Монорепозиторій системи збору та перегляду телеметрії Sarmat.

## Структура

- [`sarmat-plugin`](./sarmat-plugin) — плагін для Mission Planner, який надсилає телеметрію.
- [`sarmat-monitor`](./sarmat-monitor) — Node.js сервер, який збирає актуальну телеметрію станцій і показує її у вебінтерфейсі.

Обидва компоненти використовують компактний MessagePack-протокол через WebSocket. Вебпанель доступна на корені HTTP-сервера Sarmat Monitor, типово `http://localhost:8080/`.

## Релізи

Тег у форматі `vMAJOR.MINOR.PATCH` створює два окремі пакети:

- `SarmatPlugin-<version>.msi`;
- `SarmatMonitor-<version>.zip`.

До пакетів додаються лише приклади конфігурації без робочих секретів.
