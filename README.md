# HelpDesk

Internal help desk application built with ASP.NET Core 8 and Blazor Server.

## Architecture

The solution contains four projects:

| Project | Purpose |
|---------|---------|
| **HelpDesk.Web** | Blazor Server host — UI, controllers, middleware, services |
| **HelpDesk.Models** | EF Core domain models, DbContext, migrations |
| **HelpDesk.Books** | Books catalog library — models, controllers, UI components |
| **HelpDesk.Logging** | Cross-cutting logging and logger abstractions (NLog) |

### Key technology

- **.NET 8** / Blazor Server (interactive server-side rendering)
- **Entity Framework Core 8** with SQL Server
- **Cookie + Windows Negotiate** authentication
- **NLog** for structured logging (file, database, email alerts)
- **jQuery DataTables** for admin grids (JS interop)

### Middleware pipeline (order matters)

1. `LoggingScopeMiddleware` — sets correlation and user context for NLog
2. `WindowsAutoLoginMiddleware` — auto-signs-in domain users via Negotiate
3. `MustChangePasswordMiddleware` — redirects users who must change their password
4. Authorization / Antiforgery

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- SQL Server (local or remote)

## Getting started

### 1. Clone and restore

```
git clone <repo-url>
cd HelpDesk
dotnet restore
```

### 2. Configure connection strings

The application uses [User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) for sensitive configuration in development. Non-sensitive defaults are in `appsettings.json`.

```
cd HelpDesk.Web
dotnet user-secrets set "ConnectionStrings:HelpDesk" "Server=.;Database=HelpDesk;User Id=<user>;Password=<password>;TrustServerCertificate=True;MultipleActiveResultSets=true"
dotnet user-secrets set "ConnectionStrings:Books" "Server=.;Database=Books;User Id=<user>;Password=<password>;TrustServerCertificate=True;MultipleActiveResultSets=true"
```

For Windows integrated auth, omit `User Id` and `Password` and use `Integrated Security=True` instead.

### 3. Configure SMTP (optional)

SMTP credentials should also go in User Secrets:

```
dotnet user-secrets set "Smtp:Direct:User" "<smtp-user>"
dotnet user-secrets set "Smtp:Direct:Password" "<smtp-password>"
dotnet user-secrets set "Smtp:AlertTo" "<alert-email>"
dotnet user-secrets set "Smtp:NormalTo" "<normal-email>"
```

### 4. Run

```
cd HelpDesk.Web
dotnet run --launch-profile https
```

The app will:
1. Apply any pending EF Core migrations automatically on startup
2. Seed an initial admin account if no users exist (login: `ADMIN`, password: `ChangeMe!123`)
3. Open at **https://localhost:7099**

The initial admin password must be changed on first login.

## Database

### Migrations

Migrations live in `HelpDesk.Models/Migrations/`. To add a new migration:

```
cd HelpDesk.Models
dotnet ef migrations add <MigrationName> --startup-project ../HelpDesk.Web
```

Migrations are applied automatically at startup via `DbInitializer.MigrateAsync`. See `Docs/EF-Migrations-Naming.md` for naming conventions.

### Books catalog

The Books catalog functionality has been moved to a separate `HelpDesk.Books` library project. This includes a secondary `BooksDbContext` that connects to a separate `Books` database, demonstrating external database integration and modular architecture. See the `HelpDesk.Books` project documentation.

## Authentication

The app supports two authentication modes:

- **Forms login** — username/password with cookie authentication
- **Windows Negotiate** — automatic domain login for intranet users

Windows auto-login can be suppressed per-user (manual logout sets a cookie that prevents re-negotiation for a configurable period).

Login is rate-limited: after 5 failed attempts within 15 minutes, the account is temporarily blocked. These defaults are configurable via `App:LoginRateLimit:MaxAttempts` and `App:LoginRateLimit:WindowMinutes`.

## Authorization

