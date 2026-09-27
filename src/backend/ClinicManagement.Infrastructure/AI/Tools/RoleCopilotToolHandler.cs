using System.Text.Json;
using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
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
    private const int CatalogCandidateLimit = 200;
    private const int CatalogDefaultResultLimit = 10;
    private const int CatalogMaxResultLimit = 20;

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
        "ai", "bao", "biet", "chi", "cho", "co", "cua", "cuu", "dich", "duoc", "gia", "gi", "giup",
        "bac", "bsi", "dau", "doctor", "hay", "hien", "hoi", "kham", "khong", "khoa", "chuyen", "lich", "mo", "mot", "nao", "nhieu", "nguoi", "o", "phong", "si",
        "so", "tai", "the", "thong", "tin", "toi", "tra", "va", "ve", "voi", "vu", "xem"
    };

    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public RoleCopilotToolHandler(AppDbContext db, ICurrentUserService currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
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
            "doctor.get_diagnostic_orders" => GetDoctorOrdersAsync(context, cancellationToken),
            "technician.get_worklist" => GetTechnicianWorklistAsync(context, cancellationToken),
            "pharmacist.get_prescription_queue" => GetPharmacyQueueAsync(context, cancellationToken),
            "pharmacist.get_inventory_status" => GetInventoryAsync(context, cancellationToken),
            "admin.get_dashboard_metrics" => GetAdminMetricsAsync(context, cancellationToken),
            "admin.get_ai_health" => GetAiHealthAsync(context, cancellationToken),
            _ => Task.FromResult(AiToolExecutionResult.Failed("UNKNOWN_TOOL", "Công cụ workspace không được hỗ trợ."))
        };

    private async Task<AiToolExecutionResult> SearchKnowledgeAsync(string json, CancellationToken cancellationToken)
    {
        if (!TryReadCatalogRequest(json, out var request, out var error))
            return error!;

        var terms = BuildSearchTerms(request.Query, request.SpecialtyQuery, request.FacilityQuery);
        var broadRequest = terms.Count == 0 &&
            (request.Entity.Equals("all", StringComparison.OrdinalIgnoreCase) ||
             request.Entity.Equals("facility", StringComparison.OrdinalIgnoreCase) ||
             request.Entity.Equals("price", StringComparison.OrdinalIgnoreCase));
        var hits = new List<ClinicKnowledgeItem>();

        switch (request.Entity)
        {
            case "all":
                hits.AddRange(await SearchSpecialtiesAsync(terms, broadRequest, cancellationToken));
                hits.AddRange(await SearchDoctorsAsync(terms, broadRequest, cancellationToken));
                hits.AddRange(await SearchDiagnosticServicesAsync(terms, broadRequest, cancellationToken));
                hits.AddRange(await SearchFacilitiesAsync(terms, broadRequest, cancellationToken));
                hits.AddRange(await SearchPublishedPricesAsync(terms, broadRequest, cancellationToken));
                break;
            case "specialty":
                hits.AddRange(await SearchSpecialtiesAsync(terms, false, cancellationToken));
                break;
            case "doctor":
                hits.AddRange(await SearchDoctorsAsync(terms, false, cancellationToken));
                break;
            case "diagnostic_service":
                hits.AddRange(await SearchDiagnosticServicesAsync(terms, false, cancellationToken));
                break;
            case "facility":
                hits.AddRange(await SearchFacilitiesAsync(terms, broadRequest, cancellationToken));
                break;
            case "price":
                hits.AddRange(await SearchPublishedPricesAsync(terms, terms.Count == 0, cancellationToken));
                break;
        }

        var limited = hits
            .GroupBy(x => x.SourceId, StringComparer.Ordinal)
            .Select(x => x.First())
            .OrderBy(x => x.SourceType, StringComparer.Ordinal)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
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
                ? $"Đã tìm thấy {limited.Length} mục phù hợp từ danh mục công khai hiện hành."
                : "Không tìm thấy dữ liệu công khai phù hợp; vui lòng hỏi lễ tân để được kiểm tra thêm.",
            Data = new ClinicKnowledgeEnvelope
            {
                Status = matched ? "matched" : "not_found",
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
        var candidates = await _db.Specialties.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Take(CatalogCandidateLimit)
            .Select(x => new { x.Id, x.SpecialtyCode, x.Name, x.Description, x.ConsultationFee })
            .ToListAsync(cancellationToken);

        return candidates
            .Where(x => broadRequest || Matches(terms, x.Name, x.SpecialtyCode, x.Description))
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
        IReadOnlySet<string> terms,
        bool broadRequest,
        CancellationToken cancellationToken)
    {
        var candidates = await (from d in _db.Doctors.AsNoTracking()
                                join u in _db.Users.AsNoTracking() on d.UserId equals u.Id
                                join ds in _db.DoctorSpecialties.AsNoTracking() on d.Id equals ds.DoctorId
                                join s in _db.Specialties.AsNoTracking() on ds.SpecialtyId equals s.Id
                                join assignment in _db.StaffFacilityAssignments.AsNoTracking() on d.UserId equals assignment.UserId
                                join facility in _db.Facilities.AsNoTracking() on assignment.FacilityId equals facility.Id
                                where d.IsActive && u.IsActive && s.IsActive && assignment.IsActive &&
                                      assignment.Role == nameof(AiActorRole.Doctor) && facility.IsActive
                                orderby d.Id, facility.Id, s.Name
                                select new
                                {
                                    DoctorId = d.Id,
                                    u.FullName,
                                    d.AcademicTitle,
                                    d.ExperienceYears,
                                    d.Description,
                                    SpecialtyName = s.Name,
                                    FacilityName = facility.Name,
                                    FacilityAddress = facility.Address,
                                    FacilityCity = facility.City
                                })
            .Take(CatalogCandidateLimit)
            .ToListAsync(cancellationToken);

        return candidates
            .GroupBy(x => x.DoctorId)
            .Select(group =>
            {
                var first = group.First();
                var searchableFacilities = group.Select(x => $"{x.FacilityName} {x.FacilityAddress} {x.FacilityCity}");
                var searchable = new[] { first.FullName, first.AcademicTitle, first.Description, first.SpecialtyName }
                    .Concat(searchableFacilities);
                return new { first, group, IsMatch = broadRequest || Matches(terms, searchable.ToArray()) };
            })
            .Where(x => x.IsMatch)
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
        var candidates = await _db.DiagnosticServices.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Take(CatalogCandidateLimit)
            .Select(x => new { x.Id, x.Code, x.Name, x.Category, x.PreparationInstructions, x.Price })
            .ToListAsync(cancellationToken);

        return candidates
            .Where(x => broadRequest || Matches(terms, x.Name, x.Code, x.PreparationInstructions, x.Category.ToString()))
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
        var facilities = await _db.Facilities.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Take(CatalogCandidateLimit)
            .Select(x => new { x.Id, x.Code, x.Name, x.Address, x.City, x.Phone, x.Description })
            .ToListAsync(cancellationToken);
        var locations = await _db.ClinicLocations.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Take(CatalogCandidateLimit)
            .Select(x => new { x.Id, x.Code, x.Name, x.Address, x.City, x.Phone, x.Description, x.OpeningHours })
            .ToListAsync(cancellationToken);

        var facilityHits = facilities
            .Where(x => broadRequest || Matches(terms, x.Code, x.Name, x.Address, x.City, x.Description))
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
            .Where(x => broadRequest || Matches(terms, x.Code, x.Name, x.Address, x.City, x.Description, x.OpeningHours))
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
        var specialties = await _db.Specialties.AsNoTracking()
            .Where(x => x.IsActive && x.ConsultationFee > 0)
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Take(CatalogCandidateLimit)
            .Select(x => new { x.Id, x.Name, x.Description, x.SpecialtyCode, x.ConsultationFee })
            .ToListAsync(cancellationToken);
        var diagnostics = await _db.DiagnosticServices.AsNoTracking()
            .Where(x => x.IsActive && x.Price.HasValue && x.Price.Value > 0)
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Take(CatalogCandidateLimit)
            .Select(x => new { x.Id, x.Name, x.PreparationInstructions, x.Code, Price = x.Price!.Value, x.Category })
            .ToListAsync(cancellationToken);
        var packages = await _db.HealthPackages.AsNoTracking()
            .Where(x => x.IsActive && x.Price > 0)
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Take(CatalogCandidateLimit)
            .Select(x => new { x.Id, x.Name, x.Description, x.Code, x.Price })
            .ToListAsync(cancellationToken);

        var specialtyHits = specialties
            .Where(x => broadRequest || Matches(terms, x.Name, x.SpecialtyCode, x.Description))
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
            .Where(x => broadRequest || Matches(terms, x.Name, x.Code, x.PreparationInstructions, x.Category.ToString()))
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
            .Where(x => broadRequest || Matches(terms, x.Name, x.Code, x.Description))
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

        if (root.TryGetProperty("entity", out var entity))
        {
            if (entity.ValueKind != JsonValueKind.String || !CatalogEntities.Contains(entity.GetString()?.Trim() ?? string.Empty))
                return AiToolArgumentValidationResult.Invalid("INVALID_ENTITY", "Loại danh mục không được hỗ trợ.");
        }

        foreach (var name in new[] { "specialtyQuery", "facilityQuery" })
        {
            if (root.TryGetProperty(name, out var filter) && (filter.ValueKind != JsonValueKind.String || (filter.GetString()?.Trim().Length ?? 0) > 120))
                return AiToolArgumentValidationResult.Invalid("INVALID_FILTER", "Bộ lọc danh mục không hợp lệ.");
        }

        if (root.TryGetProperty("limit", out var limit) && (!limit.TryGetInt32(out var parsedLimit) || parsedLimit is < 1 or > CatalogMaxResultLimit))
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
            var limit = root.TryGetProperty("limit", out var limitValue) && limitValue.TryGetInt32(out var parsedLimit) ? Math.Clamp(parsedLimit, 1, CatalogMaxResultLimit) : CatalogDefaultResultLimit;
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
        var terms = Regex.Matches(normalized, @"[a-z0-9]{2,}", RegexOptions.CultureInvariant)
            .Select(x => x.Value)
            .Where(x => !CatalogStopWords.Contains(x))
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        return terms;
    }

    private static bool Matches(IReadOnlySet<string> terms, params string?[] values)
    {
        var searchable = AiTextNormalizer.NormalizeForComparison(string.Join(" ", values.Where(x => !string.IsNullOrWhiteSpace(x))));
        var searchableTerms = Regex.Matches(searchable, @"[a-z0-9]{2,}", RegexOptions.CultureInvariant)
            .Select(x => x.Value)
            .ToHashSet(StringComparer.Ordinal);
        return terms.Count > 0 && terms.All(searchableTerms.Contains);
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
            .Select(a => new { a.Id, a.AppointmentCode, a.AppointmentDate, a.StartTime, a.EndTime, a.Status, patientName = a.Patient.FullName, a.Patient.MedicalRecordNumber, doctorName = _db.Users.Where(u => u.Id == a.Doctor.UserId).Select(u => u.FullName).FirstOrDefault() ?? "Bác sĩ", specialtyId = a.SpecialtyId })
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
            .Select(v => new { v.Id, v.VisitCode, v.QueueNumber, v.Status, patientName = v.Patient.FullName, v.Patient.MedicalRecordNumber, v.AssignedDoctorId, v.DepartmentId })
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
            .Select(a => new { a.Id, a.AppointmentCode, a.AppointmentDate, a.StartTime, a.EndTime, a.Status, patientName = a.Patient.FullName, a.Patient.MedicalRecordNumber })
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
            .Select(v => new { v.Id, v.VisitCode, v.QueueNumber, v.Status, patientName = v.Patient.FullName, v.Patient.MedicalRecordNumber, v.ChiefComplaint, v.AppointmentId })
            .ToListAsync(cancellationToken);
        return Completed(items, "doctor_queue", $"Hàng đợi của bạn có {items.Count} lượt.");
    }

    private async Task<AiToolExecutionResult> GetDoctorPatientSummaryAsync(AiToolExecutionContext context, string json, CancellationToken cancellationToken)
    {
        if (!TryGetLong(json, "appointmentId", out var appointmentId)) return AiToolExecutionResult.Failed("MISSING_TOOL_ARGUMENT", "Cần appointmentId hợp lệ.");
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Doctor), cancellationToken);
        if (facilities.Count == 0) return ScopeDenied();
        var summary = await _db.Appointments.AsNoTracking()
            .Where(a => a.Id == appointmentId && a.Doctor.UserId == context.ActorId && a.FacilityId.HasValue && facilities.Contains(a.FacilityId.Value))
            .Select(a => new { a.Id, a.AppointmentCode, a.AppointmentDate, a.Status, patientName = a.Patient.FullName, a.Patient.MedicalRecordNumber, a.Reason, a.SpecialtyId })
            .SingleOrDefaultAsync(cancellationToken);
        return summary is null ? AiToolExecutionResult.Failed("NOT_FOUND", "Ca khám không thuộc bác sĩ hiện tại.") : Completed(summary, "doctor_patient_summary", "Tóm tắt được giới hạn trong ca khám được phân công.");
    }

    private async Task<AiToolExecutionResult> GetDoctorOrdersAsync(AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Doctor), cancellationToken);
        if (facilities.Count == 0) return ScopeDenied();
        var items = await _db.DiagnosticOrders.AsNoTracking()
            .Where(o => o.OrderingDoctor.UserId == context.ActorId && o.FacilityId.HasValue && facilities.Contains(o.FacilityId.Value) && o.Status != DiagnosticOrderStatus.Cancelled)
            .OrderByDescending(o => o.OrderedAtUtc).Take(100)
            .Select(o => new { o.Id, o.OrderCode, o.PatientId, o.Status, o.ClinicalIndication, o.OrderedAtUtc, o.FacilityId })
            .ToListAsync(cancellationToken);
        return Completed(items, "doctor_diagnostic_orders", $"Có {items.Count} chỉ định liên quan.");
    }

    private async Task<AiToolExecutionResult> GetTechnicianWorklistAsync(AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.DiagnosticTechnician), cancellationToken);
        if (facilities.Count == 0) return ScopeDenied();
        var items = await _db.DiagnosticOrders.AsNoTracking()
            .Where(o => o.FacilityId.HasValue && facilities.Contains(o.FacilityId.Value) && (o.Status == DiagnosticOrderStatus.Ordered || o.Status == DiagnosticOrderStatus.InProgress))
            .OrderBy(o => o.OrderedAtUtc).Take(100)
            .Select(o => new { o.Id, o.OrderCode, o.PatientId, o.Status, o.ClinicalIndication, o.OrderedAtUtc, o.PerformingDepartmentId })
            .ToListAsync(cancellationToken);
        return Completed(items, "technician_worklist", $"Có {items.Count} chỉ định đang chờ xử lý.");
    }

    private async Task<AiToolExecutionResult> GetPharmacyQueueAsync(AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Pharmacist), cancellationToken);
        if (facilities.Count == 0) return ScopeDenied();
        var items = await _db.Prescriptions.AsNoTracking()
            .Where(p => (p.Status == PrescriptionStatus.Issued || p.Status == PrescriptionStatus.ReservedForPurchase) &&
                        ((p.PatientVisit != null && facilities.Contains(p.PatientVisit.FacilityId)) ||
                         (p.PatientVisit == null && p.Appointment != null && p.Appointment.FacilityId.HasValue && facilities.Contains(p.Appointment.FacilityId.Value))))
            .OrderBy(p => p.CreatedAt).Take(100)
            .Select(p => new { p.Id, p.PatientId, patientName = p.Patient!.FullName, p.Status, p.CreatedAt, p.PatientVisitId })
            .ToListAsync(cancellationToken);
        return Completed(items, "pharmacist_prescription_queue", $"Có {items.Count} đơn thuốc trong hàng đợi.");
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
        var metrics = new
        {
            pendingActions = await _db.AiPendingToolActions.CountAsync(a => a.State == AiPendingToolActionState.PendingConfirmation, cancellationToken),
            auditEvents = await _db.AiAuditLogs.CountAsync(cancellationToken)
        };
        return Completed(metrics, "admin_ai_health", "Chỉ số AI đã được tổng hợp, không trả secret hay nội dung prompt thô.");
    }

    private static bool TryGetLong(string json, string name, out long value)
    {
        value = 0;
        try { using var document = JsonDocument.Parse(json); return document.RootElement.TryGetProperty(name, out var property) && property.TryGetInt64(out value) && value > 0; }
        catch (JsonException) { return false; }
    }

    private static AiToolExecutionResult ScopeDenied() => AiToolExecutionResult.Failed("FACILITY_SCOPE_REQUIRED", "Không xác định được phạm vi cơ sở được phân quyền; dữ liệu không được trả về.");
    private static AiToolExecutionResult Completed(object data, string type, string displayText) => new()
    {
        Status = "completed", ResultType = type, Data = data, DisplayText = displayText,
        DataSources = new[] { new AiToolDataSource("ClinicCare domain database", "database") }
    };
}
