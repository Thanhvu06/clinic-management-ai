# Từ điển dữ liệu

64 bảng từ model SQL Server của `AppDbContext`. Cột “Null” ghi có được để trống; kiểu và giới hạn là cấu hình lưu trữ hiện tại. Enum ghi theo kiểu cột SQL, không phải nhãn UI. Khóa/quan hệ/chỉ mục được ghi lại dưới từng bảng để có thể tra độc lập; xem [tổng quan bảng](database/database-tables.md).

Nguồn: [AppDbContext.cs](../src/backend/ClinicManagement.Infrastructure/Persistence/AppDbContext.cs) và [Configurations](../src/backend/ClinicManagement.Infrastructure/Persistence/Configurations).

## AiAuditLogs

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `ActionType` | `nvarchar(64)` | Không | dài tối đa 64 |
| `CorrelationId` | `nvarchar(128)` | Có | dài tối đa 128 |
| `DraftId` | `nvarchar(128)` | Có | dài tối đa 128 |
| `DraftVersion` | `int` | Có | — |
| `ErrorCode` | `nvarchar(64)` | Có | dài tối đa 64 |
| `FacilityId` | `bigint` | Có | — |
| `MetadataJson` | `nvarchar(2000)` | Có | dài tối đa 2000 |
| `Outcome` | `nvarchar(64)` | Không | dài tối đa 64 |
| `SessionId` | `nvarchar(128)` | Có | dài tối đa 128 |
| `TimestampUtc` | `datetime2` | Không | — |
| `UserId` | `uniqueidentifier` | Có | — |

- PK: `Id`.
- FK: không có FK được khai báo trong model.
- INDEX: (`ActionType`).
- INDEX: (`SessionId`, `TimestampUtc`).
- INDEX: (`UserId`, `TimestampUtc`).

## AiBookingConfirmations

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `ConfirmationId` | `nvarchar(64)` | Không | dài tối đa 64 |
| `ContextSnapshotId` | `nvarchar(64)` | Có | dài tối đa 64 |
| `CreatedAtUtc` | `datetime2` | Không | — |
| `DoctorId` | `bigint` | Không | — |
| `DraftId` | `nvarchar(128)` | Không | dài tối đa 128 |
| `DraftVersion` | `int` | Không | — |
| `EndTime` | `time` | Không | — |
| `ExpiresAtUtc` | `datetime2` | Không | — |
| `FacilityId` | `bigint` | Có | — |
| `ReasonHash` | `nvarchar(64)` | Không | dài tối đa 64 |
| `RevokedAtUtc` | `datetime2` | Có | — |
| `SessionId` | `nvarchar(128)` | Không | dài tối đa 128 |
| `SlotDate` | `date` | Không | — |
| `SlotId` | `bigint` | Không | — |
| `SpecialtyId` | `bigint` | Không | — |
| `StartTime` | `time` | Không | — |
| `UsedAppointmentId` | `bigint` | Có | — |
| `UsedAtUtc` | `datetime2` | Có | — |
| `UsedIdempotencyKey` | `nvarchar(128)` | Có | dài tối đa 128 |
| `UserId` | `uniqueidentifier` | Không | — |

- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`ConfirmationId`).
- INDEX: (`ExpiresAtUtc`).
- UNIQUE: (`UserId`, `SessionId`, `DraftId`); bộ lọc `[UsedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL`.
- INDEX: (`UserId`, `SessionId`, `DraftId`, `RevokedAtUtc`).

## AiCancelledDraftScopes

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `CancelledAtUtc` | `datetime2` | Không | — |
| `DraftId` | `nvarchar(128)` | Không | dài tối đa 128 |
| `ExpiresAtUtc` | `datetime2` | Không | — |
| `FacilityId` | `bigint` | Có | — |
| `SessionId` | `nvarchar(128)` | Không | dài tối đa 128 |
| `UserId` | `uniqueidentifier` | Có | — |

- PK: `Id`.
- FK: không có FK được khai báo trong model.
- INDEX: (`ExpiresAtUtc`).
- UNIQUE: (`UserId`, `SessionId`, `DraftId`); bộ lọc `[UserId] IS NOT NULL`.

## AiPendingToolActions

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `ActionId` | `uniqueidentifier` | Không | — |
| `ActorRole` | `nvarchar(48)` | Không | dài tối đa 48; mặc định `Patient` |
| `CancelledAtUtc` | `datetime2` | Có | — |
| `ConfirmationTokenHash` | `nvarchar(128)` | Có | dài tối đa 128 |
| `ConfirmedAtUtc` | `datetime2` | Có | — |
| `ConversationId` | `nvarchar(128)` | Có | dài tối đa 128 |
| `CreatedAtUtc` | `datetime2` | Không | — |
| `DraftId` | `nvarchar(128)` | Có | dài tối đa 128 |
| `ExecutedAtUtc` | `datetime2` | Có | — |
| `ExecutionAttemptCount` | `int` | Không | — |
| `ExecutionLeaseExpiresAtUtc` | `datetime2` | Có | — |
| `ExecutionLeaseId` | `uniqueidentifier` | Có | — |
| `ExecutionResultReference` | `nvarchar(256)` | Có | dài tối đa 256 |
| `ExpiresAtUtc` | `datetime2` | Không | — |
| `FacilityId` | `bigint` | Có | — |
| `IdempotencyKeyHash` | `nvarchar(128)` | Có | dài tối đa 128 |
| `LastErrorCode` | `nvarchar(120)` | Có | dài tối đa 120 |
| `NormalizedArgumentsJson` | `nvarchar(4000)` | Không | dài tối đa 4000 |
| `RequestHash` | `nvarchar(128)` | Không | dài tối đa 128 |
| `ResourceId` | `nvarchar(128)` | Không | dài tối đa 128 |
| `ResourceType` | `nvarchar(64)` | Không | dài tối đa 64 |
| `ResourceVersion` | `nvarchar(256)` | Có | dài tối đa 256 |
| `RowVersion` | `rowversion` | Không | concurrency token |
| `SessionId` | `nvarchar(128)` | Không | dài tối đa 128 |
| `SourceAiActionId` | `uniqueidentifier` | Có | — |
| `State` | `nvarchar(32)` | Không | dài tối đa 32 |
| `ToolName` | `nvarchar(120)` | Không | dài tối đa 120 |
| `ToolVersion` | `nvarchar(32)` | Không | dài tối đa 32 |
| `UserId` | `uniqueidentifier` | Không | — |

- PK: `ActionId`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`SourceAiActionId`); bộ lọc `[SourceAiActionId] IS NOT NULL`.
- INDEX: (`UserId`, `ExpiresAtUtc`).
- UNIQUE: (`UserId`, `ResourceType`, `ResourceId`); bộ lọc `[State] IN ('PendingConfirmation', 'Executing', 'FailedRetryable')`.
- INDEX: (`UserId`, `ResourceType`, `ResourceId`, `ToolName`).
- Kiểm soát đồng thời: `RowVersion`.

## AiSelectionSnapshots

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `CreatedAtUtc` | `datetime2` | Không | — |
| `DoctorId` | `bigint` | Có | — |
| `DoctorIdsJson` | `nvarchar(4000)` | Không | dài tối đa 4000 |
| `DraftId` | `nvarchar(128)` | Có | dài tối đa 128 |
| `DraftVersion` | `int` | Có | — |
| `ExpiresAtUtc` | `datetime2` | Không | — |
| `FacilityId` | `bigint` | Có | — |
| `IsRevoked` | `bit` | Không | — |
| `RevokedAtUtc` | `datetime2` | Có | — |
| `SessionId` | `nvarchar(128)` | Có | dài tối đa 128 |
| `SlotDate` | `nvarchar(32)` | Có | dài tối đa 32 |
| `SlotIdsJson` | `nvarchar(4000)` | Không | dài tối đa 4000 |
| `SnapshotId` | `nvarchar(64)` | Không | dài tối đa 64 |
| `SpecialtyId` | `bigint` | Có | — |
| `UserId` | `uniqueidentifier` | Có | — |

