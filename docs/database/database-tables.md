# Các bảng cơ sở dữ liệu

Đối chiếu model SQL Server của `AppDbContext` (64 bảng ứng dụng/Identity). Đây là schema trong code, không phải kiểm kê một database đang chạy. `__EFMigrationsHistory` do EF quản lý riêng, không nằm trong 64 bảng.

Nguồn: [AppDbContext.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/AppDbContext.cs) và [Configurations](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations). Các cột, khóa và ràng buộc bên dưới lấy từ metadata EF sau khi áp dụng cấu hình; tên bảng dùng đúng mapping, không suy từ tên entity.

PK: khóa chính; FK: khóa ngoại; UNIQUE: chỉ mục duy nhất. Delete ghi hành vi EF/SQL đã cấu hình; ClientSetNull là hành vi phía EF. Chi tiết kiểu/null/giới hạn từng cột nằm trong [từ điển dữ liệu](../data_dictionary.md).

## AiAuditLogs

- Entity: `AiAuditLog`.
- Cột: `Id`, `ActionType`, `CorrelationId`, `DraftId`, `DraftVersion`, `ErrorCode`, `FacilityId`, `MetadataJson`, `Outcome`, `SessionId`, `TimestampUtc`, `UserId`.
- PK: `Id`.
- FK: không có FK được khai báo trong model.
- INDEX: (`ActionType`).
- INDEX: (`SessionId`, `TimestampUtc`).
- INDEX: (`UserId`, `TimestampUtc`).
- Cấu hình: [AiAuditLogConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/AiAuditLogConfiguration.cs).

## AiBookingConfirmations

- Entity: `AiBookingConfirmation`.
- Cột: `Id`, `ConfirmationId`, `ContextSnapshotId`, `CreatedAtUtc`, `DoctorId`, `DraftId`, `DraftVersion`, `EndTime`, `ExpiresAtUtc`, `FacilityId`, `ReasonHash`, `RevokedAtUtc`, `SessionId`, `SlotDate`, `SlotId`, `SpecialtyId`, `StartTime`, `UsedAppointmentId`, `UsedAtUtc`, `UsedIdempotencyKey`, `UserId`.
- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`ConfirmationId`).
- INDEX: (`ExpiresAtUtc`).
- UNIQUE: (`UserId`, `SessionId`, `DraftId`); bộ lọc `[UsedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL`.
- INDEX: (`UserId`, `SessionId`, `DraftId`, `RevokedAtUtc`).
- Cấu hình: [AiBookingConfirmationConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/AiBookingConfirmationConfiguration.cs).

## AiCancelledDraftScopes

- Entity: `AiCancelledDraftScope`.
- Cột: `Id`, `CancelledAtUtc`, `DraftId`, `ExpiresAtUtc`, `FacilityId`, `SessionId`, `UserId`.
- PK: `Id`.
- FK: không có FK được khai báo trong model.
- INDEX: (`ExpiresAtUtc`).
- UNIQUE: (`UserId`, `SessionId`, `DraftId`); bộ lọc `[UserId] IS NOT NULL`.
- Cấu hình: [AiCancelledDraftScopeConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/AiCancelledDraftScopeConfiguration.cs).

## AiPendingToolActions

- Entity: `AiPendingToolAction`.
- Cột: `ActionId`, `ActorRole`, `CancelledAtUtc`, `ConfirmationTokenHash`, `ConfirmedAtUtc`, `ConversationId`, `CreatedAtUtc`, `DraftId`, `ExecutedAtUtc`, `ExecutionAttemptCount`, `ExecutionLeaseExpiresAtUtc`, `ExecutionLeaseId`, `ExecutionResultReference`, `ExpiresAtUtc`, `FacilityId`, `IdempotencyKeyHash`, `LastErrorCode`, `NormalizedArgumentsJson`, `RequestHash`, `ResourceId`, `ResourceType`, `ResourceVersion`, `RowVersion`, `SessionId`, `SourceAiActionId`, `State`, `ToolName`, `ToolVersion`, `UserId`.
- PK: `ActionId`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`SourceAiActionId`); bộ lọc `[SourceAiActionId] IS NOT NULL`.
- INDEX: (`UserId`, `ExpiresAtUtc`).
- UNIQUE: (`UserId`, `ResourceType`, `ResourceId`); bộ lọc `[State] IN ('PendingConfirmation', 'Executing', 'FailedRetryable')`.
- INDEX: (`UserId`, `ResourceType`, `ResourceId`, `ToolName`).
- Kiểm soát đồng thời: `RowVersion`.
- Cấu hình: [AiPendingToolActionConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/AiPendingToolActionConfiguration.cs).

## AiSelectionSnapshots

- Entity: `AiSelectionSnapshot`.
- Cột: `Id`, `CreatedAtUtc`, `DoctorId`, `DoctorIdsJson`, `DraftId`, `DraftVersion`, `ExpiresAtUtc`, `FacilityId`, `IsRevoked`, `RevokedAtUtc`, `SessionId`, `SlotDate`, `SlotIdsJson`, `SnapshotId`, `SpecialtyId`, `UserId`.
- PK: `Id`.
- FK: không có FK được khai báo trong model.
- INDEX: (`ExpiresAtUtc`).
- UNIQUE: (`SnapshotId`).
- INDEX: (`UserId`, `SessionId`, `DraftId`).
- Cấu hình: [AiSelectionSnapshotConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/AiSelectionSnapshotConfiguration.cs).

## AiSessions

- Entity: `AiSession`.
- Cột: `Id`, `ActiveDraftId`, `ActiveDraftVersion`, `ConversationStateJson`, `ConversationVersion`, `CreatedAtUtc`, `ExpiresAtUtc`, `FacilityId`, `IsActive`, `LastActiveAtUtc`, `RowVersion`, `SessionId`, `UserId`.
- PK: `Id`.
- FK: không có FK được khai báo trong model.
- INDEX: (`ExpiresAtUtc`).
- UNIQUE: (`SessionId`); bộ lọc `[UserId] IS NULL`.
- UNIQUE: (`SessionId`, `UserId`); bộ lọc `[UserId] IS NOT NULL`.
- INDEX: (`UserId`, `IsActive`).
- Kiểm soát đồng thời: `RowVersion`.
- Cấu hình: [AiSessionConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/AiSessionConfiguration.cs).

