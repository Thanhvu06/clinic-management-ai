using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq.Expressions;
using System.Reflection;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Pharmacy.Interfaces;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI.Tools;

/// <summary>
/// Read-only dispatchers for professional workspaces. Every query derives
/// actor, role and facility scope from the authenticated server context.
/// </summary>
public sealed class RoleCopilotToolHandler : IAiToolHandler
{
    private const int CatalogDefaultResultLimit = 10;
    private const int CatalogMaxResultLimit = 20;

    private static readonly MethodInfo StringToLowerMethod = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
    private static readonly MethodInfo StringReplaceMethod = typeof(string).GetMethod(nameof(string.Replace), new[] { typeof(string), typeof(string) })!;
    private static readonly MethodInfo LikeMethod = typeof(DbFunctionsExtensions).GetMethod(
        nameof(DbFunctionsExtensions.Like),
        new[] { typeof(DbFunctions), typeof(string), typeof(string) })!;

    private static readonly (string From, string To)[] VietnameseFolding =
    {
        ("á", "a"), ("à", "a"), ("ả", "a"), ("ã", "a"), ("ạ", "a"),
        ("ă", "a"), ("ắ", "a"), ("ằ", "a"), ("ẳ", "a"), ("ẵ", "a"), ("ặ", "a"),
        ("â", "a"), ("ấ", "a"), ("ầ", "a"), ("ẩ", "a"), ("ẫ", "a"), ("ậ", "a"),
        ("é", "e"), ("è", "e"), ("ẻ", "e"), ("ẽ", "e"), ("ẹ", "e"),
        ("ê", "e"), ("ế", "e"), ("ề", "e"), ("ể", "e"), ("ễ", "e"), ("ệ", "e"),
        ("í", "i"), ("ì", "i"), ("ỉ", "i"), ("ĩ", "i"), ("ị", "i"),
        ("ó", "o"), ("ò", "o"), ("ỏ", "o"), ("õ", "o"), ("ọ", "o"),
        ("ô", "o"), ("ố", "o"), ("ồ", "o"), ("ổ", "o"), ("ỗ", "o"), ("ộ", "o"),
        ("ơ", "o"), ("ớ", "o"), ("ờ", "o"), ("ở", "o"), ("ỡ", "o"), ("ợ", "o"),
        ("ú", "u"), ("ù", "u"), ("ủ", "u"), ("ũ", "u"), ("ụ", "u"),
        ("ư", "u"), ("ứ", "u"), ("ừ", "u"), ("ử", "u"), ("ữ", "u"), ("ự", "u"),
        ("ý", "y"), ("ỳ", "y"), ("ỷ", "y"), ("ỹ", "y"), ("ỵ", "y"),
        ("đ", "d")
    };