- PK: `Id`.
- FK: không có FK được khai báo trong model.
- INDEX: (`ExpiresAtUtc`).
- UNIQUE: (`SnapshotId`).
- INDEX: (`UserId`, `SessionId`, `DraftId`).

## AiSessions

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `ActiveDraftId` | `nvarchar(128)` | Có | dài tối đa 128 |
| `ActiveDraftVersion` | `int` | Có | — |
| `ConversationStateJson` | `nvarchar(4000)` | Có | dài tối đa 4000 |
| `ConversationVersion` | `int` | Không | mặc định `0` |
| `CreatedAtUtc` | `datetime2` | Không | — |
| `ExpiresAtUtc` | `datetime2` | Không | — |
| `FacilityId` | `bigint` | Có | — |
| `IsActive` | `bit` | Không | mặc định `True` |
| `LastActiveAtUtc` | `datetime2` | Không | — |
| `RowVersion` | `rowversion` | Không | concurrency token |
| `SessionId` | `nvarchar(128)` | Không | dài tối đa 128 |
| `UserId` | `uniqueidentifier` | Có | — |

- PK: `Id`.
- FK: không có FK được khai báo trong model.
- INDEX: (`ExpiresAtUtc`).
- UNIQUE: (`SessionId`); bộ lọc `[UserId] IS NULL`.
- UNIQUE: (`SessionId`, `UserId`); bộ lọc `[UserId] IS NOT NULL`.
- INDEX: (`UserId`, `IsActive`).
- Kiểm soát đồng thời: `RowVersion`.

## AiSuggestionLogs

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `CreatedAt` | `datetime2` | Không | — |
| `InputText` | `nvarchar(max)` | Không | — |
| `PatientId` | `bigint` | Không | — |
| `PromptVersion` | `nvarchar(max)` | Không | — |
| `Provider` | `nvarchar(max)` | Không | — |
| `SelectedSpecialtyId` | `bigint` | Có | — |
| `SuggestedSpecialtiesJson` | `nvarchar(max)` | Không | — |

- PK: `Id`.
- FK: `PatientId` → `Patients` (`Id`); Delete: `NoAction`.
- FK: `SelectedSpecialtyId` → `Specialties` (`Id`); Delete: `NoAction`.
- INDEX: (`PatientId`).
- INDEX: (`SelectedSpecialtyId`).

## AppointmentChangeRequests

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `AppointmentId` | `bigint` | Không | — |
| `CreatedAt` | `datetime2` | Không | — |
| `OriginalAppointmentStatus` | `nvarchar(max)` | Có | — |
| `ProcessedAt` | `datetime2` | Có | — |
| `ProcessedByUserId` | `uniqueidentifier` | Có | — |
| `Reason` | `nvarchar(max)` | Có | — |
| `RequestType` | `nvarchar(max)` | Không | — |
| `RequestedByUserId` | `uniqueidentifier` | Không | — |
| `RequestedSlotId` | `bigint` | Có | — |
| `SourceAiActionId` | `uniqueidentifier` | Có | — |
| `Status` | `nvarchar(max)` | Không | mặc định `Pending` |

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

## AppointmentHistory

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `Action` | `nvarchar(max)` | Không | — |
| `AppointmentId` | `bigint` | Không | — |
| `CreatedAt` | `datetime2` | Không | — |
| `NewStatus` | `nvarchar(max)` | Có | — |
| `Note` | `nvarchar(max)` | Có | — |
| `OldStatus` | `nvarchar(max)` | Có | — |
| `PerformedByUserId` | `uniqueidentifier` | Không | — |

- PK: `Id`.
- FK: `AppointmentId` → `Appointments` (`Id`); Delete: `NoAction`.
- FK: `PerformedByUserId` → `AspNetUsers` (`Id`); Delete: `NoAction`.
- INDEX: (`AppointmentId`).
- INDEX: (`PerformedByUserId`).

## Appointments

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `AppointmentCode` | `nvarchar(450)` | Không | — |
| `AppointmentDate` | `date` | Không | — |
| `AppointmentSlotId` | `bigint` | Không | — |
| `DoctorId` | `bigint` | Không | — |
| `EndTime` | `time` | Không | — |
| `FacilityId` | `bigint` | Có | — |
| `PatientId` | `bigint` | Không | — |
| `Reason` | `nvarchar(500)` | Có | dài tối đa 500 |
| `SpecialtyId` | `bigint` | Không | — |
| `StartTime` | `time` | Không | — |
| `Status` | `nvarchar(max)` | Không | mặc định `Pending` |

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

## AppointmentSlots

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `DoctorId` | `bigint` | Không | — |
| `EndTime` | `time` | Không | — |
| `IsBooked` | `bit` | Không | mặc định `False` |
| `SlotDate` | `date` | Không | — |
| `StartTime` | `time` | Không | — |

- PK: `Id`.
- FK: `DoctorId` → `Doctors` (`Id`); Delete: `NoAction`.
- UNIQUE: (`DoctorId`, `SlotDate`, `StartTime`).
- CHECK `CK_AppointmentSlot_Duration`: `DATEDIFF(MINUTE, [StartTime], [EndTime]) = 30`.
- CHECK `CK_AppointmentSlot_TimeRange`: `[StartTime] < [EndTime]`.

## AppointmentVitalSigns

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `AppointmentId` | `bigint` | Có | — |
| `BloodPressureDiastolic` | `int` | Có | — |
| `BloodPressureSystolic` | `int` | Có | — |
| `Bmi` | `decimal(4,1)` | Có | precision 4, scale 1 |
| `HeartRate` | `int` | Có | — |
| `Height` | `decimal(5,1)` | Có | precision 5, scale 1 |
| `PatientVisitId` | `bigint` | Có | — |
| `RecordedAtUtc` | `datetime2` | Không | — |
| `RecordedByUserId` | `uniqueidentifier` | Không | — |
| `RespiratoryRate` | `int` | Có | — |
| `RowVersion` | `rowversion` | Có | concurrency token |
| `SpO2` | `int` | Có | — |
| `Temperature` | `decimal(4,1)` | Có | precision 4, scale 1 |
| `Weight` | `decimal(5,2)` | Có | precision 5, scale 2 |

- PK: `Id`.
- FK: `AppointmentId` → `Appointments` (`Id`); Delete: `Restrict`.
- FK: `PatientVisitId` → `PatientVisits` (`Id`); Delete: `Restrict`.
- UNIQUE: (`AppointmentId`); bộ lọc `[AppointmentId] IS NOT NULL`.
- UNIQUE: (`PatientVisitId`); bộ lọc `[PatientVisitId] IS NOT NULL`.
- Kiểm soát đồng thời: `RowVersion`.

## AspNetRoleClaims

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `int` | Không | — |
| `ClaimType` | `nvarchar(max)` | Có | — |
| `ClaimValue` | `nvarchar(max)` | Có | — |
| `RoleId` | `uniqueidentifier` | Không | — |

- PK: `Id`.
- FK: `RoleId` → `AspNetRoles` (`Id`); Delete: `Cascade`.
- INDEX: (`RoleId`).

## AspNetRoles

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `uniqueidentifier` | Không | — |
| `ConcurrencyStamp` | `nvarchar(max)` | Có | concurrency token |
| `Name` | `nvarchar(256)` | Có | dài tối đa 256 |
| `NormalizedName` | `nvarchar(256)` | Có | dài tối đa 256 |

- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`NormalizedName`); bộ lọc `[NormalizedName] IS NOT NULL`.
- Kiểm soát đồng thời: `ConcurrencyStamp`.

## AspNetUserClaims

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `int` | Không | — |
| `ClaimType` | `nvarchar(max)` | Có | — |
| `ClaimValue` | `nvarchar(max)` | Có | — |
| `UserId` | `uniqueidentifier` | Không | — |

- PK: `Id`.
- FK: `UserId` → `AspNetUsers` (`Id`); Delete: `Cascade`.
- INDEX: (`UserId`).

## AspNetUserLogins

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `LoginProvider` | `nvarchar(450)` | Không | — |
| `ProviderKey` | `nvarchar(450)` | Không | — |
| `ProviderDisplayName` | `nvarchar(max)` | Có | — |
| `UserId` | `uniqueidentifier` | Không | — |

- PK: `LoginProvider`, `ProviderKey`.
- FK: `UserId` → `AspNetUsers` (`Id`); Delete: `Cascade`.
- INDEX: (`UserId`).

## AspNetUserRoles

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `UserId` | `uniqueidentifier` | Không | — |
| `RoleId` | `uniqueidentifier` | Không | — |

- PK: `UserId`, `RoleId`.
- FK: `RoleId` → `AspNetRoles` (`Id`); Delete: `Cascade`.
- FK: `UserId` → `AspNetUsers` (`Id`); Delete: `Cascade`.
- INDEX: (`RoleId`).

## AspNetUsers

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `uniqueidentifier` | Không | — |
| `AccessFailedCount` | `int` | Không | — |
| `ConcurrencyStamp` | `nvarchar(max)` | Có | concurrency token |
| `CreatedAt` | `datetime2` | Không | — |
| `Email` | `nvarchar(256)` | Không | dài tối đa 256 |
| `EmailConfirmed` | `bit` | Không | — |
| `FullName` | `nvarchar(max)` | Không | — |
| `IsActive` | `bit` | Không | mặc định `True` |
| `LockoutEnabled` | `bit` | Không | — |
| `LockoutEnd` | `datetimeoffset` | Có | — |
| `NormalizedEmail` | `nvarchar(256)` | Có | dài tối đa 256 |
| `NormalizedUserName` | `nvarchar(256)` | Có | dài tối đa 256 |
| `PasswordHash` | `nvarchar(max)` | Có | — |
| `PhoneNumber` | `nvarchar(450)` | Không | — |
| `PhoneNumberConfirmed` | `bit` | Không | — |
| `SecurityStamp` | `nvarchar(max)` | Có | — |
| `TwoFactorEnabled` | `bit` | Không | — |
| `UpdatedAt` | `datetime2` | Không | — |
| `UserName` | `nvarchar(256)` | Có | dài tối đa 256 |

- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`NormalizedEmail`); bộ lọc `[NormalizedEmail] IS NOT NULL`.
- UNIQUE: (`NormalizedUserName`); bộ lọc `[NormalizedUserName] IS NOT NULL`.
- UNIQUE: (`PhoneNumber`); bộ lọc `[PhoneNumber] IS NOT NULL`.
- Kiểm soát đồng thời: `ConcurrencyStamp`.

## AspNetUserTokens

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `UserId` | `uniqueidentifier` | Không | — |
| `LoginProvider` | `nvarchar(450)` | Không | — |
| `Name` | `nvarchar(450)` | Không | — |
| `Value` | `nvarchar(max)` | Có | — |

- PK: `UserId`, `LoginProvider`, `Name`.
- FK: `UserId` → `AspNetUsers` (`Id`); Delete: `Cascade`.

## Beds

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `BedNumber` | `nvarchar(50)` | Không | dài tối đa 50 |
| `BedType` | `nvarchar(50)` | Không | dài tối đa 50 |
| `DailyRate` | `decimal(18,2)` | Không | precision 18, scale 2; mặc định `0` |
| `IsActive` | `bit` | Không | — |
| `Notes` | `nvarchar(max)` | Có | — |
| `RoomId` | `bigint` | Không | — |
| `RowVersion` | `rowversion` | Có | concurrency token |
| `Status` | `nvarchar(50)` | Không | dài tối đa 50 |

- PK: `Id`.
- FK: `RoomId` → `Rooms` (`Id`); Delete: `Restrict`.
- UNIQUE: (`RoomId`, `BedNumber`).
- Kiểm soát đồng thời: `RowVersion`.

## Buildings

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `Code` | `nvarchar(50)` | Không | dài tối đa 50 |
| `FacilityId` | `bigint` | Không | — |
| `IsActive` | `bit` | Không | — |
| `Name` | `nvarchar(200)` | Không | dài tối đa 200 |
| `NumberOfFloors` | `int` | Không | — |

- PK: `Id`.
- FK: `FacilityId` → `Facilities` (`Id`); Delete: `Restrict`.
- UNIQUE: (`FacilityId`, `Code`).

## ClinicLocations

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `Address` | `nvarchar(300)` | Không | dài tối đa 300 |
| `City` | `nvarchar(100)` | Không | dài tối đa 100 |
| `Code` | `nvarchar(50)` | Không | dài tối đa 50 |
| `CreatedAt` | `datetime2` | Không | — |
| `Description` | `nvarchar(1000)` | Có | dài tối đa 1000 |
| `IsActive` | `bit` | Không | — |
| `Name` | `nvarchar(200)` | Không | dài tối đa 200 |
| `OpeningHours` | `nvarchar(100)` | Không | dài tối đa 100 |
| `Phone` | `nvarchar(30)` | Không | dài tối đa 30 |
| `ServicesJson` | `nvarchar(max)` | Có | — |

- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`Code`).
- INDEX: (`IsActive`).

## DailyQueueSequences

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `Date` | `date` | Không | — |
| `DepartmentId` | `bigint` | Không | — |
| `FacilityId` | `bigint` | Không | — |
| `LastNumber` | `int` | Không | — |

- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`FacilityId`, `DepartmentId`, `Date`).

## Departments

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `BuildingId` | `bigint` | Có | — |
| `Code` | `nvarchar(50)` | Không | dài tối đa 50 |
| `DepartmentType` | `nvarchar(50)` | Không | dài tối đa 50 |
| `Description` | `nvarchar(max)` | Có | — |
| `FacilityId` | `bigint` | Không | — |
| `HeadOfDepartmentDoctorId` | `bigint` | Có | — |
| `IsActive` | `bit` | Không | — |
| `Name` | `nvarchar(200)` | Không | dài tối đa 200 |
| `SpecialtyId` | `bigint` | Có | — |

- PK: `Id`.
- FK: `BuildingId` → `Buildings` (`Id`); Delete: `Restrict`.
- FK: `FacilityId` → `Facilities` (`Id`); Delete: `Restrict`.
- FK: `HeadOfDepartmentDoctorId` → `Doctors` (`Id`); Delete: `Restrict`.
- FK: `SpecialtyId` → `Specialties` (`Id`); Delete: `Restrict`.
- INDEX: (`BuildingId`).
- INDEX: (`HeadOfDepartmentDoctorId`).
- INDEX: (`SpecialtyId`).
- UNIQUE: (`FacilityId`, `Code`).