## AiSuggestionLogs

- Entity: `AiSuggestionLog`.
- Cột: `Id`, `CreatedAt`, `InputText`, `PatientId`, `PromptVersion`, `Provider`, `SelectedSpecialtyId`, `SuggestedSpecialtiesJson`.
- PK: `Id`.
- FK: `PatientId` → `Patients` (`Id`); Delete: `NoAction`.
- FK: `SelectedSpecialtyId` → `Specialties` (`Id`); Delete: `NoAction`.
- INDEX: (`PatientId`).
- INDEX: (`SelectedSpecialtyId`).
- Cấu hình: [AiSuggestionLogConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/AiSuggestionLogConfiguration.cs).

## AppointmentChangeRequests

- Entity: `AppointmentChangeRequest`.
- Cột: `Id`, `AppointmentId`, `CreatedAt`, `OriginalAppointmentStatus`, `ProcessedAt`, `ProcessedByUserId`, `Reason`, `RequestType`, `RequestedByUserId`, `RequestedSlotId`, `SourceAiActionId`, `Status`.
- PK: `Id`.
- FK: `AppointmentId` → `Appointments` (`Id`); Delete: `NoAction`.
- FK: `ProcessedByUserId` → `AspNetUsers` (`Id`); Delete: `NoAction`.
- FK: `RequestedByUserId` → `AspNetUsers` (`Id`); Delete: `NoAction`.
- FK: `RequestedSlotId` → `AppointmentSlots` (`Id`); Delete: `NoAction`.
- UNIQUE: (`AppointmentId`); bộ lọc `[Status] = 'Pending'`.
- INDEX: (`ProcessedByUserId`).
- INDEX: (`RequestedByUserId`).
- INDEX: (`RequestedSlotId`).
- UNIQUE: (`SourceAiActionId`); bộ lọc `[SourceAiActionId] IS NOT NULL`.
- Cấu hình: [AppointmentChangeRequestConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/AppointmentChangeRequestConfiguration.cs).

## AppointmentHistory

- Entity: `AppointmentHistory`.
- Cột: `Id`, `Action`, `AppointmentId`, `CreatedAt`, `NewStatus`, `Note`, `OldStatus`, `PerformedByUserId`.
- PK: `Id`.
- FK: `AppointmentId` → `Appointments` (`Id`); Delete: `NoAction`.
- FK: `PerformedByUserId` → `AspNetUsers` (`Id`); Delete: `NoAction`.
- INDEX: (`AppointmentId`).
- INDEX: (`PerformedByUserId`).
- Cấu hình: [AppointmentHistoryConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/AppointmentHistoryConfiguration.cs).

## Appointments

- Entity: `Appointment`.
- Cột: `Id`, `AppointmentCode`, `AppointmentDate`, `AppointmentSlotId`, `DoctorId`, `EndTime`, `FacilityId`, `PatientId`, `Reason`, `SpecialtyId`, `StartTime`, `Status`.
- PK: `Id`.
- FK: `AppointmentSlotId` → `AppointmentSlots` (`Id`); Delete: `NoAction`.
- FK: `DoctorId` → `Doctors` (`Id`); Delete: `NoAction`.
- FK: `FacilityId` → `Facilities` (`Id`); Delete: `NoAction`.
- FK: `PatientId` → `Patients` (`Id`); Delete: `NoAction`.
- FK: `SpecialtyId` → `Specialties` (`Id`); Delete: `NoAction`.
- UNIQUE: (`AppointmentCode`).
- INDEX: (`AppointmentSlotId`).
- INDEX: (`DoctorId`).
- INDEX: (`PatientId`).
- INDEX: (`SpecialtyId`).
- INDEX: (`FacilityId`, `AppointmentDate`).
- CHECK `CK_Appointment_Duration`: `DATEDIFF(MINUTE, [StartTime], [EndTime]) = 30`.
- CHECK `CK_Appointment_TimeRange`: `[StartTime] < [EndTime]`.
- Cấu hình: [AppointmentConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/AppointmentConfiguration.cs).

## AppointmentSlots

- Entity: `AppointmentSlot`.
- Cột: `Id`, `DoctorId`, `EndTime`, `IsBooked`, `SlotDate`, `StartTime`.
- PK: `Id`.
- FK: `DoctorId` → `Doctors` (`Id`); Delete: `NoAction`.
- UNIQUE: (`DoctorId`, `SlotDate`, `StartTime`).
- CHECK `CK_AppointmentSlot_Duration`: `DATEDIFF(MINUTE, [StartTime], [EndTime]) = 30`.
- CHECK `CK_AppointmentSlot_TimeRange`: `[StartTime] < [EndTime]`.
- Cấu hình: [AppointmentSlotConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/AppointmentSlotConfiguration.cs).

## AppointmentVitalSigns

- Entity: `AppointmentVitalSigns`.
- Cột: `Id`, `AppointmentId`, `BloodPressureDiastolic`, `BloodPressureSystolic`, `Bmi`, `HeartRate`, `Height`, `PatientVisitId`, `RecordedAtUtc`, `RecordedByUserId`, `RespiratoryRate`, `RowVersion`, `SpO2`, `Temperature`, `Weight`.
- PK: `Id`.
- FK: `AppointmentId` → `Appointments` (`Id`); Delete: `Restrict`.
- FK: `PatientVisitId` → `PatientVisits` (`Id`); Delete: `Restrict`.
- UNIQUE: (`AppointmentId`); bộ lọc `[AppointmentId] IS NOT NULL`.
- UNIQUE: (`PatientVisitId`); bộ lọc `[PatientVisitId] IS NOT NULL`.
- Kiểm soát đồng thời: `RowVersion`.
- Cấu hình: [AppointmentVitalSignsConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/AppointmentVitalSignsConfiguration.cs).

## AspNetRoleClaims

