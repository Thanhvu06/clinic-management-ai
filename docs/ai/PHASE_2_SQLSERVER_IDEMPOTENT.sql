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
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetRoles] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(256) NULL,
        [NormalizedName] nvarchar(256) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUsers] (
        [Id] uniqueidentifier NOT NULL,
        [FullName] nvarchar(max) NOT NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [UserName] nvarchar(256) NULL,
        [NormalizedUserName] nvarchar(256) NULL,
        [Email] nvarchar(256) NOT NULL,
        [NormalizedEmail] nvarchar(256) NULL,
        [EmailConfirmed] bit NOT NULL,
        [PasswordHash] nvarchar(max) NULL,
        [SecurityStamp] nvarchar(max) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(450) NOT NULL,
        [PhoneNumberConfirmed] bit NOT NULL,
        [TwoFactorEnabled] bit NOT NULL,
        [LockoutEnd] datetimeoffset NULL,
        [LockoutEnabled] bit NOT NULL,
        [AccessFailedCount] int NOT NULL,
        CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE TABLE [Specialties] (
        [Id] bigint NOT NULL IDENTITY,
        [SpecialtyCode] nvarchar(450) NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [Description] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [AiEnabled] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Specialties] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetRoleClaims] (
        [Id] int NOT NULL IDENTITY,
        [RoleId] uniqueidentifier NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserClaims] (
        [Id] int NOT NULL IDENTITY,
        [UserId] uniqueidentifier NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserLogins] (
        [LoginProvider] nvarchar(450) NOT NULL,
        [ProviderKey] nvarchar(450) NOT NULL,
        [ProviderDisplayName] nvarchar(max) NULL,
        [UserId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserRoles] (
        [UserId] uniqueidentifier NOT NULL,
        [RoleId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserTokens] (
        [UserId] uniqueidentifier NOT NULL,
        [LoginProvider] nvarchar(450) NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Value] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE TABLE [Doctors] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] uniqueidentifier NOT NULL,
        [AcademicTitle] nvarchar(max) NULL,
        [ExperienceYears] int NOT NULL,
        [Description] nvarchar(max) NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        CONSTRAINT [PK_Doctors] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Doctor_ExperienceYears] CHECK ([ExperienceYears] >= 0),
        CONSTRAINT [FK_Doctors_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE TABLE [Patients] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] uniqueidentifier NOT NULL,
        [Gender] nvarchar(max) NULL,
        [DateOfBirth] date NULL,
        [Address] nvarchar(max) NULL,
        CONSTRAINT [PK_Patients] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Patients_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE TABLE [SystemAuditLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] uniqueidentifier NOT NULL,
        [Action] nvarchar(max) NOT NULL,
        [EntityName] nvarchar(max) NOT NULL,
        [EntityId] nvarchar(max) NOT NULL,
        [Description] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_SystemAuditLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SystemAuditLogs_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE TABLE [AppointmentSlots] (
        [Id] bigint NOT NULL IDENTITY,
        [DoctorId] bigint NOT NULL,
        [SlotDate] date NOT NULL,
        [StartTime] time NOT NULL,
        [EndTime] time NOT NULL,
        [IsBooked] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_AppointmentSlots] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_AppointmentSlot_Duration] CHECK (DATEDIFF(MINUTE, [StartTime], [EndTime]) = 30),
        CONSTRAINT [CK_AppointmentSlot_TimeRange] CHECK ([StartTime] < [EndTime]),
        CONSTRAINT [FK_AppointmentSlots_Doctors_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [Doctors] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE TABLE [DoctorLeaveRequests] (
        [Id] bigint NOT NULL IDENTITY,
        [DoctorId] bigint NOT NULL,
        [StartDateTime] datetime2 NOT NULL,
        [EndDateTime] datetime2 NOT NULL,
        [Reason] nvarchar(max) NOT NULL,
        [Status] nvarchar(max) NOT NULL,
        [AdminNote] nvarchar(max) NULL,
        CONSTRAINT [PK_DoctorLeaveRequests] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_DoctorLeaveRequest_DateTimeRange] CHECK ([StartDateTime] < [EndDateTime]),
        CONSTRAINT [FK_DoctorLeaveRequests_Doctors_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [Doctors] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE TABLE [DoctorSpecialties] (
        [DoctorId] bigint NOT NULL,
        [SpecialtyId] bigint NOT NULL,
        [IsPrimary] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_DoctorSpecialties] PRIMARY KEY ([DoctorId], [SpecialtyId]),
        CONSTRAINT [FK_DoctorSpecialties_Doctors_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [Doctors] ([Id]),
        CONSTRAINT [FK_DoctorSpecialties_Specialties_SpecialtyId] FOREIGN KEY ([SpecialtyId]) REFERENCES [Specialties] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE TABLE [DoctorWorkSchedules] (
        [Id] bigint NOT NULL IDENTITY,
        [DoctorId] bigint NOT NULL,
        [WorkDate] date NOT NULL,
        [StartTime] time NOT NULL,
        [EndTime] time NOT NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        CONSTRAINT [PK_DoctorWorkSchedules] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_DoctorWorkSchedule_TimeRange] CHECK ([StartTime] < [EndTime]),
        CONSTRAINT [FK_DoctorWorkSchedules_Doctors_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [Doctors] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE TABLE [AiSuggestionLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [PatientId] bigint NOT NULL,
        [InputText] nvarchar(max) NOT NULL,
        [SuggestedSpecialtiesJson] nvarchar(max) NOT NULL,
        [SelectedSpecialtyId] bigint NULL,
        [Provider] nvarchar(max) NOT NULL,
        [PromptVersion] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_AiSuggestionLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AiSuggestionLogs_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id]),
        CONSTRAINT [FK_AiSuggestionLogs_Specialties_SelectedSpecialtyId] FOREIGN KEY ([SelectedSpecialtyId]) REFERENCES [Specialties] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE TABLE [Appointments] (
        [Id] bigint NOT NULL IDENTITY,
        [AppointmentCode] nvarchar(450) NOT NULL,
        [PatientId] bigint NOT NULL,
        [DoctorId] bigint NOT NULL,
        [SpecialtyId] bigint NOT NULL,
        [AppointmentSlotId] bigint NOT NULL,
        [AppointmentDate] date NOT NULL,
        [StartTime] time NOT NULL,
        [EndTime] time NOT NULL,
        [Reason] nvarchar(500) NULL,
        [Status] nvarchar(max) NOT NULL DEFAULT N'Pending',
        CONSTRAINT [PK_Appointments] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Appointment_Duration] CHECK (DATEDIFF(MINUTE, [StartTime], [EndTime]) = 30),
        CONSTRAINT [CK_Appointment_TimeRange] CHECK ([StartTime] < [EndTime]),
        CONSTRAINT [FK_Appointments_AppointmentSlots_AppointmentSlotId] FOREIGN KEY ([AppointmentSlotId]) REFERENCES [AppointmentSlots] ([Id]),
        CONSTRAINT [FK_Appointments_Doctors_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [Doctors] ([Id]),
        CONSTRAINT [FK_Appointments_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id]),
        CONSTRAINT [FK_Appointments_Specialties_SpecialtyId] FOREIGN KEY ([SpecialtyId]) REFERENCES [Specialties] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE TABLE [AppointmentChangeRequests] (
        [Id] bigint NOT NULL IDENTITY,
        [AppointmentId] bigint NOT NULL,
        [RequestType] nvarchar(max) NOT NULL,
        [RequestedSlotId] bigint NULL,
        [Reason] nvarchar(max) NULL,
        [Status] nvarchar(max) NOT NULL DEFAULT N'Pending',
        [RequestedByUserId] uniqueidentifier NOT NULL,
        [ProcessedByUserId] uniqueidentifier NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ProcessedAt] datetime2 NULL,
        CONSTRAINT [PK_AppointmentChangeRequests] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AppointmentChangeRequests_AppointmentSlots_RequestedSlotId] FOREIGN KEY ([RequestedSlotId]) REFERENCES [AppointmentSlots] ([Id]),
        CONSTRAINT [FK_AppointmentChangeRequests_Appointments_AppointmentId] FOREIGN KEY ([AppointmentId]) REFERENCES [Appointments] ([Id]),
        CONSTRAINT [FK_AppointmentChangeRequests_AspNetUsers_ProcessedByUserId] FOREIGN KEY ([ProcessedByUserId]) REFERENCES [AspNetUsers] ([Id]),
        CONSTRAINT [FK_AppointmentChangeRequests_AspNetUsers_RequestedByUserId] FOREIGN KEY ([RequestedByUserId]) REFERENCES [AspNetUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE TABLE [AppointmentHistory] (
        [Id] bigint NOT NULL IDENTITY,
        [AppointmentId] bigint NOT NULL,
        [Action] nvarchar(max) NOT NULL,
        [OldStatus] nvarchar(max) NULL,
        [NewStatus] nvarchar(max) NULL,
        [Note] nvarchar(max) NULL,
        [PerformedByUserId] uniqueidentifier NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_AppointmentHistory] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AppointmentHistory_Appointments_AppointmentId] FOREIGN KEY ([AppointmentId]) REFERENCES [Appointments] ([Id]),
        CONSTRAINT [FK_AppointmentHistory_AspNetUsers_PerformedByUserId] FOREIGN KEY ([PerformedByUserId]) REFERENCES [AspNetUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE TABLE [RevisitRequests] (
        [Id] bigint NOT NULL IDENTITY,
        [AppointmentId] bigint NOT NULL,
        [PatientId] bigint NOT NULL,
        [DoctorId] bigint NOT NULL,
        [SuggestedDate] date NOT NULL,
        [Note] nvarchar(max) NULL,
        [Status] nvarchar(max) NOT NULL DEFAULT N'PendingPatientResponse',
        [NewAppointmentId] bigint NULL,
        CONSTRAINT [PK_RevisitRequests] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RevisitRequests_Appointments_AppointmentId] FOREIGN KEY ([AppointmentId]) REFERENCES [Appointments] ([Id]),
        CONSTRAINT [FK_RevisitRequests_Appointments_NewAppointmentId] FOREIGN KEY ([NewAppointmentId]) REFERENCES [Appointments] ([Id]),
        CONSTRAINT [FK_RevisitRequests_Doctors_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [Doctors] ([Id]),
        CONSTRAINT [FK_RevisitRequests_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE TABLE [VisitSummaries] (
        [Id] bigint NOT NULL IDENTITY,
        [AppointmentId] bigint NOT NULL,
        [DoctorId] bigint NOT NULL,
        [Summary] nvarchar(max) NOT NULL,
        [FollowUpInstruction] nvarchar(max) NULL,
        CONSTRAINT [PK_VisitSummaries] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_VisitSummaries_Appointments_AppointmentId] FOREIGN KEY ([AppointmentId]) REFERENCES [Appointments] ([Id]),
        CONSTRAINT [FK_VisitSummaries_Doctors_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [Doctors] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AiSuggestionLogs_PatientId] ON [AiSuggestionLogs] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AiSuggestionLogs_SelectedSpecialtyId] ON [AiSuggestionLogs] ([SelectedSpecialtyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_AppointmentChangeRequests_AppointmentId] ON [AppointmentChangeRequests] ([AppointmentId]) WHERE [Status] = ''Pending''');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AppointmentChangeRequests_ProcessedByUserId] ON [AppointmentChangeRequests] ([ProcessedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AppointmentChangeRequests_RequestedByUserId] ON [AppointmentChangeRequests] ([RequestedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AppointmentChangeRequests_RequestedSlotId] ON [AppointmentChangeRequests] ([RequestedSlotId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AppointmentHistory_AppointmentId] ON [AppointmentHistory] ([AppointmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AppointmentHistory_PerformedByUserId] ON [AppointmentHistory] ([PerformedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Appointments_AppointmentCode] ON [Appointments] ([AppointmentCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Appointments_AppointmentSlotId] ON [Appointments] ([AppointmentSlotId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Appointments_DoctorId] ON [Appointments] ([DoctorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Appointments_PatientId] ON [Appointments] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Appointments_SpecialtyId] ON [Appointments] ([SpecialtyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AppointmentSlots_DoctorId_SlotDate_StartTime] ON [AppointmentSlots] ([DoctorId], [SlotDate], [StartTime]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]) WHERE [NormalizedEmail] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_AspNetUsers_PhoneNumber] ON [AspNetUsers] ([PhoneNumber]) WHERE [PhoneNumber] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DoctorLeaveRequests_DoctorId] ON [DoctorLeaveRequests] ([DoctorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Doctors_UserId] ON [Doctors] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_DoctorSpecialties_DoctorId] ON [DoctorSpecialties] ([DoctorId]) WHERE [IsPrimary] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DoctorSpecialties_SpecialtyId] ON [DoctorSpecialties] ([SpecialtyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DoctorWorkSchedules_DoctorId] ON [DoctorWorkSchedules] ([DoctorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Patients_UserId] ON [Patients] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RevisitRequests_AppointmentId] ON [RevisitRequests] ([AppointmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RevisitRequests_DoctorId] ON [RevisitRequests] ([DoctorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RevisitRequests_NewAppointmentId] ON [RevisitRequests] ([NewAppointmentId]) WHERE [NewAppointmentId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RevisitRequests_PatientId] ON [RevisitRequests] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Specialties_SpecialtyCode] ON [Specialties] ([SpecialtyCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SystemAuditLogs_UserId] ON [SystemAuditLogs] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_VisitSummaries_AppointmentId] ON [VisitSummaries] ([AppointmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_VisitSummaries_DoctorId] ON [VisitSummaries] ([DoctorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822032304_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260822032304_InitialCreate', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823111813_AddPharmacyModule'
)
BEGIN
    CREATE TABLE [Medicines] (
        [Id] bigint NOT NULL IDENTITY,
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Unit] nvarchar(50) NOT NULL,
        [StockQuantity] int NOT NULL,
        [ReorderLevel] int NOT NULL,
        [IsActive] bit NOT NULL,
        [RowVersion] rowversion NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_Medicines] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823111813_AddPharmacyModule'
)
BEGIN
    CREATE TABLE [Prescriptions] (
        [Id] bigint NOT NULL IDENTITY,
        [AppointmentId] bigint NOT NULL,
        [PatientId] bigint NOT NULL,
        [DoctorId] bigint NOT NULL,
        [Status] int NOT NULL,
        [Notes] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [DispensedAt] datetime2 NULL,
        [DispensedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_Prescriptions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Prescriptions_Appointments_AppointmentId] FOREIGN KEY ([AppointmentId]) REFERENCES [Appointments] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Prescriptions_Doctors_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [Doctors] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Prescriptions_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823111813_AddPharmacyModule'
)
BEGIN
    CREATE TABLE [MedicineStockTransactions] (
        [Id] bigint NOT NULL IDENTITY,
        [MedicineId] bigint NOT NULL,
        [Type] int NOT NULL,
        [QuantityChange] int NOT NULL,
        [BalanceAfter] int NOT NULL,
        [PrescriptionId] bigint NULL,
        [Reason] nvarchar(max) NULL,
        [ActorUserId] uniqueidentifier NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_MedicineStockTransactions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MedicineStockTransactions_Medicines_MedicineId] FOREIGN KEY ([MedicineId]) REFERENCES [Medicines] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_MedicineStockTransactions_Prescriptions_PrescriptionId] FOREIGN KEY ([PrescriptionId]) REFERENCES [Prescriptions] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823111813_AddPharmacyModule'
)
BEGIN
    CREATE TABLE [PrescriptionItems] (
        [PrescriptionId] bigint NOT NULL,
        [MedicineId] bigint NOT NULL,
        [Quantity] int NOT NULL,
        [Dosage] nvarchar(100) NOT NULL,
        [Frequency] nvarchar(100) NOT NULL,
        [DurationDays] int NULL,
        [Instructions] nvarchar(max) NULL,
        CONSTRAINT [PK_PrescriptionItems] PRIMARY KEY ([PrescriptionId], [MedicineId]),
        CONSTRAINT [FK_PrescriptionItems_Medicines_MedicineId] FOREIGN KEY ([MedicineId]) REFERENCES [Medicines] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_PrescriptionItems_Prescriptions_PrescriptionId] FOREIGN KEY ([PrescriptionId]) REFERENCES [Prescriptions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823111813_AddPharmacyModule'
)
BEGIN
    CREATE INDEX [IX_MedicineStockTransactions_MedicineId] ON [MedicineStockTransactions] ([MedicineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823111813_AddPharmacyModule'
)
BEGIN
    CREATE INDEX [IX_MedicineStockTransactions_PrescriptionId] ON [MedicineStockTransactions] ([PrescriptionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823111813_AddPharmacyModule'
)
BEGIN
    CREATE INDEX [IX_PrescriptionItems_MedicineId] ON [PrescriptionItems] ([MedicineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823111813_AddPharmacyModule'
)
BEGIN
    CREATE INDEX [IX_Prescriptions_AppointmentId] ON [Prescriptions] ([AppointmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823111813_AddPharmacyModule'
)
BEGIN
    CREATE INDEX [IX_Prescriptions_DoctorId] ON [Prescriptions] ([DoctorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823111813_AddPharmacyModule'
)
BEGIN
    CREATE INDEX [IX_Prescriptions_PatientId] ON [Prescriptions] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823111813_AddPharmacyModule'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823111813_AddPharmacyModule', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904140521_AddHealthPackages'
)
BEGIN
    CREATE TABLE [HealthPackages] (
        [Id] bigint NOT NULL IDENTITY,
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [TargetAudience] nvarchar(100) NOT NULL,
        [Description] nvarchar(max) NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        [ImageUrl] nvarchar(500) NULL,
        [IncludedServicesJson] nvarchar(max) NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_HealthPackages] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904140521_AddHealthPackages'
)
BEGIN
    CREATE UNIQUE INDEX [IX_HealthPackages_Code] ON [HealthPackages] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904140521_AddHealthPackages'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260904140521_AddHealthPackages', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905014912_AddHealthPackageRegistration'
)
BEGIN
    CREATE TABLE [HealthPackageRegistrations] (
        [Id] bigint NOT NULL IDENTITY,
        [RegistrationCode] nvarchar(50) NOT NULL,
        [HealthPackageId] bigint NOT NULL,
        [PatientId] bigint NOT NULL,
        [PreferredDate] date NOT NULL,
        [ContactPhone] nvarchar(20) NOT NULL,
        [Note] nvarchar(1000) NULL,
        [Status] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_HealthPackageRegistrations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_HealthPackageRegistrations_HealthPackages_HealthPackageId] FOREIGN KEY ([HealthPackageId]) REFERENCES [HealthPackages] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_HealthPackageRegistrations_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905014912_AddHealthPackageRegistration'
)
BEGIN
    CREATE INDEX [IX_HealthPackageRegistrations_HealthPackageId] ON [HealthPackageRegistrations] ([HealthPackageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905014912_AddHealthPackageRegistration'
)
BEGIN
    CREATE INDEX [IX_HealthPackageRegistrations_PatientId] ON [HealthPackageRegistrations] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905014912_AddHealthPackageRegistration'
)
BEGIN
    CREATE UNIQUE INDEX [IX_HealthPackageRegistrations_RegistrationCode] ON [HealthPackageRegistrations] ([RegistrationCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905014912_AddHealthPackageRegistration'
)
BEGIN
    CREATE INDEX [IX_HealthPackageRegistrations_Status] ON [HealthPackageRegistrations] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905014912_AddHealthPackageRegistration'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260905014912_AddHealthPackageRegistration', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905030819_AddClinicLocationAndContractFixes'
)
BEGIN
    ALTER TABLE [HealthPackageRegistrations] ADD [AdminNotes] nvarchar(1000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905030819_AddClinicLocationAndContractFixes'
)
BEGIN
    ALTER TABLE [HealthPackageRegistrations] ADD [CancellationReason] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905030819_AddClinicLocationAndContractFixes'
)
BEGIN
    CREATE TABLE [ClinicLocations] (
        [Id] bigint NOT NULL IDENTITY,
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Address] nvarchar(300) NOT NULL,
        [City] nvarchar(100) NOT NULL,
        [Phone] nvarchar(30) NOT NULL,
        [OpeningHours] nvarchar(100) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [ServicesJson] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ClinicLocations] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905030819_AddClinicLocationAndContractFixes'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ClinicLocations_Code] ON [ClinicLocations] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905030819_AddClinicLocationAndContractFixes'
)
BEGIN
    CREATE INDEX [IX_ClinicLocations_IsActive] ON [ClinicLocations] ([IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905030819_AddClinicLocationAndContractFixes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260905030819_AddClinicLocationAndContractFixes', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905043428_AddDoctorClinicalWorkflow'
)
BEGIN
    DROP INDEX [IX_Prescriptions_AppointmentId] ON [Prescriptions];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905043428_AddDoctorClinicalWorkflow'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[VisitSummaries]') AND [c].[name] = N'Summary');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [VisitSummaries] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [VisitSummaries] ALTER COLUMN [Summary] nvarchar(4000) NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905043428_AddDoctorClinicalWorkflow'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[VisitSummaries]') AND [c].[name] = N'FollowUpInstruction');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [VisitSummaries] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [VisitSummaries] ALTER COLUMN [FollowUpInstruction] nvarchar(1000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905043428_AddDoctorClinicalWorkflow'
)
BEGIN
    ALTER TABLE [VisitSummaries] ADD [ChiefComplaint] nvarchar(1000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905043428_AddDoctorClinicalWorkflow'
)
BEGIN
    ALTER TABLE [VisitSummaries] ADD [ClinicalFindings] nvarchar(4000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905043428_AddDoctorClinicalWorkflow'
)
BEGIN
    ALTER TABLE [VisitSummaries] ADD [CompletedAtUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905043428_AddDoctorClinicalWorkflow'
)
BEGIN
    ALTER TABLE [VisitSummaries] ADD [CreatedAtUtc] datetime2 NOT NULL DEFAULT '0001-01-01T00:00:00.0000000';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905043428_AddDoctorClinicalWorkflow'
)
BEGIN
    ALTER TABLE [VisitSummaries] ADD [Diagnosis] nvarchar(1000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905043428_AddDoctorClinicalWorkflow'
)
BEGIN
    ALTER TABLE [VisitSummaries] ADD [DiagnosisCode] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905043428_AddDoctorClinicalWorkflow'
)
BEGIN
    ALTER TABLE [VisitSummaries] ADD [RowVersion] rowversion NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905043428_AddDoctorClinicalWorkflow'
)
BEGIN
    ALTER TABLE [VisitSummaries] ADD [TreatmentPlan] nvarchar(4000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905043428_AddDoctorClinicalWorkflow'
)
BEGIN
    ALTER TABLE [VisitSummaries] ADD [UpdatedAtUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905043428_AddDoctorClinicalWorkflow'
)
BEGIN
    ALTER TABLE [Prescriptions] ADD [RowVersion] rowversion NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905043428_AddDoctorClinicalWorkflow'
)
BEGIN
    CREATE TABLE [AppointmentVitalSigns] (
        [Id] bigint NOT NULL IDENTITY,
        [AppointmentId] bigint NOT NULL,
        [Temperature] decimal(4,1) NULL,
        [BloodPressureSystolic] int NULL,
        [BloodPressureDiastolic] int NULL,
        [HeartRate] int NULL,
        [RespiratoryRate] int NULL,
        [Weight] decimal(5,2) NULL,
        [Height] decimal(5,1) NULL,
        [Bmi] decimal(4,1) NULL,
        [SpO2] int NULL,
        [RecordedAtUtc] datetime2 NOT NULL,
        [RecordedByUserId] uniqueidentifier NOT NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_AppointmentVitalSigns] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AppointmentVitalSigns_Appointments_AppointmentId] FOREIGN KEY ([AppointmentId]) REFERENCES [Appointments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905043428_AddDoctorClinicalWorkflow'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Prescriptions_AppointmentId] ON [Prescriptions] ([AppointmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905043428_AddDoctorClinicalWorkflow'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AppointmentVitalSigns_AppointmentId] ON [AppointmentVitalSigns] ([AppointmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905043428_AddDoctorClinicalWorkflow'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260905043428_AddDoctorClinicalWorkflow', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906050423_AddInAppNotificationsAndConstraints'
)
BEGIN
    DROP INDEX [IX_DoctorWorkSchedules_DoctorId] ON [DoctorWorkSchedules];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906050423_AddInAppNotificationsAndConstraints'
)
BEGIN
    CREATE TABLE [Notifications] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] uniqueidentifier NOT NULL,
        [Type] int NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Message] nvarchar(1000) NOT NULL,
        [Route] nvarchar(250) NULL,
        [RelatedEntityType] nvarchar(100) NULL,
        [RelatedEntityId] nvarchar(100) NULL,
        [IsRead] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedAtUtc] datetime2 NOT NULL,
        [ReadAtUtc] datetime2 NULL,
        [DedupeKey] nvarchar(150) NULL,
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906050423_AddInAppNotificationsAndConstraints'
)
BEGIN

                    WITH DuplicateSchedules AS (
                        SELECT Id, ROW_NUMBER() OVER(PARTITION BY DoctorId, WorkDate, StartTime, EndTime ORDER BY Id) as rn
                        FROM DoctorWorkSchedules
                    )
                    DELETE FROM DuplicateSchedules WHERE rn > 1;

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906050423_AddInAppNotificationsAndConstraints'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DoctorWorkSchedules_DoctorId_WorkDate_StartTime_EndTime] ON [DoctorWorkSchedules] ([DoctorId], [WorkDate], [StartTime], [EndTime]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906050423_AddInAppNotificationsAndConstraints'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Notifications_DedupeKey] ON [Notifications] ([DedupeKey]) WHERE [DedupeKey] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906050423_AddInAppNotificationsAndConstraints'
)
BEGIN
    CREATE INDEX [IX_Notifications_UserId_IsRead_CreatedAtUtc] ON [Notifications] ([UserId], [IsRead], [CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906050423_AddInAppNotificationsAndConstraints'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260906050423_AddInAppNotificationsAndConstraints', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906063514_AddBillingModule'
)
BEGIN
    ALTER TABLE [Specialties] ADD [ConsultationFee] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906063514_AddBillingModule'
)
BEGIN
    CREATE TABLE [Invoices] (
        [Id] bigint NOT NULL IDENTITY,
        [InvoiceCode] nvarchar(50) NOT NULL,
        [PatientId] bigint NOT NULL,
        [SourceType] int NOT NULL,
        [AppointmentId] bigint NULL,
        [HealthPackageRegistrationId] bigint NULL,
        [Status] int NOT NULL,
        [Subtotal] decimal(18,2) NOT NULL,
        [TotalAmount] decimal(18,2) NOT NULL,
        [CreatedByUserId] uniqueidentifier NOT NULL,
        [PaidByUserId] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [PaidAtUtc] datetime2 NULL,
        [CancelledAtUtc] datetime2 NULL,
        [CancellationReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Invoices] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Invoices_SingleSource] CHECK (([AppointmentId] IS NOT NULL AND [HealthPackageRegistrationId] IS NULL) OR ([AppointmentId] IS NULL AND [HealthPackageRegistrationId] IS NOT NULL)),
        CONSTRAINT [CK_Invoices_Subtotal_NonNegative] CHECK ([Subtotal] >= 0),
        CONSTRAINT [CK_Invoices_TotalAmount_NonNegative] CHECK ([TotalAmount] >= 0),
        CONSTRAINT [FK_Invoices_Appointments_AppointmentId] FOREIGN KEY ([AppointmentId]) REFERENCES [Appointments] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Invoices_HealthPackageRegistrations_HealthPackageRegistrationId] FOREIGN KEY ([HealthPackageRegistrationId]) REFERENCES [HealthPackageRegistrations] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Invoices_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906063514_AddBillingModule'
)
BEGIN
    CREATE TABLE [InvoiceItems] (
        [Id] bigint NOT NULL IDENTITY,
        [InvoiceId] bigint NOT NULL,
        [ItemCode] nvarchar(50) NOT NULL,
        [Description] nvarchar(500) NOT NULL,
        [Quantity] int NOT NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        [LineTotal] decimal(18,2) NOT NULL,
        [ReferenceType] nvarchar(50) NOT NULL,
        [ReferenceId] bigint NOT NULL,
        CONSTRAINT [PK_InvoiceItems] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_InvoiceItems_LineTotal_NonNegative] CHECK ([LineTotal] >= 0),
        CONSTRAINT [CK_InvoiceItems_Quantity_Positive] CHECK ([Quantity] > 0),
        CONSTRAINT [CK_InvoiceItems_UnitPrice_NonNegative] CHECK ([UnitPrice] >= 0),
        CONSTRAINT [FK_InvoiceItems_Invoices_InvoiceId] FOREIGN KEY ([InvoiceId]) REFERENCES [Invoices] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906063514_AddBillingModule'
)
BEGIN
    CREATE TABLE [Payments] (
        [Id] bigint NOT NULL IDENTITY,
        [PaymentCode] nvarchar(50) NOT NULL,
        [InvoiceId] bigint NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Method] int NOT NULL,
        [ReferenceCode] nvarchar(100) NULL,
        [Note] nvarchar(500) NULL,
        [ReceivedByUserId] uniqueidentifier NOT NULL,
        [ReceivedAtUtc] datetime2 NOT NULL,
        [Status] int NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Payments] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Payments_Amount_Positive] CHECK ([Amount] > 0),
        CONSTRAINT [FK_Payments_Invoices_InvoiceId] FOREIGN KEY ([InvoiceId]) REFERENCES [Invoices] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906063514_AddBillingModule'
)
BEGIN
    EXEC(N'ALTER TABLE [Specialties] ADD CONSTRAINT [CK_Specialties_ConsultationFee_NonNegative] CHECK ([ConsultationFee] >= 0)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906063514_AddBillingModule'
)
BEGIN
    CREATE INDEX [IX_InvoiceItems_InvoiceId] ON [InvoiceItems] ([InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906063514_AddBillingModule'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Invoices_AppointmentId] ON [Invoices] ([AppointmentId]) WHERE [AppointmentId] IS NOT NULL AND [Status] <> 3');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906063514_AddBillingModule'
)
BEGIN
    CREATE INDEX [IX_Invoices_CreatedAtUtc] ON [Invoices] ([CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906063514_AddBillingModule'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Invoices_HealthPackageRegistrationId] ON [Invoices] ([HealthPackageRegistrationId]) WHERE [HealthPackageRegistrationId] IS NOT NULL AND [Status] <> 3');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906063514_AddBillingModule'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Invoices_InvoiceCode] ON [Invoices] ([InvoiceCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906063514_AddBillingModule'
)
BEGIN
    CREATE INDEX [IX_Invoices_PatientId] ON [Invoices] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906063514_AddBillingModule'
)
BEGIN
    CREATE INDEX [IX_Invoices_PatientId_CreatedAtUtc] ON [Invoices] ([PatientId], [CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906063514_AddBillingModule'
)
BEGIN
    CREATE INDEX [IX_Invoices_Status] ON [Invoices] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906063514_AddBillingModule'
)
BEGIN
    CREATE INDEX [IX_Payments_InvoiceId] ON [Payments] ([InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906063514_AddBillingModule'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Payments_PaymentCode] ON [Payments] ([PaymentCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906063514_AddBillingModule'
)
BEGIN
    CREATE INDEX [IX_Payments_ReceivedAtUtc] ON [Payments] ([ReceivedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906063514_AddBillingModule'
)
BEGIN
    CREATE INDEX [IX_Payments_Status] ON [Payments] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906063514_AddBillingModule'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260906063514_AddBillingModule', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906101520_AddOriginalAppointmentStatusToAppointmentChangeRequest'
)
BEGIN
    ALTER TABLE [AppointmentChangeRequests] ADD [OriginalAppointmentStatus] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260906101520_AddOriginalAppointmentStatusToAppointmentChangeRequest'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260906101520_AddOriginalAppointmentStatusToAppointmentChangeRequest', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907070223_AddDiagnosticOrderWorkflow'
)
BEGIN
    CREATE TABLE [DiagnosticOrders] (
        [Id] bigint NOT NULL IDENTITY,
        [OrderCode] nvarchar(50) NOT NULL,
        [AppointmentId] bigint NOT NULL,
        [PatientId] bigint NOT NULL,
        [OrderingDoctorId] bigint NOT NULL,
        [ClinicalIndication] nvarchar(1000) NOT NULL,
        [Note] nvarchar(1000) NULL,
        [Status] nvarchar(450) NOT NULL DEFAULT N'Ordered',
        [OrderedAtUtc] datetime2 NOT NULL,
        [StartedAtUtc] datetime2 NULL,
        [CompletedAtUtc] datetime2 NULL,
        [CancelledAtUtc] datetime2 NULL,
        [StartedByUserId] uniqueidentifier NULL,
        [CompletedByUserId] uniqueidentifier NULL,
        [ReviewedAtUtc] datetime2 NULL,
        [ReviewedByDoctorId] bigint NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_DiagnosticOrders] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DiagnosticOrders_Appointments_AppointmentId] FOREIGN KEY ([AppointmentId]) REFERENCES [Appointments] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DiagnosticOrders_Doctors_OrderingDoctorId] FOREIGN KEY ([OrderingDoctorId]) REFERENCES [Doctors] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DiagnosticOrders_Doctors_ReviewedByDoctorId] FOREIGN KEY ([ReviewedByDoctorId]) REFERENCES [Doctors] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DiagnosticOrders_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907070223_AddDiagnosticOrderWorkflow'
)
BEGIN
    CREATE TABLE [DiagnosticServices] (
        [Id] bigint NOT NULL IDENTITY,
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Category] nvarchar(max) NOT NULL,
        [PreparationInstructions] nvarchar(1000) NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        CONSTRAINT [PK_DiagnosticServices] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907070223_AddDiagnosticOrderWorkflow'
)
BEGIN
    CREATE TABLE [DiagnosticOrderItems] (
        [Id] bigint NOT NULL IDENTITY,
        [DiagnosticOrderId] bigint NOT NULL,
        [DiagnosticServiceId] bigint NOT NULL,
        [Status] nvarchar(max) NOT NULL DEFAULT N'Ordered',
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_DiagnosticOrderItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DiagnosticOrderItems_DiagnosticOrders_DiagnosticOrderId] FOREIGN KEY ([DiagnosticOrderId]) REFERENCES [DiagnosticOrders] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_DiagnosticOrderItems_DiagnosticServices_DiagnosticServiceId] FOREIGN KEY ([DiagnosticServiceId]) REFERENCES [DiagnosticServices] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907070223_AddDiagnosticOrderWorkflow'
)
BEGIN
    CREATE TABLE [DiagnosticResults] (
        [Id] bigint NOT NULL IDENTITY,
        [DiagnosticOrderItemId] bigint NOT NULL,
        [ResultText] nvarchar(4000) NOT NULL,
        [Conclusion] nvarchar(2000) NULL,
        [ReferenceRange] nvarchar(200) NULL,
        [Unit] nvarchar(50) NULL,
        [ResultedAtUtc] datetime2 NOT NULL,
        [ResultedByUserId] uniqueidentifier NOT NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_DiagnosticResults] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DiagnosticResults_DiagnosticOrderItems_DiagnosticOrderItemId] FOREIGN KEY ([DiagnosticOrderItemId]) REFERENCES [DiagnosticOrderItems] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907070223_AddDiagnosticOrderWorkflow'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DiagnosticOrderItems_DiagnosticOrderId_DiagnosticServiceId] ON [DiagnosticOrderItems] ([DiagnosticOrderId], [DiagnosticServiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907070223_AddDiagnosticOrderWorkflow'
)
BEGIN
    CREATE INDEX [IX_DiagnosticOrderItems_DiagnosticServiceId] ON [DiagnosticOrderItems] ([DiagnosticServiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907070223_AddDiagnosticOrderWorkflow'
)
BEGIN
    CREATE INDEX [IX_DiagnosticOrders_AppointmentId] ON [DiagnosticOrders] ([AppointmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907070223_AddDiagnosticOrderWorkflow'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DiagnosticOrders_OrderCode] ON [DiagnosticOrders] ([OrderCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907070223_AddDiagnosticOrderWorkflow'
)
BEGIN
    CREATE INDEX [IX_DiagnosticOrders_OrderingDoctorId_Status] ON [DiagnosticOrders] ([OrderingDoctorId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907070223_AddDiagnosticOrderWorkflow'
)
BEGIN
    CREATE INDEX [IX_DiagnosticOrders_PatientId_OrderedAtUtc] ON [DiagnosticOrders] ([PatientId], [OrderedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907070223_AddDiagnosticOrderWorkflow'
)
BEGIN
    CREATE INDEX [IX_DiagnosticOrders_ReviewedByDoctorId] ON [DiagnosticOrders] ([ReviewedByDoctorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907070223_AddDiagnosticOrderWorkflow'
)
BEGIN
    CREATE INDEX [IX_DiagnosticOrders_Status_OrderedAtUtc] ON [DiagnosticOrders] ([Status], [OrderedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907070223_AddDiagnosticOrderWorkflow'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DiagnosticResults_DiagnosticOrderItemId] ON [DiagnosticResults] ([DiagnosticOrderItemId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907070223_AddDiagnosticOrderWorkflow'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DiagnosticServices_Code] ON [DiagnosticServices] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907070223_AddDiagnosticOrderWorkflow'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260907070223_AddDiagnosticOrderWorkflow', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260908072449_RemoveDiagnosticStatusDefaultValues'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[DiagnosticOrders]') AND [c].[name] = N'Status');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [DiagnosticOrders] DROP CONSTRAINT ' + @var2 + ';');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260908072449_RemoveDiagnosticStatusDefaultValues'
)
BEGIN
    DECLARE @var3 nvarchar(max);
    SELECT @var3 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[DiagnosticOrderItems]') AND [c].[name] = N'Status');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [DiagnosticOrderItems] DROP CONSTRAINT ' + @var3 + ';');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260908072449_RemoveDiagnosticStatusDefaultValues'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260908072449_RemoveDiagnosticStatusDefaultValues', N'10.0.11');
END;

COMMIT;
GO

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
    DECLARE @var4 nvarchar(max);
    SELECT @var4 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Patients]') AND [c].[name] = N'UserId');
    IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [Patients] DROP CONSTRAINT ' + @var4 + ';');
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
    EXEC(N'
                        UPDATE p
                        SET p.FullName = ISNULL(NULLIF(u.FullName, ''''), N''Bệnh nhân''),
                            p.PhoneNumber = COALESCE(p.PhoneNumber, u.PhoneNumber),
                            p.Email = COALESCE(p.Email, u.Email)
                        FROM Patients p
                        INNER JOIN AspNetUsers u ON p.UserId = u.Id
                        WHERE p.FullName IS NULL OR p.FullName = '''';
                    ');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    EXEC(N'
                        UPDATE Patients
                        SET FullName = N''Bệnh nhân '' + CAST(Id AS NVARCHAR(20))
                        WHERE FullName IS NULL OR FullName = '''';
                    ');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    EXEC(N'
                        ;WITH NumberedPatients AS (
                            SELECT Id, ROW_NUMBER() OVER (ORDER BY Id) AS RowNum
                            FROM Patients
                            WHERE MedicalRecordNumber IS NULL OR MedicalRecordNumber = ''''
                        )
                        UPDATE p
                        SET p.MedicalRecordNumber = ''BN-'' + CAST(YEAR(GETUTCDATE()) AS NVARCHAR(4)) + ''-'' + RIGHT(''000000'' + CAST(np.RowNum AS NVARCHAR(10)), 6)
                        FROM Patients p
                        INNER JOIN NumberedPatients np ON p.Id = np.Id;
                    ');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    EXEC(N'
                        DECLARE @CurrentYear INT = YEAR(GETUTCDATE());
                        DECLARE @MaxSeq BIGINT = (
                            SELECT ISNULL(MAX(CAST(RIGHT(MedicalRecordNumber, 6) AS BIGINT)), 0)
                            FROM Patients
                            WHERE MedicalRecordNumber LIKE ''BN-'' + CAST(@CurrentYear AS NVARCHAR(4)) + ''-%''
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
                    ');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    DECLARE @var5 nvarchar(max);
    SELECT @var5 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Patients]') AND [c].[name] = N'FullName');
    IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [Patients] DROP CONSTRAINT ' + @var5 + ';');
    ALTER TABLE [Patients] ALTER COLUMN [FullName] nvarchar(200) NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913045133_AddHospitalOrganizationCoreAndMpi'
)
BEGIN
    DECLARE @var6 nvarchar(max);
    SELECT @var6 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Patients]') AND [c].[name] = N'MedicalRecordNumber');
    IF @var6 IS NOT NULL EXEC(N'ALTER TABLE [Patients] DROP CONSTRAINT ' + @var6 + ';');
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

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913082311_AddPatientNationalIdUniqueIndex'
)
BEGIN
    DROP INDEX [IX_Patients_NationalId] ON [Patients];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913082311_AddPatientNationalIdUniqueIndex'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Patients_NationalId] ON [Patients] ([NationalId]) WHERE [NationalId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913082311_AddPatientNationalIdUniqueIndex'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260913082311_AddPatientNationalIdUniqueIndex', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    ALTER TABLE [AppointmentVitalSigns] DROP CONSTRAINT [FK_AppointmentVitalSigns_Appointments_AppointmentId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    ALTER TABLE [Prescriptions] DROP CONSTRAINT [FK_Prescriptions_Appointments_AppointmentId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    ALTER TABLE [VisitSummaries] DROP CONSTRAINT [FK_VisitSummaries_Appointments_AppointmentId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    DROP INDEX [IX_VisitSummaries_AppointmentId] ON [VisitSummaries];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    DROP INDEX [IX_Prescriptions_AppointmentId] ON [Prescriptions];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    ALTER TABLE [Invoices] DROP CONSTRAINT [CK_Invoices_SingleSource];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    DROP INDEX [IX_AppointmentVitalSigns_AppointmentId] ON [AppointmentVitalSigns];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    DECLARE @var7 nvarchar(max);
    SELECT @var7 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[VisitSummaries]') AND [c].[name] = N'AppointmentId');
    IF @var7 IS NOT NULL EXEC(N'ALTER TABLE [VisitSummaries] DROP CONSTRAINT ' + @var7 + ';');
    ALTER TABLE [VisitSummaries] ALTER COLUMN [AppointmentId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    ALTER TABLE [VisitSummaries] ADD [PatientVisitId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    DECLARE @var8 nvarchar(max);
    SELECT @var8 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Prescriptions]') AND [c].[name] = N'AppointmentId');
    IF @var8 IS NOT NULL EXEC(N'ALTER TABLE [Prescriptions] DROP CONSTRAINT ' + @var8 + ';');
    ALTER TABLE [Prescriptions] ALTER COLUMN [AppointmentId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    ALTER TABLE [Prescriptions] ADD [PatientVisitId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    DECLARE @var9 nvarchar(max);
    SELECT @var9 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Medicines]') AND [c].[name] = N'IsActive');
    IF @var9 IS NOT NULL EXEC(N'ALTER TABLE [Medicines] DROP CONSTRAINT ' + @var9 + ';');
    ALTER TABLE [Medicines] ADD DEFAULT CAST(1 AS bit) FOR [IsActive];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    ALTER TABLE [Medicines] ADD [UnitPrice] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    ALTER TABLE [Invoices] ADD [PatientVisitId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    ALTER TABLE [DiagnosticServices] ADD [Price] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    DECLARE @var10 nvarchar(max);
    SELECT @var10 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[DiagnosticOrders]') AND [c].[name] = N'AppointmentId');
    IF @var10 IS NOT NULL EXEC(N'ALTER TABLE [DiagnosticOrders] DROP CONSTRAINT ' + @var10 + ';');
    ALTER TABLE [DiagnosticOrders] ALTER COLUMN [AppointmentId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    ALTER TABLE [DiagnosticOrders] ADD [FacilityId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    ALTER TABLE [DiagnosticOrders] ADD [PatientVisitId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    ALTER TABLE [DiagnosticOrders] ADD [PerformingDepartmentId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    ALTER TABLE [Departments] ADD [SpecialtyId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    DECLARE @var11 nvarchar(max);
    SELECT @var11 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AppointmentVitalSigns]') AND [c].[name] = N'AppointmentId');
    IF @var11 IS NOT NULL EXEC(N'ALTER TABLE [AppointmentVitalSigns] DROP CONSTRAINT ' + @var11 + ';');
    ALTER TABLE [AppointmentVitalSigns] ALTER COLUMN [AppointmentId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    ALTER TABLE [AppointmentVitalSigns] ADD [PatientVisitId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    CREATE TABLE [DailyQueueSequences] (
        [Id] bigint NOT NULL IDENTITY,
        [FacilityId] bigint NOT NULL,
        [DepartmentId] bigint NOT NULL,
        [Date] date NOT NULL,
        [LastNumber] int NOT NULL,
        CONSTRAINT [PK_DailyQueueSequences] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    CREATE TABLE [PatientVisits] (
        [Id] bigint NOT NULL IDENTITY,
        [VisitCode] nvarchar(50) NOT NULL,
        [PatientId] bigint NOT NULL,
        [AppointmentId] bigint NULL,
        [FacilityId] bigint NOT NULL,
        [DepartmentId] bigint NOT NULL,
        [RoomId] bigint NULL,
        [AssignedDoctorId] bigint NULL,
        [VisitDate] date NOT NULL,
        [ArrivalType] nvarchar(max) NOT NULL,
        [Priority] nvarchar(max) NOT NULL,
        [ChiefComplaint] nvarchar(1000) NULL,
        [QueueNumber] int NOT NULL,
        [Status] nvarchar(450) NOT NULL,
        [CheckedInAtUtc] datetime2 NOT NULL,
        [ConsultationStartedAtUtc] datetime2 NULL,
        [CompletedAtUtc] datetime2 NULL,
        [CancelledAtUtc] datetime2 NULL,
        [CancellationReason] nvarchar(500) NULL,
        [CreatedByUserId] uniqueidentifier NOT NULL,
        [RowVersion] rowversion NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_PatientVisits] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PatientVisits_Appointments_AppointmentId] FOREIGN KEY ([AppointmentId]) REFERENCES [Appointments] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PatientVisits_Departments_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [Departments] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PatientVisits_Doctors_AssignedDoctorId] FOREIGN KEY ([AssignedDoctorId]) REFERENCES [Doctors] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PatientVisits_Facilities_FacilityId] FOREIGN KEY ([FacilityId]) REFERENCES [Facilities] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PatientVisits_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PatientVisits_Rooms_RoomId] FOREIGN KEY ([RoomId]) REFERENCES [Rooms] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    CREATE TABLE [StaffFacilityAssignments] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] uniqueidentifier NOT NULL,
        [FacilityId] bigint NOT NULL,
        [DepartmentId] bigint NULL,
        [Role] nvarchar(100) NOT NULL,
        [IsPrimary] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [AssignedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_StaffFacilityAssignments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StaffFacilityAssignments_Departments_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [Departments] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StaffFacilityAssignments_Facilities_FacilityId] FOREIGN KEY ([FacilityId]) REFERENCES [Facilities] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_VisitSummaries_AppointmentId] ON [VisitSummaries] ([AppointmentId]) WHERE [AppointmentId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_VisitSummaries_PatientVisitId] ON [VisitSummaries] ([PatientVisitId]) WHERE [PatientVisitId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Prescriptions_AppointmentId] ON [Prescriptions] ([AppointmentId]) WHERE [AppointmentId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    CREATE INDEX [IX_Prescriptions_PatientVisitId] ON [Prescriptions] ([PatientVisitId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Medicines_Code] ON [Medicines] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    CREATE INDEX [IX_Invoices_PatientVisitId] ON [Invoices] ([PatientVisitId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    EXEC(N'ALTER TABLE [Invoices] ADD CONSTRAINT [CK_Invoices_SingleSource] CHECK ((([AppointmentId] IS NOT NULL OR [PatientVisitId] IS NOT NULL) AND [HealthPackageRegistrationId] IS NULL) OR ([AppointmentId] IS NULL AND [PatientVisitId] IS NULL AND [HealthPackageRegistrationId] IS NOT NULL))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    CREATE INDEX [IX_DiagnosticOrders_FacilityId_PerformingDepartmentId_Status] ON [DiagnosticOrders] ([FacilityId], [PerformingDepartmentId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    CREATE INDEX [IX_DiagnosticOrders_PatientVisitId] ON [DiagnosticOrders] ([PatientVisitId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    CREATE INDEX [IX_DiagnosticOrders_PerformingDepartmentId] ON [DiagnosticOrders] ([PerformingDepartmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    CREATE INDEX [IX_Departments_SpecialtyId] ON [Departments] ([SpecialtyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_AppointmentVitalSigns_AppointmentId] ON [AppointmentVitalSigns] ([AppointmentId]) WHERE [AppointmentId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_AppointmentVitalSigns_PatientVisitId] ON [AppointmentVitalSigns] ([PatientVisitId]) WHERE [PatientVisitId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DailyQueueSequences_FacilityId_DepartmentId_Date] ON [DailyQueueSequences] ([FacilityId], [DepartmentId], [Date]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_PatientVisits_AppointmentId] ON [PatientVisits] ([AppointmentId]) WHERE [AppointmentId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    CREATE INDEX [IX_PatientVisits_AssignedDoctorId_VisitDate_Status] ON [PatientVisits] ([AssignedDoctorId], [VisitDate], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    CREATE INDEX [IX_PatientVisits_DepartmentId] ON [PatientVisits] ([DepartmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    CREATE INDEX [IX_PatientVisits_FacilityId_DepartmentId_VisitDate_Status] ON [PatientVisits] ([FacilityId], [DepartmentId], [VisitDate], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    CREATE INDEX [IX_PatientVisits_PatientId] ON [PatientVisits] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    CREATE INDEX [IX_PatientVisits_RoomId] ON [PatientVisits] ([RoomId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PatientVisits_VisitCode] ON [PatientVisits] ([VisitCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PatientVisits_VisitDate_FacilityId_DepartmentId_QueueNumber] ON [PatientVisits] ([VisitDate], [FacilityId], [DepartmentId], [QueueNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    CREATE INDEX [IX_StaffFacilityAssignments_DepartmentId] ON [StaffFacilityAssignments] ([DepartmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    CREATE INDEX [IX_StaffFacilityAssignments_FacilityId] ON [StaffFacilityAssignments] ([FacilityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StaffFacilityAssignments_UserId_FacilityId_Role] ON [StaffFacilityAssignments] ([UserId], [FacilityId], [Role]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    ALTER TABLE [AppointmentVitalSigns] ADD CONSTRAINT [FK_AppointmentVitalSigns_Appointments_AppointmentId] FOREIGN KEY ([AppointmentId]) REFERENCES [Appointments] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    ALTER TABLE [AppointmentVitalSigns] ADD CONSTRAINT [FK_AppointmentVitalSigns_PatientVisits_PatientVisitId] FOREIGN KEY ([PatientVisitId]) REFERENCES [PatientVisits] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    ALTER TABLE [Departments] ADD CONSTRAINT [FK_Departments_Specialties_SpecialtyId] FOREIGN KEY ([SpecialtyId]) REFERENCES [Specialties] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    ALTER TABLE [DiagnosticOrders] ADD CONSTRAINT [FK_DiagnosticOrders_Departments_PerformingDepartmentId] FOREIGN KEY ([PerformingDepartmentId]) REFERENCES [Departments] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    ALTER TABLE [DiagnosticOrders] ADD CONSTRAINT [FK_DiagnosticOrders_Facilities_FacilityId] FOREIGN KEY ([FacilityId]) REFERENCES [Facilities] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    ALTER TABLE [DiagnosticOrders] ADD CONSTRAINT [FK_DiagnosticOrders_PatientVisits_PatientVisitId] FOREIGN KEY ([PatientVisitId]) REFERENCES [PatientVisits] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    ALTER TABLE [Invoices] ADD CONSTRAINT [FK_Invoices_PatientVisits_PatientVisitId] FOREIGN KEY ([PatientVisitId]) REFERENCES [PatientVisits] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    ALTER TABLE [Prescriptions] ADD CONSTRAINT [FK_Prescriptions_Appointments_AppointmentId] FOREIGN KEY ([AppointmentId]) REFERENCES [Appointments] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    ALTER TABLE [Prescriptions] ADD CONSTRAINT [FK_Prescriptions_PatientVisits_PatientVisitId] FOREIGN KEY ([PatientVisitId]) REFERENCES [PatientVisits] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    ALTER TABLE [VisitSummaries] ADD CONSTRAINT [FK_VisitSummaries_Appointments_AppointmentId] FOREIGN KEY ([AppointmentId]) REFERENCES [Appointments] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    ALTER TABLE [VisitSummaries] ADD CONSTRAINT [FK_VisitSummaries_PatientVisits_PatientVisitId] FOREIGN KEY ([PatientVisitId]) REFERENCES [PatientVisits] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913111424_AddPatientVisitOutpatientCore'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260913111424_AddPatientVisitOutpatientCore', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914015035_AddIdempotencyAndChargeItemLocking'
)
BEGIN
    ALTER TABLE [PatientVisits] ADD [HealthPackageRegistrationId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914015035_AddIdempotencyAndChargeItemLocking'
)
BEGIN
    ALTER TABLE [InvoiceItems] ADD [IsCancelled] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914015035_AddIdempotencyAndChargeItemLocking'
)
BEGIN
    ALTER TABLE [DiagnosticOrderItems] ADD [IsPackageCovered] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914015035_AddIdempotencyAndChargeItemLocking'
)
BEGIN
    ALTER TABLE [DiagnosticOrderItems] ADD [PackageRegistrationId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914015035_AddIdempotencyAndChargeItemLocking'
)
BEGIN
    CREATE TABLE [IdempotencyRecords] (
        [Id] bigint NOT NULL IDENTITY,
        [Key] nvarchar(100) NOT NULL,
        [Scope] nvarchar(50) NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [RequestHash] nvarchar(64) NOT NULL,
        [StatusCode] int NOT NULL,
        [ResponseBody] nvarchar(max) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_IdempotencyRecords] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914015035_AddIdempotencyAndChargeItemLocking'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_PatientVisits_HealthPackageRegistrationId] ON [PatientVisits] ([HealthPackageRegistrationId]) WHERE [HealthPackageRegistrationId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914015035_AddIdempotencyAndChargeItemLocking'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_InvoiceItems_ReferenceType_ReferenceId] ON [InvoiceItems] ([ReferenceType], [ReferenceId]) WHERE [ReferenceType] <> '''' AND [ReferenceId] > 0 AND [IsCancelled] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914015035_AddIdempotencyAndChargeItemLocking'
)
BEGIN
    CREATE INDEX [IX_DiagnosticOrderItems_PackageRegistrationId] ON [DiagnosticOrderItems] ([PackageRegistrationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914015035_AddIdempotencyAndChargeItemLocking'
)
BEGIN
    CREATE INDEX [IX_IdempotencyRecords_ExpiresAtUtc] ON [IdempotencyRecords] ([ExpiresAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914015035_AddIdempotencyAndChargeItemLocking'
)
BEGIN
    CREATE UNIQUE INDEX [IX_IdempotencyRecords_Key_Scope] ON [IdempotencyRecords] ([Key], [Scope]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914015035_AddIdempotencyAndChargeItemLocking'
)
BEGIN
    ALTER TABLE [DiagnosticOrderItems] ADD CONSTRAINT [FK_DiagnosticOrderItems_HealthPackageRegistrations_PackageRegistrationId] FOREIGN KEY ([PackageRegistrationId]) REFERENCES [HealthPackageRegistrations] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914015035_AddIdempotencyAndChargeItemLocking'
)
BEGIN
    ALTER TABLE [PatientVisits] ADD CONSTRAINT [FK_PatientVisits_HealthPackageRegistrations_HealthPackageRegistrationId] FOREIGN KEY ([HealthPackageRegistrationId]) REFERENCES [HealthPackageRegistrations] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914015035_AddIdempotencyAndChargeItemLocking'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260914015035_AddIdempotencyAndChargeItemLocking', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925143413_AddAiSessionSnapshotAndAuditPersistence'
)
BEGIN
    CREATE TABLE [AiAuditLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] uniqueidentifier NULL,
        [SessionId] nvarchar(128) NULL,
        [DraftId] nvarchar(128) NULL,
        [DraftVersion] int NULL,
        [FacilityId] bigint NULL,
        [ActionType] nvarchar(64) NOT NULL,
        [Outcome] nvarchar(64) NOT NULL,
        [ErrorCode] nvarchar(64) NULL,
        [CorrelationId] nvarchar(128) NULL,
        [MetadataJson] nvarchar(2000) NULL,
        [TimestampUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AiAuditLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925143413_AddAiSessionSnapshotAndAuditPersistence'
)
BEGIN
    CREATE TABLE [AiCancelledDraftScopes] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] uniqueidentifier NULL,
        [SessionId] nvarchar(128) NOT NULL,
        [DraftId] nvarchar(128) NOT NULL,
        [FacilityId] bigint NULL,
        [CancelledAtUtc] datetime2 NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AiCancelledDraftScopes] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925143413_AddAiSessionSnapshotAndAuditPersistence'
)
BEGIN
    CREATE TABLE [AiSelectionSnapshots] (
        [Id] bigint NOT NULL IDENTITY,
        [SnapshotId] nvarchar(64) NOT NULL,
        [UserId] uniqueidentifier NULL,
        [SessionId] nvarchar(128) NULL,
        [DraftId] nvarchar(128) NULL,
        [DraftVersion] int NULL,
        [FacilityId] bigint NULL,
        [SpecialtyId] bigint NULL,
        [DoctorId] bigint NULL,
        [SlotDate] nvarchar(32) NULL,
        [DoctorIdsJson] nvarchar(4000) NOT NULL,
        [SlotIdsJson] nvarchar(4000) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        [IsRevoked] bit NOT NULL,
        [RevokedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_AiSelectionSnapshots] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925143413_AddAiSessionSnapshotAndAuditPersistence'
)
BEGIN
    CREATE TABLE [AiSessions] (
        [Id] bigint NOT NULL IDENTITY,
        [SessionId] nvarchar(128) NOT NULL,
        [UserId] uniqueidentifier NULL,
        [ActiveDraftId] nvarchar(128) NULL,
        [ActiveDraftVersion] int NULL,
        [FacilityId] bigint NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [LastActiveAtUtc] datetime2 NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_AiSessions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925143413_AddAiSessionSnapshotAndAuditPersistence'
)
BEGIN
    CREATE INDEX [IX_AiAuditLogs_ActionType] ON [AiAuditLogs] ([ActionType]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925143413_AddAiSessionSnapshotAndAuditPersistence'
)
BEGIN
    CREATE INDEX [IX_AiAuditLogs_SessionId_TimestampUtc] ON [AiAuditLogs] ([SessionId], [TimestampUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925143413_AddAiSessionSnapshotAndAuditPersistence'
)
BEGIN
    CREATE INDEX [IX_AiAuditLogs_UserId_TimestampUtc] ON [AiAuditLogs] ([UserId], [TimestampUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925143413_AddAiSessionSnapshotAndAuditPersistence'
)
BEGIN
    CREATE INDEX [IX_AiCancelledDraftScopes_ExpiresAtUtc] ON [AiCancelledDraftScopes] ([ExpiresAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925143413_AddAiSessionSnapshotAndAuditPersistence'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_AiCancelledDraftScopes_UserId_SessionId_DraftId] ON [AiCancelledDraftScopes] ([UserId], [SessionId], [DraftId]) WHERE [UserId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925143413_AddAiSessionSnapshotAndAuditPersistence'
)
BEGIN
    CREATE INDEX [IX_AiSelectionSnapshots_ExpiresAtUtc] ON [AiSelectionSnapshots] ([ExpiresAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925143413_AddAiSessionSnapshotAndAuditPersistence'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AiSelectionSnapshots_SnapshotId] ON [AiSelectionSnapshots] ([SnapshotId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925143413_AddAiSessionSnapshotAndAuditPersistence'
)
BEGIN
    CREATE INDEX [IX_AiSelectionSnapshots_UserId_SessionId_DraftId] ON [AiSelectionSnapshots] ([UserId], [SessionId], [DraftId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925143413_AddAiSessionSnapshotAndAuditPersistence'
)
BEGIN
    CREATE INDEX [IX_AiSessions_ExpiresAtUtc] ON [AiSessions] ([ExpiresAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925143413_AddAiSessionSnapshotAndAuditPersistence'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AiSessions_SessionId] ON [AiSessions] ([SessionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925143413_AddAiSessionSnapshotAndAuditPersistence'
)
BEGIN
    CREATE INDEX [IX_AiSessions_UserId_IsActive] ON [AiSessions] ([UserId], [IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925143413_AddAiSessionSnapshotAndAuditPersistence'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925143413_AddAiSessionSnapshotAndAuditPersistence', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926055757_AddAiBookingConfirmation'
)
BEGIN
    CREATE TABLE [AiBookingConfirmations] (
        [Id] bigint NOT NULL IDENTITY,
        [ConfirmationId] nvarchar(64) NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [SessionId] nvarchar(128) NOT NULL,
        [DraftId] nvarchar(128) NOT NULL,
        [DraftVersion] int NOT NULL,
        [ContextSnapshotId] nvarchar(64) NULL,
        [SpecialtyId] bigint NOT NULL,
        [DoctorId] bigint NOT NULL,
        [SlotId] bigint NOT NULL,
        [SlotDate] date NOT NULL,
        [StartTime] time NOT NULL,
        [EndTime] time NOT NULL,
        [ReasonHash] nvarchar(64) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        [UsedAtUtc] datetime2 NULL,
        [UsedAppointmentId] bigint NULL,
        [UsedIdempotencyKey] nvarchar(128) NULL,
        [RevokedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_AiBookingConfirmations] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926055757_AddAiBookingConfirmation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AiBookingConfirmations_ConfirmationId] ON [AiBookingConfirmations] ([ConfirmationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926055757_AddAiBookingConfirmation'
)
BEGIN
    CREATE INDEX [IX_AiBookingConfirmations_ExpiresAtUtc] ON [AiBookingConfirmations] ([ExpiresAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926055757_AddAiBookingConfirmation'
)
BEGIN
    CREATE INDEX [IX_AiBookingConfirmations_UserId_SessionId_DraftId_RevokedAtUtc] ON [AiBookingConfirmations] ([UserId], [SessionId], [DraftId], [RevokedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926055757_AddAiBookingConfirmation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260926055757_AddAiBookingConfirmation', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926060514_ScopeAiSessionsPerUser'
)
BEGIN
    DROP INDEX [IX_AiSessions_SessionId] ON [AiSessions];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926060514_ScopeAiSessionsPerUser'
)
BEGIN
    CREATE INDEX [IX_AiSessions_SessionId] ON [AiSessions] ([SessionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926060514_ScopeAiSessionsPerUser'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260926060514_ScopeAiSessionsPerUser', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926065741_EnforceAiSessionScopeAndActiveConfirmation'
)
BEGIN

    ;WITH ranked_sessions AS
    (
        SELECT Id,
               ROW_NUMBER() OVER
               (
                   PARTITION BY SessionId, UserId
                   ORDER BY CASE WHEN IsActive = 1 THEN 0 ELSE 1 END,
                            LastActiveAtUtc DESC,
                            Id DESC
               ) AS RowNumber
        FROM AiSessions
    )
    DELETE FROM AiSessions
    WHERE Id IN (SELECT Id FROM ranked_sessions WHERE RowNumber > 1);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926065741_EnforceAiSessionScopeAndActiveConfirmation'
)
BEGIN

    ;WITH ranked_confirmations AS
    (
        SELECT Id,
               ROW_NUMBER() OVER
               (
                   PARTITION BY UserId, SessionId, DraftId
                   ORDER BY CreatedAtUtc DESC, Id DESC
               ) AS RowNumber
        FROM AiBookingConfirmations
        WHERE UsedAtUtc IS NULL AND RevokedAtUtc IS NULL
    )
    UPDATE c
    SET RevokedAtUtc = SYSUTCDATETIME()
    FROM AiBookingConfirmations AS c
    INNER JOIN ranked_confirmations AS r ON r.Id = c.Id
    WHERE r.RowNumber > 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926065741_EnforceAiSessionScopeAndActiveConfirmation'
)
BEGIN
    DROP INDEX [IX_AiSessions_SessionId] ON [AiSessions];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926065741_EnforceAiSessionScopeAndActiveConfirmation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_AiSessions_SessionId] ON [AiSessions] ([SessionId]) WHERE [UserId] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926065741_EnforceAiSessionScopeAndActiveConfirmation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_AiSessions_SessionId_UserId] ON [AiSessions] ([SessionId], [UserId]) WHERE [UserId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926065741_EnforceAiSessionScopeAndActiveConfirmation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_AiBookingConfirmations_UserId_SessionId_DraftId] ON [AiBookingConfirmations] ([UserId], [SessionId], [DraftId]) WHERE [UsedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926065741_EnforceAiSessionScopeAndActiveConfirmation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260926065741_EnforceAiSessionScopeAndActiveConfirmation', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926100743_PreserveAiCancellationTombstones'
)
BEGIN

    UPDATE [AiCancelledDraftScopes]
    SET [ExpiresAtUtc] = CONVERT(datetime2(7), '9999-12-31T23:59:59.9999999')
    WHERE [ExpiresAtUtc] <> CONVERT(datetime2(7), '9999-12-31T23:59:59.9999999');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926100743_PreserveAiCancellationTombstones'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260926100743_PreserveAiCancellationTombstones', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926105347_AddAiPendingToolAction'
)
BEGIN
    CREATE TABLE [AiPendingToolActions] (
        [ActionId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [SessionId] nvarchar(128) NOT NULL,
        [DraftId] nvarchar(128) NULL,
        [ToolName] nvarchar(120) NOT NULL,
        [ToolVersion] nvarchar(32) NOT NULL,
        [RequestHash] nvarchar(128) NOT NULL,
        [ResourceType] nvarchar(64) NOT NULL,
        [ResourceId] nvarchar(128) NOT NULL,
        [NormalizedArgumentsJson] nvarchar(4000) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        [ConfirmedAtUtc] datetime2 NULL,
        [ExecutedAtUtc] datetime2 NULL,
        [CancelledAtUtc] datetime2 NULL,
        [IdempotencyKeyHash] nvarchar(128) NULL,
        [ExecutionResultReference] nvarchar(256) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_AiPendingToolActions] PRIMARY KEY ([ActionId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926105347_AddAiPendingToolAction'
)
BEGIN
    CREATE INDEX [IX_AiPendingToolActions_UserId_ExpiresAtUtc] ON [AiPendingToolActions] ([UserId], [ExpiresAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926105347_AddAiPendingToolAction'
)
BEGIN
    CREATE INDEX [IX_AiPendingToolActions_UserId_ResourceType_ResourceId_ToolName] ON [AiPendingToolActions] ([UserId], [ResourceType], [ResourceId], [ToolName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926105347_AddAiPendingToolAction'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260926105347_AddAiPendingToolAction', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926111032_EnforceSingleActiveAiToolAction'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_AiPendingToolActions_UserId_ResourceType_ResourceId] ON [AiPendingToolActions] ([UserId], [ResourceType], [ResourceId]) WHERE [ExecutedAtUtc] IS NULL AND [CancelledAtUtc] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926111032_EnforceSingleActiveAiToolAction'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260926111032_EnforceSingleActiveAiToolAction', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926114519_HardenAiPendingToolActionExecution'
)
BEGIN
    DROP INDEX [IX_AiPendingToolActions_UserId_ResourceType_ResourceId] ON [AiPendingToolActions];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926114519_HardenAiPendingToolActionExecution'
)
BEGIN
    ALTER TABLE [AppointmentChangeRequests] ADD [SourceAiActionId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926114519_HardenAiPendingToolActionExecution'
)
BEGIN
    ALTER TABLE [AiPendingToolActions] ADD [ExecutionAttemptCount] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926114519_HardenAiPendingToolActionExecution'
)
BEGIN
    ALTER TABLE [AiPendingToolActions] ADD [ExecutionLeaseExpiresAtUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926114519_HardenAiPendingToolActionExecution'
)
BEGIN
    ALTER TABLE [AiPendingToolActions] ADD [ExecutionLeaseId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926114519_HardenAiPendingToolActionExecution'
)
BEGIN
    ALTER TABLE [AiPendingToolActions] ADD [LastErrorCode] nvarchar(120) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926114519_HardenAiPendingToolActionExecution'
)
BEGIN
    ALTER TABLE [AiPendingToolActions] ADD [State] nvarchar(32) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926114519_HardenAiPendingToolActionExecution'
)
BEGIN
    EXEC(N'
    UPDATE [AiPendingToolActions]
    SET [State] = CASE
        WHEN [ExecutedAtUtc] IS NOT NULL THEN ''Completed''
        WHEN [CancelledAtUtc] IS NOT NULL THEN ''Cancelled''
        WHEN [ExpiresAtUtc] <= SYSUTCDATETIME() THEN ''Expired''
        ELSE ''PendingConfirmation''
    END
    WHERE [State] IS NULL;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926114519_HardenAiPendingToolActionExecution'
)
BEGIN
    DECLARE @var12 nvarchar(max);
    SELECT @var12 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AiPendingToolActions]') AND [c].[name] = N'State');
    IF @var12 IS NOT NULL EXEC(N'ALTER TABLE [AiPendingToolActions] DROP CONSTRAINT ' + @var12 + ';');
    ALTER TABLE [AiPendingToolActions] ALTER COLUMN [State] nvarchar(32) NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926114519_HardenAiPendingToolActionExecution'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_AppointmentChangeRequests_SourceAiActionId] ON [AppointmentChangeRequests] ([SourceAiActionId]) WHERE [SourceAiActionId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926114519_HardenAiPendingToolActionExecution'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_AiPendingToolActions_UserId_ResourceType_ResourceId] ON [AiPendingToolActions] ([UserId], [ResourceType], [ResourceId]) WHERE [State] IN (''PendingConfirmation'', ''Executing'', ''FailedRetryable'')');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926114519_HardenAiPendingToolActionExecution'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260926114519_HardenAiPendingToolActionExecution', N'10.0.11');
END;

COMMIT;
GO
