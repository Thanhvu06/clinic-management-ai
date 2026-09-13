BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    DROP INDEX [IX_Patients_UserId] ON [Patients];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Patients]') AND [c].[name] = N'UserId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Patients] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Patients] ALTER COLUMN [UserId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    ALTER TABLE [Patients] ADD [BhytNumber] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    ALTER TABLE [Patients] ADD [BloodType] nvarchar(10) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    ALTER TABLE [Patients] ADD [Email] nvarchar(150) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    ALTER TABLE [Patients] ADD [FullName] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    ALTER TABLE [Patients] ADD [MedicalRecordNumber] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    ALTER TABLE [Patients] ADD [NationalId] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    ALTER TABLE [Patients] ADD [PhoneNumber] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    ALTER TABLE [Patients] ADD [PrimaryFacilityId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    ALTER TABLE [Patients] ADD [RhFactor] nvarchar(10) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    CREATE TABLE [EmergencyContacts] (
        [Id] bigint NOT NULL IDENTITY,
        [PatientId] bigint NOT NULL,
        [FullName] nvarchar(200) NOT NULL,
        [Relationship] nvarchar(100) NOT NULL,
        [PhoneNumber] nvarchar(50) NOT NULL,
        [Address] nvarchar(500) NULL,
        [IsPrimary] bit NOT NULL,
        CONSTRAINT [PK_EmergencyContacts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EmergencyContacts_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    CREATE TABLE [Facilities] (
        [Id] bigint NOT NULL IDENTITY,
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Address] nvarchar(500) NOT NULL,
        [City] nvarchar(100) NOT NULL,
        [Phone] nvarchar(50) NOT NULL,
        [Email] nvarchar(150) NULL,
        [TaxCode] nvarchar(50) NULL,
        [HospitalLevel] nvarchar(50) NULL,
        [Description] nvarchar(max) NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_Facilities] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    CREATE TABLE [MrnSequences] (
        [Id] int NOT NULL IDENTITY,
        [Year] int NOT NULL,
        [LastSequenceNumber] bigint NOT NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_MrnSequences] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    CREATE TABLE [PatientAllergies] (
        [Id] bigint NOT NULL IDENTITY,
        [PatientId] bigint NOT NULL,
        [AllergenType] nvarchar(50) NOT NULL,
        [AllergenName] nvarchar(200) NOT NULL,
        [Severity] nvarchar(50) NOT NULL,
        [ReactionDescription] nvarchar(500) NULL,
        [RecordedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_PatientAllergies] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PatientAllergies_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    CREATE TABLE [Buildings] (
        [Id] bigint NOT NULL IDENTITY,
        [FacilityId] bigint NOT NULL,
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [NumberOfFloors] int NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Buildings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Buildings_Facilities_FacilityId] FOREIGN KEY ([FacilityId]) REFERENCES [Facilities] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    CREATE TABLE [Departments] (
        [Id] bigint NOT NULL IDENTITY,
        [FacilityId] bigint NOT NULL,
        [BuildingId] bigint NULL,
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [DepartmentType] nvarchar(50) NOT NULL,
        [HeadOfDepartmentDoctorId] bigint NULL,
        [Description] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Departments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Departments_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Departments_Doctors_HeadOfDepartmentDoctorId] FOREIGN KEY ([HeadOfDepartmentDoctorId]) REFERENCES [Doctors] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Departments_Facilities_FacilityId] FOREIGN KEY ([FacilityId]) REFERENCES [Facilities] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    CREATE TABLE [Rooms] (
        [Id] bigint NOT NULL IDENTITY,
        [DepartmentId] bigint NOT NULL,
        [BuildingId] bigint NULL,
        [RoomNumber] nvarchar(50) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [RoomType] nvarchar(50) NOT NULL,
        [FloorNumber] int NOT NULL,
        [MaxCapacity] int NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Rooms] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Rooms_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Rooms_Departments_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [Departments] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    CREATE TABLE [Beds] (
        [Id] bigint NOT NULL IDENTITY,
        [RoomId] bigint NOT NULL,
        [BedNumber] nvarchar(50) NOT NULL,
        [BedType] nvarchar(50) NOT NULL,
        [DailyRate] decimal(18,2) NOT NULL DEFAULT 0.0,
        [Status] nvarchar(50) NOT NULL,
        [Notes] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_Beds] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Beds_Rooms_RoomId] FOREIGN KEY ([RoomId]) REFERENCES [Rooms] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN

                        UPDATE p
                        SET p.FullName = ISNULL(NULLIF(u.FullName, ''), N'Bệnh nhân'),
                            p.PhoneNumber = COALESCE(p.PhoneNumber, u.PhoneNumber),
                            p.Email = COALESCE(p.Email, u.Email)
                        FROM Patients p
                        INNER JOIN AspNetUsers u ON p.UserId = u.Id
                        WHERE p.FullName IS NULL OR p.FullName = '';
                    
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN

                        UPDATE Patients
                        SET FullName = N'Bệnh nhân ' + CAST(Id AS NVARCHAR(20))
                        WHERE FullName IS NULL OR FullName = '';
                    
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN

                        ;WITH NumberedPatients AS (
                            SELECT Id, ROW_NUMBER() OVER (ORDER BY Id) AS RowNum
                            FROM Patients
                            WHERE MedicalRecordNumber IS NULL OR MedicalRecordNumber = ''
                        )
                        UPDATE p
                        SET p.MedicalRecordNumber = 'BN-' + CAST(YEAR(GETUTCDATE()) AS NVARCHAR(4)) + '-' + RIGHT('000000' + CAST(np.RowNum AS NVARCHAR(10)), 6)
                        FROM Patients p
                        INNER JOIN NumberedPatients np ON p.Id = np.Id;
                    
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN

                        DECLARE @CurrentYear INT = YEAR(GETUTCDATE());
                        DECLARE @MaxSeq BIGINT = (
                            SELECT ISNULL(MAX(CAST(RIGHT(MedicalRecordNumber, 6) AS BIGINT)), 0)
                            FROM Patients
                            WHERE MedicalRecordNumber LIKE 'BN-' + CAST(@CurrentYear AS NVARCHAR(4)) + '-%'
                        );

                        IF EXISTS (SELECT 1 FROM MrnSequences WHERE [Year] = @CurrentYear)
                        BEGIN
                            UPDATE MrnSequences
                            SET LastSequenceNumber = CASE WHEN @MaxSeq > LastSequenceNumber THEN @MaxSeq ELSE LastSequenceNumber END
                            WHERE [Year] = @CurrentYear;
                        END
                        ELSE IF @MaxSeq > 0
                        BEGIN
                            INSERT INTO MrnSequences ([Year], LastSequenceNumber)
                            VALUES (@CurrentYear, @MaxSeq);
                        END
                    
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Patients]') AND [c].[name] = N'FullName');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Patients] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [Patients] ALTER COLUMN [FullName] nvarchar(200) NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Patients]') AND [c].[name] = N'MedicalRecordNumber');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [Patients] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [Patients] ALTER COLUMN [MedicalRecordNumber] nvarchar(50) NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    CREATE INDEX [IX_Patients_BhytNumber] ON [Patients] ([BhytNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Patients_MedicalRecordNumber] ON [Patients] ([MedicalRecordNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    CREATE INDEX [IX_Patients_NationalId] ON [Patients] ([NationalId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    CREATE INDEX [IX_Patients_PrimaryFacilityId] ON [Patients] ([PrimaryFacilityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Patients_UserId] ON [Patients] ([UserId]) WHERE [UserId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Beds_RoomId_BedNumber] ON [Beds] ([RoomId], [BedNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Buildings_FacilityId_Code] ON [Buildings] ([FacilityId], [Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    CREATE INDEX [IX_Departments_BuildingId] ON [Departments] ([BuildingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Departments_FacilityId_Code] ON [Departments] ([FacilityId], [Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    CREATE INDEX [IX_Departments_HeadOfDepartmentDoctorId] ON [Departments] ([HeadOfDepartmentDoctorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    CREATE INDEX [IX_EmergencyContacts_PatientId] ON [EmergencyContacts] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Facilities_Code] ON [Facilities] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    CREATE INDEX [IX_Facilities_IsActive] ON [Facilities] ([IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    CREATE UNIQUE INDEX [IX_MrnSequences_Year] ON [MrnSequences] ([Year]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    CREATE INDEX [IX_PatientAllergies_PatientId] ON [PatientAllergies] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    CREATE INDEX [IX_Rooms_BuildingId] ON [Rooms] ([BuildingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Rooms_DepartmentId_RoomNumber] ON [Rooms] ([DepartmentId], [RoomNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    ALTER TABLE [Patients] ADD CONSTRAINT [FK_Patients_Facilities_PrimaryFacilityId] FOREIGN KEY ([PrimaryFacilityId]) REFERENCES [Facilities] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260913045133_AddHospitalOrganizationCoreAndMpi', N'10.0.11');
END;

COMMIT;
GO