- Entity: `IdentityRoleClaim`1`.
- Cột: `Id`, `ClaimType`, `ClaimValue`, `RoleId`.
- PK: `Id`.
- FK: `RoleId` → `AspNetRoles` (`Id`); Delete: `Cascade`.
- INDEX: (`RoleId`).

## AspNetRoles

- Entity: `IdentityRole`1`.
- Cột: `Id`, `ConcurrencyStamp`, `Name`, `NormalizedName`.
- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`NormalizedName`); bộ lọc `[NormalizedName] IS NOT NULL`.
- Kiểm soát đồng thời: `ConcurrencyStamp`.

## AspNetUserClaims

- Entity: `IdentityUserClaim`1`.
- Cột: `Id`, `ClaimType`, `ClaimValue`, `UserId`.
- PK: `Id`.
- FK: `UserId` → `AspNetUsers` (`Id`); Delete: `Cascade`.
- INDEX: (`UserId`).

## AspNetUserLogins

- Entity: `IdentityUserLogin`1`.
- Cột: `LoginProvider`, `ProviderKey`, `ProviderDisplayName`, `UserId`.
- PK: `LoginProvider`, `ProviderKey`.
- FK: `UserId` → `AspNetUsers` (`Id`); Delete: `Cascade`.
- INDEX: (`UserId`).

## AspNetUserRoles

- Entity: `IdentityUserRole`1`.
- Cột: `UserId`, `RoleId`.
- PK: `UserId`, `RoleId`.
- FK: `RoleId` → `AspNetRoles` (`Id`); Delete: `Cascade`.
- FK: `UserId` → `AspNetUsers` (`Id`); Delete: `Cascade`.
- INDEX: (`RoleId`).

## AspNetUsers

- Entity: `ApplicationUser`.
- Cột: `Id`, `AccessFailedCount`, `ConcurrencyStamp`, `CreatedAt`, `Email`, `EmailConfirmed`, `FullName`, `IsActive`, `LockoutEnabled`, `LockoutEnd`, `NormalizedEmail`, `NormalizedUserName`, `PasswordHash`, `PhoneNumber`, `PhoneNumberConfirmed`, `SecurityStamp`, `TwoFactorEnabled`, `UpdatedAt`, `UserName`.
- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`NormalizedEmail`); bộ lọc `[NormalizedEmail] IS NOT NULL`.
- UNIQUE: (`NormalizedUserName`); bộ lọc `[NormalizedUserName] IS NOT NULL`.
- UNIQUE: (`PhoneNumber`); bộ lọc `[PhoneNumber] IS NOT NULL`.
- Kiểm soát đồng thời: `ConcurrencyStamp`.
- Cấu hình: [ApplicationUserConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/ApplicationUserConfiguration.cs).

## AspNetUserTokens

- Entity: `IdentityUserToken`1`.
- Cột: `UserId`, `LoginProvider`, `Name`, `Value`.
- PK: `UserId`, `LoginProvider`, `Name`.
- FK: `UserId` → `AspNetUsers` (`Id`); Delete: `Cascade`.

## Beds

- Entity: `Bed`.
- Cột: `Id`, `BedNumber`, `BedType`, `DailyRate`, `IsActive`, `Notes`, `RoomId`, `RowVersion`, `Status`.
- PK: `Id`.
- FK: `RoomId` → `Rooms` (`Id`); Delete: `Restrict`.
- UNIQUE: (`RoomId`, `BedNumber`).
- Kiểm soát đồng thời: `RowVersion`.
- Cấu hình: [BedConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/BedConfiguration.cs).

## Buildings

- Entity: `Building`.
- Cột: `Id`, `Code`, `FacilityId`, `IsActive`, `Name`, `NumberOfFloors`.
- PK: `Id`.
- FK: `FacilityId` → `Facilities` (`Id`); Delete: `Restrict`.
- UNIQUE: (`FacilityId`, `Code`).
- Cấu hình: [BuildingConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/BuildingConfiguration.cs).

## ClinicLocations

- Entity: `ClinicLocation`.
- Cột: `Id`, `Address`, `City`, `Code`, `CreatedAt`, `Description`, `IsActive`, `Name`, `OpeningHours`, `Phone`, `ServicesJson`.
- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`Code`).
- INDEX: (`IsActive`).

## DailyQueueSequences

- Entity: `DailyQueueSequence`.
- Cột: `Id`, `Date`, `DepartmentId`, `FacilityId`, `LastNumber`.
- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`FacilityId`, `DepartmentId`, `Date`).
- Cấu hình: [DailyQueueSequenceConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/DailyQueueSequenceConfiguration.cs).

## Departments

- Entity: `Department`.
- Cột: `Id`, `BuildingId`, `Code`, `DepartmentType`, `Description`, `FacilityId`, `HeadOfDepartmentDoctorId`, `IsActive`, `Name`, `SpecialtyId`.
- PK: `Id`.
- FK: `BuildingId` → `Buildings` (`Id`); Delete: `Restrict`.
- FK: `FacilityId` → `Facilities` (`Id`); Delete: `Restrict`.
- FK: `HeadOfDepartmentDoctorId` → `Doctors` (`Id`); Delete: `Restrict`.
- FK: `SpecialtyId` → `Specialties` (`Id`); Delete: `Restrict`.
- INDEX: (`BuildingId`).
- INDEX: (`HeadOfDepartmentDoctorId`).
- INDEX: (`SpecialtyId`).
- UNIQUE: (`FacilityId`, `Code`).
- Cấu hình: [DepartmentConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/DepartmentConfiguration.cs).

## DiagnosticOrderItems

