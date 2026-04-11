# HelpDesk module scripts

## `New-HelpDeskModule.ps1`

Scaffolds a new `HelpDesk.{Name}` Razor class library (same shape as `HelpDesk.Books`) and adds it to `HelpDesk.sln`.

If you chose **A (library + solution only)**, the new project is not referenced by `HelpDesk.Web` yet. Use the steps below to wire it in manually. If you chose **B**, these steps were applied for you; you can still use this file as a checklist.

Throughout, replace **`{Name}`** with your module’s PascalCase name (for example **`Inventory`**). Types and extension methods use that name: `{Name}DbContext`, `AddHelpDesk{Name}`, `MapHelpDesk{Name}`.

### 1. Reference the module from the web app

In [`HelpDesk.Web/HelpDesk.Web.csproj`](../../HelpDesk.Web/HelpDesk.Web.csproj), add a project reference next to the other module references:

```xml
<ProjectReference Include="..\HelpDesk.{Name}\HelpDesk.{Name}.csproj" />
```

### 2. Connection strings

The module’s design-time factory and runtime registration both read **`ConnectionStrings:{Name}`** (the key matches the PascalCase module name).

Add the key to both:

- [`HelpDesk.Web/appsettings.json`](../../HelpDesk.Web/appsettings.json)
- [`HelpDesk.Web/appsettings.Development.json`](../../HelpDesk.Web/appsettings.Development.json)

Example (adjust server and database to your environment):

```json
"ConnectionStrings": {
  "HelpDesk": "...",
  "Books": "...",
  "{Name}": "Server=.;Database={Name};MultipleActiveResultSets=true"
}
```

### 3. Register services and static files in `Program.cs`

In [`HelpDesk.Web/Program.cs`](../../HelpDesk.Web/Program.cs):

1. Add a using for the module’s extensions namespace:

   ```csharp
   using HelpDesk.{Name}.Extensions;
   ```

2. After the existing module registrations (for example after `AddHelpDeskBooks`), register yours. Use a camelCase variable for the connection string (first letter of `{Name}` lowercased, e.g. `inventoryCs` for `Inventory`):

   ```csharp
   var inventoryCs = builder.Configuration.GetConnectionString("Inventory")
       ?? throw new InvalidOperationException("Connection string 'Inventory' is not configured.");
   builder.Services.AddHelpDeskInventory(inventoryCs);
   ```

3. After `app.MapHelpDeskBooks();` (or alongside other `Map*` calls), map this module’s pipeline (embedded `wwwroot` under `/{kebab}-assets`, e.g. `/inventory-assets` for `Inventory`):

   ```csharp
   app.MapHelpDeskInventory();
   ```

4. Include the module assembly in Blazor’s additional assemblies so routable components in the library are discovered. Extend the existing `AddAdditionalAssemblies` call to add a second (or further) `typeof(...)` — do not register the same assembly twice:

   ```csharp
   .AddAdditionalAssemblies(
       typeof(HelpDesk.Books.Models.Data.BooksDbContext).Assembly,
       typeof(HelpDesk.{Name}.Models.Data.{Name}DbContext).Assembly);
   ```

### 4. Router additional assemblies

In [`HelpDesk.Web/Components/Routes.razor`](../../HelpDesk.Web/Components/Routes.razor), add the same `typeof(HelpDesk.{Name}.Models.Data.{Name}DbContext).Assembly` entry to the `AdditionalAssemblies` array so it matches `Program.cs`.

### 5. Build and database

- Run `dotnet build` on the solution.
- Create the SQL Server database and point the connection string at it.
- Add entities to `HelpDesk.{Name}/Models`, configure `{Name}DbContext`, then add EF Core migrations from the module project with `--startup-project ../HelpDesk.Web` (same workflow as [HelpDesk.Books README](../../HelpDesk.Books/README.md)).

### What this does not cover

Menu entries, `DbInitializer` seed data, and HelpDesk authorization pages for the new app are separate from this shell; add those when you expose real pages and APIs.
