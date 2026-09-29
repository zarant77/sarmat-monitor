# Drone flight-hours rollout

This runbook is the production gate for drone and motor flight-hour accounting. Do not treat a successful web build as a substitute for the Mission Planner field check.

## 1. Before deployment

1. Back up the PostgreSQL database and verify that the backup can be read.
2. Record the currently deployed Monitor and plugin versions.
3. Prepare the real inventory: crew, drone name, model, motor count (4 or 6), motor serial numbers, position numbers, and existing flight hours.
4. Close Mission Planner before installing or replacing the plugin DLL.

## 2. Database and Monitor

From the deployed `monitor` directory, with the production `DATABASE_URL` configured:

```bash
npm ci
npm run db:migrate
npm run typecheck
npm test
npm run build
```

The migration command must finish with `Database migrations and inferred battery history rebuild complete.` The database must contain migrations through `0012_bizarre_bastion` before the new plugin is enabled.

Start the server and verify:

- `GET /health` returns `{ "status": "ok" }`;
- a group administrator sees only their own group's drones and motors;
- both 4- and 6-motor drones can be registered;
- initial flight hours are correct before telemetry is enabled.

## 3. Windows plugin

On the Mission Planner workstation run:

```powershell
.\telemetry-plugin\scripts\build.ps1 -MissionPlannerPath "C:\Program Files (x86)\Mission Planner"
```

Install the generated package, start Mission Planner, enable API access and automatic battery tracking, then select both the current drone and battery.

## 4. Field acceptance test

Use a safe bench setup first. Record the wall-clock times for comparison.

1. Connect while disarmed and confirm the intended drone and battery.
2. Arm, wait at least 30 seconds, then disarm.
3. Confirm that exactly one completed flight appears and that its duration is reasonable.
4. Confirm that the drone and every motor installed at arming received the same increment.
5. Replace one motor in Monitor, repeat the flight, and confirm that only the replacement motor receives the new increment.
6. Disconnect the network, complete another arm/disarm pair, restore the network, and confirm that the queued flight arrives once without duplication.
7. Arm and terminate Mission Planner before disarming. Confirm that the drone registry shows an unfinished-flight warning, then complete it through the correction dialog with a reason.
8. Confirm that excluding and restoring a flight recalculates the drone and motor totals.
9. Confirm that a drone with an unfinished flight cannot be retired.

## 5. Rollback

If telemetry ingestion is incorrect, disable API access in the plugin first. Keep the database: flight events are idempotent and corrections are audited. Restore the previous Monitor/plugin release only after capturing logs and the affected flight IDs. Do not delete flight rows manually; exclude or correct them through the administrative UI.

Database restoration is the last resort and must use the backup taken immediately before migration. A code rollback does not automatically reverse database migrations.
