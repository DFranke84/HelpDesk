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

CREATE TABLE [Applications] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(200) NOT NULL,
    [Description] nvarchar(max) NULL,
    [UsesExternalDatabase] bit NOT NULL,
    [ExternalConnectionStringName] nvarchar(200) NULL,
    [IsActive] bit NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    [RowVersion] rowversion NULL,
    CONSTRAINT [PK_Applications] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [Groups] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(200) NOT NULL,
    [Description] nvarchar(max) NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    [RowVersion] rowversion NULL,
    CONSTRAINT [PK_Groups] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [NLOG] (
    [Id] bigint NOT NULL IDENTITY,
    [MachineName] nvarchar(200) NOT NULL,
    [Logged] datetime2 NOT NULL,
    [Level] nvarchar(16) NOT NULL,
    [Message] nvarchar(max) NOT NULL,
    [Logger] nvarchar(300) NULL,
    [Exception] nvarchar(max) NULL,
    [ElapsedTime] decimal(11,5) NULL,
    [NumIterations] int NULL,
    [ActivityCategory] nvarchar(30) NULL,
    [CorrelationId] uniqueidentifier NULL,
    [LoginID] nvarchar(256) NULL,
    [DomainLogin] nvarchar(256) NULL,
    [ReturnCode] int NULL,
    CONSTRAINT [PK_NLOG] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [Users] (
    [Id] int NOT NULL IDENTITY,
    [LoginID] nvarchar(256) NOT NULL,
    [FirstName] nvarchar(100) NULL,
    [LastName] nvarchar(100) NULL,
    [PasswordHash] nvarchar(500) NOT NULL,
    [Email] nvarchar(256) NULL,
    [Role] int NOT NULL,
    [DomainSamAccountName] nvarchar(256) NULL,
    [DomainUserPrincipalName] nvarchar(256) NULL,
    [MustChangePassword] bit NOT NULL DEFAULT CAST(0 AS bit),
    [IsActive] bit NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    [RowVersion] rowversion NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [Pages] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(200) NOT NULL,
    [Route] nvarchar(500) NOT NULL,
    [DisplayOrder] int NOT NULL,
    [IsActive] bit NOT NULL,
    [DeskApplicationId] int NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    [RowVersion] rowversion NULL,
    CONSTRAINT [PK_Pages] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Pages_Applications_DeskApplicationId] FOREIGN KEY ([DeskApplicationId]) REFERENCES [Applications] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [ApplicationGroups] (
    [DeskApplicationId] int NOT NULL,
    [DeskGroupId] int NOT NULL,
    CONSTRAINT [PK_ApplicationGroups] PRIMARY KEY ([DeskApplicationId], [DeskGroupId]),
    CONSTRAINT [FK_ApplicationGroups_Applications_DeskApplicationId] FOREIGN KEY ([DeskApplicationId]) REFERENCES [Applications] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ApplicationGroups_Groups_DeskGroupId] FOREIGN KEY ([DeskGroupId]) REFERENCES [Groups] ([Id])
);
GO

CREATE TABLE [EntityChangeHistories] (
    [Id] bigint NOT NULL IDENTITY,
    [EntityType] nvarchar(100) NOT NULL,
    [EntityId] nvarchar(64) NOT NULL,
    [Action] int NOT NULL,
    [JsonChanges] nvarchar(max) NULL,
    [ChangedByUserId] int NULL,
    [ChangedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_EntityChangeHistories] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_EntityChangeHistories_Users_ChangedByUserId] FOREIGN KEY ([ChangedByUserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL
);
GO

CREATE TABLE [PasswordResetTokens] (
    [Id] int NOT NULL IDENTITY,
    [UserId] int NOT NULL,
    [TokenHash] nvarchar(128) NOT NULL,
    [ExpiresAt] datetime2 NOT NULL,
    [UsedAt] datetime2 NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_PasswordResetTokens] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PasswordResetTokens_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [UserGroups] (
    [UserId] int NOT NULL,
    [GroupId] int NOT NULL,
    [AssignedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_UserGroups] PRIMARY KEY ([UserId], [GroupId]),
    CONSTRAINT [FK_UserGroups_Groups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [Groups] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_UserGroups_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
);
GO

CREATE INDEX [IX_ApplicationGroups_DeskGroupId] ON [ApplicationGroups] ([DeskGroupId]);
GO

CREATE INDEX [IX_Applications_Name] ON [Applications] ([Name]);
GO

CREATE INDEX [IX_EntityChangeHistories_ChangedAt] ON [EntityChangeHistories] ([ChangedAt]);
GO

CREATE INDEX [IX_EntityChangeHistories_ChangedByUserId] ON [EntityChangeHistories] ([ChangedByUserId]);
GO

CREATE INDEX [IX_EntityChangeHistories_EntityType_EntityId] ON [EntityChangeHistories] ([EntityType], [EntityId]);
GO

CREATE INDEX [IX_NLOG_ActivityCategory] ON [NLOG] ([ActivityCategory]);
GO

CREATE INDEX [IX_NLOG_CorrelationId] ON [NLOG] ([CorrelationId]);
GO

CREATE INDEX [IX_NLOG_Level] ON [NLOG] ([Level]);
GO

CREATE INDEX [IX_NLOG_Logged] ON [NLOG] ([Logged]);
GO

CREATE INDEX [IX_NLOG_LoginID] ON [NLOG] ([LoginID]);
GO

CREATE INDEX [IX_Pages_DeskApplicationId] ON [Pages] ([DeskApplicationId]);
GO

CREATE INDEX [IX_PasswordResetTokens_UserId] ON [PasswordResetTokens] ([UserId]);
GO

CREATE UNIQUE INDEX [IX_PasswordResetTokens_TokenHash] ON [PasswordResetTokens] ([TokenHash]);
GO

CREATE INDEX [IX_UserGroups_GroupId] ON [UserGroups] ([GroupId]);
GO

CREATE UNIQUE INDEX [IX_Users_LoginID] ON [Users] ([LoginID]);
GO


CREATE OR ALTER PROCEDURE [dbo].[NLog_AddEntry_p]
    @machineName      nvarchar(200),
    @logged           datetime2,
    @level            nvarchar(16),
    @message          nvarchar(max),
    @logger           nvarchar(300) = NULL,
    @exception        nvarchar(max) = NULL,
    @elapsedTime      decimal(11,5) = NULL,
    @numIterations    int = NULL,
    @activityCategory nvarchar(30) = NULL,
    @correlationId    uniqueidentifier = NULL,
    @loginId          nvarchar(256) = NULL,
    @domainLogin      nvarchar(256) = NULL,
    @returnCode       int = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO [dbo].[NLOG]
    (
        [MachineName],
        [Logged],
        [Level],
        [Message],
        [Logger],
        [Exception],
        [ElapsedTime],
        [NumIterations],
        [ActivityCategory],
        [CorrelationId],
        [LoginID],
        [DomainLogin],
        [ReturnCode]
    )
    VALUES
    (
        @machineName,
        @logged,
        @level,
        @message,
        @logger,
        @exception,
        @elapsedTime,
        @numIterations,
        @activityCategory,
        @correlationId,
        @loginId,
        @domainLogin,
        @returnCode
    );
END;

GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260330212626_DBMigration01', N'8.0.11');
GO

COMMIT;
GO