- Entity: `DiagnosticOrderItem`.
- Cột: `Id`, `CollectedAtUtc`, `CollectedByUserId`, `DiagnosticOrderId`, `DiagnosticServiceId`, `IsPackageCovered`, `PackageRegistrationId`, `ReceivedAtUtc`, `ReceivedByUserId`, `RecollectCount`, `RejectedAtUtc`, `RejectedByUserId`, `RejectionNote`, `RejectionReason`, `RowVersion`, `SpecimenBarcode`, `SpecimenStatus`, `Status`.
- PK: `Id`.
- FK: `DiagnosticOrderId` → `DiagnosticOrders` (`Id`); Delete: `Cascade`.
- FK: `DiagnosticServiceId` → `DiagnosticServices` (`Id`); Delete: `Restrict`.
- FK: `PackageRegistrationId` → `HealthPackageRegistrations` (`Id`); Delete: `Restrict`.
- INDEX: (`DiagnosticServiceId`).
- INDEX: (`PackageRegistrationId`).
- UNIQUE: (`SpecimenBarcode`); bộ lọc `[SpecimenBarcode] IS NOT NULL`.
- UNIQUE: (`DiagnosticOrderId`, `DiagnosticServiceId`).
- Kiểm soát đồng thời: `RowVersion`.
- Cấu hình: [DiagnosticOrderItemConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/DiagnosticOrderItemConfiguration.cs).

## DiagnosticOrders

- Entity: `DiagnosticOrder`.
- Cột: `Id`, `AppointmentId`, `CancelledAtUtc`, `ClinicalIndication`, `CompletedAtUtc`, `CompletedByUserId`, `FacilityId`, `Note`, `OrderCode`, `OrderedAtUtc`, `OrderingDoctorId`, `PatientId`, `PatientVisitId`, `PerformingDepartmentId`, `ReturnCount`, `ReturnReason`, `ReturnedAtUtc`, `ReturnedByUserId`, `ReviewedAtUtc`, `ReviewedByDoctorId`, `RowVersion`, `SourceAiActionId`, `StartedAtUtc`, `StartedByUserId`, `Status`, `SubmittedForVerificationAtUtc`, `SubmittedForVerificationByUserId`, `VerificationNote`, `VerificationStatus`, `VerifiedAtUtc`, `VerifiedByUserId`.
- PK: `Id`.
- FK: `AppointmentId` → `Appointments` (`Id`); Delete: `Restrict`.
- FK: `FacilityId` → `Facilities` (`Id`); Delete: `Restrict`.
- FK: `OrderingDoctorId` → `Doctors` (`Id`); Delete: `Restrict`.
- FK: `PatientId` → `Patients` (`Id`); Delete: `Restrict`.
- FK: `PatientVisitId` → `PatientVisits` (`Id`); Delete: `Restrict`.
- FK: `PerformingDepartmentId` → `Departments` (`Id`); Delete: `Restrict`.
- FK: `ReviewedByDoctorId` → `Doctors` (`Id`); Delete: `Restrict`.
- INDEX: (`AppointmentId`).
- UNIQUE: (`OrderCode`).
- INDEX: (`PatientVisitId`).
- INDEX: (`PerformingDepartmentId`).
- INDEX: (`ReviewedByDoctorId`).
- UNIQUE: (`SourceAiActionId`); bộ lọc `[SourceAiActionId] IS NOT NULL`.
- INDEX: (`VerificationStatus`).
- INDEX: (`OrderingDoctorId`, `Status`).
- INDEX: (`PatientId`, `OrderedAtUtc`).
- INDEX: (`Status`, `OrderedAtUtc`).
- INDEX: (`FacilityId`, `PerformingDepartmentId`, `Status`).
- Kiểm soát đồng thời: `RowVersion`.
- Cấu hình: [DiagnosticOrderConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/DiagnosticOrderConfiguration.cs).

## DiagnosticResultAttachments

- Entity: `DiagnosticResultAttachment`.
- Cột: `Id`, `ContentType`, `DiagnosticOrderItemId`, `OriginalFileName`, `SizeBytes`, `StoredFileName`, `UploadedAtUtc`, `UploadedByUserId`.
- PK: `Id`.
- FK: `DiagnosticOrderItemId` → `DiagnosticOrderItems` (`Id`); Delete: `Cascade`.
- INDEX: (`DiagnosticOrderItemId`).
- UNIQUE: (`StoredFileName`).
- Cấu hình: [DiagnosticResultAttachmentConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/DiagnosticResultAttachmentConfiguration.cs).

## DiagnosticResults

- Entity: `DiagnosticResult`.
- Cột: `Id`, `Conclusion`, `CriticalNotifiedAtUtc`, `DiagnosticOrderItemId`, `HasCriticalValue`, `ReferenceRange`, `ResultText`, `ResultedAtUtc`, `ResultedByUserId`, `RowVersion`, `Unit`.
- PK: `Id`.
- FK: `DiagnosticOrderItemId` → `DiagnosticOrderItems` (`Id`); Delete: `Cascade`.
- UNIQUE: (`DiagnosticOrderItemId`).
- Kiểm soát đồng thời: `RowVersion`.
- Cấu hình: [DiagnosticResultConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/DiagnosticResultConfiguration.cs).

## DiagnosticResultValues

- Entity: `DiagnosticResultValue`.
- Cột: `Id`, `CriticalHigh`, `CriticalLow`, `DeltaChange`, `DeltaPercent`, `DiagnosticResultId`, `DiagnosticServiceParameterId`, `Flag`, `IsDeltaFailure`, `NumericValue`, `ParameterCode`, `ParameterName`, `PreviousNumericValue`, `PreviousOrderCode`, `PreviousResultedAtUtc`, `RefHigh`, `RefLow`, `SortOrder`, `Unit`.
- PK: `Id`.
- FK: `DiagnosticResultId` → `DiagnosticResults` (`Id`); Delete: `Cascade`.
- FK: `DiagnosticServiceParameterId` → `DiagnosticServiceParameters` (`Id`); Delete: `Restrict`.
- INDEX: (`DiagnosticServiceParameterId`).
- UNIQUE: (`DiagnosticResultId`, `DiagnosticServiceParameterId`).
- Cấu hình: [DiagnosticResultValueConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/DiagnosticResultValueConfiguration.cs).

## DiagnosticServiceParameters

- Entity: `DiagnosticServiceParameter`.
- Cột: `Id`, `Code`, `CriticalHigh`, `CriticalLow`, `DeltaAbsoluteLimit`, `DeltaPercentLimit`, `DeltaWindowDays`, `DiagnosticServiceId`, `IsActive`, `Name`, `RefHigh`, `RefHighFemale`, `RefLow`, `RefLowFemale`, `SortOrder`, `Unit`.
- PK: `Id`.
- FK: `DiagnosticServiceId` → `DiagnosticServices` (`Id`); Delete: `Restrict`.
- UNIQUE: (`DiagnosticServiceId`, `Code`).
- Cấu hình: [DiagnosticServiceParameterConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/DiagnosticServiceParameterConfiguration.cs).

