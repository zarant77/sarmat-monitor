# Sarmat Monitor

Node.js сервер, який автентифікує станції Sarmat, зберігає їхній останній пакет телеметрії в пам'яті та показує стан усіх станцій у вебінтерфейсі. Історія на диск не записується.

## Вимоги

- Node.js 20 або новіший

## Налаштування і запуск

Скопіюйте `config.example.json` у проігнорований `config.json`, задайте один спільний секрет і запустіть сервер:

```powershell
Copy-Item config.example.json config.json
corepack pnpm install
corepack pnpm start
```

Вебпанель відкривається за адресою `http://localhost:8080/`. Для входу використовується той самий спільний секрет. Панель щосекунди оновлює дані для всіх підключених станцій.

Інший конфігураційний файл можна вказати через `SARMAT_CONFIG`. Для Railway та інших хостингів повний JSON можна передати через `SARMAT_CONFIG_JSON`; змінна `PORT` перевизначає порт.

Порогові значення кольорів задаються у `thresholds`: для напруги, супутників і RSSI більше значення краще (`goodMin`/`normalMin`), а для струму та HDOP — менше краще (`goodMax`/`normalMax`). Типові пороги наведені в `config.example.json`. Зв'язок передається і показується як RSSI у dBm.

OBS оцінюється з урахуванням стану апарата: для Armed `REC` — good, `NR` — bad; для Disarmed `REC` — normal, `NR` — good.

## Маршрути

- `GET /` — вебінтерфейс моніторингу.
- `POST /api/login` — перевірка спільного секрету.
- `GET /api/stations` — актуальний JSON-знімок підключених станцій.
- `GET /health` — стан процесу.
- `/ws/station` — підключення станцій.
API та WebSocket-маршрут вимагають заголовок `Authorization: Bearer <secret>`. Поза ізольованою мережею використовуйте TLS (`wss://`/`https://`). Першим текстовим WebSocket-повідомленням станція передає `{"name":"Red","color":"#FF0000"}`, після чого надсилає бінарну телеметрію.

Станція надсилає бінарний MessagePack-масив:

```text
[sequence, voltageV, currentA, satellites, hdop, headingDeg,
 relativeAltitudeM, linkRssiDbm, flags]
```

`flags`: біт 0 — запис OBS, біт 1 — ARMED.
