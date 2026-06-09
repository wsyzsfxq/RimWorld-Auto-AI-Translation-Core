# File-Based Cloudflare Worker Deployment

This is the recommended path once the dashboard-pasted Worker becomes hard to
maintain.

## 0. Production Data Rule

Changing from dashboard paste to file-based Wrangler deployment does not clear
D1 data by itself.

Data is only removed if you run destructive SQL such as `DROP TABLE`, `DELETE`,
or deploy Worker logic that deletes records. For the existing production D1
database, do not run `schema.sql` as a migration.

The current `worker-v2.js` keeps upload cleanup and scheduled purge disabled by
default:

```js
const ENABLE_UPLOAD_HISTORY_CLEANUP = false;
const ENABLE_SCHEDULED_PURGE = false;
```

Keep both disabled for the first production rollout.

## 1. Install Wrangler Project Files

From this folder:

```powershell
cd C:\Users\g1061\Documents\GitHub\RimWorld-Auto-AI-Translation-Core\cloudflare\worker
npm install
copy wrangler.toml.example wrangler.toml
```

Edit `wrangler.toml`:

- `database_name`: your existing D1 database name
- `database_id`: your existing D1 database id
- `bucket_name`: your existing R2 bucket name

Do not create a new D1 database unless you are intentionally making a test
environment.

## 2. Login

```powershell
npx wrangler login
```

## 3. Inspect Existing D1

```powershell
npx wrangler d1 execute YOUR_D1_DATABASE_NAME --remote --file d1-tools/inspect-existing-registry.sql
```

Check that the total count matches your expected workshop data count before
continuing.

## 4. Add RBAC Tables

```powershell
npx wrangler d1 execute YOUR_D1_DATABASE_NAME --remote --file migrations/0001_add_rbac_tables.sql
```

This migration creates privilege-code and audit tables only. It does not delete
or modify existing translation rows.

## 5. Set Secrets

```powershell
npx wrangler secret put MASTER_SECRET
npx wrangler secret put TOKEN_HASH_PEPPER
npx wrangler secret put LEGACY_OFFICIAL_SECRETS
```

Use `MINI_666` as `MASTER_SECRET` only if you still want that as the bootstrap
code. `TOKEN_HASH_PEPPER` should be a long random string.

`LEGACY_OFFICIAL_SECRETS` is optional. Use it during migration if existing
translator groups already have old fixed official codes, for example:

```text
RIM_GOD_001,RIM_GOD_002,RIM_GOD_003
```

## 6. Deploy

```powershell
npx wrangler deploy
```

## 7. Smoke Test

Test these endpoints first:

- `GET /api/v1/health`
- `GET /api/v1/registry`
- `GET /api/v1/download/{packageId}/{language}`

Only test production upload after registry/download still show the old records.

## 8. Optional Registry Columns

After everything is stable, compare the output of `PRAGMA table_info` with
`d1-tools/optional-registry-columns.sql`.

Only uncomment and run missing `ALTER TABLE` lines. Do not run the whole file
blindly, because D1/SQLite errors when adding a column that already exists.