## DiagnosticServices

- Entity: `DiagnosticService`.
- Cột: `Id`, `Category`, `Code`, `ContainerType`, `IsActive`, `Name`, `PreparationInstructions`, `Price`, `SpecimenType`.
- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`Code`).
- Cấu hình: [DiagnosticServiceConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/DiagnosticServiceConfiguration.cs).

## DoctorLeaveRequests

- Entity: `DoctorLeaveRequest`.
- Cột: `Id`, `AdminNote`, `DoctorId`, `EndDateTime`, `Reason`, `StartDateTime`, `Status`.
- PK: `Id`.
- FK: `DoctorId` → `Doctors` (`Id`); Delete: `NoAction`.
- INDEX: (`DoctorId`).
- CHECK `CK_DoctorLeaveRequest_DateTimeRange`: `[StartDateTime] < [EndDateTime]`.
- Cấu hình: [DoctorLeaveRequestConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/DoctorLeaveRequestConfiguration.cs).

## Doctors

- Entity: `Doctor`.
- Cột: `Id`, `AcademicTitle`, `Description`, `ExperienceYears`, `IsActive`, `UserId`.
- PK: `Id`.
- FK: `UserId` → `AspNetUsers` (`Id`); Delete: `NoAction`.
- UNIQUE: (`UserId`).
- CHECK `CK_Doctor_ExperienceYears`: `[ExperienceYears] >= 0`.
- Cấu hình: [DoctorConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/DoctorConfiguration.cs).

## DoctorSpecialties

- Entity: `DoctorSpecialty`.
- Cột: `DoctorId`, `SpecialtyId`, `IsPrimary`.
- PK: `DoctorId`, `SpecialtyId`.
- FK: `DoctorId` → `Doctors` (`Id`); Delete: `NoAction`.
- FK: `SpecialtyId` → `Specialties` (`Id`); Delete: `NoAction`.
- UNIQUE: (`DoctorId`); bộ lọc `[IsPrimary] = 1`.
- INDEX: (`SpecialtyId`).
- Cấu hình: [DoctorSpecialtyConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/DoctorSpecialtyConfiguration.cs).

## DoctorWorkSchedules

- Entity: `DoctorWorkSchedule`.
- Cột: `Id`, `DoctorId`, `EndTime`, `IsActive`, `StartTime`, `WorkDate`.
- PK: `Id`.
- FK: `DoctorId` → `Doctors` (`Id`); Delete: `NoAction`.
- UNIQUE: (`DoctorId`, `WorkDate`, `StartTime`, `EndTime`).
- CHECK `CK_DoctorWorkSchedule_TimeRange`: `[StartTime] < [EndTime]`.
- Cấu hình: [DoctorWorkScheduleConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/DoctorWorkScheduleConfiguration.cs).

## DrugAllergyCrossReactions

- Entity: `DrugAllergyCrossReaction`.
- Cột: `Id`, `AllergenClassKey`, `Note`, `RelatedClassKey`.
- PK: `Id`.
- FK: không có FK được khai báo trong model.
- Cấu hình: [PrescriptionSafetyConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/PrescriptionSafetyConfiguration.cs).

## DrugClassMembers

- Entity: `DrugClassMember`.
- Cột: `Id`, `ClassKey`, `ClassName`, `IngredientKey`.
- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`ClassKey`, `IngredientKey`).
- Cấu hình: [PrescriptionSafetyConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/PrescriptionSafetyConfiguration.cs).

## DrugInteractionRules

- Entity: `DrugInteractionRule`.
- Cột: `Id`, `Description`, `IngredientKeyA`, `IngredientKeyB`, `IsActive`, `Recommendation`, `Severity`.
- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`IngredientKeyA`, `IngredientKeyB`).
- CHECK `CK_DrugInteractionRules_OrderedPair`: `[IngredientKeyA] < [IngredientKeyB]`.
- Cấu hình: [PrescriptionSafetyConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/PrescriptionSafetyConfiguration.cs).

## EmergencyContacts

- Entity: `EmergencyContact`.
- Cột: `Id`, `Address`, `FullName`, `IsGuardian`, `IsPrimary`, `PatientId`, `PhoneNumber`, `Relationship`.
- PK: `Id`.
- FK: `PatientId` → `Patients` (`Id`); Delete: `Cascade`.
- INDEX: (`PatientId`).
- Cấu hình: [EmergencyContactConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/EmergencyContactConfiguration.cs).

## Facilities

- Entity: `Facility`.
- Cột: `Id`, `Address`, `City`, `Code`, `CreatedAtUtc`, `Description`, `Email`, `HospitalLevel`, `IsActive`, `Name`, `Phone`, `TaxCode`, `UpdatedAtUtc`.
- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`Code`).
- INDEX: (`IsActive`).
- Cấu hình: [FacilityConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/FacilityConfiguration.cs).

## HealthPackageRegistrations

- Entity: `HealthPackageRegistration`.
- Cột: `Id`, `AdminNotes`, `CancellationReason`, `ContactPhone`, `CreatedAt`, `HealthPackageId`, `Note`, `PatientId`, `PreferredDate`, `RegistrationCode`, `Status`, `UpdatedAt`.
- PK: `Id`.
- FK: `HealthPackageId` → `HealthPackages` (`Id`); Delete: `Restrict`.
- FK: `PatientId` → `Patients` (`Id`); Delete: `Restrict`.
- INDEX: (`HealthPackageId`).
- INDEX: (`PatientId`).
- UNIQUE: (`RegistrationCode`).
- INDEX: (`Status`).

## HealthPackages

- Entity: `HealthPackage`.
- Cột: `Id`, `Code`, `CreatedAt`, `Description`, `ImageUrl`, `IncludedServicesJson`, `IsActive`, `Name`, `Price`, `TargetAudience`.
- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`Code`).
- Cấu hình: [HealthPackageConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/HealthPackageConfiguration.cs).

