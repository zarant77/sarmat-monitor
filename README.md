# Sarmat

Основний монорепозиторій Sarmat для моніторингу станцій, керування парком акумуляторів і інтеграції з Mission Planner.

Розгорнутий опис призначення, архітектури, реалізованих можливостей і алгоритму сканування LCD наведено у [`SUMMARY.md`](./SUMMARY.md).

## Структура

- [`telemetry-plugin`](./telemetry-plugin) — плагін для Mission Planner, який надсилає телеметрію.
- [`monitor`](./monitor) — основний вебзастосунок для телеметрії станцій, керування акумуляторами, перевірками, циклами та передаванням між екіпажами.
- [`sarmat-crew`](./sarmat-crew) — нативний Android-застосунок екіпажу: список батарей, сканування LCD і запис вимірювань.
- [`theme`](./theme) — спільні ресурси оформлення Mission Planner для всіх плагінів.

Telemetry plugin і Monitor використовують компактний MessagePack-протокол через WebSocket. Під час локальної розробки вебінтерфейс типово доступний на `http://localhost:5173/` (або `https://localhost:5173/`, якщо налаштовані локальні сертифікати), а API — на `http://localhost:3000/`.

Детальні інструкції запуску є в [`monitor/README.md`](./monitor/README.md). Із кореня монорепозиторію доступні команди `npm run dev`, `npm run build`, `npm run typecheck`, `npm test`, `npm run db:migrate` і `npm run db:seed`.

## Railway

Monitor розгортається з одного репозиторію як два Railway-сервіси та керована PostgreSQL:

1. Створіть у Railway проєкт і додайте PostgreSQL.
2. Додайте два сервіси з цього GitHub-репозиторію: `frontend` і `backend`. Для обох залиште кореневу директорію `/`, оскільки застосунки використовують спільний workspace.
3. Для frontend у **Settings → Config file path** укажіть `/railway.frontend.json`.
4. Для backend укажіть `/railway.backend.json`.
5. Згенеруйте публічні домени для обох сервісів.
6. Додайте змінні frontend:

   ```text
   VITE_API_URL=https://<backend-domain>
   ```

7. Додайте змінні backend:

   ```text
   DATABASE_URL=${{Postgres.DATABASE_URL}}
   CLIENT_ORIGIN=https://<frontend-domain>
   NODE_ENV=production
   ```

   Назва `Postgres` у reference variable має збігатися з назвою PostgreSQL-сервісу в Railway. `CLIENT_ORIGIN` підтримує кілька адрес через кому, наприклад Railway-домен і власний домен.

Railway виконає міграції перед запуском backend. Початкові дані навмисно не додаються автоматично: за потреби виконайте `npm run db:seed` вручну з налаштованим `DATABASE_URL` та змініть демонстраційні паролі.

Для Mission Planner використовуйте `wss://<backend-domain>/ws/station`. Якщо frontend і backend мають різні домени, production-сесії використовують захищену cross-site cookie; для найкращої сумісності браузерів рекомендовані власні домени в межах одного сайту, наприклад `monitor.example.com` і `api.example.com`.

## Релізи

Тег у форматі `vMAJOR.MINOR.PATCH` створює два окремі пакети:

- `SarmatPlugins-<version>.msi`;
- `SarmatMonitor-<version>.zip`.

До пакетів додаються лише приклади конфігурації без робочих секретів.
