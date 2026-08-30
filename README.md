# Sarmat

Основний монорепозиторій Sarmat для моніторингу станцій, керування парком акумуляторів і інтеграції з Mission Planner.

## Структура

- [`telemetry-plugin`](./telemetry-plugin) — плагін для Mission Planner, який надсилає телеметрію.
- [`monitor`](./monitor) — основний вебзастосунок для телеметрії станцій, керування акумуляторами, перевірками, циклами та передаванням між екіпажами.
- [`theme`](./theme) — спільні ресурси оформлення Mission Planner для всіх плагінів.

Telemetry plugin і Monitor використовують компактний MessagePack-протокол через WebSocket. Під час локальної розробки вебінтерфейс типово доступний на `http://localhost:5173/`, а API — на `http://localhost:3000/`.

Детальні інструкції запуску є в [`monitor/README.md`](./monitor/README.md). Із кореня монорепозиторію доступні команди `npm run dev`, `npm run build`, `npm run typecheck`, `npm test`, `npm run db:migrate` і `npm run db:seed`.

## Релізи

Тег у форматі `vMAJOR.MINOR.PATCH` створює два окремі пакети:

- `SarmatPlugins-<version>.msi`;
- `SarmatMonitor-<version>.zip`.

До пакетів додаються лише приклади конфігурації без робочих секретів.