## DiagnosticOrderItems

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `CollectedAtUtc` | `datetime2` | Có | — |
| `CollectedByUserId` | `uniqueidentifier` | Có | — |
| `DiagnosticOrderId` | `bigint` | Không | — |
| `DiagnosticServiceId` | `bigint` | Không | — |
| `IsPackageCovered` | `bit` | Không | mặc định `False` |
| `PackageRegistrationId` | `bigint` | Có | — |
| `ReceivedAtUtc` | `datetime2` | Có | — |
| `ReceivedByUserId` | `uniqueidentifier` | Có | — |
| `RecollectCount` | `int` | Không | mặc định `0` |
| `RejectedAtUtc` | `datetime2` | Có | — |
| `RejectedByUserId` | `uniqueidentifier` | Có | — |
| `RejectionNote` | `nvarchar(500)` | Có | dài tối đa 500 |
| `RejectionReason` | `nvarchar(30)` | Có | dài tối đa 30 |
| `RowVersion` | `rowversion` | Có | concurrency token |
| `SpecimenBarcode` | `nvarchar(40)` | Có | dài tối đa 40 |
| `SpecimenStatus` | `nvarchar(30)` | Không | dài tối đa 30; mặc định `NotRequired` |
| `Status` | `nvarchar(max)` | Không | — |

- PK: `Id`.
- FK: `DiagnosticOrderId` → `DiagnosticOrders` (`Id`); Delete: `Cascade`.
- FK: `DiagnosticServiceId` → `DiagnosticServices` (`Id`); Delete: `Restrict`.
- FK: `PackageRegistrationId` → `HealthPackageRegistrations` (`Id`); Delete: `Restrict`.
- INDEX: (`DiagnosticServiceId`).
- INDEX: (`PackageRegistrationId`).
- UNIQUE: (`SpecimenBarcode`); bộ lọc `[SpecimenBarcode] IS NOT NULL`.
- UNIQUE: (`DiagnosticOrderId`, `DiagnosticServiceId`).
- Kiểm soát đồng thời: `RowVersion`.

## DiagnosticOrders

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `AppointmentId` | `bigint` | Có | — |
| `CancelledAtUtc` | `datetime2` | Có | — |
| `ClinicalIndication` | `nvarchar(1000)` | Không | dài tối đa 1000 |
| `CompletedAtUtc` | `datetime2` | Có | — |
| `CompletedByUserId` | `uniqueidentifier` | Có | — |
| `FacilityId` | `bigint` | Có | — |
| `Note` | `nvarchar(1000)` | Có | dài tối đa 1000 |
| `OrderCode` | `nvarchar(50)` | Không | dài tối đa 50 |
| `OrderedAtUtc` | `datetime2` | Không | — |
| `OrderingDoctorId` | `bigint` | Không | — |
| `PatientId` | `bigint` | Không | — |
| `PatientVisitId` | `bigint` | Có | — |
| `PerformingDepartmentId` | `bigint` | Có | — |
| `ReturnCount` | `int` | Không | mặc định `0` |
| `ReturnReason` | `nvarchar(500)` | Có | dài tối đa 500 |
| `ReturnedAtUtc` | `datetime2` | Có | — |
| `ReturnedByUserId` | `uniqueidentifier` | Có | — |
| `ReviewedAtUtc` | `datetime2` | Có | — |
| `ReviewedByDoctorId` | `bigint` | Có | — |
| `RowVersion` | `rowversion` | Có | concurrency token |
| `SourceAiActionId` | `uniqueidentifier` | Có | — |
| `StartedAtUtc` | `datetime2` | Có | — |
| `StartedByUserId` | `uniqueidentifier` | Có | — |
| `Status` | `nvarchar(450)` | Không | — |
| `SubmittedForVerificationAtUtc` | `datetime2` | Có | — |
| `SubmittedForVerificationByUserId` | `uniqueidentifier` | Có | — |
| `VerificationNote` | `nvarchar(500)` | Có | dài tối đa 500 |
| `VerificationStatus` | `nvarchar(30)` | Không | dài tối đa 30; mặc định `NotRequired` |
| `VerifiedAtUtc` | `datetime2` | Có | — |
| `VerifiedByUserId` | `uniqueidentifier` | Có | — |

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

## DiagnosticResultAttachments

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `ContentType` | `nvarchar(50)` | Không | dài tối đa 50 |
| `DiagnosticOrderItemId` | `bigint` | Không | — |
| `OriginalFileName` | `nvarchar(200)` | Không | dài tối đa 200 |
| `SizeBytes` | `bigint` | Không | — |
| `StoredFileName` | `nvarchar(64)` | Không | dài tối đa 64 |
| `UploadedAtUtc` | `datetime2` | Không | — |
| `UploadedByUserId` | `uniqueidentifier` | Không | — |

- PK: `Id`.
- FK: `DiagnosticOrderItemId` → `DiagnosticOrderItems` (`Id`); Delete: `Cascade`.
- INDEX: (`DiagnosticOrderItemId`).
- UNIQUE: (`StoredFileName`).

## DiagnosticResults

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `Conclusion` | `nvarchar(2000)` | Có | dài tối đa 2000 |
| `CriticalNotifiedAtUtc` | `datetime2` | Có | — |
| `DiagnosticOrderItemId` | `bigint` | Không | — |
| `HasCriticalValue` | `bit` | Không | mặc định `False` |
| `ReferenceRange` | `nvarchar(200)` | Có | dài tối đa 200 |
| `ResultText` | `nvarchar(4000)` | Không | dài tối đa 4000 |
| `ResultedAtUtc` | `datetime2` | Không | — |
| `ResultedByUserId` | `uniqueidentifier` | Không | — |
| `RowVersion` | `rowversion` | Có | concurrency token |
| `Unit` | `nvarchar(50)` | Có | dài tối đa 50 |

- PK: `Id`.
- FK: `DiagnosticOrderItemId` → `DiagnosticOrderItems` (`Id`); Delete: `Cascade`.
- UNIQUE: (`DiagnosticOrderItemId`).
- Kiểm soát đồng thời: `RowVersion`.

## DiagnosticResultValues

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `CriticalHigh` | `decimal(18,4)` | Có | precision 18, scale 4 |
| `CriticalLow` | `decimal(18,4)` | Có | precision 18, scale 4 |
| `DeltaChange` | `decimal(18,4)` | Có | precision 18, scale 4 |
| `DeltaPercent` | `decimal(9,2)` | Có | precision 9, scale 2 |
| `DiagnosticResultId` | `bigint` | Không | — |
| `DiagnosticServiceParameterId` | `bigint` | Không | — |
| `Flag` | `int` | Không | — |
| `IsDeltaFailure` | `bit` | Không | mặc định `False` |
| `NumericValue` | `decimal(18,4)` | Không | precision 18, scale 4 |
| `ParameterCode` | `nvarchar(30)` | Không | dài tối đa 30 |
| `ParameterName` | `nvarchar(150)` | Không | dài tối đa 150 |
| `PreviousNumericValue` | `decimal(18,4)` | Có | precision 18, scale 4 |
| `PreviousOrderCode` | `nvarchar(50)` | Có | dài tối đa 50 |
| `PreviousResultedAtUtc` | `datetime2` | Có | — |
| `RefHigh` | `decimal(18,4)` | Có | precision 18, scale 4 |
| `RefLow` | `decimal(18,4)` | Có | precision 18, scale 4 |
| `SortOrder` | `int` | Không | — |
| `Unit` | `nvarchar(30)` | Không | dài tối đa 30 |

- PK: `Id`.
- FK: `DiagnosticResultId` → `DiagnosticResults` (`Id`); Delete: `Cascade`.
- FK: `DiagnosticServiceParameterId` → `DiagnosticServiceParameters` (`Id`); Delete: `Restrict`.
- INDEX: (`DiagnosticServiceParameterId`).
- UNIQUE: (`DiagnosticResultId`, `DiagnosticServiceParameterId`).

