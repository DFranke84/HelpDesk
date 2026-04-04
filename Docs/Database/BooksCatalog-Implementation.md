# Books catalog (`dbo.Books`) — implementation guide

The **BOOKS** HelpDesk application points at the **Books** SQL database (`ConnectionStrings:Books`). The **Books** menu page is a jQuery **DataTables** grid over **`Books.dbo.Books`** with modal **Add / Edit / Delete** (same patterns as admin catalog grids).

## Integrated behavior (this repo)

| Item | Value |
|------|--------|
| Tool route | `/user/books/catalog` |
| Menu | **Applications → BOOKS → Books** → `/app/{appId}/page/{pageId}` then **`AppPage.razor`** redirects to `Pages.Route` |
| Grid JSON | `GET /api/grid/books/catalog` — JSON **array** for DataTables `dataSrc: ''` |
| Interop | `booksCatalog` / `helpdeskAdmin.initBooksCatalog` (`wwwroot/js/booksCatalogGrids.js`) |
| EF | `HelpDesk.Models.Books.Book`, `Publisher`; `BooksDbContext` in `HelpDesk.Models` |
| Startup | `DbInitializer.EnsureBooksCatalogAsync` ensures group **BOOKS**, app **BOOKS**, page **Books** → `/user/books/catalog`, and upgrades legacy **Book Users** / `/user/books/users` rows |

## Database schema

Create / refresh the Books database with **`Docs/Database/Books.sql`** (publishers, authors, categories, **`Books`**, junction tables, seed data).

There is **no** `BookUsers` table in this feature; remove an old `dbo.BookUsers` table if you no longer need it: see **`Docs/Database/Code/BooksCatalog/sql/Drop-BookUsers-Optional.sql`**.

## Template files (green-field / reference)

Under **`Docs/Database/Code/BooksCatalog/`**:

| Path | Purpose |
|------|---------|
| `backend/` | `Book.cs`, `Publisher.cs`, `BooksDbContext.cs`, `BooksCatalogGridController.cs` templates |
| `frontend/BooksCatalog.razor` | Routable page template |
| `wwwroot/booksCatalogGrids.js` | `helpdeskAdmin.initBooksCatalog` |
| `config/` | Snippets for `ConnectionStrings:Books` and `Program.cs` |
| `sql/Drop-BookUsers-Optional.sql` | Optional cleanup of obsolete `BookUsers` |

## Manual wiring checklist

1. **Connection string** `Books` in `appsettings` (same server/auth pattern as `HelpDesk`).
2. **`Program.cs`**: `AddDbContextFactory<BooksDbContext>(..., ServiceLifetime.Scoped)`.
3. Copy **controllers**, **pages**, **models**, **`booksCatalogGrids.js`**, and register the script in **`App.razor`** after **`adminGrids.js`**.
4. **HelpDesk catalog**: Application **BOOKS** uses `ExternalConnectionStringName = Books`; **Pages** row **Books** with **`Route = /user/books/catalog`**.

## DataTables lifecycle (Blazor Interactive Server)

Do **not** initialize the grid only on `firstRender`. Use **`helpdeskAdmin.needsInit`**, create **`DotNetObjectReference`** once (`if (_interop is null)`), then call **`initBooksCatalog`** — see **`AdminUserGroups.razor`** and **`BooksCatalog.razor`**.

## Troubleshooting

| Symptom | Check |
|--------|--------|
| Blank grid (no DataTables UI) | Script order, `needsInit` + lazy interop; `initBooksCatalog` defined in `booksCatalogGrids.js`. |
| Empty rows | Run **`Books.sql`**; confirm `Publishers` / `Books` exist; API **`/api/grid/books/catalog`** while logged in. |
| Menu still says “Book Users” | Restart app so **`DbInitializer`** migrates legacy page; or fix **Pages** in admin. |
| 401 on API | Must be authenticated. |
