IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

CREATE TABLE [Publishers] (
    [PublisherId] int NOT NULL IDENTITY,
    [Name] nvarchar(200) NOT NULL,
    CONSTRAINT [PK_Publishers] PRIMARY KEY ([PublisherId])
);
GO

CREATE TABLE [Books] (
    [BookId] int NOT NULL IDENTITY,
    [Isbn13] nchar(13) NOT NULL,
    [Title] nvarchar(300) NOT NULL,
    [Subtitle] nvarchar(400) NULL,
    [PublicationYear] smallint NOT NULL,
    [PublisherId] int NOT NULL,
    [Pages] int NULL,
    [LanguageCode] nchar(2) NOT NULL,
    [Description] nvarchar(max) NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_Books] PRIMARY KEY ([BookId]),
    CONSTRAINT [FK_Books_Publishers_PublisherId] FOREIGN KEY ([PublisherId]) REFERENCES [Publishers] ([PublisherId]) ON DELETE NO ACTION
);
GO

CREATE UNIQUE INDEX [IX_Books_Isbn13] ON [Books] ([Isbn13]);
GO

CREATE INDEX [IX_Books_PublisherId] ON [Books] ([PublisherId]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260404181139_BooksInitial', N'8.0.11');
GO

COMMIT;
GO