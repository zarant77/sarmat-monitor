# Sarmat

Основний монорепозиторій Sarmat для моніторингу станцій, керування парком акумуляторів і інтеграції з Mission Planner.

Розгорнутий опис призначення, архітектури, реалізованих можливостей і алгоритму сканування LCD наведено у [`SUMMARY.md`](./SUMMARY.md).

## Структура

- [`telemetry-plugin`](./telemetry-plugin) — плагін для Mission Planner, який надсилає телеметрію.
- [`monitor`](./monitor) — основний вебзастосунок для телеметрії станцій, керування акумуляторами, перевірками, циклами та передаванням між екіпажами.
- [`sarmat-crew`](./sarmat-crew) — нативний Android-застосунок екіпажу: список батарей, ручне введення та запис вимірювань.
- [`theme`](./theme) — спільні ресурси оформлення Mission Planner для всіх плагінів.

Telemetry plugin і Monitor використовують компактний MessagePack-протокол через WebSocket. Під час локальної розробки вебінтерфейс типово доступний на `http://localhost:5173/` (або `https://localhost:5173/`, якщо налаштовані локальні сертифікати), а API — на `http://localhost:3000/`.

Детальні інструкції запуску є в [`monitor/README.md`](./monitor/README.md). Із кореня монорепозиторію доступні команди `npm run dev`, `npm run build`, `npm run typecheck`, `npm test`, `npm run db:migrate` і `npm run db:seed`.

У Windows двічі натисніть **`launcher.bat`** у корені проєкту. Меню та команди відповідають `launcher.sh`:

1. Запуск Monitor у dev-режимі.
2. Production-збірка Monitor.
3. Android release — APK та AAB (без підпису, якщо signingConfig не налаштовано).
4. Збірка й встановлення Android debug на телефон.
5. Збірка Android debug APK.
6. Тести Monitor та Android.
7. Перевірка TypeScript.
8. Міграції БД.
9. Початкові дані БД.
10. Встановлення Node.js-залежностей (`npm ci`).
11. Збірка плагіна Mission Planner (Windows).

Команди без меню: `launcher.bat dev`, `build`, `android-release`, `android-debug`, `android-install DEVICE_SERIAL`, `test`, `typecheck`, `db-migrate`, `db-seed`, `install`, `help`. Підтримуються ті самі короткі назви, що в `launcher.sh`.

Windows-лаунчер автоматично знаходить JDK 17/21 та Android SDK 36. Збірка APK не потребує телефона; встановлення потребує USB debugging. Перша збірка може завантажувати залежності. Готові Android-файли містяться в `sarmat-crew/app/build/outputs/`.

Пункт **11** у Windows збирає Release DLL, запускає тести й готує `telemetry-plugin/dist`. Команда без меню: `launcher.bat plugin`, або `launcher.bat plugin "D:\Mission Planner"` для іншої інсталяції. У `launcher.sh` цей пункт лише повідомляє, що потрібна Windows, і повертає до меню. Нижчорівневий скрипт також доступний: `telemetry-plugin\scripts\build.bat "C:\Program Files (x86)\Mission Planner"`. Кореневий `build.bat` видалено.

Для Linux/macOS використовуйте кореневий launcher:

```bash
./launcher.sh
```

Він відкриває інтерактивне меню для dev-режиму, production build, тестів, міграцій та Android-збірок. Ті самі операції доступні без меню, наприклад `./launcher.sh dev`, `./launcher.sh android-release` або `./launcher.sh android-install DEVICE_SERIAL`.

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