    private static readonly IReadOnlySet<string> CatalogEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "all",
        "specialty",
        "doctor",
        "diagnostic_service",
        "facility",
        "price"
    };

    private static readonly IReadOnlySet<string> CatalogStopWords = new HashSet<string>(StringComparer.Ordinal)
    {
        "ai", "bao", "bang", "bac", "biet", "cac", "ca", "chi", "cho", "co", "cong", "cua", "cuu", "danh", "dich", "doctor", "duoc", "gia", "gi", "giup",
        "bsi", "dau", "hay", "hien", "hoi", "kham", "ke", "khong", "khoa", "chuyen", "lich", "liet", "mo", "mot", "muc", "nao", "nhieu", "nguoi", "o", "phong", "sach", "si",
        "a", "e", "i", "o", "u", "y", "khai", "so", "tai", "tat", "the", "thong", "tin", "toi", "tra", "va", "ve", "voi", "vu", "xem"
    };

    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IPharmacyService _pharmacy;
    private readonly IAiProviderHealth? _providerHealth;
    private readonly IAiProviderConfigurationInspector? _providerConfiguration;

    public RoleCopilotToolHandler(
        AppDbContext db,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IPharmacyService pharmacy,
        IAiProviderHealth? providerHealth = null,
        IAiProviderConfigurationInspector? providerConfiguration = null)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _pharmacy = pharmacy;
        _providerHealth = providerHealth;
        _providerConfiguration = providerConfiguration;
    }

    public AiToolDefinition Definition { get; } = new() { Name = "role.copilot.dispatch", Version = "1.0" };

    public AiToolArgumentValidationResult ValidateArguments(AiToolInvocation invocation, AiToolExecutionContext context)
    {
        if (invocation.ArgumentsJson.Length > 4000)
            return AiToolArgumentValidationResult.Invalid("INVALID_TOOL_ARGUMENTS", "Tham số công cụ vượt quá giới hạn.");
        try
        {
            using var document = JsonDocument.Parse(invocation.ArgumentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return AiToolArgumentValidationResult.Invalid("INVALID_TOOL_ARGUMENTS", "Tham số phải là JSON object.");
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (IsAuthorityProperty(property.Name))
                    return AiToolArgumentValidationResult.Invalid("FORBIDDEN_TOOL_ARGUMENT", "Phạm vi quyền chỉ do server xác định.");
            }
            if (invocation.ToolName.Equals("clinic.search_knowledge", StringComparison.OrdinalIgnoreCase))
                return ValidateCatalogArguments(document.RootElement);
            if (invocation.ToolName.Equals("reception.lookup_appointment", StringComparison.OrdinalIgnoreCase) &&
                (!document.RootElement.TryGetProperty("appointmentCode", out var code) || code.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(code.GetString())))
                return AiToolArgumentValidationResult.Invalid("MISSING_TOOL_ARGUMENT", "Cần mã lịch hẹn để tra cứu.");
            if (invocation.ToolName.Equals("doctor.get_patient_summary", StringComparison.OrdinalIgnoreCase) && !HasExactlyOneResourceId(document.RootElement, "appointmentId", "visitId"))
                return AiToolArgumentValidationResult.Invalid("MISSING_TOOL_ARGUMENT", "Cần đúng một appointmentId hoặc visitId thuộc ca được phân công.");
            if (invocation.ToolName.Equals("doctor.get_prescription_status", StringComparison.OrdinalIgnoreCase) && !HasExactlyOneResourceId(document.RootElement, "appointmentId", "visitId"))
                return AiToolArgumentValidationResult.Invalid("MISSING_TOOL_ARGUMENT", "Cần đúng một appointmentId hoặc visitId thuộc ca được phân công.");
            if (invocation.ToolName.Equals("doctor.get_diagnostic_orders", StringComparison.OrdinalIgnoreCase) &&
                !HasAtMostOnePositiveResourceId(document.RootElement, "appointmentId", "visitId"))
                return AiToolArgumentValidationResult.Invalid("INVALID_TOOL_ARGUMENTS", "Chỉ hỗ trợ tối đa một appointmentId hoặc visitId hợp lệ cho truy vấn chỉ định.");
            if (invocation.ToolName.Equals("pharmacist.get_prescription_payment_status", StringComparison.OrdinalIgnoreCase))
            {
                if (document.RootElement.EnumerateObject().Any(x => !x.Name.Equals("prescriptionId", StringComparison.OrdinalIgnoreCase)))
                    return AiToolArgumentValidationResult.Invalid("UNKNOWN_TOOL_ARGUMENT", "Công cụ đối chiếu thanh toán chỉ nhận prescriptionId do context hiện tại cung cấp.");
                if (!document.RootElement.TryGetProperty("prescriptionId", out var prescriptionId) ||
                    prescriptionId.ValueKind != JsonValueKind.Number || !prescriptionId.TryGetInt64(out var parsedPrescriptionId) || parsedPrescriptionId <= 0)
                    return AiToolArgumentValidationResult.Invalid("MISSING_TOOL_ARGUMENT", "Cần mở đúng đơn thuốc để đối chiếu thanh toán.");
            }
            return AiToolArgumentValidationResult.Valid();
        }
        catch (JsonException)
        {
            return AiToolArgumentValidationResult.Invalid("INVALID_TOOL_ARGUMENTS", "Tham số công cụ không hợp lệ.");
        }
    }

    public Task<AiToolExecutionResult> ExecuteAsync(AiToolInvocation invocation, AiToolExecutionContext context, CancellationToken cancellationToken = default) =>
        invocation.ToolName.Trim().ToLowerInvariant() switch
        {
            "clinic.search_knowledge" => SearchKnowledgeAsync(invocation.ArgumentsJson, cancellationToken),
            "reception.get_today_appointments" => GetReceptionAppointmentsAsync(context, cancellationToken),
            "reception.get_queue" => GetReceptionQueueAsync(context, cancellationToken),
            "reception.lookup_appointment" => LookupAppointmentAsync(context, invocation.ArgumentsJson, cancellationToken),
            "doctor.get_my_queue" => GetDoctorQueueAsync(context, cancellationToken),
            "doctor.get_patient_summary" => GetDoctorPatientSummaryAsync(context, invocation.ArgumentsJson, cancellationToken),
            "doctor.get_diagnostic_orders" => GetDoctorOrdersAsync(context, invocation.ArgumentsJson, cancellationToken),
            "doctor.get_prescription_status" => GetDoctorPrescriptionStatusAsync(context, invocation.ArgumentsJson, cancellationToken),
            "technician.get_worklist" => GetTechnicianWorklistAsync(context, cancellationToken),
            "pharmacist.get_prescription_queue" => GetPharmacyQueueAsync(context, cancellationToken),
            "pharmacist.get_prescription_payment_status" => GetPrescriptionPaymentStatusAsync(context, invocation.ArgumentsJson, cancellationToken),
            "pharmacist.get_inventory_status" => GetInventoryAsync(context, cancellationToken),
            "admin.get_dashboard_metrics" => GetAdminMetricsAsync(context, cancellationToken),
            "admin.get_ai_health" => GetAiHealthAsync(context, cancellationToken),
            _ => Task.FromResult(AiToolExecutionResult.Failed("UNKNOWN_TOOL", "Công cụ workspace không được hỗ trợ."))
        };

    private async Task<AiToolExecutionResult> SearchKnowledgeAsync(string json, CancellationToken cancellationToken)
    {
        if (!TryReadCatalogRequest(json, out var request, out var error))
            return error!;

        var queryTerms = BuildSearchTerms(request.Query);
        var specialtyTerms = BuildSearchTerms(request.SpecialtyQuery);
        var facilityTerms = BuildSearchTerms(request.FacilityQuery);
        var requestedList = IsExplicitListRequest(request.Query);
        var hasMeaningfulCriteria = queryTerms.Count > 0 || specialtyTerms.Count > 0 || facilityTerms.Count > 0;
        var useBroadListing = requestedList && !hasMeaningfulCriteria;
        if (!requestedList && !hasMeaningfulCriteria)
            return AiToolExecutionResult.Failed("AMBIGUOUS_CATALOG_QUERY", "Vui lòng nêu rõ tên chuyên khoa, bác sĩ, dịch vụ hoặc cơ sở; hoặc yêu cầu một danh sách công khai cụ thể.");

        var hits = new List<ClinicKnowledgeItem>();

        switch (request.Entity)
        {
            case "all":
                hits.AddRange(await SearchSpecialtiesAsync(queryTerms, useBroadListing, cancellationToken));
                hits.AddRange(await SearchDoctorsAsync(queryTerms, specialtyTerms, facilityTerms, useBroadListing, cancellationToken));
                hits.AddRange(await SearchDiagnosticServicesAsync(queryTerms, useBroadListing, cancellationToken));
                hits.AddRange(await SearchFacilitiesAsync(queryTerms, useBroadListing, cancellationToken));
                hits.AddRange(await SearchPublishedPricesAsync(queryTerms, useBroadListing, cancellationToken));
                break;
            case "specialty":
                hits.AddRange(await SearchSpecialtiesAsync(queryTerms, useBroadListing, cancellationToken));
                break;
            case "doctor":
                hits.AddRange(await SearchDoctorsAsync(queryTerms, specialtyTerms, facilityTerms, useBroadListing, cancellationToken));
                break;
            case "diagnostic_service":
                hits.AddRange(await SearchDiagnosticServicesAsync(queryTerms, useBroadListing, cancellationToken));
                break;
            case "facility":
                hits.AddRange(await SearchFacilitiesAsync(queryTerms, useBroadListing, cancellationToken));
                break;
            case "price":
                hits.AddRange(await SearchPublishedPricesAsync(queryTerms, useBroadListing, cancellationToken));
                break;
        }

        var limited = hits
            .GroupBy(x => x.SourceId, StringComparer.Ordinal)
            .Select(x => x.First())
            .OrderBy(x => x.SourceType, StringComparer.Ordinal)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SourceId, StringComparer.Ordinal)
            .Take(request.Limit)
            .ToArray();
        var retrievedAtUtc = new DateTimeOffset(DateTime.SpecifyKind(_clock.UtcNow, DateTimeKind.Utc));
        var matched = limited.Length > 0;
        var sourceType = request.Entity.Equals("all", StringComparison.OrdinalIgnoreCase) ? "clinic_catalog" : request.Entity;

        return new AiToolExecutionResult
        {
            Status = "completed",
            ResultType = "clinic_knowledge",
            DisplayText = matched
                ? requestedList
                    ? $"Danh sách công khai hiện hành gồm {limited.Length} mục phù hợp."
                    : $"Đã tìm thấy {limited.Length} mục phù hợp từ danh mục công khai hiện hành."
                : "Không tìm thấy dữ liệu công khai phù hợp; vui lòng hỏi lễ tân để được kiểm tra thêm.",
            Data = new ClinicKnowledgeEnvelope
            {
                Status = matched ? "matched" : "not_found",
                Mode = requestedList ? "list" : "search",
                SourceType = sourceType,
                Items = limited,
                RetrievedAtUtc = retrievedAtUtc
            },
            DataSources = new[] { new AiToolDataSource("clinic_public_catalog", "approved_database") },
            RetrievedAtUtc = retrievedAtUtc
        };
    }

    private async Task<IReadOnlyList<ClinicKnowledgeItem>> SearchSpecialtiesAsync(
        IReadOnlySet<string> terms,
        bool broadRequest,
        CancellationToken cancellationToken)
    {
        var query = _db.Specialties.AsNoTracking()
            .Where(x => x.IsActive);
        if (!broadRequest)
            query = WhereCatalogMatch(query, terms, x => x.Name, x => x.SpecialtyCode, x => x.Description);

        var candidates = await query
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Take(CatalogMaxResultLimit)
            .Select(x => new { x.Id, x.SpecialtyCode, x.Name, x.Description, x.ConsultationFee })
            .ToListAsync(cancellationToken);

        return candidates
            .Select(x => new ClinicKnowledgeItem
            {
                SourceType = "specialty",
                SourceId = $"specialty:{x.Id}",
                Title = x.Name,
                Description = x.Description,
                PublishedPrice = x.ConsultationFee > 0 ? x.ConsultationFee : null,
                PriceType = x.ConsultationFee > 0 ? "consultation_fee" : null,
                Currency = x.ConsultationFee > 0 ? "VND" : null,
                Details = new Dictionary<string, string?> { ["specialtyCode"] = x.SpecialtyCode }
            })
            .ToArray();
    }

    private async Task<IReadOnlyList<ClinicKnowledgeItem>> SearchDoctorsAsync(
        IReadOnlySet<string> queryTerms,
        IReadOnlySet<string> specialtyTerms,
        IReadOnlySet<string> facilityTerms,
        bool broadRequest,
        CancellationToken cancellationToken)
    {
        var query = from d in _db.Doctors.AsNoTracking()
                    join u in _db.Users.AsNoTracking() on d.UserId equals u.Id
                    join ds in _db.DoctorSpecialties.AsNoTracking() on d.Id equals ds.DoctorId
                    join s in _db.Specialties.AsNoTracking() on ds.SpecialtyId equals s.Id
                    join assignment in _db.StaffFacilityAssignments.AsNoTracking() on d.UserId equals assignment.UserId
                    join facility in _db.Facilities.AsNoTracking() on assignment.FacilityId equals facility.Id
                    where d.IsActive && u.IsActive && s.IsActive && assignment.IsActive &&
                          assignment.Role == nameof(AiActorRole.Doctor) && facility.IsActive
                    select new
                    {
                        DoctorId = d.Id,
                        u.FullName,
                        d.AcademicTitle,
                        d.ExperienceYears,
                        d.Description,
                        SpecialtyName = s.Name,
                        SpecialtyCode = s.SpecialtyCode,
                        SpecialtyDescription = s.Description,
                        FacilityCode = facility.Code,
                        FacilityName = facility.Name,
                        FacilityAddress = facility.Address,
                        FacilityCity = facility.City
                    };

        if (!broadRequest && queryTerms.Count > 0)
            query = WhereCatalogMatch(query, queryTerms, x => x.FullName, x => x.AcademicTitle, x => x.Description, x => x.SpecialtyName, x => x.SpecialtyCode, x => x.FacilityCode, x => x.FacilityName, x => x.FacilityAddress, x => x.FacilityCity);
        if (specialtyTerms.Count > 0)
            query = WhereCatalogMatch(query, specialtyTerms, x => x.SpecialtyName, x => x.SpecialtyCode, x => x.SpecialtyDescription);
        if (facilityTerms.Count > 0)
            query = WhereCatalogMatch(query, facilityTerms, x => x.FacilityCode, x => x.FacilityName, x => x.FacilityAddress, x => x.FacilityCity);

        var doctorIds = await query
            .Select(x => x.DoctorId)
            .Distinct()
            .OrderBy(x => x)
            .Take(CatalogMaxResultLimit)
            .ToListAsync(cancellationToken);
        if (doctorIds.Count == 0)
            return Array.Empty<ClinicKnowledgeItem>();

        var candidates = await query
            .Where(x => doctorIds.Contains(x.DoctorId))
            .OrderBy(x => x.DoctorId).ThenBy(x => x.FacilityCode).ThenBy(x => x.SpecialtyCode)
            .ToListAsync(cancellationToken);

        return candidates
            .GroupBy(x => x.DoctorId)
            .Select(group =>
            {
                var first = group.First();
                return new { first, group };
            })
            .Select(x => new ClinicKnowledgeItem
            {
                SourceType = "doctor",
                SourceId = $"doctor:{x.first.DoctorId}",
                Title = x.first.FullName,
                Description = x.first.Description,
                Details = new Dictionary<string, string?>
                {
                    ["academicTitle"] = x.first.AcademicTitle,
                    ["experienceYears"] = x.first.ExperienceYears.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["specialties"] = string.Join(", ", x.group.Select(y => y.SpecialtyName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(y => y)),
                    ["facilities"] = string.Join("; ", x.group.Select(y => $"{y.FacilityName} — {y.FacilityAddress}, {y.FacilityCity}").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(y => y))
                }
            })
            .ToArray();
    }

    private async Task<IReadOnlyList<ClinicKnowledgeItem>> SearchDiagnosticServicesAsync(
        IReadOnlySet<string> terms,
        bool broadRequest,
        CancellationToken cancellationToken)
    {
        var query = _db.DiagnosticServices.AsNoTracking()
            .Where(x => x.IsActive);
        if (!broadRequest)
            query = WhereCatalogMatch(query, terms, x => x.Name, x => x.Code, x => x.PreparationInstructions);

        var candidates = await query
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Take(CatalogMaxResultLimit)
            .Select(x => new { x.Id, x.Code, x.Name, x.Category, x.PreparationInstructions, x.Price })
            .ToListAsync(cancellationToken);

        return candidates
            .Select(x => new ClinicKnowledgeItem
            {
                SourceType = "diagnostic_service",
                SourceId = $"diagnostic_service:{x.Id}",
                Title = x.Name,
                Description = x.PreparationInstructions,
                PublishedPrice = x.Price,
                PriceType = x.Price.HasValue && x.Price.Value > 0 ? "service_price" : null,
                Currency = x.Price.HasValue && x.Price.Value > 0 ? "VND" : null,
                Details = new Dictionary<string, string?> { ["code"] = x.Code, ["category"] = x.Category.ToString() }
            })
            .ToArray();
    }

    private async Task<IReadOnlyList<ClinicKnowledgeItem>> SearchFacilitiesAsync(
        IReadOnlySet<string> terms,
        bool broadRequest,
        CancellationToken cancellationToken)
    {
        var facilityQuery = _db.Facilities.AsNoTracking()
            .Where(x => x.IsActive);
        if (!broadRequest)
            facilityQuery = WhereCatalogMatch(facilityQuery, terms, x => x.Code, x => x.Name, x => x.Address, x => x.City, x => x.Description);

        var facilities = await facilityQuery
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Take(CatalogMaxResultLimit)
            .Select(x => new { x.Id, x.Code, x.Name, x.Address, x.City, x.Phone, x.Description })
            .ToListAsync(cancellationToken);

        var locationQuery = _db.ClinicLocations.AsNoTracking()
            .Where(x => x.IsActive);
        if (!broadRequest)
            locationQuery = WhereCatalogMatch(locationQuery, terms, x => x.Code, x => x.Name, x => x.Address, x => x.City, x => x.Description, x => x.OpeningHours);

        var locations = await locationQuery
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Take(CatalogMaxResultLimit)
            .Select(x => new { x.Id, x.Code, x.Name, x.Address, x.City, x.Phone, x.Description, x.OpeningHours })
            .ToListAsync(cancellationToken);

        var facilityHits = facilities
            .Select(x => new ClinicKnowledgeItem
            {
                SourceType = "facility",
                SourceId = $"facility:{x.Id}",
                Title = x.Name,
                Description = x.Description,
                Address = x.Address,
                Phone = x.Phone,
                Details = new Dictionary<string, string?> { ["code"] = x.Code, ["city"] = x.City }
            });
        var locationHits = locations
            .Select(x => new ClinicKnowledgeItem
            {
                SourceType = "clinic_location",
                SourceId = $"clinic_location:{x.Id}",
                Title = x.Name,
                Description = x.Description,
                Address = x.Address,
                Phone = x.Phone,
                OpeningHours = x.OpeningHours,
                Details = new Dictionary<string, string?> { ["code"] = x.Code, ["city"] = x.City }
            });

        return facilityHits.Concat(locationHits).ToArray();
    }

    private async Task<IReadOnlyList<ClinicKnowledgeItem>> SearchPublishedPricesAsync(
        IReadOnlySet<string> terms,
        bool broadRequest,
        CancellationToken cancellationToken)
    {
        var specialtyQuery = _db.Specialties.AsNoTracking()
            .Where(x => x.IsActive && x.ConsultationFee > 0);
        if (!broadRequest)
            specialtyQuery = WhereCatalogMatch(specialtyQuery, terms, x => x.Name, x => x.SpecialtyCode, x => x.Description);

        var specialties = await specialtyQuery
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Take(CatalogMaxResultLimit)
            .Select(x => new { x.Id, x.Name, x.Description, x.SpecialtyCode, x.ConsultationFee })
            .ToListAsync(cancellationToken);

        var diagnosticQuery = _db.DiagnosticServices.AsNoTracking()
            .Where(x => x.IsActive && x.Price.HasValue && x.Price.Value > 0);
        if (!broadRequest)
            diagnosticQuery = WhereCatalogMatch(diagnosticQuery, terms, x => x.Name, x => x.Code, x => x.PreparationInstructions);

        var diagnostics = await diagnosticQuery
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Take(CatalogMaxResultLimit)
            .Select(x => new { x.Id, x.Name, x.PreparationInstructions, x.Code, Price = x.Price!.Value, x.Category })
            .ToListAsync(cancellationToken);

        var packageQuery = _db.HealthPackages.AsNoTracking()
            .Where(x => x.IsActive && x.Price > 0);
        if (!broadRequest)
            packageQuery = WhereCatalogMatch(packageQuery, terms, x => x.Name, x => x.Code, x => x.Description);

        var packages = await packageQuery
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Take(CatalogMaxResultLimit)
            .Select(x => new { x.Id, x.Name, x.Description, x.Code, x.Price })
            .ToListAsync(cancellationToken);

        var specialtyHits = specialties
            .Select(x => new ClinicKnowledgeItem
            {
                SourceType = "published_price",
                SourceId = $"specialty-price:{x.Id}",
                Title = x.Name,
                Description = x.Description,
                PublishedPrice = x.ConsultationFee,
                PriceType = "consultation_fee",
                Currency = "VND",
                Details = new Dictionary<string, string?> { ["sourceEntity"] = "specialty", ["specialtyCode"] = x.SpecialtyCode }
            });
        var diagnosticHits = diagnostics
            .Select(x => new ClinicKnowledgeItem
            {
                SourceType = "published_price",
                SourceId = $"diagnostic-price:{x.Id}",
                Title = x.Name,
                Description = x.PreparationInstructions,
                PublishedPrice = x.Price,
                PriceType = "service_price",
                Currency = "VND",
                Details = new Dictionary<string, string?> { ["sourceEntity"] = "diagnostic_service", ["code"] = x.Code, ["category"] = x.Category.ToString() }
            });
        var packageHits = packages
            .Select(x => new ClinicKnowledgeItem
            {
                SourceType = "published_price",
                SourceId = $"health-package-price:{x.Id}",
                Title = x.Name,
                Description = x.Description,
                PublishedPrice = x.Price,
                PriceType = "health_package_price",
                Currency = "VND",
                Details = new Dictionary<string, string?> { ["sourceEntity"] = "health_package", ["code"] = x.Code }
            });

        return specialtyHits.Concat(diagnosticHits).Concat(packageHits).ToArray();
    }

    private static AiToolArgumentValidationResult ValidateCatalogArguments(JsonElement root)
    {
        var allowed = new HashSet<string>(new[] { "entity", "query", "specialtyQuery", "facilityQuery", "limit" }, StringComparer.OrdinalIgnoreCase);
        foreach (var property in root.EnumerateObject())
        {
            if (IsAuthorityProperty(property.Name))
                return AiToolArgumentValidationResult.Invalid("FORBIDDEN_TOOL_ARGUMENT", "Phạm vi quyền chỉ do server xác định.");
            if (!allowed.Contains(property.Name))
                return AiToolArgumentValidationResult.Invalid("UNKNOWN_TOOL_ARGUMENT", $"Tham số '{property.Name}' không được phép cho danh mục công khai.");
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                return AiToolArgumentValidationResult.Invalid("INVALID_TOOL_ARGUMENTS", "Tham số danh mục không được chứa object hoặc array lồng nhau.");
        }

        if (!root.TryGetProperty("query", out var query) || query.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(query.GetString()))
            return AiToolArgumentValidationResult.Invalid("MISSING_TOOL_ARGUMENT", "Cần nội dung cần tra cứu trong danh mục công khai.");
        if ((query.GetString()?.Trim().Length ?? 0) > 160)
            return AiToolArgumentValidationResult.Invalid("INVALID_QUERY", "Query danh mục tối đa 160 ký tự.");

        var entityName = "all";
        if (root.TryGetProperty("entity", out var entity))
        {
            entityName = entity.ValueKind == JsonValueKind.String ? entity.GetString()?.Trim().ToLowerInvariant() ?? string.Empty : string.Empty;
            if (!CatalogEntities.Contains(entityName))
                return AiToolArgumentValidationResult.Invalid("INVALID_ENTITY", "Loại danh mục không được hỗ trợ.");
        }

        var hasSpecialtyFilter = false;
        var hasFacilityFilter = false;
        foreach (var name in new[] { "specialtyQuery", "facilityQuery" })
        {
            if (!root.TryGetProperty(name, out var filter))
                continue;
            if (filter.ValueKind != JsonValueKind.String || (filter.GetString()?.Trim().Length ?? 0) > 120)
                return AiToolArgumentValidationResult.Invalid("INVALID_FILTER", "Bộ lọc danh mục không hợp lệ.");
            if (name.Equals("specialtyQuery", StringComparison.OrdinalIgnoreCase))
                hasSpecialtyFilter = !string.IsNullOrWhiteSpace(filter.GetString());
            else
                hasFacilityFilter = !string.IsNullOrWhiteSpace(filter.GetString());
        }

        if ((hasSpecialtyFilter || hasFacilityFilter) && entityName is not "doctor")
            return AiToolArgumentValidationResult.Invalid("INVALID_FILTER_ENTITY", "Bộ lọc chuyên khoa/cơ sở chỉ áp dụng khi entity=doctor; entity=all không được trộn bộ lọc quan hệ bác sĩ với các nguồn khác.");

        if (root.TryGetProperty("limit", out var limit) && (limit.ValueKind != JsonValueKind.Number || !limit.TryGetInt32(out var parsedLimit) || parsedLimit is < 1 or > CatalogMaxResultLimit))
            return AiToolArgumentValidationResult.Invalid("INVALID_LIMIT", $"Giới hạn danh mục phải từ 1 đến {CatalogMaxResultLimit}.");

        return AiToolArgumentValidationResult.Valid();
    }

    private static bool TryReadCatalogRequest(string json, out CatalogSearchRequest request, out AiToolExecutionResult? error)
    {
        request = new CatalogSearchRequest("all", string.Empty, null, null, CatalogDefaultResultLimit);
        error = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var query = root.TryGetProperty("query", out var queryValue) && queryValue.ValueKind == JsonValueKind.String
                ? queryValue.GetString()?.Trim()
                : null;
            if (string.IsNullOrWhiteSpace(query))
            {
                error = AiToolExecutionResult.Failed("MISSING_TOOL_ARGUMENT", "Cần nội dung cần tra cứu trong danh mục công khai.");
                return false;
            }

            var entity = root.TryGetProperty("entity", out var entityValue) && entityValue.ValueKind == JsonValueKind.String
                ? entityValue.GetString()?.Trim().ToLowerInvariant()
                : "all";
            var specialtyQuery = root.TryGetProperty("specialtyQuery", out var specialtyValue) && specialtyValue.ValueKind == JsonValueKind.String ? specialtyValue.GetString()?.Trim() : null;
            var facilityQuery = root.TryGetProperty("facilityQuery", out var facilityValue) && facilityValue.ValueKind == JsonValueKind.String ? facilityValue.GetString()?.Trim() : null;
            var limit = root.TryGetProperty("limit", out var limitValue) && limitValue.ValueKind == JsonValueKind.Number && limitValue.TryGetInt32(out var parsedLimit) ? Math.Clamp(parsedLimit, 1, CatalogMaxResultLimit) : CatalogDefaultResultLimit;
            request = new CatalogSearchRequest(entity!, query, specialtyQuery, facilityQuery, limit);
            return true;
        }
        catch (JsonException)
        {
            error = AiToolExecutionResult.Failed("INVALID_TOOL_ARGUMENTS", "Tham số danh mục không hợp lệ.");
            return false;
        }
    }

    private static IReadOnlySet<string> BuildSearchTerms(params string?[] values)
    {
        var normalized = AiTextNormalizer.NormalizeForComparison(string.Join(" ", values.Where(x => !string.IsNullOrWhiteSpace(x))));
        var terms = Regex.Matches(normalized, @"[a-z0-9]+", RegexOptions.CultureInvariant)
            .Select(x => x.Value)
            .Where(x => !CatalogStopWords.Contains(x))
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        return terms;
    }

    private static bool IsExplicitListRequest(string query)
    {
        var normalized = AiTextNormalizer.NormalizeForComparison(query);
        if (Regex.IsMatch(normalized, @"\b(?:danh sach|liet ke|tat ca|danh muc|tong hop)\b", RegexOptions.CultureInvariant))
            return true;
        if (Regex.IsMatch(normalized, @"\b(?:bang gia|muc gia)\b", RegexOptions.CultureInvariant))
            return true;
        return Regex.IsMatch(normalized, @"\bcac\s+(?:co so|bac si|dich vu|chuyen khoa)\b", RegexOptions.CultureInvariant);
    }

    private static IQueryable<T> WhereCatalogMatch<T>(
        IQueryable<T> source,
        IReadOnlySet<string> terms,
        params Expression<Func<T, string?>>[] selectors)
    {
        if (terms.Count == 0)
            return source.Where(_ => false);

        var parameter = Expression.Parameter(typeof(T), "catalogRow");
        Expression? allTerms = null;
        foreach (var term in terms)
        {
            Expression? anyField = null;
            foreach (var selector in selectors)
            {
                var field = new ReplaceParameterVisitor(selector.Parameters[0], parameter).Visit(selector.Body)!;
                var normalizedField = NormalizeCatalogField(field);
                var like = Expression.Call(
                    LikeMethod,
                    Expression.Property(null, typeof(EF), nameof(EF.Functions)),
                    normalizedField,
                    Expression.Constant($"%{term}%"));
                anyField = anyField is null ? like : Expression.OrElse(anyField, like);
            }

            allTerms = allTerms is null ? anyField : Expression.AndAlso(allTerms, anyField!);
        }

        return source.Where(Expression.Lambda<Func<T, bool>>(allTerms!, parameter));
    }

    private static Expression NormalizeCatalogField(Expression field)
    {
        var normalized = Expression.Call(field, StringToLowerMethod);
        foreach (var (from, to) in VietnameseFolding)
        {
            normalized = Expression.Call(
                normalized,
                StringReplaceMethod,
                Expression.Constant(from),
                Expression.Constant(to));
        }

        return normalized;
    }

    private static bool IsAuthorityProperty(string propertyName) =>
        propertyName.Equals("userId", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Equals("actorId", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Equals("role", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Equals("facilityId", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Equals("facilityAuthorization", StringComparison.OrdinalIgnoreCase);

    private sealed record CatalogSearchRequest(string Entity, string Query, string? SpecialtyQuery, string? FacilityQuery, int Limit);

    private sealed class ClinicKnowledgeEnvelope
    {
        public string Status { get; init; } = "not_found";
        public string Mode { get; init; } = "search";
        public string SourceType { get; init; } = "clinic_catalog";
        public IReadOnlyList<ClinicKnowledgeItem> Items { get; init; } = Array.Empty<ClinicKnowledgeItem>();
        public DateTimeOffset RetrievedAtUtc { get; init; }
    }

    private sealed class ClinicKnowledgeItem
    {
        public string SourceType { get; init; } = string.Empty;
        public string SourceId { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string? Description { get; init; }
        public decimal? PublishedPrice { get; init; }
        public string? PriceType { get; init; }
        public string? Currency { get; init; }
        public string? Address { get; init; }
        public string? Phone { get; init; }
        public string? OpeningHours { get; init; }
        public IReadOnlyDictionary<string, string?> Details { get; init; } = new Dictionary<string, string?>();
    }

    private sealed class ReplaceParameterVisitor : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly ParameterExpression _to;

        public ReplaceParameterVisitor(ParameterExpression from, ParameterExpression to)
        {
            _from = from;
            _to = to;
        }

        protected override Expression VisitParameter(ParameterExpression node) =>
            node == _from ? _to : base.VisitParameter(node);
    }

    private async Task<HashSet<long>> ResolveFacilityScopeAsync(AiToolExecutionContext context, string role, CancellationToken cancellationToken)
    {
        if (!context.ActorId.HasValue || context.ActorId == Guid.Empty) return new();
        var query = _db.StaffFacilityAssignments.AsNoTracking()
            .Where(x => x.UserId == context.ActorId.Value && x.IsActive && x.Role == role);
        if (context.FacilityId.HasValue)
            query = query.Where(x => x.FacilityId == context.FacilityId.Value);
        return (await query.Select(x => x.FacilityId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
    }

    private async Task<AiToolExecutionResult> GetReceptionAppointmentsAsync(AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Receptionist), cancellationToken);
        if (facilities.Count == 0) return ScopeDenied();
        var today = _clock.VietnamToday;
        var appointments = await _db.Appointments.AsNoTracking()
            .Where(a => a.AppointmentDate == today && a.FacilityId.HasValue && facilities.Contains(a.FacilityId.Value))
            .OrderBy(a => a.StartTime).Take(100)
            .Select(a => new { a.Id, a.AppointmentCode, a.AppointmentDate, a.StartTime, a.EndTime, status = a.Status.ToString(), patientName = a.Patient.FullName, doctorName = _db.Users.Where(u => u.Id == a.Doctor.UserId).Select(u => u.FullName).FirstOrDefault() ?? "Bác sĩ", specialtyId = a.SpecialtyId })
            .ToListAsync(cancellationToken);
        return Completed(appointments, "reception_appointments", $"Có {appointments.Count} lịch hẹn trong ngày hôm nay.");
    }

    private async Task<AiToolExecutionResult> GetReceptionQueueAsync(AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Receptionist), cancellationToken);
        if (facilities.Count == 0) return ScopeDenied();
        var queue = await _db.PatientVisits.AsNoTracking()
            .Where(v => facilities.Contains(v.FacilityId) && v.VisitDate == _clock.VietnamToday && v.Status != VisitStatus.Cancelled && v.Status != VisitStatus.Completed)
            .OrderBy(v => v.QueueNumber).Take(100)
            .Select(v => new { v.Id, v.VisitCode, v.QueueNumber, status = v.Status.ToString(), patientName = v.Patient.FullName, v.AssignedDoctorId, v.DepartmentId })
            .ToListAsync(cancellationToken);
        return Completed(queue, "reception_queue", $"Hàng đợi hiện có {queue.Count} lượt.");
    }

    private async Task<AiToolExecutionResult> LookupAppointmentAsync(AiToolExecutionContext context, string json, CancellationToken cancellationToken)
    {
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Receptionist), cancellationToken);
        if (facilities.Count == 0) return ScopeDenied();
        using var document = JsonDocument.Parse(json);
        var code = document.RootElement.GetProperty("appointmentCode").GetString()!.Trim();
        var appointment = await _db.Appointments.AsNoTracking()
            .Where(a => a.AppointmentCode == code && a.FacilityId.HasValue && facilities.Contains(a.FacilityId.Value))
            .Select(a => new { a.Id, a.AppointmentCode, a.AppointmentDate, a.StartTime, a.EndTime, status = a.Status.ToString(), patientName = a.Patient.FullName })
            .SingleOrDefaultAsync(cancellationToken);
        return appointment is null ? AiToolExecutionResult.Failed("NOT_FOUND", "Không tìm thấy lịch hẹn trong phạm vi cơ sở được phân quyền.") : Completed(appointment, "appointment_lookup", "Đã tra cứu lịch hẹn từ hệ thống.");
    }

    private async Task<AiToolExecutionResult> GetDoctorQueueAsync(AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Doctor), cancellationToken);
        if (facilities.Count == 0) return ScopeDenied();
        var doctorId = await _db.Doctors.AsNoTracking().Where(d => d.UserId == context.ActorId).Select(d => (long?)d.Id).SingleOrDefaultAsync(cancellationToken);
        if (!doctorId.HasValue) return ScopeDenied();
        var items = await _db.PatientVisits.AsNoTracking()
            .Where(v => v.AssignedDoctorId == doctorId && facilities.Contains(v.FacilityId) && v.VisitDate == _clock.VietnamToday && v.Status != VisitStatus.Cancelled && v.Status != VisitStatus.Completed)
            .OrderBy(v => v.QueueNumber).Take(100)
            .Select(v => new { v.Id, v.VisitCode, v.QueueNumber, status = v.Status.ToString(), patientName = v.Patient.FullName, v.ChiefComplaint, v.AppointmentId })
            .ToListAsync(cancellationToken);
        return Completed(items, "doctor_queue", $"Hàng đợi của bạn có {items.Count} lượt.");
    }

    private async Task<AiToolExecutionResult> GetDoctorPatientSummaryAsync(AiToolExecutionContext context, string json, CancellationToken cancellationToken)
    {
        if (!TryGetExactlyOneLong(json, "appointmentId", "visitId", out var appointmentId, out var visitId))
            return AiToolExecutionResult.Failed("MISSING_TOOL_ARGUMENT", "Cần đúng một appointmentId hoặc visitId hợp lệ.");
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Doctor), cancellationToken);
        if (facilities.Count == 0) return ScopeDenied();
        if (visitId.HasValue)
        {
            var visit = await _db.PatientVisits.AsNoTracking()
                .Where(v => v.Id == visitId.Value && v.FacilityId > 0 && facilities.Contains(v.FacilityId) && v.AssignedDoctor != null && v.AssignedDoctor.UserId == context.ActorId)
                .Select(v => new
                {
                    caseType = v.AppointmentId.HasValue ? "scheduled_visit" : "walk_in_visit",
                    v.Id, v.VisitCode, v.AppointmentId, v.VisitDate, arrivalType = v.ArrivalType.ToString(), status = v.Status.ToString(),
                    patientName = v.Patient.FullName, v.ChiefComplaint,
                    summary = v.VisitSummary == null ? null : new { v.VisitSummary.Summary, v.VisitSummary.ClinicalFindings, v.VisitSummary.Diagnosis, v.VisitSummary.TreatmentPlan, v.VisitSummary.FollowUpInstruction, v.VisitSummary.CompletedAtUtc },
                    vitals = v.VitalSigns == null ? null : new { v.VitalSigns.Temperature, v.VitalSigns.BloodPressureSystolic, v.VitalSigns.BloodPressureDiastolic, v.VitalSigns.HeartRate, v.VitalSigns.RespiratoryRate, v.VitalSigns.SpO2, v.VitalSigns.Weight, v.VitalSigns.Height, v.VitalSigns.Bmi, v.VitalSigns.RecordedAtUtc }
                }).SingleOrDefaultAsync(cancellationToken);
            return visit is null ? AiToolExecutionResult.Failed("NOT_FOUND", "Ca khám không thuộc bác sĩ hiện tại.") : Completed(visit, "doctor_patient_summary", "Tóm tắt ca khám, triệu chứng và sinh hiệu được đọc từ hồ sơ được phân công.");
        }

        var appointment = await _db.Appointments.AsNoTracking()
            .Where(a => a.Id == appointmentId!.Value && a.Doctor.UserId == context.ActorId && a.FacilityId.HasValue && facilities.Contains(a.FacilityId.Value))
            .Select(a => new
            {
                caseType = "appointment",
                a.Id, a.AppointmentCode, a.AppointmentDate, a.Status, patientName = a.Patient.FullName,
                a.Reason, a.SpecialtyId, patientVisitId = a.PatientVisit == null ? (long?)null : a.PatientVisit.Id,
                patientVisitAssignedDoctorUserId = a.PatientVisit == null || a.PatientVisit.AssignedDoctor == null ? (Guid?)null : a.PatientVisit.AssignedDoctor.UserId,
                summary = a.VisitSummary == null ? (a.PatientVisit == null || a.PatientVisit.VisitSummary == null ? null : new { a.PatientVisit.VisitSummary.Summary, a.PatientVisit.VisitSummary.ClinicalFindings, a.PatientVisit.VisitSummary.Diagnosis, a.PatientVisit.VisitSummary.TreatmentPlan, a.PatientVisit.VisitSummary.FollowUpInstruction, a.PatientVisit.VisitSummary.CompletedAtUtc }) : new { a.VisitSummary.Summary, a.VisitSummary.ClinicalFindings, a.VisitSummary.Diagnosis, a.VisitSummary.TreatmentPlan, a.VisitSummary.FollowUpInstruction, a.VisitSummary.CompletedAtUtc },
                vitals = a.VitalSigns == null ? (a.PatientVisit == null || a.PatientVisit.VitalSigns == null ? null : new { a.PatientVisit.VitalSigns.Temperature, a.PatientVisit.VitalSigns.BloodPressureSystolic, a.PatientVisit.VitalSigns.BloodPressureDiastolic, a.PatientVisit.VitalSigns.HeartRate, a.PatientVisit.VitalSigns.RespiratoryRate, a.PatientVisit.VitalSigns.SpO2, a.PatientVisit.VitalSigns.Weight, a.PatientVisit.VitalSigns.Height, a.PatientVisit.VitalSigns.Bmi, a.PatientVisit.VitalSigns.RecordedAtUtc }) : new { a.VitalSigns.Temperature, a.VitalSigns.BloodPressureSystolic, a.VitalSigns.BloodPressureDiastolic, a.VitalSigns.HeartRate, a.VitalSigns.RespiratoryRate, a.VitalSigns.SpO2, a.VitalSigns.Weight, a.VitalSigns.Height, a.VitalSigns.Bmi, a.VitalSigns.RecordedAtUtc }
            }).SingleOrDefaultAsync(cancellationToken);
        if (appointment is null)
            return AiToolExecutionResult.Failed("NOT_FOUND", "Ca khám không thuộc bác sĩ hiện tại.");

        // An appointment can remain historically owned by doctor A after its visit
        // is reassigned to doctor B. Appointment metadata remains readable by A,
        // but visit clinical data must follow the current visit assignment.
        var canReadLinkedVisitClinical = !appointment.patientVisitId.HasValue || appointment.patientVisitAssignedDoctorUserId == context.ActorId;
        var projection = new
        {
            appointment.caseType,
            appointment.Id,
            appointment.AppointmentCode,
            appointment.AppointmentDate,
            appointment.Status,
            appointment.patientName,
            appointment.Reason,
            appointment.SpecialtyId,
            appointment.patientVisitId,
            clinicalDataStatus = canReadLinkedVisitClinical ? "available" : "visit_reassigned",
            summary = canReadLinkedVisitClinical ? appointment.summary : null,
            vitals = canReadLinkedVisitClinical ? appointment.vitals : null
        };
        return Completed(projection, "doctor_patient_summary", canReadLinkedVisitClinical
            ? "Tóm tắt ca khám được giới hạn trong lịch hẹn được phân công."
            : "Lịch hẹn vẫn được xác minh, nhưng dữ liệu lâm sàng của lượt khám đã chuyển sang bác sĩ được phân công hiện tại.");
    }

    private async Task<AiToolExecutionResult> GetDoctorOrdersAsync(AiToolExecutionContext context, string json, CancellationToken cancellationToken)
    {
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Doctor), cancellationToken);
        if (facilities.Count == 0) return ScopeDenied();
        var visitId = TryGetLong(json, "visitId", out var requestedVisitId) ? requestedVisitId : (long?)null;
        var appointmentId = TryGetLong(json, "appointmentId", out var requestedAppointmentId) ? requestedAppointmentId : (long?)null;
        var query = _db.DiagnosticOrders.AsNoTracking()
            .Where(o => o.OrderingDoctor.UserId == context.ActorId && o.FacilityId.HasValue && facilities.Contains(o.FacilityId.Value) && o.Status != DiagnosticOrderStatus.Cancelled)
            .Where(o => !visitId.HasValue || (o.PatientVisitId == visitId.Value && o.PatientVisit != null && o.PatientVisit.AssignedDoctor != null && o.PatientVisit.AssignedDoctor.UserId == context.ActorId))
            .Where(o => !appointmentId.HasValue ||
                        (o.AppointmentId == appointmentId.Value && o.Appointment != null && o.Appointment.Doctor.UserId == context.ActorId &&
                         (o.PatientVisit == null || o.PatientVisit.AssignedDoctor != null && o.PatientVisit.AssignedDoctor.UserId == context.ActorId)) ||
                        (o.PatientVisit != null && o.PatientVisit.AppointmentId == appointmentId.Value && o.PatientVisit.AssignedDoctor != null && o.PatientVisit.AssignedDoctor.UserId == context.ActorId));
        var items = await query.OrderByDescending(o => o.OrderedAtUtc).Take(100)
            .Select(o => new
            {
                o.Id, o.OrderCode, status = o.Status.ToString(), o.ClinicalIndication, o.OrderedAtUtc,
                items = o.Items.OrderBy(i => i.Id).Select(i => new
                {
                    service = i.DiagnosticService.Name,
                    status = i.Status.ToString(),
                    result = i.Result == null ? null : new { i.Result.ResultText, i.Result.Conclusion, i.Result.ReferenceRange, i.Result.Unit, i.Result.ResultedAtUtc }
                }).ToList()
            })
            .ToListAsync(cancellationToken);
        var hasPendingResult = items.Any(order => order.items.Any(item =>
            item.result is null || !string.Equals(item.status, DiagnosticItemStatus.Completed.ToString(), StringComparison.OrdinalIgnoreCase)));
        return Completed(items, "doctor_diagnostic_orders", items.Count == 0 ? "Chưa có chỉ định trong ca này." : hasPendingResult ? $"Có {items.Count} chỉ định; một số chỉ định chưa có kết quả." : $"Có {items.Count} chỉ định và đã có kết quả tương ứng.");
    }

    private async Task<AiToolExecutionResult> GetDoctorPrescriptionStatusAsync(AiToolExecutionContext context, string json, CancellationToken cancellationToken)
    {
        if (!TryGetExactlyOneLong(json, "appointmentId", "visitId", out var appointmentId, out var visitId))
            return AiToolExecutionResult.Failed("MISSING_TOOL_ARGUMENT", "Cần đúng một appointmentId hoặc visitId hợp lệ.");
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Doctor), cancellationToken);
        if (facilities.Count == 0) return ScopeDenied();
        var items = await _db.Prescriptions.AsNoTracking()
            .Where(p => p.Doctor != null && p.Doctor.UserId == context.ActorId &&
                        ((p.PatientVisit != null && facilities.Contains(p.PatientVisit.FacilityId)) ||
                         (p.PatientVisit == null && p.Appointment != null && p.Appointment.FacilityId.HasValue && facilities.Contains(p.Appointment.FacilityId.Value))))
            .Where(p => !visitId.HasValue || (p.PatientVisitId == visitId.Value && p.PatientVisit != null && p.PatientVisit.AssignedDoctor != null && p.PatientVisit.AssignedDoctor.UserId == context.ActorId))
            .Where(p => !appointmentId.HasValue ||
                        (p.AppointmentId == appointmentId.Value && p.Appointment != null && p.Appointment.Doctor.UserId == context.ActorId &&
                         (p.PatientVisit == null || p.PatientVisit.AssignedDoctor != null && p.PatientVisit.AssignedDoctor.UserId == context.ActorId)) ||
                        (p.PatientVisit != null && p.PatientVisit.AppointmentId == appointmentId.Value && p.PatientVisit.AssignedDoctor != null && p.PatientVisit.AssignedDoctor.UserId == context.ActorId))
            .OrderByDescending(p => p.CreatedAt).Take(20)
            .Select(p => new
            {
                p.Id, status = p.Status.ToString(), p.CreatedAt, p.DispensedAt, p.Notes,
                items = p.Items.Select(i => new { medicine = i.Medicine!.Name, i.Dosage, i.Frequency, i.DurationDays, i.Instructions }).ToList()
            }).ToListAsync(cancellationToken);
        return Completed(items, "doctor_prescription_status", items.Count == 0 ? "Ca khám chưa có đơn thuốc." : $"Có {items.Count} đơn thuốc của ca khám.");
    }

    private async Task<AiToolExecutionResult> GetTechnicianWorklistAsync(AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        if (!context.ActorId.HasValue || context.ActorId == Guid.Empty) return ScopeDenied();
        var items = await _db.DiagnosticOrders.AsNoTracking()
            .Where(o => o.FacilityId.HasValue && o.PerformingDepartmentId.HasValue &&
                        _db.StaffFacilityAssignments.Any(a => a.UserId == context.ActorId.Value && a.IsActive && a.Role == nameof(AiActorRole.DiagnosticTechnician) &&
                            a.FacilityId == o.FacilityId.Value && a.DepartmentId == o.PerformingDepartmentId.Value) &&
                        (!context.FacilityId.HasValue || o.FacilityId == context.FacilityId.Value) &&
                        (o.Status == DiagnosticOrderStatus.Ordered || o.Status == DiagnosticOrderStatus.InProgress))
            .OrderBy(o => o.OrderedAtUtc).Take(100)
            .Select(o => new
            {
                o.Id, o.OrderCode, status = o.Status.ToString(), o.ClinicalIndication, o.OrderedAtUtc, o.PerformingDepartmentId,
                items = o.Items.OrderBy(i => i.Id).Select(i => new { service = i.DiagnosticService.Name, status = i.Status.ToString() }).ToList()
            })
            .ToListAsync(cancellationToken);
        return Completed(items, "technician_worklist", $"Có {items.Count} chỉ định đang chờ xử lý.");
    }

    private async Task<AiToolExecutionResult> GetPharmacyQueueAsync(AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Pharmacist), cancellationToken);
        if (facilities.Count == 0) return ScopeDenied();
        var prescriptions = await _db.Prescriptions.AsNoTracking()
            .Where(p => (p.Status == PrescriptionStatus.Issued || p.Status == PrescriptionStatus.ReservedForPurchase) &&
                        ((p.PatientVisit != null && facilities.Contains(p.PatientVisit.FacilityId)) ||
                         (p.PatientVisit == null && p.Appointment != null && p.Appointment.FacilityId.HasValue && facilities.Contains(p.Appointment.FacilityId.Value))))
            .OrderBy(p => p.CreatedAt).Take(100)
            .Select(p => new { p.Id, status = p.Status.ToString(), p.CreatedAt, p.PatientVisitId })
            .ToListAsync(cancellationToken);
        var items = new List<object>(prescriptions.Count);
        foreach (var prescription in prescriptions)
        {
            var payment = await _pharmacy.EvaluatePrescriptionPaymentAsync(prescription.Id, cancellationToken);
            items.Add(new
            {
                prescription.Id,
                prescription.status,
                prescription.CreatedAt,
                prescription.PatientVisitId,
                paymentStatus = payment.PaymentStatus,
                paymentItems = payment.Items.Select(item => new
                {
                    medicine = item.MedicineName,
                    requiredQuantity = item.RequiredQuantity,
                    paidQuantity = item.PaidQuantity,
                    itemPaymentStatus = item.IsPaidInFull ? "paid_in_full" : item.PaidQuantity > 0 ? "partially_paid" : "unpaid"
                }).ToList()
            });
        }
        return Completed(items, "pharmacist_prescription_queue", $"Có {items.Count} đơn thuốc trong hàng đợi.");
    }

    private async Task<AiToolExecutionResult> GetPrescriptionPaymentStatusAsync(AiToolExecutionContext context, string json, CancellationToken cancellationToken)
    {
        if (!TryGetLong(json, "prescriptionId", out var prescriptionId))
            return AiToolExecutionResult.Failed("MISSING_TOOL_ARGUMENT", "Cần mở đúng đơn thuốc để đối chiếu thanh toán.");

        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Pharmacist), cancellationToken);
        if (facilities.Count == 0) return ScopeDenied();

        var prescription = await _db.Prescriptions.AsNoTracking()
            .Where(p => p.Id == prescriptionId &&
                        ((p.PatientVisit != null && facilities.Contains(p.PatientVisit.FacilityId)) ||
                         (p.PatientVisit == null && p.Appointment != null && p.Appointment.FacilityId.HasValue && facilities.Contains(p.Appointment.FacilityId.Value))))
            .Select(p => new { status = p.Status.ToString(), p.CreatedAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (prescription is null)
            return AiToolExecutionResult.Failed("NOT_FOUND", "Đơn thuốc không thuộc phạm vi cơ sở được phân quyền hoặc không còn tồn tại.");

        var payment = await _pharmacy.EvaluatePrescriptionPaymentAsync(prescriptionId, cancellationToken);
        var data = new
        {
            prescriptionStatus = prescription.status,
            prescriptionCreatedAt = prescription.CreatedAt,
            paymentStatus = payment.PaymentStatus,
            paymentItems = payment.Items.Select(item => new
            {
                medicine = item.MedicineName,
                requiredQuantity = item.RequiredQuantity,
                paidQuantity = item.PaidQuantity,
                itemPaymentStatus = item.IsPaidInFull ? "paid_in_full" : item.PaidQuantity > 0 ? "partially_paid" : "unpaid"
            }).ToList()
        };
        return Completed(data, "pharmacist_prescription_payment", payment.PaymentStatus switch
        {
            "paid_in_full" => "Đơn thuốc đã được thanh toán đủ theo từng dòng thuốc.",
            "partially_paid" => "Đơn thuốc mới được thanh toán một phần theo từng dòng thuốc.",
            "unpaid" => "Đơn thuốc chưa được thanh toán đủ theo từng dòng thuốc.",
            _ => "Chưa thể xác minh đầy đủ trạng thái thanh toán của đơn thuốc."
        });
    }

    private async Task<AiToolExecutionResult> GetInventoryAsync(AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Pharmacist), cancellationToken);
        if (facilities.Count == 0) return ScopeDenied();
        var medicines = await _db.Medicines.AsNoTracking().Where(m => m.IsActive).OrderBy(m => m.Name).Take(200)
            .Select(m => new { m.Id, m.Code, m.Name, m.Unit, m.StockQuantity, reorderLevel = m.ReorderLevel }).ToListAsync(cancellationToken);
        return Completed(medicines, "pharmacy_inventory", "Tồn kho toàn hệ thống được lấy từ danh mục thuốc hiện tại; mô hình dữ liệu chưa phân tách tồn kho theo cơ sở.");
    }

    private async Task<AiToolExecutionResult> GetAdminMetricsAsync(AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var today = _clock.VietnamToday;
        var metrics = new
        {
            appointmentsToday = await _db.Appointments.CountAsync(a => a.AppointmentDate == today, cancellationToken),
            activeVisits = await _db.PatientVisits.CountAsync(v => v.VisitDate == today && v.Status != VisitStatus.Cancelled && v.Status != VisitStatus.Completed, cancellationToken),
            openDiagnosticOrders = await _db.DiagnosticOrders.CountAsync(o => o.Status == DiagnosticOrderStatus.Ordered || o.Status == DiagnosticOrderStatus.InProgress, cancellationToken),
            issuedPrescriptions = await _db.Prescriptions.CountAsync(p => p.Status == PrescriptionStatus.Issued || p.Status == PrescriptionStatus.ReservedForPurchase, cancellationToken)
        };
        return Completed(metrics, "admin_dashboard_metrics", "Chỉ số tổng hợp không chứa hồ sơ lâm sàng.");
    }

    private async Task<AiToolExecutionResult> GetAiHealthAsync(AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var configuration = _providerConfiguration?.GetSnapshot();
        var metrics = new
        {
            pendingActions = await _db.AiPendingToolActions.CountAsync(a => a.State == AiPendingToolActionState.PendingConfirmation, cancellationToken),
            auditEvents = await _db.AiAuditLogs.CountAsync(cancellationToken),
            providerState = _providerHealth?.State ?? "Unavailable",
            circuitState = _providerHealth?.State ?? "Unavailable",
            fallbackActive = _providerHealth?.State is "Open" or "HalfOpen" || _providerHealth is null,
            requestCount = _providerHealth?.AttemptCount ?? 0,
            successCount = _providerHealth?.SuccessCount ?? 0,
            failureCount = _providerHealth?.FailureCount ?? 0,
            failureCounts = _providerHealth?.FailureCounts ?? new Dictionary<string, long>(),
            lastFailureCode = _providerHealth?.LastFailureCode,
            lastSuccessUtc = _providerHealth?.LastSuccessAtUtc,
            nextProbeUtc = _providerHealth?.NextProbeAtUtc,
            providerEnabled = configuration?.IsEnabled ?? false,
            providerName = configuration?.ProviderName,
            effectiveModelName = configuration?.ModelName,
            providerBaseUrl = configuration?.ProviderBaseUrl,
            timeoutSeconds = configuration?.TimeoutSeconds,
            maxAttempts = configuration?.MaxAttempts,
            retryBaseDelayMilliseconds = configuration?.RetryBaseDelayMilliseconds,
            circuitFailureThreshold = configuration?.CircuitFailureThreshold,
            circuitCooldownSeconds = configuration?.CircuitCooldownSeconds,
            keyConfigured = configuration?.KeyConfigured ?? false,
            keyConfigurationSource = configuration?.KeyConfigurationSource,
            configurationSources = configuration?.ConfigurationSources,
            retrievedAtUtc = configuration?.RetrievedAtUtc
        };
        return Completed(metrics, "admin_ai_health", "Chỉ số AI đã được tổng hợp, không trả secret hay nội dung prompt thô.");
    }

    private static bool TryGetLong(string json, string name, out long value)
    {
        value = 0;
        try { using var document = JsonDocument.Parse(json); return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out value) && value > 0; }
        catch (JsonException) { return false; }
    }

    private static bool HasExactlyOneResourceId(JsonElement root, string firstName, string secondName)
    {
        if (root.EnumerateObject().Any(x => !x.Name.Equals(firstName, StringComparison.OrdinalIgnoreCase) && !x.Name.Equals(secondName, StringComparison.OrdinalIgnoreCase)))
            return false;
        var first = root.TryGetProperty(firstName, out var firstValue) && firstValue.ValueKind == JsonValueKind.Number && firstValue.TryGetInt64(out var firstId) && firstId > 0;
        var second = root.TryGetProperty(secondName, out var secondValue) && secondValue.ValueKind == JsonValueKind.Number && secondValue.TryGetInt64(out var secondId) && secondId > 0;
        return first ^ second;
    }

    private static bool HasAtMostOnePositiveResourceId(JsonElement root, string firstName, string secondName)
    {
        var count = 0;
        foreach (var property in root.EnumerateObject())
        {
            if (!property.Name.Equals(firstName, StringComparison.OrdinalIgnoreCase) && !property.Name.Equals(secondName, StringComparison.OrdinalIgnoreCase))
                return false;
            if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt64(out var id) || id <= 0)
                return false;
            count++;
        }
        return count <= 1;
    }

    private static bool TryGetExactlyOneLong(string json, string firstName, string secondName, out long? first, out long? second)
    {
        first = null;
        second = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (document.RootElement.TryGetProperty(firstName, out var firstValue) && firstValue.ValueKind == JsonValueKind.Number && firstValue.TryGetInt64(out var firstId) && firstId > 0)
                first = firstId;
            if (document.RootElement.TryGetProperty(secondName, out var secondValue) && secondValue.ValueKind == JsonValueKind.Number && secondValue.TryGetInt64(out var secondId) && secondId > 0)
                second = secondId;
            return first.HasValue ^ second.HasValue;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static AiToolExecutionResult ScopeDenied() => AiToolExecutionResult.Failed("FACILITY_SCOPE_REQUIRED", "Không xác định được phạm vi cơ sở được phân quyền; dữ liệu không được trả về.");
    private static AiToolExecutionResult Completed(object data, string type, string displayText) => new()
    {
        Status = "completed", ResultType = type, Data = data, DisplayText = displayText,
        DataSources = new[] { new AiToolDataSource("ClinicCare domain database", "database") }
    };
}
