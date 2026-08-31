# Sarmat Monitor

Sarmat Monitor is a local-first operational web application for monitoring UAV stations and managing battery fleets across independent operational groups and their crews. It keeps live telemetry, battery identity, checks, usage, and transfer history together so the record follows the physical pack.

## What is included

- Fastify + TypeScript REST API
- PostgreSQL 17 with Drizzle ORM and a checked-in SQL migration
- React + TypeScript client built with Vite, TanStack Query, and React Router
- Shared Zod input schemas and TypeScript API contracts
- Responsive fleet overview, battery details, cell voltage view, unified battery history, transfers, and configurable health/cycle thresholds
- Username/password authentication with database-backed HTTP-only sessions
- Server-enforced `SUPER_ADMIN`, group-isolated `GROUP_ADMIN`, and crew-isolated `CREW` roles
- Responsive admin dashboard for crews, credentials, batteries, corrections, archives, and global settings
- Live MissionPlanner telemetry for group-scoped administrators over an authenticated WebSocket endpoint
- Reusable battery-type catalog for capacity, voltage range, cell count, and chemistry
- Automatic English/Ukrainian client localization based on the browser locale, with English fallback
- Realtime, browser-side checker scanning for physical battery modules A and B
- Deterministic seven-segment recognition with a confirm/correct step before saving
- Demo data and server-side tests for battery health and authorization rules

## Local setup on macOS

Requirements: Node.js 20+, npm 10+, and a native PostgreSQL 17 installation. Docker is not used by this project.

### 1. Install PostgreSQL