- **Fallback policy**: all routes require an authenticated user by default
- **Admin role**: admin pages (`/admin/*`) and diagnostics (`/diag/*`) require `Role = Admin`
- **Group-based catalog access**: non-admin users see only applications and pages assigned to their groups

## Project structure

```
HelpDesk.Web/
├── Components/
│   ├── Layout/          # MainLayout, LoginLayout
│   ├── Pages/           # Razor page components
│   └── ...              # Shared components (NavMenu, LoginPanel, etc.)
├── Controllers/         # MVC endpoints (account, grid APIs)
├── Middleware/           # Request pipeline middleware
├── Services/            # Application services (auth, email, menus, etc.)
├── Data/                # DbInitializer (startup seeding)
├── Configuration/       # SMTP profile merging
├── Validation/          # Shared validation helpers
└── wwwroot/
    ├── js/              # DataTables interop, auth, idle timeout
    └── css/             # Application styles

HelpDesk.Models/
├── Data/                # DbContexts and design-time factory
├── External/            # External database context factory
├── Migrations/          # EF Core migrations
└── *.cs                 # Domain entities (User, DeskApplication, etc.)

HelpDesk.Books/
├── Models/              # Book and Publisher entities
├── Data/                # BooksDbContext and design-time factory
├── Controllers/         # Books catalog API controller
├── Components/          # BooksCatalog Blazor component
├── Migrations/          # Books database migrations
├── Extensions/          # Service registration extensions
├── wwwroot/js/          # Books-specific JavaScript
└── Database/            # SQL scripts for Books database setup

HelpDesk.Logging/
└── *.cs                 # Correlation, logger, event log abstractions
```

## Deployment

### IIS hosting

1. Publish the application:
   ```
   dotnet publish HelpDesk.Web -c Release -o ./publish
   ```
2. Create an IIS site pointing to the `publish` folder.
3. Install the [ASP.NET Core Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/8.0) on the server.
4. Set the application pool to **No Managed Code**.
5. For Windows Negotiate authentication, enable **Windows Authentication** on the IIS site and disable **Anonymous Authentication**.
6. Configure environment-specific settings via environment variables or a production `appsettings.Production.json` (do **not** copy development secrets).

### Environment variables

Sensitive values should be set as environment variables rather than stored in configuration files:

| Variable | Maps to |
|----------|---------|
| `ConnectionStrings__HelpDesk` | `ConnectionStrings:HelpDesk` |
| `ConnectionStrings__Books` | `ConnectionStrings:Books` |
| `Smtp__Direct__User` | `Smtp:Direct:User` |
| `Smtp__Direct__Password` | `Smtp:Direct:Password` |
| `App__BaseUrl` | `App:BaseUrl` |

### Production checklist

- [ ] Rotate any credentials that were committed to source control during development
- [ ] Set `AllowedHosts` to the production hostname(s)
- [ ] Set `App:BaseUrl` to the production URL (used in password reset emails)
- [ ] Confirm SMTP settings and test email delivery
- [ ] Verify the initial admin password is changed on first login
- [ ] Enable HTTPS and set `CookieSecurePolicy.Always` (already the default)
- [ ] Review NLog targets — ensure file paths and database connection strings are valid

## Configuration reference

Key settings in `appsettings.json` under `App`:

| Key | Default | Description |
|-----|---------|-------------|
| `BaseUrl` | `https://localhost:5001` | Base URL for password reset email links |
| `InitialAdminLoginId` | `ADMIN` | Login ID for the seeded admin account |
| `InitialAdminPassword` | `ChangeMe!123` | Initial password (must be changed on first login) |
| `PasswordResetTokenHours` | `1` | Hours before a password reset token expires |
| `WindowsAutoLoginChallenge` | `true` | Enable Windows Negotiate auto-login |
| `SuppressWindowsAutoLoginMinutes` | `1` | Minutes to suppress auto-login after manual logout |
| `LoginRateLimit:MaxAttempts` | `5` | Failed attempts before login is blocked |
| `LoginRateLimit:WindowMinutes` | `15` | Duration of the rate limit window |
