/*
  Optional: remove legacy dbo.BookUsers from the Books database (replaced by catalog on dbo.Books).
*/
SET NOCOUNT ON;
GO

IF DB_ID(N'Books') IS NULL
BEGIN
    RAISERROR('Database Books not found.', 16, 1);
    RETURN;
END
GO

USE Books;
GO

IF OBJECT_ID(N'dbo.BookUsers', N'U') IS NOT NULL
    DROP TABLE dbo.BookUsers;
GO