The recommended macOS option is [Postgres.app](https://postgresapp.com/):

```bash
brew install --cask postgres-app
open -a Postgres
```

In Postgres.app, click **Initialize** to create and start the local server. Then add its command-line tools to your system path:

```bash
sudo mkdir -p /etc/paths.d
echo /Applications/Postgres.app/Contents/Versions/latest/bin | sudo tee /etc/paths.d/postgresapp
```

Close and reopen Terminal, then confirm PostgreSQL is available:

```bash
psql --version
pg_isready
```

Alternatively, install PostgreSQL 17 with Homebrew:

```bash
brew install postgresql@17
brew services start postgresql@17
export PATH="$(brew --prefix)/opt/postgresql@17/bin:$PATH"
```

### 2. Create the local SBM database

Create the development role and enter `sbm` when prompted for its password:

```bash
createuser -P sbm
createdb -O sbm sbm
```

Verify the connection expected by the application:

```bash
psql postgresql://sbm:sbm@localhost:5432/sbm -c "select 1;"
```

### 3. Configure and start SBM

```bash
cp .env.example .env
npm install
npm run db:migrate
npm run db:seed
npm run dev
```

Open [http://localhost:5173](http://localhost:5173). The API listens on [http://localhost:3000](http://localhost:3000); its health endpoint is `/health`.

The default `DATABASE_URL` is `postgresql://sbm:sbm@localhost:5432/sbm`. Update it in `.env` if your native PostgreSQL role, password, host, port, or database differs. Vite proxies `/api` to the local server in development. For Railway or another hosted API, provide the platform's `DATABASE_URL`; no application code changes are required.

### Development demo credentials

These accounts are created by `npm run db:seed` and are only for local development:

| Role | Scope | Username | Password |
| --- | --- | --- | --- |
| Super admin | All groups | `admin` | `SarmatAdmin!2026` |
| Group admin | Північ | `north-admin` | `NorthAdmin!2026` |
| Group admin | Південь | `south-admin` | `SouthAdmin!2026` |
| Crew | Північ / Червона станція | `red` | `RedCrew!2026` |
| Crew | Південь / Сокіл | `falcon` | `FalconCrew!2026` |

Set `SEED_ADMIN_USERNAME` and `SEED_ADMIN_PASSWORD` in `.env` before running the seed command to override the initial administrator. Change all seeded passwords outside local development.

## Useful commands

```bash
npm run dev           # API and client together
npm run build         # build all workspaces
npm run typecheck     # type-check all workspaces
npm test              # server unit tests
npm run db:generate   # generate a migration after schema changes
npm run db:migrate    # apply migrations
npm run db:seed       # add demo crews, packs, checks, and cycle data
```

## Architecture

```text
apps/
  server/             Fastify routes, server-side health rules, Drizzle schema
  client/             Responsive React SPA and API query layer
packages/
  shared/             Zod request schemas and cross-app TypeScript contracts
```

The API owns validation and business rules. When a battery check is recorded, the server verifies the number of cells, calculates min/max cell voltage and delta, reads the current warning/danger thresholds, assigns health, and stores both the result and the thresholds used. Changing thresholds therefore does not rewrite historical meaning.

Measurements are the primary source for cycle tracking. The server classifies readings using configurable charged/discharged percentage thresholds, ignores intermediate readings, records usage when a charged pack becomes discharged, and increments the cycle count only when a previously discharged pack becomes charged again. Repeated measurements in the same state do not add cycles. Inferred events reference their source measurement and are rebuilt after measurement corrections or threshold changes, preventing duplicates while preserving manual and imported history.

The battery page merges measurements, inferred charge/discharge transitions, manual maintenance records, and crew transfers into one chronological battery history. Manual events are limited to maintenance, repair, inspection, service, retirement, and notes; normal charge/discharge/cycle events come from measurements.

Battery specifications are normalized into reusable battery types. A type owns its name, capacity, minimum/maximum pack voltage, cell count, and chemistry. Individual batteries keep only their field name, serial number, selected type, operational state, notes, and crew assignment. Types that are already assigned to batteries cannot be deleted.

The client is a browser SPA with no Node-only dependencies. This keeps the UI suitable for a later Capacitor Android wrapper; native packaging and hardware checker integration are intentionally outside this iteration.

### Checker camera scanning

The battery-check form scans Battery A and Battery B separately with the rear browser camera. The guide matches the LCD display itself, so the operator fills the frame with only the screen. The browser crops that region to 340×600 pixels and runs the deterministic seven-segment recognizer about four times per second.

Red, yellow, and green guide states indicate missing/invalid, partial/unstable, and stable readings. A result is locked only when the same six valid cell values occur in at least three of the last five attempts. The operator can retry, confirm, and edit every voltage before saving. The displayed checker Total is excluded from analysis.

Recognition uses only TypeScript, Canvas, and ImageData in the browser. It works without a server connection and never uploads or stores camera frames. Browser camera access requires a secure context: use HTTPS on deployed/mobile devices (`localhost` remains permitted for desktop development).

The server receives only cell voltages. It independently validates the count and battery-type range, then recalculates totals, min/max, delta, charge, health, and inferred charge/discharge transitions before persisting the measurement.

## Authentication and authorization

There is no registration route. Passwords are hashed with bcrypt at cost 12. Login creates a random seven-day session token; only its SHA-256 hash is stored in PostgreSQL, while the browser receives the token in an HTTP-only, same-site cookie. Password changes and credential disabling invalidate existing sessions.

Every `/api` route except login requires a valid session. The hierarchy is `Group → Crews → Batteries`. A crew belongs to exactly one group and can be marked as reserve; crew numbers are unique inside their group. Battery group ownership is derived through its current crew, while measurements and transfer history remain attached to the battery.

The server derives scope from the authenticated session—client-supplied group or crew IDs cannot widen access:

- `SUPER_ADMIN` is global and manages groups, group administrators, all crews, all batteries, cross-group transfers, battery types, and global settings.
- `GROUP_ADMIN` is assigned to one group and can manage that group's crews, reserve designation, crew credentials, batteries, measurements, and transfers between crews in that same group.
- `CREW` is assigned to one crew and only sees that crew's operational batteries, checker workflow, measurements, and history.

All direct resource lookups resolve the owning crew and group before returning data. Cross-group access through altered REST IDs is returned as not found. A group administrator cannot transfer a battery outside their group; a super administrator can. Transfers only change the battery's current crew and append a transfer record, so measurements, cycles, checker data, and previous transfers remain intact. Disabled users, crews, and groups are checked both during login and on protected requests.

The checked-in migrations create the schema, including groups, all three roles, and per-crew telemetry secrets. Run `npm run db:migrate` after pulling schema changes.

Super administrators can use `/admin/groups` to create, edit, disable, and inspect groups and issue multiple group-administrator accounts. Opening a group shows its crews, battery health summary, and administrators. Group administrators enter directly into their own scoped dashboard and never receive a group selector.

Administrators can use `/admin` to:

- enable, disable, inspect, safely delete, or edit crews;
- issue, reset, disable, or delete crew credentials;
- inspect and manage batteries across crews, including transfers and archived packs;
- correct measurements while retaining correction metadata;
- edit global health and charged/discharged state thresholds (`SUPER_ADMIN` only).

Crew users are sent directly to their own operational fleet. They do not see or supply a crew selector.

## MissionPlanner telemetry

Administrators configure a visible, unique telemetry secret in the crew edit form. In the Sarmat MissionPlanner plugin, enable **Monitor** and set:

- WebSocket URL: `ws://<server>:3000/ws/station` locally, or the deployed `wss://<host>/ws/station` URL;
- Station secret: the secret from that crew's settings.

The server accepts the plugin's existing 9-field MessagePack packet once per second and keeps only the latest reading in memory. It does not store telemetry history. The **Telemetry** page polls live snapshots, marks delayed data stale after 5 seconds and offline after 10 seconds, and applies the same voltage, current, satellite, HDOP, and link thresholds as Sarmat Monitor.

`GROUP_ADMIN` users only receive crews from their own group. `SUPER_ADMIN` users select a group. Crew accounts cannot access the page or `GET /api/telemetry`. Disabled crews and groups cannot establish a telemetry WebSocket connection.

## Localization

The React client detects Ukrainian browser locales (`uk` and `uk-*`) automatically and otherwise uses English. English is also the fallback for missing translation keys. All client language constants live in:

- `apps/client/src/locales/en.json`
- `apps/client/src/locales/uk.json`

## API summary

- `POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/auth/me`
- `GET/POST /api/groups`, `GET/PATCH/DELETE /api/groups/:id` (`SUPER_ADMIN` writes; scoped read for `GROUP_ADMIN`)
- `GET/POST /api/crews`, `PATCH/DELETE /api/crews/:id` (scoped administrator)
- `GET /api/telemetry` (group-scoped administrator snapshots)
- `GET /ws/station` (MissionPlanner plugin WebSocket authenticated by crew secret)
- `GET /api/admin/users`, `POST /api/admin/crews/:id/users`, `PATCH/DELETE /api/admin/users/:id`
- `POST /api/admin/group-users` (`SUPER_ADMIN` creates group administrators)
- `GET/POST /api/batteries`, `GET/PATCH /api/batteries/:id`
- `GET/POST /api/battery-types`, `PATCH/DELETE /api/battery-types/:id` (admin)
- `POST /api/batteries/:id/transfer` (same-group for `GROUP_ADMIN`; cross-group for `SUPER_ADMIN`)
- `POST /api/batteries/:id/measurements`
- `POST /api/batteries/:id/measurement-preview` (structured A/B cell values only)
- `POST /api/batteries/:id/cycles` (manual maintenance/repair/inspection/service/retirement/note events only)
- `PATCH /api/admin/measurements/:id` (admin correction)
- `POST /api/admin/batteries/:id/archive|restore`
- `GET/PUT /api/settings/thresholds` for cell-health and charge-state thresholds (`PUT` is admin-only)
