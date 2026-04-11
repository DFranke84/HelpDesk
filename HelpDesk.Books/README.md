# HelpDesk.Books

A modular library for managing a Books catalog, extracted from the main HelpDesk application. This library provides a complete CRUD interface for managing books and publishers using Entity Framework Core, Blazor Server components, and API controllers.

## Features

- **Entity Models**: `Book` and `Publisher` entities with full data annotations
- **Database Context**: `BooksDbContext` with fluent configuration and migrations
- **API Controller**: RESTful endpoints for books catalog grid data
- **Blazor Components**: Interactive UI for managing books with DataTables integration
- **JavaScript Integration**: Custom grid functionality with permission-based actions
- **Database Scripts**: Setup and seed scripts for the Books database

## Architecture

The HelpDesk.Books library is designed as a self-contained module that can be easily integrated into ASP.NET Core applications. It follows the same patterns as the main HelpDesk application:

- **Models**: Entity classes, EF Core DbContext with configuration and design-time factory (`Models/Data`), migrations (`Models/Migrations`), and SQL scripts (`Models/Scripts`)
- **Controllers**: API endpoints for JSON data
- **Components**: Blazor Server components with JS interop
- **Extensions**: Service registration helpers

## Integration

### 1. Add Project Reference

```xml
<ProjectReference Include="..\HelpDesk.Books\HelpDesk.Books.csproj" />
```

### 2. Configure Services

```csharp
using HelpDesk.Books.Extensions;

var booksConnectionString = builder.Configuration.GetConnectionString("Books")
    ?? throw new InvalidOperationException("Connection string 'Books' is not configured.");

builder.Services.AddHelpDeskBooks(booksConnectionString);
```

### 3. Configure Application

```csharp
app.MapHelpDeskBooks();
```

### 4. Add Component Import

In your `_Imports.razor`:
```razor
@using HelpDesk.Books.Components
```

## Database Setup

### 1. Connection String

Configure the Books database connection string in your `appsettings.json` or User Secrets:

```json
{
  "ConnectionStrings": {
    "Books": "Server=.;Database=Books;Integrated Security=true;TrustServerCertificate=true"
  }
}
```

### 2. Database Creation

Option A - Use EF Core Migrations:
```bash
cd HelpDesk.Books
dotnet ef database update --startup-project ../HelpDesk.Web
```

Option B - Use SQL Scripts:
1. Run `Models/Scripts/HelpDesk-BooksInitial.sql` to create tables and migration history
2. Run `Models/Scripts/SeedBooks50.sql` to populate with sample data (optional)

## Usage

Once integrated, the Books catalog will be available at `/user/books/catalog`. The interface provides:

- **Browse**: DataTables grid with search, sort, and pagination
- **Add**: Modal form for creating new books
- **Edit**: In-place editing of existing books
- **Delete**: Confirmation dialog for removing books
- **Validation**: Client and server-side validation for all fields

## API Endpoints

- `GET /api/grid/books/catalog` - Returns JSON data for the books grid

## Database Schema

### Publishers Table
- `PublisherId` (int, identity, PK)
- `Name` (nvarchar(200), required)

### Books Table  
- `BookId` (int, identity, PK)
- `Isbn13` (nchar(13), required, unique)
- `Title` (nvarchar(300), required)
- `Subtitle` (nvarchar(400), optional)
- `PublicationYear` (smallint)
- `PublisherId` (int, FK to Publishers)
- `Pages` (int, optional)
- `LanguageCode` (nchar(2), required, default 'en')
- `Description` (nvarchar(max), optional)
- `CreatedAt` (datetime2)

## Customization

The library is designed to be customizable:

- **Styling**: Uses CSS classes that can be overridden in the host application
- **Validation**: Entity validation attributes can be extended
- **UI Components**: Blazor components can be replaced or extended
- **JavaScript**: Grid behavior can be customized via the `booksCatalogGrids.js` file

## Dependencies

- .NET 8
- ASP.NET Core 8
- Entity Framework Core 8
- Microsoft.EntityFrameworkCore.SqlServer 8.0.11
- Microsoft.EntityFrameworkCore.Design 8.0.11 (for migrations)

The library also depends on the host application providing:
- jQuery and DataTables libraries
- Bootstrap CSS framework
- Authentication and authorization infrastructure