## Icd10Codes

- Entity: `Icd10Code`.
- Cột: `Code`, `ChapterCode`, `IsActive`, `NameVi`, `SearchKey`.
- PK: `Code`.
- FK: không có FK được khai báo trong model.
- Cấu hình: [Icd10CodeConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/Icd10CodeConfiguration.cs).

## IdempotencyRecords

- Entity: `IdempotencyRecord`.
- Cột: `Id`, `CreatedAtUtc`, `ExpiresAtUtc`, `Key`, `RequestHash`, `ResponseBody`, `Scope`, `StatusCode`, `UserId`.
- PK: `Id`.
- FK: không có FK được khai báo trong model.
- INDEX: (`ExpiresAtUtc`).
- UNIQUE: (`Key`, `Scope`).
- Cấu hình: [IdempotencyRecordConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/IdempotencyRecordConfiguration.cs).

## InvoiceItems

- Entity: `InvoiceItem`.
- Cột: `Id`, `Description`, `InvoiceId`, `IsCancelled`, `ItemCode`, `LineTotal`, `Quantity`, `ReferenceId`, `ReferenceType`, `UnitPrice`.
- PK: `Id`.
- FK: `InvoiceId` → `Invoices` (`Id`); Delete: `Cascade`.
- INDEX: (`InvoiceId`).
- UNIQUE: (`ReferenceType`, `ReferenceId`); bộ lọc `[ReferenceType] <> '' AND [ReferenceId] > 0 AND [IsCancelled] = 0`.
- CHECK `CK_InvoiceItems_LineTotal_NonNegative`: `[LineTotal] >= 0`.
- CHECK `CK_InvoiceItems_Quantity_Positive`: `[Quantity] > 0`.
- CHECK `CK_InvoiceItems_UnitPrice_NonNegative`: `[UnitPrice] >= 0`.
- Cấu hình: [InvoiceItemConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/InvoiceItemConfiguration.cs).

## Invoices

- Entity: `Invoice`.
- Cột: `Id`, `AppointmentId`, `CancellationReason`, `CancelledAtUtc`, `CreatedAtUtc`, `CreatedByUserId`, `HealthPackageRegistrationId`, `InvoiceCode`, `PaidAtUtc`, `PaidByUserId`, `PatientId`, `PatientVisitId`, `RowVersion`, `SourceType`, `Status`, `Subtotal`, `TotalAmount`.
- PK: `Id`.
- FK: `AppointmentId` → `Appointments` (`Id`); Delete: `Restrict`.
- FK: `HealthPackageRegistrationId` → `HealthPackageRegistrations` (`Id`); Delete: `Restrict`.
- FK: `PatientId` → `Patients` (`Id`); Delete: `Restrict`.
- FK: `PatientVisitId` → `PatientVisits` (`Id`); Delete: `Restrict`.
- UNIQUE: (`AppointmentId`); bộ lọc `[AppointmentId] IS NOT NULL AND [Status] <> 3`.
- INDEX: (`CreatedAtUtc`).
- UNIQUE: (`HealthPackageRegistrationId`); bộ lọc `[HealthPackageRegistrationId] IS NOT NULL AND [Status] <> 3`.
- UNIQUE: (`InvoiceCode`).
- INDEX: (`PatientId`).
- INDEX: (`PatientVisitId`).
- INDEX: (`Status`).
- INDEX: (`PatientId`, `CreatedAtUtc`).
- CHECK `CK_Invoices_SingleSource`: `(([AppointmentId] IS NOT NULL OR [PatientVisitId] IS NOT NULL) AND [HealthPackageRegistrationId] IS NULL) OR ([AppointmentId] IS NULL AND [PatientVisitId] IS NULL AND [HealthPackageRegistrationId] IS NOT NULL)`.
- CHECK `CK_Invoices_Subtotal_NonNegative`: `[Subtotal] >= 0`.
- CHECK `CK_Invoices_TotalAmount_NonNegative`: `[TotalAmount] >= 0`.
- Kiểm soát đồng thời: `RowVersion`.
- Cấu hình: [InvoiceConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/InvoiceConfiguration.cs).

## MedicineBatches

- Entity: `MedicineBatch`.
- Cột: `Id`, `BatchNumber`, `CreatedByUserId`, `DisposeReason`, `DisposedAt`, `ExpiryDate`, `MedicineId`, `QuantityReceived`, `QuantityRemaining`, `ReceivedAt`, `RowVersion`, `UnitCost`.
- PK: `Id`.
- FK: `MedicineId` → `Medicines` (`Id`); Delete: `Restrict`.
- UNIQUE: (`MedicineId`, `BatchNumber`, `ExpiryDate`).
- CHECK `CK_MedicineBatch_QuantityReceived`: `[QuantityReceived] >= 0`.
- CHECK `CK_MedicineBatch_QuantityRemaining`: `[QuantityRemaining] >= 0`.
- Kiểm soát đồng thời: `RowVersion`.
- Cấu hình: [MedicineBatchConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/MedicineBatchConfiguration.cs).

## MedicineCategories

- Entity: `MedicineCategory`.
- Cột: `Id`, `CreatedAt`, `Description`, `IsActive`, `Name`, `SortOrder`, `UpdatedAt`.
- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`Name`).
- Cấu hình: [MedicineCategoryConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/MedicineCategoryConfiguration.cs).

## Medicines

- Entity: `Medicine`.
- Cột: `Id`, `ActiveIngredient`, `CategoryId`, `Code`, `Contraindications`, `CreatedAt`, `Description`, `DosageAndAdministration`, `DosageForm`, `ImagePath`, `Indications`, `IsActive`, `IsPrescriptionRequired`, `Manufacturer`, `Name`, `OverdoseManagement`, `Precautions`, `ReorderLevel`, `RowVersion`, `SideEffects`, `StockQuantity`, `StorageInstructions`, `Strength`, `Unit`, `UnitPrice`, `UpdatedAt`.
- PK: `Id`.
- FK: `CategoryId` → `MedicineCategories` (`Id`); Delete: `Restrict`.
- INDEX: (`CategoryId`).
- UNIQUE: (`Code`).
- Kiểm soát đồng thời: `RowVersion`.
- Cấu hình: [MedicineConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/MedicineConfiguration.cs).

