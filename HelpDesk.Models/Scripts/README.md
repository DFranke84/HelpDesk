# Creating a new module

A module is a separate project, shaped like `HelpDesk.Books`, that adds a feature with its own database to the Help Desk. `New-HelpDeskModule.ps1` creates the empty shell for one; this guide covers running it and turning the shell into a working feature.

## 1. Run the script

From the repository root, in Windows PowerShell:

```
powershell -ExecutionPolicy Bypass -File HelpDesk.Models\Scripts\New-HelpDeskModule.ps1
```

It asks two questions:

1. **Module name**, for example `Inventory` or `asset tracking`. The name is converted to PascalCase (`AssetTracking`). That name is used everywhere below as **`{Name}`**.
2. **A or B**
   - **A**: create the project and add it to `HelpDesk.sln` only.
   - **B**: also wire it into `HelpDesk.Web`. Choose this unless you want to do the wiring by hand.

The script then builds the solution. It refuses to run if `HelpDesk.{Name}` already exists.

### What it creates

```
HelpDesk.{Name}/
├── HelpDesk.{Name}.csproj                   Razor class library, references HelpDesk.Models
├── _Imports.razor
├── Extensions/ServiceCollectionExtensions.cs AddHelpDesk{Name}(...) and MapHelpDesk{Name}()
├── Models/Module.cs                         placeholder for your entity namespace
├── Models/Data/{Name}DbContext.cs           empty EF Core context
├── Models/Data/{Name}DbContextFactory.cs    design-time factory for migrations
└── wwwroot/                                 embedded static files, served at /{name-in-kebab-case}-assets
```

### What option B changes in `HelpDesk.Web`

| File | Change |
|---|---|
| `HelpDesk.Web.csproj` | Project reference to the module |
| `appsettings.json`, `appsettings.Development.json` | A `ConnectionStrings:{Name}` entry with a placeholder value |
| `Program.cs` | `using HelpDesk.{Name}.Extensions;`, `AddHelpDesk{Name}(...)`, `MapHelpDesk{Name}()`, and the module assembly added to `AddAdditionalAssemblies(...)` |
| `Components/Routes.razor` | The module assembly added to the router's `AdditionalAssemblies` |

If you chose **A**, make those four changes yourself, copying how `HelpDesk.Books` appears in each file.

## 2. Point it at a database

Create the SQL Server database, then set the real connection string. Keep credentials out of the repository:

```
cd HelpDesk.Web
dotnet user-secrets set "ConnectionStrings:{Name}" "Server=.;Database={Name};Integrated Security=true;TrustServerCertificate=true;MultipleActiveResultSets=true"
```

On a server use the environment variable `ConnectionStrings__{Name}`.

## 3. Add entities and migrations

1. Add entity classes under `HelpDesk.{Name}/Models` and `DbSet` properties to `{Name}DbContext`.
2. Create a migration from the module folder:

   ```
   cd HelpDesk.{Name}
   dotnet ef migrations add {Name}Initial --startup-project ../HelpDesk.Web --output-dir Models/Migrations
   ```

3. Apply it with `dotnet ef database update --startup-project ../HelpDesk.Web`, or generate a SQL script as `HelpDesk.Books` does. Module databases are **not** migrated automatically at start-up; only the main HelpDesk database is.

See [migration naming](../../Docs/EF-Migrations-Naming.md).

## 4. Add a page and a data API

Follow `HelpDesk.Books`:

- **Page**: a routable component such as `Components/{Name}List.razor` with `@page "/user/{name}/list"` and `@attribute [Authorize]`.
- **API**: a controller under `Controllers/` for grid data, marked `[ApiController]` and `[Authorize]`.
- **Script**: put grid JavaScript in `wwwroot/js`. Escape any database text you place in a grid cell (`window.helpdeskEscapeHtml`), and add a `?v=` value to the script address so browsers pick up new versions.

## 5. Enforce permissions on the server

This step is not optional. Hiding a button does not stop anyone calling the server.

1. In the module, define a small interface, as Books does in `HelpDesk.Books/Services/IBooksAccess.cs`, that returns the current user's Read / Create / Update / Delete rights.
2. In `HelpDesk.Web`, implement it with `IPagePermissionService` (copy `Services/BooksAccess.cs`, changing the page route) and register it in `Program.cs`.
3. In the page, check the rights again on the server before every add, edit and delete, including methods called from JavaScript (`[JSInvokable]`).
4. In the API, return 403 when the caller lacks Read.

## 6. Put it in the menu

Sign in as an administrator and use the administration screens:

1. **Applications**: add the application. If it has its own database, tick *Uses external database* and enter the connection string key (`{Name}`).
2. **Pages**: add a page for that application with *Internal Route* set to your component's route.
3. **Groups** and **Application groups**: create or choose a group and link it to the application.
4. **User groups**: put users (or Windows group accounts) in that group.
5. **User page permissions**: grant Read, and Create / Update / Delete as needed.

Administrators see the new page immediately; other users see it once steps 3 to 5 are done. If you want the catalog entries created automatically on a fresh install, add them to `HelpDesk.Web/Data/DbInitializer.cs` the way the Books entries are.

## 7. Add tests

Add tests to `HelpDesk.Tests` for at least: the API refusing a user without Read, and the page refusing a change from a read-only user. `Components/BooksCatalogTests.cs` and the Books tests in `Security/AccessControlTests.cs` are the patterns to copy. If the module's connection string is required at start-up, also set it in `Infrastructure/AppFixture.cs` alongside the HelpDesk and Books ones.

## Other files in this folder

- `HelpDesk-DBMigration01.sql`: a SQL script for the main HelpDesk database schema, for environments where migrations are applied by script rather than at start-up.