## DiagnosticServiceParameters

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `Code` | `nvarchar(30)` | Không | dài tối đa 30 |
| `CriticalHigh` | `decimal(18,4)` | Có | precision 18, scale 4 |
| `CriticalLow` | `decimal(18,4)` | Có | precision 18, scale 4 |
| `DeltaAbsoluteLimit` | `decimal(18,4)` | Có | precision 18, scale 4 |
| `DeltaPercentLimit` | `decimal(9,2)` | Có | precision 9, scale 2 |
| `DeltaWindowDays` | `int` | Có | — |
| `DiagnosticServiceId` | `bigint` | Không | — |
| `IsActive` | `bit` | Không | — |
| `Name` | `nvarchar(150)` | Không | dài tối đa 150 |
| `RefHigh` | `decimal(18,4)` | Có | precision 18, scale 4 |
| `RefHighFemale` | `decimal(18,4)` | Có | precision 18, scale 4 |
| `RefLow` | `decimal(18,4)` | Có | precision 18, scale 4 |
| `RefLowFemale` | `decimal(18,4)` | Có | precision 18, scale 4 |
| `SortOrder` | `int` | Không | — |
| `Unit` | `nvarchar(30)` | Không | dài tối đa 30 |

- PK: `Id`.
- FK: `DiagnosticServiceId` → `DiagnosticServices` (`Id`); Delete: `Restrict`.
- UNIQUE: (`DiagnosticServiceId`, `Code`).

## DiagnosticServices

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `Category` | `nvarchar(max)` | Không | — |
| `Code` | `nvarchar(50)` | Không | dài tối đa 50 |
| `ContainerType` | `nvarchar(100)` | Có | dài tối đa 100 |
| `IsActive` | `bit` | Không | mặc định `True` |
| `Name` | `nvarchar(200)` | Không | dài tối đa 200 |
| `PreparationInstructions` | `nvarchar(1000)` | Có | dài tối đa 1000 |
| `Price` | `decimal(18,2)` | Có | precision 18, scale 2 |
| `SpecimenType` | `nvarchar(100)` | Có | dài tối đa 100 |

- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`Code`).

## DoctorLeaveRequests

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `AdminNote` | `nvarchar(max)` | Có | — |
| `DoctorId` | `bigint` | Không | — |
| `EndDateTime` | `datetime2` | Không | — |
| `Reason` | `nvarchar(max)` | Không | — |
| `StartDateTime` | `datetime2` | Không | — |
| `Status` | `nvarchar(max)` | Không | — |

- PK: `Id`.
- FK: `DoctorId` → `Doctors` (`Id`); Delete: `NoAction`.
- INDEX: (`DoctorId`).
- CHECK `CK_DoctorLeaveRequest_DateTimeRange`: `[StartDateTime] < [EndDateTime]`.

## Doctors

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `AcademicTitle` | `nvarchar(max)` | Có | — |
| `Description` | `nvarchar(max)` | Có | — |
| `ExperienceYears` | `int` | Không | — |
| `IsActive` | `bit` | Không | mặc định `True` |
| `UserId` | `uniqueidentifier` | Không | — |

- PK: `Id`.
- FK: `UserId` → `AspNetUsers` (`Id`); Delete: `NoAction`.
- UNIQUE: (`UserId`).
- CHECK `CK_Doctor_ExperienceYears`: `[ExperienceYears] >= 0`.

## DoctorSpecialties

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `DoctorId` | `bigint` | Không | — |
| `SpecialtyId` | `bigint` | Không | — |
| `IsPrimary` | `bit` | Không | mặc định `False` |

- PK: `DoctorId`, `SpecialtyId`.
- FK: `DoctorId` → `Doctors` (`Id`); Delete: `NoAction`.
- FK: `SpecialtyId` → `Specialties` (`Id`); Delete: `NoAction`.
- UNIQUE: (`DoctorId`); bộ lọc `[IsPrimary] = 1`.
- INDEX: (`SpecialtyId`).

## DoctorWorkSchedules

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `DoctorId` | `bigint` | Không | — |
| `EndTime` | `time` | Không | — |
| `IsActive` | `bit` | Không | mặc định `True` |
| `StartTime` | `time` | Không | — |
| `WorkDate` | `date` | Không | — |

- PK: `Id`.
- FK: `DoctorId` → `Doctors` (`Id`); Delete: `NoAction`.
- UNIQUE: (`DoctorId`, `WorkDate`, `StartTime`, `EndTime`).
- CHECK `CK_DoctorWorkSchedule_TimeRange`: `[StartTime] < [EndTime]`.

## DrugAllergyCrossReactions

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `AllergenClassKey` | `nvarchar(50)` | Không | dài tối đa 50 |
| `Note` | `nvarchar(300)` | Không | dài tối đa 300 |
| `RelatedClassKey` | `nvarchar(50)` | Không | dài tối đa 50 |

- PK: `Id`.
- FK: không có FK được khai báo trong model.

## DrugClassMembers

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `ClassKey` | `nvarchar(50)` | Không | dài tối đa 50 |
| `ClassName` | `nvarchar(100)` | Không | dài tối đa 100 |
| `IngredientKey` | `nvarchar(100)` | Không | dài tối đa 100 |

- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`ClassKey`, `IngredientKey`).

## DrugInteractionRules

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `Description` | `nvarchar(500)` | Không | dài tối đa 500 |
| `IngredientKeyA` | `nvarchar(100)` | Không | dài tối đa 100 |
| `IngredientKeyB` | `nvarchar(100)` | Không | dài tối đa 100 |
| `IsActive` | `bit` | Không | — |
| `Recommendation` | `nvarchar(500)` | Không | dài tối đa 500 |
| `Severity` | `int` | Không | — |

- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`IngredientKeyA`, `IngredientKeyB`).
- CHECK `CK_DrugInteractionRules_OrderedPair`: `[IngredientKeyA] < [IngredientKeyB]`.

## EmergencyContacts

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `Address` | `nvarchar(500)` | Có | dài tối đa 500 |
| `FullName` | `nvarchar(200)` | Không | dài tối đa 200 |
| `IsGuardian` | `bit` | Không | mặc định `False` |
| `IsPrimary` | `bit` | Không | — |
| `PatientId` | `bigint` | Không | — |
| `PhoneNumber` | `nvarchar(50)` | Không | dài tối đa 50 |
| `Relationship` | `nvarchar(100)` | Không | dài tối đa 100 |

- PK: `Id`.
- FK: `PatientId` → `Patients` (`Id`); Delete: `Cascade`.
- INDEX: (`PatientId`).

## Facilities

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `Address` | `nvarchar(500)` | Không | dài tối đa 500 |
| `City` | `nvarchar(100)` | Không | dài tối đa 100 |
| `Code` | `nvarchar(50)` | Không | dài tối đa 50 |
| `CreatedAtUtc` | `datetime2` | Không | — |
| `Description` | `nvarchar(max)` | Có | — |
| `Email` | `nvarchar(150)` | Có | dài tối đa 150 |
| `HospitalLevel` | `nvarchar(50)` | Có | dài tối đa 50 |
| `IsActive` | `bit` | Không | mặc định `True` |
| `Name` | `nvarchar(200)` | Không | dài tối đa 200 |
| `Phone` | `nvarchar(50)` | Không | dài tối đa 50 |
| `TaxCode` | `nvarchar(50)` | Có | dài tối đa 50 |
| `UpdatedAtUtc` | `datetime2` | Có | — |

- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`Code`).
- INDEX: (`IsActive`).

## HealthPackageRegistrations

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `AdminNotes` | `nvarchar(1000)` | Có | dài tối đa 1000 |
| `CancellationReason` | `nvarchar(500)` | Có | dài tối đa 500 |
| `ContactPhone` | `nvarchar(20)` | Không | dài tối đa 20 |
| `CreatedAt` | `datetime2` | Không | — |
| `HealthPackageId` | `bigint` | Không | — |
| `Note` | `nvarchar(1000)` | Có | dài tối đa 1000 |
| `PatientId` | `bigint` | Không | — |
| `PreferredDate` | `date` | Không | — |
| `RegistrationCode` | `nvarchar(50)` | Không | dài tối đa 50 |
| `Status` | `int` | Không | — |
| `UpdatedAt` | `datetime2` | Có | — |