## MedicineStockTransactions

- Entity: `MedicineStockTransaction`.
- Cột: `Id`, `ActorUserId`, `BalanceAfter`, `CreatedAt`, `MedicineBatchId`, `MedicineId`, `PrescriptionId`, `QuantityChange`, `Reason`, `Type`.
- PK: `Id`.
- FK: `MedicineBatchId` → `MedicineBatches` (`Id`); Delete: `Restrict`.
- FK: `MedicineId` → `Medicines` (`Id`); Delete: `Cascade`.
- FK: `PrescriptionId` → `Prescriptions` (`Id`); Delete: `ClientSetNull`.
- INDEX: (`MedicineBatchId`).
- INDEX: (`MedicineId`).
- INDEX: (`PrescriptionId`).

## MrnSequences

- Entity: `MrnSequence`.
- Cột: `Id`, `LastSequenceNumber`, `RowVersion`, `Year`.
- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`Year`).
- Kiểm soát đồng thời: `RowVersion`.
- Cấu hình: [MrnSequenceConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/MrnSequenceConfiguration.cs).

## Notifications

- Entity: `Notification`.
- Cột: `Id`, `CreatedAtUtc`, `DedupeKey`, `IsRead`, `Message`, `ReadAtUtc`, `RelatedEntityId`, `RelatedEntityType`, `Route`, `Title`, `Type`, `UserId`.
- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`DedupeKey`); bộ lọc `[DedupeKey] IS NOT NULL`.
- INDEX: (`UserId`, `IsRead`, `CreatedAtUtc`).
- Cấu hình: [NotificationConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/NotificationConfiguration.cs).

## PatientAllergies

- Entity: `PatientAllergy`.
- Cột: `Id`, `AllergenName`, `AllergenType`, `PatientId`, `ReactionDescription`, `RecordedAtUtc`, `Severity`.
- PK: `Id`.
- FK: `PatientId` → `Patients` (`Id`); Delete: `Cascade`.
- INDEX: (`PatientId`).
- Cấu hình: [PatientAllergyConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/PatientAllergyConfiguration.cs).

## Patients

- Entity: `Patient`.
- Cột: `Id`, `Address`, `BhytNumber`, `BloodType`, `DateOfBirth`, `Email`, `FullName`, `Gender`, `MedicalRecordNumber`, `NationalId`, `PhoneNumber`, `PrimaryFacilityId`, `RhFactor`, `UserId`.
- PK: `Id`.
- FK: `PrimaryFacilityId` → `Facilities` (`Id`); Delete: `Restrict`.
- FK: `UserId` → `AspNetUsers` (`Id`); Delete: `NoAction`.
- INDEX: (`BhytNumber`).
- UNIQUE: (`MedicalRecordNumber`).
- UNIQUE: (`NationalId`); bộ lọc `[NationalId] IS NOT NULL`.
- INDEX: (`PrimaryFacilityId`).
- UNIQUE: (`UserId`); bộ lọc `[UserId] IS NOT NULL`.
- Cấu hình: [PatientConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/PatientConfiguration.cs).

## PatientVisits

- Entity: `PatientVisit`.
- Cột: `Id`, `AppointmentId`, `ArrivalType`, `AssignedDoctorId`, `CancellationReason`, `CancelledAtUtc`, `CheckedInAtUtc`, `ChiefComplaint`, `CompletedAtUtc`, `ConsultationStartedAtUtc`, `CreatedAtUtc`, `CreatedByUserId`, `DepartmentId`, `FacilityId`, `HealthPackageRegistrationId`, `PatientId`, `Priority`, `QueueNumber`, `RoomId`, `RowVersion`, `Status`, `UpdatedAtUtc`, `VisitCode`, `VisitDate`.
- PK: `Id`.
- FK: `AppointmentId` → `Appointments` (`Id`); Delete: `Restrict`.
- FK: `AssignedDoctorId` → `Doctors` (`Id`); Delete: `Restrict`.
- FK: `DepartmentId` → `Departments` (`Id`); Delete: `Restrict`.
- FK: `FacilityId` → `Facilities` (`Id`); Delete: `Restrict`.
- FK: `HealthPackageRegistrationId` → `HealthPackageRegistrations` (`Id`); Delete: `Restrict`.
- FK: `PatientId` → `Patients` (`Id`); Delete: `Restrict`.
- FK: `RoomId` → `Rooms` (`Id`); Delete: `Restrict`.
- UNIQUE: (`AppointmentId`); bộ lọc `[AppointmentId] IS NOT NULL`.
- INDEX: (`DepartmentId`).
- INDEX: (`HealthPackageRegistrationId`); bộ lọc `[HealthPackageRegistrationId] IS NOT NULL`.
- INDEX: (`PatientId`).
- INDEX: (`RoomId`).
- UNIQUE: (`VisitCode`).
- INDEX: (`AssignedDoctorId`, `VisitDate`, `Status`).
- INDEX: (`FacilityId`, `DepartmentId`, `VisitDate`, `Status`).
- UNIQUE: (`VisitDate`, `FacilityId`, `DepartmentId`, `QueueNumber`).
- Kiểm soát đồng thời: `RowVersion`.
- Cấu hình: [PatientVisitConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/PatientVisitConfiguration.cs).

## Payments

- Entity: `Payment`.
- Cột: `Id`, `Amount`, `InvoiceId`, `Method`, `Note`, `PaymentCode`, `ReceivedAtUtc`, `ReceivedByUserId`, `ReferenceCode`, `RowVersion`, `Status`.
- PK: `Id`.
- FK: `InvoiceId` → `Invoices` (`Id`); Delete: `Restrict`.
- INDEX: (`InvoiceId`).
- UNIQUE: (`PaymentCode`).
- INDEX: (`ReceivedAtUtc`).
- INDEX: (`Status`).
- CHECK `CK_Payments_Amount_Positive`: `[Amount] > 0`.
- Kiểm soát đồng thời: `RowVersion`.
- Cấu hình: [PaymentConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/PaymentConfiguration.cs).

