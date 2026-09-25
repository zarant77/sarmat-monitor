# Dynamic charge percentage rollout

Scope: derive charge from stored total voltage and the current battery type limits.
Keep `chargePercent` in API responses for web and Android compatibility.

Implementation status:
1. Complete: one server calculation is used by list/detail/history responses, previews and cycle inference. The API still returns `chargePercent`.
2. Complete: edits to voltage limits, battery type assignments and cycle thresholds rebuild affected inferred events in the same transaction. Measurement creation/correction does likewise. Client caches are invalidated after edits.
3. Complete: migration `0006_dynamic_charge_percent.sql` removes the obsolete column. `db:migrate` then rebuilds all inferred events from measured voltages and current type limits, including archived batteries.

The rebuild preserves measurements and manual lifecycle/transfer events. Only inferred charge/discharge events are replaced; inferred event IDs and cycle counts can change. No historical voltage is rejected merely because it is below the new minimum: the computed charge is clamped to 0–100%.

Deployment still pending:
- Use the new application version together with this migration. Old server versions require the removed column.
- During deployment, stop old server instances, run `npm run db:migrate` from the repository root, then start the new server. Do not apply only the SQL file: the command also rebuilds existing history.
- The rebuild runs in a transaction and temporarily blocks writes to its source tables. If it fails, migration exits unsuccessfully; fix the cause and rerun the command before starting the server. Re-running recalculates derived events safely.
- No Android update is required for the API response format. Existing screens need to refresh their data to show the new values.

No tests are to be run for this task, per user instruction. TypeScript checks are allowed.
Production deployment and database migration are not performed by this implementation task.

Validation completed: TypeScript checks for server, client and shared package; static search found no remaining stored-percentage reads/writes in server source. Tests were not run.
