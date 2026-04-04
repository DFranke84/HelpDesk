/*
  Books.sql
  Creates a SQL Server database named [Books], schema, relationships, and seed data (50+ books).

  Notes:
  - This script is rerunnable: it drops and recreates the tables in [Books] (it does NOT drop the database).
  - It grants access to the same SQL login used by HelpDesk's Development connection string.
  - HelpDesk surfaces dbo.Books via the BOOKS application page "Books" (route /user/books/catalog); see Docs/Database/BooksCatalog-Implementation.md.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @DatabaseName sysname = N'Books';

-- From HelpDesk.Web/appsettings.Development.json: ConnectionStrings.HelpDesk uses "User Id=sa"
DECLARE @AppLogin sysname = N'sa';

IF DB_ID(@DatabaseName) IS NULL
BEGIN
    DECLARE @createDb nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@DatabaseName) + N';';
    EXEC (@createDb);
END

DECLARE @useDb nvarchar(max) = N'USE ' + QUOTENAME(@DatabaseName) + N';';
EXEC (@useDb);

BEGIN TRY
    BEGIN TRAN;

    -- Ensure the application login can access this database
    -- Note: 'sa' is a special principal and cannot be added as a database user/role member.
    IF @AppLogin <> N'sa' AND SUSER_ID(@AppLogin) IS NOT NULL
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @AppLogin)
        BEGIN
            DECLARE @createUser nvarchar(max) =
                N'CREATE USER ' + QUOTENAME(@AppLogin) + N' FOR LOGIN ' + QUOTENAME(@AppLogin) + N';';
            EXEC (@createUser);
        END

        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.database_role_members rm
            JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
            JOIN sys.database_principals m ON m.principal_id = rm.member_principal_id
            WHERE r.name = N'db_owner' AND m.name = @AppLogin
        )
        BEGIN
            DECLARE @addRole nvarchar(max) =
                N'ALTER ROLE [db_owner] ADD MEMBER ' + QUOTENAME(@AppLogin) + N';';
            EXEC (@addRole);
        END
    END

    -- Drop in dependency order (rerunnable)
    IF OBJECT_ID(N'dbo.BookCategories', N'U') IS NOT NULL DROP TABLE dbo.BookCategories;
    IF OBJECT_ID(N'dbo.BookAuthors', N'U') IS NOT NULL DROP TABLE dbo.BookAuthors;
    IF OBJECT_ID(N'dbo.Books', N'U') IS NOT NULL DROP TABLE dbo.Books;
    IF OBJECT_ID(N'dbo.Categories', N'U') IS NOT NULL DROP TABLE dbo.Categories;
    IF OBJECT_ID(N'dbo.Authors', N'U') IS NOT NULL DROP TABLE dbo.Authors;
    IF OBJECT_ID(N'dbo.Publishers', N'U') IS NOT NULL DROP TABLE dbo.Publishers;

    CREATE TABLE dbo.Publishers
    (
        PublisherId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Publishers PRIMARY KEY,
        Name nvarchar(200) NOT NULL,
        Website nvarchar(400) NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_Publishers_CreatedAt DEFAULT (sysutcdatetime()),
        CONSTRAINT UQ_Publishers_Name UNIQUE (Name)
    );

    CREATE TABLE dbo.Authors
    (
        AuthorId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Authors PRIMARY KEY,
        FirstName nvarchar(100) NOT NULL,
        LastName nvarchar(120) NOT NULL,
        BirthDate date NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_Authors_CreatedAt DEFAULT (sysutcdatetime())
    );

    CREATE TABLE dbo.Categories
    (
        CategoryId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Categories PRIMARY KEY,
        Name nvarchar(120) NOT NULL,
        CONSTRAINT UQ_Categories_Name UNIQUE (Name)
    );

    CREATE TABLE dbo.Books
    (
        BookId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Books PRIMARY KEY,
        Isbn13 char(13) NOT NULL,
        Title nvarchar(300) NOT NULL,
        Subtitle nvarchar(400) NULL,
        PublicationYear smallint NOT NULL,
        PublisherId int NOT NULL,
        Pages int NULL,
        LanguageCode char(2) NOT NULL CONSTRAINT DF_Books_LanguageCode DEFAULT ('en'),
        Description nvarchar(max) NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_Books_CreatedAt DEFAULT (sysutcdatetime()),
        CONSTRAINT UQ_Books_Isbn13 UNIQUE (Isbn13),
        CONSTRAINT FK_Books_Publishers FOREIGN KEY (PublisherId) REFERENCES dbo.Publishers(PublisherId)
    );

    CREATE TABLE dbo.BookAuthors
    (
        BookId int NOT NULL,
        AuthorId int NOT NULL,
        AuthorOrder tinyint NOT NULL CONSTRAINT DF_BookAuthors_AuthorOrder DEFAULT (1),
        CONSTRAINT PK_BookAuthors PRIMARY KEY (BookId, AuthorId),
        CONSTRAINT FK_BookAuthors_Books FOREIGN KEY (BookId) REFERENCES dbo.Books(BookId) ON DELETE CASCADE,
        CONSTRAINT FK_BookAuthors_Authors FOREIGN KEY (AuthorId) REFERENCES dbo.Authors(AuthorId)
    );

    CREATE TABLE dbo.BookCategories
    (
        BookId int NOT NULL,
        CategoryId int NOT NULL,
        CONSTRAINT PK_BookCategories PRIMARY KEY (BookId, CategoryId),
        CONSTRAINT FK_BookCategories_Books FOREIGN KEY (BookId) REFERENCES dbo.Books(BookId) ON DELETE CASCADE,
        CONSTRAINT FK_BookCategories_Categories FOREIGN KEY (CategoryId) REFERENCES dbo.Categories(CategoryId)
    );

    INSERT INTO dbo.Publishers (Name, Website) VALUES
    (N'Northwind Press', N'https://example.invalid/northwind'),
    (N'Contoso Books', N'https://example.invalid/contoso'),
    (N'Fabrikam Publishing', N'https://example.invalid/fabrikam'),
    (N'Adventure Works Media', N'https://example.invalid/adventureworks'),
    (N'Wingtip Editions', N'https://example.invalid/wingtip'),
    (N'Litware House', N'https://example.invalid/litware'),
    (N'Blue Yonder Reads', N'https://example.invalid/blueyonder'),
    (N'Tailspin Titles', N'https://example.invalid/tailspin'),
    (N'Prose & Code', N'https://example.invalid/prose-code'),
    (N'Prairie Leaf Publishing', N'https://example.invalid/prairieleaf');

    INSERT INTO dbo.Categories (Name) VALUES
    (N'Programming'),
    (N'Databases'),
    (N'Cloud'),
    (N'Architecture'),
    (N'Security'),
    (N'Productivity'),
    (N'Science Fiction'),
    (N'Fantasy'),
    (N'History'),
    (N'Business');

    INSERT INTO dbo.Authors (FirstName, LastName, BirthDate) VALUES
    (N'Ava', N'Nguyen', '1986-02-11'),
    (N'Liam', N'Patel', '1982-09-03'),
    (N'Noah', N'Kim', '1979-06-24'),
    (N'Emma', N'Rodriguez', '1988-12-19'),
    (N'Olivia', N'Bennett', '1975-04-07'),
    (N'Sophia', N'Chen', '1990-10-28'),
    (N'Isabella', N'Martin', '1984-01-15'),
    (N'Mason', N'Wright', '1977-08-30'),
    (N'Ethan', N'Johnson', '1981-03-12'),
    (N'James', N'Anderson', '1973-11-05'),
    (N'Mia', N'Turner', '1992-05-21'),
    (N'Charlotte', N'Parker', '1989-07-09'),
    (N'Amelia', N'Garcia', '1983-09-17'),
    (N'Harper', N'Evans', '1978-12-02'),
    (N'Elijah', N'Lopez', '1985-06-14'),
    (N'Lucas', N'Hughes', '1976-02-25'),
    (N'Benjamin', N'Russell', '1980-10-01'),
    (N'Evelyn', N'Cole', '1991-01-29'),
    (N'Henry', N'Price', '1974-04-18'),
    (N'Abigail', N'Foster', '1987-08-08');

    /*
      Seed 50 books.
      ISBNs are synthetic (unique) for sample/demo data.
    */
    INSERT INTO dbo.Books (Isbn13, Title, Subtitle, PublicationYear, PublisherId, Pages, LanguageCode, Description)
    SELECT v.Isbn13, v.Title, v.Subtitle, v.PublicationYear, p.PublisherId, v.Pages, v.LanguageCode, v.Description
    FROM (VALUES
        ('9780000000001', N'Blazor Fundamentals', N'Building modern web UIs with .NET', 2022, N'Prose & Code', 392, 'en', N'Hands-on guide to component-based UI development with Blazor.'),
        ('9780000000002', N'Effective T-SQL', N'Patterns for maintainable queries', 2021, N'Contoso Books', 448, 'en', N'Query patterns, readability, and performance-focused SQL Server techniques.'),
        ('9780000000003', N'Clean Architecture in Practice', N'Domain-driven design for teams', 2020, N'Northwind Press', 384, 'en', N'Applying layered architecture and DDD concepts in real projects.'),
        ('9780000000004', N'Pragmatic Logging', N'Observability for production systems', 2019, N'Adventure Works Media', 312, 'en', N'How to structure logs, traces, and metrics for fast diagnosis.'),
        ('9780000000005', N'Azure for Builders', N'From zero to deployed', 2023, N'Blue Yonder Reads', 520, 'en', N'Practical cloud fundamentals with an emphasis on delivery.'),
        ('9780000000006', N'The Database Toolkit', N'Indexes, plans, and troubleshooting', 2018, N'Fabrikam Publishing', 462, 'en', N'SQL Server internals, indexing strategies, and plan analysis.'),
        ('9780000000007', N'API Design Notes', N'Contracts that age well', 2020, N'Wingtip Editions', 276, 'en', N'Designing resilient APIs: versioning, errors, and consistency.'),
        ('9780000000008', N'Zero Trust Essentials', N'Practical security for developers', 2022, N'Litware House', 338, 'en', N'Identity, least privilege, and secure defaults in everyday systems.'),
        ('9780000000009', N'Working With Legacy Code', N'Strategies for safe change', 2017, N'Prairie Leaf Publishing', 410, 'en', N'Techniques to add tests, refactor safely, and improve reliability.'),
        ('9780000000010', N'The Timekeeper''s Paradox', N'A science fiction novella', 2016, N'Tailspin Titles', 198, 'en', N'A courier discovers the cost of altering timelines.'),
        ('9780000000011', N'Dragons of the Inland Sea', N'Book I of The Saltbound Saga', 2015, N'Tailspin Titles', 544, 'en', N'A reluctant sailor bargains with a dragon-god for safe passage.'),
        ('9780000000012', N'History of Hidden Cities', N'Urban myths and true stories', 2014, N'Prairie Leaf Publishing', 360, 'en', N'A tour of forgotten infrastructure beneath modern streets.'),
        ('9780000000013', N'Business Systems Thinking', N'Decisions, feedback, and incentives', 2019, N'Contoso Books', 328, 'en', N'How to analyze organizations as systems to reduce unintended outcomes.'),
        ('9780000000014', N'Kubernetes Without Tears', N'Core concepts explained clearly', 2021, N'Blue Yonder Reads', 412, 'en', N'Pods, deployments, services, and the operational mental model.'),
        ('9780000000015', N'Practical Git Workflows', N'Team habits that scale', 2018, N'Northwind Press', 248, 'en', N'Branching models, code review practices, and release hygiene.'),
        ('9780000000016', N'Refactoring Databases', N'Incremental change with confidence', 2020, N'Fabrikam Publishing', 372, 'en', N'Migrations, backwards compatibility, and deployment-safe patterns.'),
        ('9780000000017', N'The Architect''s Sketchbook', N'Diagrams that communicate', 2022, N'Wingtip Editions', 290, 'en', N'Using simple visuals to align stakeholders and engineers.'),
        ('9780000000018', N'Identity in Depth', N'Authentication and authorization patterns', 2023, N'Litware House', 468, 'en', N'Claims, tokens, sessions, and secure authorization boundaries.'),
        ('9780000000019', N'Focus Engineering', N'How to ship without burnout', 2017, N'Adventure Works Media', 256, 'en', N'Prioritization, timeboxing, and sustainable delivery practices.'),
        ('9780000000020', N'The Last Algorithm', N'A near-future thriller', 2021, N'Prairie Leaf Publishing', 304, 'en', N'An engineer races to stop a model that predicts too much.'),
        ('9780000000021', N'Blazor Components Cookbook', N'Recipes for real apps', 2024, N'Prose & Code', 536, 'en', N'Common UI patterns implemented with reusable Blazor components.'),
        ('9780000000022', N'SQL Server Performance Field Guide', N'What to measure and why', 2022, N'Fabrikam Publishing', 498, 'en', N'Practical guidance on waits, IO, CPU, and query plan regressions.'),
        ('9780000000023', N'Cloud Cost Discipline', N'Governance for engineers', 2020, N'Blue Yonder Reads', 280, 'en', N'Budgets, tagging, showback, and building cost-aware systems.'),
        ('9780000000024', N'Event-Driven Architecture', N'From queues to outcomes', 2019, N'Northwind Press', 344, 'en', N'Designing asynchronous systems with clear event contracts.'),
        ('9780000000025', N'Secure by Default', N'Designing safer products', 2021, N'Litware House', 316, 'en', N'Principles and checklists for reducing risk early in design.'),
        ('9780000000026', N'Productive Teams', N'Communication patterns that work', 2018, N'Adventure Works Media', 272, 'en', N'Cadence, feedback loops, and making decisions visible.'),
        ('9780000000027', N'The Observatory at End of Night', N'Science fiction short stories', 2013, N'Tailspin Titles', 224, 'en', N'Five stories about discovery, loss, and distant signals.'),
        ('9780000000028', N'The Glass Forest', N'Fantasy tales of a shifting realm', 2012, N'Tailspin Titles', 512, 'en', N'A ranger navigates a forest that remembers every footstep.'),
        ('9780000000029', N'Lost Empires Atlas', N'History through trade routes', 2016, N'Prairie Leaf Publishing', 402, 'en', N'An illustrated narrative of empires connected by commerce.'),
        ('9780000000030', N'Startup Finance for Engineers', N'Numbers without the noise', 2019, N'Contoso Books', 240, 'en', N'Runway, burn, unit economics, and decision-making fundamentals.'),
        ('9780000000031', N'Concurrency for Humans', N'Async patterns explained', 2020, N'Prose & Code', 368, 'en', N'Understanding concurrency pitfalls and designing predictable async code.'),
        ('9780000000032', N'Practical Monitoring', N'Alerts that matter', 2018, N'Adventure Works Media', 310, 'en', N'How to avoid noisy alerts and build actionable on-call runbooks.'),
        ('9780000000033', N'Data Modeling Basics', N'Relationships, constraints, and change', 2017, N'Fabrikam Publishing', 284, 'en', N'Foundations of relational design with practical examples.'),
        ('9780000000034', N'Negotiating Requirements', N'How to clarify the real need', 2016, N'Wingtip Editions', 230, 'en', N'Techniques for turning ambiguous requests into shared understanding.'),
        ('9780000000035', N'Secrets Management', N'Keys, vaults, and rotations', 2022, N'Litware House', 352, 'en', N'Patterns for storing secrets, rotating credentials, and limiting blast radius.'),
        ('9780000000036', N'Microservices Reality Check', N'When (and when not) to split', 2019, N'Northwind Press', 336, 'en', N'Trade-offs, operational overhead, and organizational impacts.'),
        ('9780000000037', N'The Memory Cartographer', N'A fantasy novel', 2015, N'Tailspin Titles', 608, 'en', N'A mapmaker charts memories to heal a kingdom''s curse.'),
        ('9780000000038', N'Edge of Orion', N'Hard sci-fi adventure', 2014, N'Prairie Leaf Publishing', 388, 'en', N'Explorers uncover a derelict beacon beyond the Kuiper belt.'),
        ('9780000000039', N'Industrial Revolutions Explained', N'History in three turning points', 2011, N'Prairie Leaf Publishing', 352, 'en', N'An accessible history of technology shifts and social change.'),
        ('9780000000040', N'Business Writing for Developers', N'Clear updates and proposals', 2021, N'Contoso Books', 214, 'en', N'Write specs, emails, and proposals that reduce back-and-forth.'),
        ('9780000000041', N'Blazor Testing Playbook', N'Unit, integration, and UI tests', 2024, N'Prose & Code', 420, 'en', N'Practical strategies for reliable tests in Blazor applications.'),
        ('9780000000042', N'SQL Patterns Catalog', N'Joins, window functions, and more', 2023, N'Fabrikam Publishing', 530, 'en', N'A catalog of common SQL patterns with explanations and trade-offs.'),
        ('9780000000043', N'Cloud Reliability', N'Designing for failure', 2022, N'Blue Yonder Reads', 460, 'en', N'Resiliency patterns, chaos testing basics, and capacity thinking.'),
        ('9780000000044', N'Message Queues Demystified', N'Reliable async communication', 2020, N'Northwind Press', 318, 'en', N'How queues work, delivery semantics, and consumer design.'),
        ('9780000000045', N'Security Reviews That Work', N'Practical threat modeling', 2019, N'Litware House', 268, 'en', N'A lightweight process for finding risks without slowing delivery.'),
        ('9780000000046', N'Better Meetings', N'A productivity handbook', 2016, N'Adventure Works Media', 180, 'en', N'Structures and facilitation to make meetings short and useful.'),
        ('9780000000047', N'The Starwell Covenant', N'Science fiction epic', 2012, N'Tailspin Titles', 672, 'en', N'Colonies negotiate an uneasy alliance across light-years.'),
        ('9780000000048', N'Rivers of Ember', N'Fantasy adventure', 2013, N'Tailspin Titles', 496, 'en', N'A thief and a monk chase a relic along volcanic rivers.'),
        ('9780000000049', N'History of Codebreakers', N'From ciphers to computers', 2015, N'Prairie Leaf Publishing', 420, 'en', N'The people and ideas behind cryptography''s biggest leaps.'),
        ('9780000000050', N'Business Metrics That Matter', N'From vanity to value', 2020, N'Contoso Books', 260, 'en', N'Pick metrics that support decisions, not dashboards.')
    ) v(Isbn13, Title, Subtitle, PublicationYear, PublisherName, Pages, LanguageCode, Description)
    JOIN dbo.Publishers p ON p.Name = v.PublisherName;

    /*
      Map authors and categories.
      Each book gets 1-2 authors and 1-3 categories.
    */
    ;WITH b AS
    (
        SELECT BookId, Isbn13, Title FROM dbo.Books
    ),
    a AS
    (
        SELECT AuthorId, (FirstName + N' ' + LastName) AS FullName FROM dbo.Authors
    ),
    c AS
    (
        SELECT CategoryId, Name FROM dbo.Categories
    )
    INSERT INTO dbo.BookAuthors (BookId, AuthorId, AuthorOrder)
    SELECT b.BookId, a.AuthorId, x.AuthorOrder
    FROM b
    JOIN (VALUES
        ('9780000000001', N'Ava Nguyen', 1),
        ('9780000000002', N'Noah Kim', 1),
        ('9780000000003', N'Olivia Bennett', 1),
        ('9780000000004', N'James Anderson', 1),
        ('9780000000005', N'Emma Rodriguez', 1),
        ('9780000000006', N'Liam Patel', 1),
        ('9780000000007', N'Isabella Martin', 1),
        ('9780000000008', N'Benjamin Russell', 1),
        ('9780000000009', N'Harper Evans', 1),
        ('9780000000010', N'Evelyn Cole', 1),
        ('9780000000011', N'Abigail Foster', 1),
        ('9780000000012', N'Henry Price', 1),
        ('9780000000013', N'Charlotte Parker', 1),
        ('9780000000014', N'Elijah Lopez', 1),
        ('9780000000015', N'Mason Wright', 1),
        ('9780000000016', N'Noah Kim', 1),
        ('9780000000017', N'Lucas Hughes', 1),
        ('9780000000018', N'Sophia Chen', 1),
        ('9780000000019', N'Mia Turner', 1),
        ('9780000000020', N'Evelyn Cole', 1),
        ('9780000000021', N'Ava Nguyen', 1),
        ('9780000000022', N'Liam Patel', 1),
        ('9780000000023', N'Emma Rodriguez', 1),
        ('9780000000024', N'Olivia Bennett', 1),
        ('9780000000025', N'Benjamin Russell', 1),
        ('9780000000026', N'Mia Turner', 1),
        ('9780000000027', N'Evelyn Cole', 1),
        ('9780000000028', N'Abigail Foster', 1),
        ('9780000000029', N'Henry Price', 1),
        ('9780000000030', N'Charlotte Parker', 1),
        ('9780000000031', N'Ethan Johnson', 1),
        ('9780000000032', N'James Anderson', 1),
        ('9780000000033', N'Liam Patel', 1),
        ('9780000000034', N'Isabella Martin', 1),
        ('9780000000035', N'Sophia Chen', 1),
        ('9780000000036', N'Olivia Bennett', 1),
        ('9780000000037', N'Abigail Foster', 1),
        ('9780000000038', N'Evelyn Cole', 1),
        ('9780000000039', N'Henry Price', 1),
        ('9780000000040', N'Charlotte Parker', 1),
        ('9780000000041', N'Ava Nguyen', 1),
        ('9780000000042', N'Noah Kim', 1),
        ('9780000000043', N'Emma Rodriguez', 1),
        ('9780000000044', N'Lucas Hughes', 1),
        ('9780000000045', N'Benjamin Russell', 1),
        ('9780000000046', N'Mia Turner', 1),
        ('9780000000047', N'Evelyn Cole', 1),
        ('9780000000048', N'Abigail Foster', 1),
        ('9780000000049', N'Sophia Chen', 1),
        ('9780000000050', N'Charlotte Parker', 1),

        -- second author on a few titles
        ('9780000000005', N'Lucas Hughes', 2),
        ('9780000000014', N'Harper Evans', 2),
        ('9780000000024', N'Ethan Johnson', 2),
        ('9780000000036', N'James Anderson', 2),
        ('9780000000043', N'Liam Patel', 2),
        ('9780000000044', N'Isabella Martin', 2)
    ) x(Isbn13, AuthorFullName, AuthorOrder) ON x.Isbn13 = b.Isbn13
    JOIN a ON a.FullName = x.AuthorFullName;

    INSERT INTO dbo.BookCategories (BookId, CategoryId)
    SELECT b.BookId, c.CategoryId
    FROM dbo.Books b
    JOIN (VALUES
        ('9780000000001', N'Programming'),
        ('9780000000002', N'Databases'),
        ('9780000000003', N'Architecture'),
        ('9780000000004', N'Productivity'),
        ('9780000000005', N'Cloud'),
        ('9780000000006', N'Databases'),
        ('9780000000007', N'Architecture'),
        ('9780000000008', N'Security'),
        ('9780000000009', N'Programming'),
        ('9780000000010', N'Science Fiction'),
        ('9780000000011', N'Fantasy'),
        ('9780000000012', N'History'),
        ('9780000000013', N'Business'),
        ('9780000000014', N'Cloud'),
        ('9780000000015', N'Productivity'),
        ('9780000000016', N'Databases'),
        ('9780000000017', N'Architecture'),
        ('9780000000018', N'Security'),
        ('9780000000019', N'Productivity'),
        ('9780000000020', N'Science Fiction'),
        ('9780000000021', N'Programming'),
        ('9780000000022', N'Databases'),
        ('9780000000023', N'Cloud'),
        ('9780000000024', N'Architecture'),
        ('9780000000025', N'Security'),
        ('9780000000026', N'Business'),
        ('9780000000027', N'Science Fiction'),
        ('9780000000028', N'Fantasy'),
        ('9780000000029', N'History'),
        ('9780000000030', N'Business'),
        ('9780000000031', N'Programming'),
        ('9780000000032', N'Productivity'),
        ('9780000000033', N'Databases'),
        ('9780000000034', N'Business'),
        ('9780000000035', N'Security'),
        ('9780000000036', N'Architecture'),
        ('9780000000037', N'Fantasy'),
        ('9780000000038', N'Science Fiction'),
        ('9780000000039', N'History'),
        ('9780000000040', N'Business'),
        ('9780000000041', N'Programming'),
        ('9780000000042', N'Databases'),
        ('9780000000043', N'Cloud'),
        ('9780000000044', N'Architecture'),
        ('9780000000045', N'Security'),
        ('9780000000046', N'Productivity'),
        ('9780000000047', N'Science Fiction'),
        ('9780000000048', N'Fantasy'),
        ('9780000000049', N'History'),
        ('9780000000050', N'Business'),

        -- additional categories (multi-tag a few)
        ('9780000000001', N'Architecture'),
        ('9780000000002', N'Architecture'),
        ('9780000000005', N'Architecture'),
        ('9780000000014', N'Architecture'),
        ('9780000000018', N'Architecture'),
        ('9780000000023', N'Business'),
        ('9780000000035', N'Architecture'),
        ('9780000000043', N'Architecture')
    ) x(Isbn13, CategoryName) ON x.Isbn13 = b.Isbn13
    JOIN dbo.Categories c ON c.Name = x.CategoryName;

    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;

-- Quick verification
SELECT
    (SELECT COUNT(*) FROM dbo.Publishers) AS Publishers,
    (SELECT COUNT(*) FROM dbo.Authors) AS Authors,
    (SELECT COUNT(*) FROM dbo.Categories) AS Categories,
    (SELECT COUNT(*) FROM dbo.Books) AS Books,
    (SELECT COUNT(*) FROM dbo.BookAuthors) AS BookAuthors,
    (SELECT COUNT(*) FROM dbo.BookCategories) AS BookCategories;