## PrescriptionItems

- Entity: `PrescriptionItem`.
- Cột: `PrescriptionId`, `MedicineId`, `Dosage`, `DurationDays`, `Frequency`, `Instructions`, `Quantity`.
- PK: `PrescriptionId`, `MedicineId`.
- FK: `MedicineId` → `Medicines` (`Id`); Delete: `Cascade`.
- FK: `PrescriptionId` → `Prescriptions` (`Id`); Delete: `Cascade`.
- INDEX: (`MedicineId`).

## Prescriptions

- Entity: `Prescription`.
- Cột: `Id`, `AppointmentId`, `CreatedAt`, `DispensedAt`, `DispensedByUserId`, `DoctorId`, `Notes`, `PatientId`, `PatientVisitId`, `RowVersion`, `SafetyAcknowledgedAtUtc`, `SafetyAcknowledgedByUserId`, `SafetyOverrideReason`, `SafetyWarningsSnapshot`, `Status`.
- PK: `Id`.
- FK: `AppointmentId` → `Appointments` (`Id`); Delete: `ClientSetNull`.
- FK: `DoctorId` → `Doctors` (`Id`); Delete: `Cascade`.
- FK: `PatientId` → `Patients` (`Id`); Delete: `Cascade`.
- FK: `PatientVisitId` → `PatientVisits` (`Id`); Delete: `Restrict`.
- UNIQUE: (`AppointmentId`); bộ lọc `[AppointmentId] IS NOT NULL`.
- INDEX: (`DoctorId`).
- INDEX: (`PatientId`).
- INDEX: (`PatientVisitId`).
- Kiểm soát đồng thời: `RowVersion`.
- Cấu hình: [PrescriptionSafetyConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/PrescriptionSafetyConfiguration.cs).

## RevisitRequests

- Entity: `RevisitRequest`.
- Cột: `Id`, `AppointmentId`, `DoctorId`, `NewAppointmentId`, `Note`, `PatientId`, `Status`, `SuggestedDate`.
- PK: `Id`.
- FK: `AppointmentId` → `Appointments` (`Id`); Delete: `NoAction`.
- FK: `DoctorId` → `Doctors` (`Id`); Delete: `NoAction`.
- FK: `NewAppointmentId` → `Appointments` (`Id`); Delete: `NoAction`.
- FK: `PatientId` → `Patients` (`Id`); Delete: `NoAction`.
- UNIQUE: (`AppointmentId`).
- INDEX: (`DoctorId`).
- UNIQUE: (`NewAppointmentId`); bộ lọc `[NewAppointmentId] IS NOT NULL`.
- INDEX: (`PatientId`).
- Cấu hình: [RevisitRequestConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/RevisitRequestConfiguration.cs).

## Rooms

- Entity: `Room`.
- Cột: `Id`, `BuildingId`, `DepartmentId`, `FloorNumber`, `IsActive`, `MaxCapacity`, `Name`, `RoomNumber`, `RoomType`.
- PK: `Id`.
- FK: `BuildingId` → `Buildings` (`Id`); Delete: `Restrict`.
- FK: `DepartmentId` → `Departments` (`Id`); Delete: `Restrict`.
- INDEX: (`BuildingId`).
- UNIQUE: (`DepartmentId`, `RoomNumber`).
- Cấu hình: [RoomConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/RoomConfiguration.cs).

## Specialties

- Entity: `Specialty`.
- Cột: `Id`, `AiEnabled`, `ConsultationFee`, `Description`, `IsActive`, `Name`, `SpecialtyCode`.
- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`SpecialtyCode`).
- CHECK `CK_Specialties_ConsultationFee_NonNegative`: `[ConsultationFee] >= 0`.
- Cấu hình: [SpecialtyConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/SpecialtyConfiguration.cs).

## StaffFacilityAssignments

- Entity: `StaffFacilityAssignment`.
- Cột: `Id`, `AssignedAtUtc`, `DepartmentId`, `FacilityId`, `IsActive`, `IsPrimary`, `Role`, `UserId`.
- PK: `Id`.
- FK: `DepartmentId` → `Departments` (`Id`); Delete: `Restrict`.
- FK: `FacilityId` → `Facilities` (`Id`); Delete: `Restrict`.
- INDEX: (`DepartmentId`).
- INDEX: (`FacilityId`).
- UNIQUE: (`UserId`, `FacilityId`, `Role`).
- Cấu hình: [StaffFacilityAssignmentConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/StaffFacilityAssignmentConfiguration.cs).

## SystemAuditLogs

- Entity: `SystemAuditLog`.
- Cột: `Id`, `Action`, `CreatedAt`, `Description`, `EntityId`, `EntityName`, `UserId`.
- PK: `Id`.
- FK: `UserId` → `AspNetUsers` (`Id`); Delete: `NoAction`.
- INDEX: (`UserId`).
- Cấu hình: [SystemAuditLogConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/SystemAuditLogConfiguration.cs).

## VisitSummaries

- Entity: `VisitSummary`.
- Cột: `Id`, `AppointmentId`, `ChiefComplaint`, `ClinicalFindings`, `CompletedAtUtc`, `CreatedAtUtc`, `Diagnosis`, `DiagnosisCode`, `DoctorId`, `FollowUpInstruction`, `PatientVisitId`, `RowVersion`, `Summary`, `TreatmentPlan`, `UpdatedAtUtc`.
- PK: `Id`.
- FK: `AppointmentId` → `Appointments` (`Id`); Delete: `Restrict`.
- FK: `DoctorId` → `Doctors` (`Id`); Delete: `NoAction`.
- FK: `PatientVisitId` → `PatientVisits` (`Id`); Delete: `Restrict`.
- UNIQUE: (`AppointmentId`); bộ lọc `[AppointmentId] IS NOT NULL`.
- INDEX: (`DoctorId`).
- UNIQUE: (`PatientVisitId`); bộ lọc `[PatientVisitId] IS NOT NULL`.
- Kiểm soát đồng thời: `RowVersion`.
- Cấu hình: [VisitSummaryConfiguration.cs](../../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations/VisitSummaryConfiguration.cs).