- PK: `Id`.
- FK: `HealthPackageId` → `HealthPackages` (`Id`); Delete: `Restrict`.
- FK: `PatientId` → `Patients` (`Id`); Delete: `Restrict`.
- INDEX: (`HealthPackageId`).
- INDEX: (`PatientId`).
- UNIQUE: (`RegistrationCode`).
- INDEX: (`Status`).

## HealthPackages

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `Code` | `nvarchar(50)` | Không | dài tối đa 50 |
| `CreatedAt` | `datetime2` | Không | — |
| `Description` | `nvarchar(max)` | Không | — |
| `ImageUrl` | `nvarchar(500)` | Có | dài tối đa 500 |
| `IncludedServicesJson` | `nvarchar(max)` | Có | — |
| `IsActive` | `bit` | Không | mặc định `True` |
| `Name` | `nvarchar(200)` | Không | dài tối đa 200 |
| `Price` | `decimal(18,2)` | Không | precision 18, scale 2 |
| `TargetAudience` | `nvarchar(100)` | Không | dài tối đa 100 |

- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`Code`).

## Icd10Codes

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Code` | `nvarchar(10)` | Không | dài tối đa 10 |
| `ChapterCode` | `nvarchar(10)` | Không | dài tối đa 10 |
| `IsActive` | `bit` | Không | — |
| `NameVi` | `nvarchar(300)` | Không | dài tối đa 300 |
| `SearchKey` | `nvarchar(400)` | Không | dài tối đa 400 |

- PK: `Code`.
- FK: không có FK được khai báo trong model.

## IdempotencyRecords

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `CreatedAtUtc` | `datetime2` | Không | — |
| `ExpiresAtUtc` | `datetime2` | Không | — |
| `Key` | `nvarchar(100)` | Không | dài tối đa 100 |
| `RequestHash` | `nvarchar(64)` | Không | dài tối đa 64 |
| `ResponseBody` | `nvarchar(max)` | Không | — |
| `Scope` | `nvarchar(50)` | Không | dài tối đa 50 |
| `StatusCode` | `int` | Không | — |
| `UserId` | `uniqueidentifier` | Không | — |

- PK: `Id`.
- FK: không có FK được khai báo trong model.
- INDEX: (`ExpiresAtUtc`).
- UNIQUE: (`Key`, `Scope`).

## InvoiceItems

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `Description` | `nvarchar(500)` | Không | dài tối đa 500 |
| `InvoiceId` | `bigint` | Không | — |
| `IsCancelled` | `bit` | Không | mặc định `False` |
| `ItemCode` | `nvarchar(50)` | Không | dài tối đa 50 |
| `LineTotal` | `decimal(18,2)` | Không | precision 18, scale 2 |
| `Quantity` | `int` | Không | — |
| `ReferenceId` | `bigint` | Không | — |
| `ReferenceType` | `nvarchar(50)` | Không | dài tối đa 50 |
| `UnitPrice` | `decimal(18,2)` | Không | precision 18, scale 2 |

- PK: `Id`.
- FK: `InvoiceId` → `Invoices` (`Id`); Delete: `Cascade`.
- INDEX: (`InvoiceId`).
- UNIQUE: (`ReferenceType`, `ReferenceId`); bộ lọc `[ReferenceType] <> '' AND [ReferenceId] > 0 AND [IsCancelled] = 0`.
- CHECK `CK_InvoiceItems_LineTotal_NonNegative`: `[LineTotal] >= 0`.
- CHECK `CK_InvoiceItems_Quantity_Positive`: `[Quantity] > 0`.
- CHECK `CK_InvoiceItems_UnitPrice_NonNegative`: `[UnitPrice] >= 0`.

## Invoices

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `AppointmentId` | `bigint` | Có | — |
| `CancellationReason` | `nvarchar(500)` | Có | dài tối đa 500 |
| `CancelledAtUtc` | `datetime2` | Có | — |
| `CreatedAtUtc` | `datetime2` | Không | — |
| `CreatedByUserId` | `uniqueidentifier` | Không | — |
| `HealthPackageRegistrationId` | `bigint` | Có | — |
| `InvoiceCode` | `nvarchar(50)` | Không | dài tối đa 50 |
| `PaidAtUtc` | `datetime2` | Có | — |
| `PaidByUserId` | `uniqueidentifier` | Có | — |
| `PatientId` | `bigint` | Không | — |
| `PatientVisitId` | `bigint` | Có | — |
| `RowVersion` | `rowversion` | Không | concurrency token |
| `SourceType` | `int` | Không | — |
| `Status` | `int` | Không | — |
| `Subtotal` | `decimal(18,2)` | Không | precision 18, scale 2 |
| `TotalAmount` | `decimal(18,2)` | Không | precision 18, scale 2 |

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

## MedicineBatches

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `BatchNumber` | `nvarchar(50)` | Không | dài tối đa 50 |
| `CreatedByUserId` | `uniqueidentifier` | Không | — |
| `DisposeReason` | `nvarchar(500)` | Có | dài tối đa 500 |
| `DisposedAt` | `datetime2` | Có | — |
| `ExpiryDate` | `date` | Không | — |
| `MedicineId` | `bigint` | Không | — |
| `QuantityReceived` | `int` | Không | — |
| `QuantityRemaining` | `int` | Không | — |
| `ReceivedAt` | `datetime2` | Không | — |
| `RowVersion` | `rowversion` | Có | concurrency token |
| `UnitCost` | `decimal(18,2)` | Có | precision 18, scale 2 |

- PK: `Id`.
- FK: `MedicineId` → `Medicines` (`Id`); Delete: `Restrict`.
- UNIQUE: (`MedicineId`, `BatchNumber`, `ExpiryDate`).
- CHECK `CK_MedicineBatch_QuantityReceived`: `[QuantityReceived] >= 0`.
- CHECK `CK_MedicineBatch_QuantityRemaining`: `[QuantityRemaining] >= 0`.
- Kiểm soát đồng thời: `RowVersion`.

## MedicineCategories

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `CreatedAt` | `datetime2` | Không | — |
| `Description` | `nvarchar(500)` | Có | dài tối đa 500 |
| `IsActive` | `bit` | Không | mặc định `True` |
| `Name` | `nvarchar(100)` | Không | dài tối đa 100 |
| `SortOrder` | `int` | Không | — |
| `UpdatedAt` | `datetime2` | Có | — |

- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`Name`).

## Medicines

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `ActiveIngredient` | `nvarchar(200)` | Có | dài tối đa 200 |
| `CategoryId` | `bigint` | Có | — |
| `Code` | `nvarchar(50)` | Không | dài tối đa 50 |
| `Contraindications` | `nvarchar(2000)` | Có | dài tối đa 2000 |
| `CreatedAt` | `datetime2` | Không | — |
| `Description` | `nvarchar(1000)` | Có | dài tối đa 1000 |
| `DosageAndAdministration` | `nvarchar(2000)` | Có | dài tối đa 2000 |
| `DosageForm` | `nvarchar(100)` | Có | dài tối đa 100 |
| `ImagePath` | `nvarchar(300)` | Có | dài tối đa 300 |
| `Indications` | `nvarchar(2000)` | Có | dài tối đa 2000 |
| `IsActive` | `bit` | Không | mặc định `True` |
| `IsPrescriptionRequired` | `bit` | Không | mặc định `True` |
| `Manufacturer` | `nvarchar(200)` | Có | dài tối đa 200 |
| `Name` | `nvarchar(200)` | Không | dài tối đa 200 |
| `OverdoseManagement` | `nvarchar(1000)` | Có | dài tối đa 1000 |
| `Precautions` | `nvarchar(2000)` | Có | dài tối đa 2000 |
| `ReorderLevel` | `int` | Không | — |
| `RowVersion` | `rowversion` | Có | concurrency token |
| `SideEffects` | `nvarchar(2000)` | Có | dài tối đa 2000 |
| `StockQuantity` | `int` | Không | — |
| `StorageInstructions` | `nvarchar(500)` | Có | dài tối đa 500 |
| `Strength` | `nvarchar(100)` | Có | dài tối đa 100 |
| `Unit` | `nvarchar(50)` | Không | dài tối đa 50 |
| `UnitPrice` | `decimal(18,2)` | Có | precision 18, scale 2 |
| `UpdatedAt` | `datetime2` | Có | — |

- PK: `Id`.
- FK: `CategoryId` → `MedicineCategories` (`Id`); Delete: `Restrict`.
- INDEX: (`CategoryId`).
- UNIQUE: (`Code`).
- Kiểm soát đồng thời: `RowVersion`.

## MedicineStockTransactions

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `ActorUserId` | `uniqueidentifier` | Không | — |
| `BalanceAfter` | `int` | Không | — |
| `CreatedAt` | `datetime2` | Không | — |
| `MedicineBatchId` | `bigint` | Có | — |
| `MedicineId` | `bigint` | Không | — |
| `PrescriptionId` | `bigint` | Có | — |
| `QuantityChange` | `int` | Không | — |
| `Reason` | `nvarchar(max)` | Có | — |
| `Type` | `int` | Không | — |

- PK: `Id`.
- FK: `MedicineBatchId` → `MedicineBatches` (`Id`); Delete: `Restrict`.
- FK: `MedicineId` → `Medicines` (`Id`); Delete: `Cascade`.
- FK: `PrescriptionId` → `Prescriptions` (`Id`); Delete: `ClientSetNull`.
- INDEX: (`MedicineBatchId`).
- INDEX: (`MedicineId`).
- INDEX: (`PrescriptionId`).

## MrnSequences

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `int` | Không | — |
| `LastSequenceNumber` | `bigint` | Không | — |
| `RowVersion` | `rowversion` | Có | concurrency token |
| `Year` | `int` | Không | — |

- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`Year`).
- Kiểm soát đồng thời: `RowVersion`.

## Notifications

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `CreatedAtUtc` | `datetime2` | Không | — |
| `DedupeKey` | `nvarchar(150)` | Có | dài tối đa 150 |
| `IsRead` | `bit` | Không | mặc định `False` |
| `Message` | `nvarchar(1000)` | Không | dài tối đa 1000 |
| `ReadAtUtc` | `datetime2` | Có | — |
| `RelatedEntityId` | `nvarchar(100)` | Có | dài tối đa 100 |
| `RelatedEntityType` | `nvarchar(100)` | Có | dài tối đa 100 |
| `Route` | `nvarchar(250)` | Có | dài tối đa 250 |
| `Title` | `nvarchar(200)` | Không | dài tối đa 200 |
| `Type` | `int` | Không | — |
| `UserId` | `uniqueidentifier` | Không | — |

- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`DedupeKey`); bộ lọc `[DedupeKey] IS NOT NULL`.
- INDEX: (`UserId`, `IsRead`, `CreatedAtUtc`).

## PatientAllergies

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `AllergenName` | `nvarchar(200)` | Không | dài tối đa 200 |
| `AllergenType` | `nvarchar(50)` | Không | dài tối đa 50 |
| `PatientId` | `bigint` | Không | — |
| `ReactionDescription` | `nvarchar(500)` | Có | dài tối đa 500 |
| `RecordedAtUtc` | `datetime2` | Không | — |
| `Severity` | `nvarchar(50)` | Không | dài tối đa 50 |

- PK: `Id`.
- FK: `PatientId` → `Patients` (`Id`); Delete: `Cascade`.
- INDEX: (`PatientId`).

## Patients

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `Address` | `nvarchar(max)` | Có | — |
| `BhytNumber` | `nvarchar(50)` | Có | dài tối đa 50 |
| `BloodType` | `nvarchar(10)` | Có | dài tối đa 10 |
| `DateOfBirth` | `date` | Có | — |
| `Email` | `nvarchar(150)` | Có | dài tối đa 150 |
| `FullName` | `nvarchar(200)` | Không | dài tối đa 200 |
| `Gender` | `nvarchar(max)` | Có | — |
| `MedicalRecordNumber` | `nvarchar(50)` | Không | dài tối đa 50 |
| `NationalId` | `nvarchar(50)` | Có | dài tối đa 50 |
| `PhoneNumber` | `nvarchar(50)` | Có | dài tối đa 50 |
| `PrimaryFacilityId` | `bigint` | Có | — |
| `RhFactor` | `nvarchar(10)` | Có | dài tối đa 10 |
| `UserId` | `uniqueidentifier` | Có | — |

- PK: `Id`.
- FK: `PrimaryFacilityId` → `Facilities` (`Id`); Delete: `Restrict`.
- FK: `UserId` → `AspNetUsers` (`Id`); Delete: `NoAction`.
- INDEX: (`BhytNumber`).
- UNIQUE: (`MedicalRecordNumber`).
- UNIQUE: (`NationalId`); bộ lọc `[NationalId] IS NOT NULL`.
- INDEX: (`PrimaryFacilityId`).
- UNIQUE: (`UserId`); bộ lọc `[UserId] IS NOT NULL`.

## PatientVisits

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `AppointmentId` | `bigint` | Có | — |
| `ArrivalType` | `nvarchar(max)` | Không | — |
| `AssignedDoctorId` | `bigint` | Có | — |
| `CancellationReason` | `nvarchar(500)` | Có | dài tối đa 500 |
| `CancelledAtUtc` | `datetime2` | Có | — |
| `CheckedInAtUtc` | `datetime2` | Không | — |
| `ChiefComplaint` | `nvarchar(1000)` | Có | dài tối đa 1000 |
| `CompletedAtUtc` | `datetime2` | Có | — |
| `ConsultationStartedAtUtc` | `datetime2` | Có | — |
| `CreatedAtUtc` | `datetime2` | Không | — |
| `CreatedByUserId` | `uniqueidentifier` | Không | — |
| `DepartmentId` | `bigint` | Không | — |
| `FacilityId` | `bigint` | Không | — |
| `HealthPackageRegistrationId` | `bigint` | Có | — |
| `PatientId` | `bigint` | Không | — |
| `Priority` | `nvarchar(max)` | Không | — |
| `QueueNumber` | `int` | Không | — |
| `RoomId` | `bigint` | Có | — |
| `RowVersion` | `rowversion` | Có | concurrency token |
| `Status` | `nvarchar(450)` | Không | — |
| `UpdatedAtUtc` | `datetime2` | Có | — |
| `VisitCode` | `nvarchar(50)` | Không | dài tối đa 50 |
| `VisitDate` | `date` | Không | — |

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

## Payments

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `Amount` | `decimal(18,2)` | Không | precision 18, scale 2 |
| `InvoiceId` | `bigint` | Không | — |
| `Method` | `int` | Không | — |
| `Note` | `nvarchar(500)` | Có | dài tối đa 500 |
| `PaymentCode` | `nvarchar(50)` | Không | dài tối đa 50 |
| `ReceivedAtUtc` | `datetime2` | Không | — |
| `ReceivedByUserId` | `uniqueidentifier` | Không | — |
| `ReferenceCode` | `nvarchar(100)` | Có | dài tối đa 100 |
| `RowVersion` | `rowversion` | Không | concurrency token |
| `Status` | `int` | Không | — |

- PK: `Id`.
- FK: `InvoiceId` → `Invoices` (`Id`); Delete: `Restrict`.
- INDEX: (`InvoiceId`).
- UNIQUE: (`PaymentCode`).
- INDEX: (`ReceivedAtUtc`).
- INDEX: (`Status`).
- CHECK `CK_Payments_Amount_Positive`: `[Amount] > 0`.
- Kiểm soát đồng thời: `RowVersion`.

## PrescriptionItems

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `PrescriptionId` | `bigint` | Không | — |
| `MedicineId` | `bigint` | Không | — |
| `Dosage` | `nvarchar(100)` | Không | dài tối đa 100 |
| `DurationDays` | `int` | Có | — |
| `Frequency` | `nvarchar(100)` | Không | dài tối đa 100 |
| `Instructions` | `nvarchar(max)` | Có | — |
| `Quantity` | `int` | Không | — |

- PK: `PrescriptionId`, `MedicineId`.
- FK: `MedicineId` → `Medicines` (`Id`); Delete: `Cascade`.
- FK: `PrescriptionId` → `Prescriptions` (`Id`); Delete: `Cascade`.
- INDEX: (`MedicineId`).

## Prescriptions

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `AppointmentId` | `bigint` | Có | — |
| `CreatedAt` | `datetime2` | Không | — |
| `DispensedAt` | `datetime2` | Có | — |
| `DispensedByUserId` | `uniqueidentifier` | Có | — |
| `DoctorId` | `bigint` | Không | — |
| `Notes` | `nvarchar(max)` | Có | — |
| `PatientId` | `bigint` | Không | — |
| `PatientVisitId` | `bigint` | Có | — |
| `RowVersion` | `rowversion` | Có | concurrency token |
| `SafetyAcknowledgedAtUtc` | `datetime2` | Có | — |
| `SafetyAcknowledgedByUserId` | `uniqueidentifier` | Có | — |
| `SafetyOverrideReason` | `nvarchar(500)` | Có | dài tối đa 500 |
| `SafetyWarningsSnapshot` | `nvarchar(max)` | Có | — |
| `Status` | `int` | Không | — |

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

## RevisitRequests

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `AppointmentId` | `bigint` | Không | — |
| `DoctorId` | `bigint` | Không | — |
| `NewAppointmentId` | `bigint` | Có | — |
| `Note` | `nvarchar(max)` | Có | — |
| `PatientId` | `bigint` | Không | — |
| `Status` | `nvarchar(max)` | Không | mặc định `PendingPatientResponse` |
| `SuggestedDate` | `date` | Không | — |

- PK: `Id`.
- FK: `AppointmentId` → `Appointments` (`Id`); Delete: `NoAction`.
- FK: `DoctorId` → `Doctors` (`Id`); Delete: `NoAction`.
- FK: `NewAppointmentId` → `Appointments` (`Id`); Delete: `NoAction`.
- FK: `PatientId` → `Patients` (`Id`); Delete: `NoAction`.
- UNIQUE: (`AppointmentId`).
- INDEX: (`DoctorId`).
- UNIQUE: (`NewAppointmentId`); bộ lọc `[NewAppointmentId] IS NOT NULL`.
- INDEX: (`PatientId`).

## Rooms

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `BuildingId` | `bigint` | Có | — |
| `DepartmentId` | `bigint` | Không | — |
| `FloorNumber` | `int` | Không | — |
| `IsActive` | `bit` | Không | — |
| `MaxCapacity` | `int` | Không | — |
| `Name` | `nvarchar(200)` | Không | dài tối đa 200 |
| `RoomNumber` | `nvarchar(50)` | Không | dài tối đa 50 |
| `RoomType` | `nvarchar(50)` | Không | dài tối đa 50 |

- PK: `Id`.
- FK: `BuildingId` → `Buildings` (`Id`); Delete: `Restrict`.
- FK: `DepartmentId` → `Departments` (`Id`); Delete: `Restrict`.
- INDEX: (`BuildingId`).
- UNIQUE: (`DepartmentId`, `RoomNumber`).

## Specialties

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `AiEnabled` | `bit` | Không | mặc định `False` |
| `ConsultationFee` | `decimal(18,2)` | Không | precision 18, scale 2; mặc định `0` |
| `Description` | `nvarchar(max)` | Có | — |
| `IsActive` | `bit` | Không | — |
| `Name` | `nvarchar(max)` | Không | — |
| `SpecialtyCode` | `nvarchar(450)` | Không | — |

- PK: `Id`.
- FK: không có FK được khai báo trong model.
- UNIQUE: (`SpecialtyCode`).
- CHECK `CK_Specialties_ConsultationFee_NonNegative`: `[ConsultationFee] >= 0`.

## StaffFacilityAssignments

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `AssignedAtUtc` | `datetime2` | Không | — |
| `DepartmentId` | `bigint` | Có | — |
| `FacilityId` | `bigint` | Không | — |
| `IsActive` | `bit` | Không | — |
| `IsPrimary` | `bit` | Không | — |
| `Role` | `nvarchar(100)` | Không | dài tối đa 100 |
| `UserId` | `uniqueidentifier` | Không | — |

- PK: `Id`.
- FK: `DepartmentId` → `Departments` (`Id`); Delete: `Restrict`.
- FK: `FacilityId` → `Facilities` (`Id`); Delete: `Restrict`.
- INDEX: (`DepartmentId`).
- INDEX: (`FacilityId`).
- UNIQUE: (`UserId`, `FacilityId`, `Role`).

## SystemAuditLogs

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `Action` | `nvarchar(max)` | Không | — |
| `CreatedAt` | `datetime2` | Không | — |
| `Description` | `nvarchar(max)` | Không | — |
| `EntityId` | `nvarchar(max)` | Không | — |
| `EntityName` | `nvarchar(max)` | Không | — |
| `UserId` | `uniqueidentifier` | Không | — |

- PK: `Id`.
- FK: `UserId` → `AspNetUsers` (`Id`); Delete: `NoAction`.
- INDEX: (`UserId`).

## VisitSummaries

| Cột | Kiểu SQL | Null | Giới hạn / mặc định |
| --- | --- | --- | --- |
| `Id` | `bigint` | Không | — |
| `AppointmentId` | `bigint` | Có | — |
| `ChiefComplaint` | `nvarchar(1000)` | Có | dài tối đa 1000 |
| `ClinicalFindings` | `nvarchar(4000)` | Có | dài tối đa 4000 |
| `CompletedAtUtc` | `datetime2` | Có | — |
| `CreatedAtUtc` | `datetime2` | Không | — |
| `Diagnosis` | `nvarchar(1000)` | Có | dài tối đa 1000 |
| `DiagnosisCode` | `nvarchar(50)` | Có | dài tối đa 50 |
| `DoctorId` | `bigint` | Không | — |
| `FollowUpInstruction` | `nvarchar(1000)` | Có | dài tối đa 1000 |
| `PatientVisitId` | `bigint` | Có | — |
| `RowVersion` | `rowversion` | Có | concurrency token |
| `Summary` | `nvarchar(4000)` | Không | dài tối đa 4000 |
| `TreatmentPlan` | `nvarchar(4000)` | Có | dài tối đa 4000 |
| `UpdatedAtUtc` | `datetime2` | Có | — |

- PK: `Id`.
- FK: `AppointmentId` → `Appointments` (`Id`); Delete: `Restrict`.
- FK: `DoctorId` → `Doctors` (`Id`); Delete: `NoAction`.
- FK: `PatientVisitId` → `PatientVisits` (`Id`); Delete: `Restrict`.
- UNIQUE: (`AppointmentId`); bộ lọc `[AppointmentId] IS NOT NULL`.
- INDEX: (`DoctorId`).
- UNIQUE: (`PatientVisitId`); bộ lọc `[PatientVisitId] IS NOT NULL`.
- Kiểm soát đồng thời: `RowVersion`.
