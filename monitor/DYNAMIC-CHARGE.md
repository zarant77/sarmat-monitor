# Dynamic charge percentage rollout

Scope: derive charge from stored total voltage and the current battery type limits.
Keep `chargePercent` in API responses for web and Android compatibility.

Stages:
1. Centralize the calculation and replace stored-percentage reads in API and cycle inference.
2. Rebuild inferred events atomically when voltage limits or a battery's type change; refresh client caches.
3. Remove the obsolete database column, and rebuild existing inferred events during migration.

No tests are to be run for this task, per user instruction. TypeScript checks are allowed.
Production deployment and database migration are not performed by this implementation task.
