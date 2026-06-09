# Manual Cloudflare Dashboard Deployment

This guide assumes you are still pasting Worker code manually in the Cloudflare dashboard.

## 1. D1

1. Open Cloudflare Dashboard.
2. Go to Workers & Pages -> D1.
3. Open your existing database or create a new one.

If this is a brand-new empty database, run `schema.sql`.

If this is your current production database with existing Steam Workshop/player
translations, do not run `schema.sql` as a migration. Use this safer order:

1. Run `d1-tools/inspect-existing-registry.sql`.
2. Confirm the record count looks right.
3. Run `migrations/0001_add_rbac_tables.sql`.
4. Deploy the Worker.
5. Later, add only missing optional columns from `d1-tools/optional-registry-columns.sql`.

The RBAC migration only creates new tables. It does not delete or replace rows
in `TranslationRegistry`.

## 2. R2

1. Open R2.
2. Create or open the translation ZIP bucket.
3. Bind it to the Worker as `BUCKET`.

## 3. Worker Bindings

Bind:

- D1 database as `DB`
- R2 bucket as `BUCKET`

## 4. Worker Secrets

Set these Worker secrets:

- `MASTER_SECRET`: your master bootstrap code, for example `MINI_666`
- `TOKEN_HASH_PEPPER`: a long random string used when hashing privilege codes
- `LEGACY_OFFICIAL_SECRETS`: optional comma-separated old official codes, for example `RIM_GOD_001,RIM_GOD_002`

Do not hardcode real secrets in `worker-v2.js`.

## 5. Paste Worker

Copy `worker-v2.js` into the Worker dashboard editor.

## 6. Smoke Tests

Open:

- `/api/v1/health`
- `/api/v1/registry`

Before testing uploads on production, confirm the registry still returns the
existing translations.

Create first privilege code using `MASTER_SECRET`:

```http
POST /api/v1/admin/privilege-codes
X-Admin-Token: MINI_666
Content-Type: application/json

{
  "label": "Example Group",
  "ownerName": "Translator",
  "groupName": "Example Team",
  "role": "official_group",
  "scopes": ["upload:official", "record:delete:own"],
  "expiresAt": null,
  "notes": "First test code"
}
```

The raw generated code is returned once. Store it safely.
