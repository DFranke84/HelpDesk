# EF Core migrations naming (Help Desk)

## Convention

- **Baseline migration:** `DBMigration01` (already in the repo as `20260330182400_DBMigration01`).
- **Additional migrations:** name the **migration** `DBMigrationxx`, where `xx` is the **next two-digit number in sequence** (`02`, `03`, …).

The **file name** still uses an EF timestamp prefix (e.g. `20260330120000_DBMigration02.cs`); only the **migration class name** must follow `DBMigrationxx`.

### How to pick the next `xx`

1. Open `*.Models/Migrations/`.
2. Find existing migrations whose **name part** (after the timestamp) matches `DBMigration*` — e.g. `*_DBMigration01.cs`, `*_DBMigration02.cs`.
3. Ignore `*.Designer.cs` and `*Snapshot.cs`.
4. Take the **highest** `xx` and add 1, with two digits (`09` → `10`).

If only `DBMigration01` exists, the next migration name is **`DBMigration02`**.

### CLI example

From the repo root (adjust paths if needed):

```bash
dotnet ef migrations add DBMigration02 ^
  --project *.Models ^
  --startup-project *.Web
```

On PowerShell use backtick line continuation or a single line:

```powershell
dotnet ef migrations add DBMigration02 --project *.Models --startup-project *.Web
```

Replace `DBMigration02` with the next name from the sequence above.

---

## Squashed history note (`DBMigration01` only)

The database schema is fully represented by **`20260330020409_DBMigration01`** (including `LoginID` on `Users` and `Audits`). Older separate migrations were merged into that file.

### If your database already had the **second** migration applied

If `__EFMigrationsHistory` contains a row for `20260330030334_RenameUserNameToLoginId` and you pull this squashed repo, remove that obsolete row so EF does not expect a missing migration:

```sql
DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20260330030334_RenameUserNameToLoginId';
```

Only do this if the live schema already matches the current model (LoginID columns present). When in doubt on a dev database, drop and recreate the database and run `dotnet ef database update` or let the app run `MigrateAsync`.
