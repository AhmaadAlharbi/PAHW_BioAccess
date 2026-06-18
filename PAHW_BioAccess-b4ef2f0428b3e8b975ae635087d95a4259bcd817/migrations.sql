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
CREATE TABLE [AllowedUsers] (
    [Id] int NOT NULL IDENTITY,
    [EmployeeId] int NOT NULL,
    [FullName] nvarchar(200) NOT NULL,
    [Email] nvarchar(200) NOT NULL,
    [Department] nvarchar(200) NOT NULL,
    [IsActive] bit NOT NULL,
    [ValidUntil] datetime2 NULL,
    [IsAdmin] bit NOT NULL,
    CONSTRAINT [PK_AllowedUsers] PRIMARY KEY ([Id])
);

CREATE TABLE [Delegations] (
    [Id] int NOT NULL IDENTITY,
    [EmployeeId] int NOT NULL,
    [StartDate] datetime2 NOT NULL,
    [EndDate] datetime2 NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [ActivatedAt] datetime2 NULL,
    [ExpiredAt] datetime2 NULL,
    CONSTRAINT [PK_Delegations] PRIMARY KEY ([Id])
);

CREATE TABLE [Regions] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(100) NOT NULL,
    CONSTRAINT [PK_Regions] PRIMARY KEY ([Id])
);

CREATE TABLE [DelegationTerminals] (
    [Id] int NOT NULL IDENTITY,
    [DelegationId] int NOT NULL,
    [TerminalId] nvarchar(50) NOT NULL,
    [WasAssignedBefore] bit NOT NULL,
    CONSTRAINT [PK_DelegationTerminals] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DelegationTerminals_Delegations_DelegationId] FOREIGN KEY ([DelegationId]) REFERENCES [Delegations] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [TerminalRegionMaps] (
    [TerminalId] nvarchar(450) NOT NULL,
    [RegionId] int NOT NULL,
    CONSTRAINT [PK_TerminalRegionMaps] PRIMARY KEY ([TerminalId]),
    CONSTRAINT [FK_TerminalRegionMaps_Regions_RegionId] FOREIGN KEY ([RegionId]) REFERENCES [Regions] ([Id]) ON DELETE NO ACTION
);

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Department', N'Email', N'EmployeeId', N'FullName', N'IsActive', N'IsAdmin', N'ValidUntil') AND [object_id] = OBJECT_ID(N'[AllowedUsers]'))
    SET IDENTITY_INSERT [AllowedUsers] ON;
INSERT INTO [AllowedUsers] ([Id], [Department], [Email], [EmployeeId], [FullName], [IsActive], [IsAdmin], [ValidUntil])
VALUES (1, N'', N'admin@admin.com', 7300, N'أحمد زيد الحربي', CAST(1 AS bit), CAST(1 AS bit), NULL);
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Department', N'Email', N'EmployeeId', N'FullName', N'IsActive', N'IsAdmin', N'ValidUntil') AND [object_id] = OBJECT_ID(N'[AllowedUsers]'))
    SET IDENTITY_INSERT [AllowedUsers] OFF;

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name') AND [object_id] = OBJECT_ID(N'[Regions]'))
    SET IDENTITY_INSERT [Regions] ON;
INSERT INTO [Regions] ([Id], [Name])
VALUES (1, N'المبنى الرئيسي'),
(2, N'المطلاع'),
(3, N'برج التحرير'),
(4, N'صباح السالم'),
(5, N'الجهراء - حكومة مول'),
(6, N'الجهراء - تيماء'),
(7, N'جابر الأحمد'),
(8, N'سعد العبدالله'),
(9, N'الصليبية'),
(10, N'القرين - حكومة مول'),
(11, N'مبارك الكبير'),
(12, N'النهضة'),
(13, N'غرب الجليب'),
(14, N'مواقع أخرى'),
(15, N'السالمي');
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name') AND [object_id] = OBJECT_ID(N'[Regions]'))
    SET IDENTITY_INSERT [Regions] OFF;

CREATE UNIQUE INDEX [IX_AllowedUsers_EmployeeId] ON [AllowedUsers] ([EmployeeId]);

CREATE INDEX [IX_DelegationTerminals_DelegationId] ON [DelegationTerminals] ([DelegationId]);

CREATE INDEX [IX_TerminalRegionMaps_RegionId] ON [TerminalRegionMaps] ([RegionId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260201074537_InitialSqlServer', N'10.0.2');

COMMIT;
GO

