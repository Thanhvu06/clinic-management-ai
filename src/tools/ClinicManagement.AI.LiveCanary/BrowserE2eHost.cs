using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Infrastructure.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.AI.LiveCanary;

internal static class BrowserE2eHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        var port = ParseInt(args, "--browser-port", 5318);
        var mode = Option(args, "--browser-provider-mode") ?? "online";
        var readyFile = Option(args, "--browser-ready-file");
        using var handler = new FakeGeminiHttpHandler(mode);
        using var factory = new SyntheticCanaryFactory(
            new CanaryProviderAttemptBudget(12),
            handler,
            serverUrl: null,
            providerEnabled: !string.Equals(mode, "disabled", StringComparison.OrdinalIgnoreCase));

        // CreateClient starts the full API pipeline, but the browser itself
        // talks to the Kestrel listener configured above.
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            AllowAutoRedirect = false
        });
        var proxyBuilder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
        proxyBuilder.WebHost.UseKestrel().UseUrls($"http://127.0.0.1:{port}");
        proxyBuilder.Logging.ClearProviders();
        proxyBuilder.Logging.AddConsole();
        var proxy = proxyBuilder.Build();
        proxy.Use(async (context, next) =>
        {
            ApplyCorsHeaders(context);
            if (HttpMethods.IsOptions(context.Request.Method))
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            await next();
        });
        proxy.Map("/{**path}", async context =>
        {
            var target = new Uri(client.BaseAddress!, $"{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}");
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);
            if (context.Request.ContentLength is > 0)
                request.Content = new StreamContent(context.Request.Body);

            foreach (var header in context.Request.Headers)
            {
                if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                    header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) && request.Content is not null)
                    request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            context.Response.StatusCode = (int)response.StatusCode;
            foreach (var header in response.Headers)
                context.Response.Headers[header.Key] = header.Value.ToArray();
            foreach (var header in response.Content.Headers)
                context.Response.Headers[header.Key] = header.Value.ToArray();
            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        });
        await proxy.StartAsync();
        await factory.SeedAsync();
        var ready = JsonSerializer.Serialize(new
        {
            port,
            mode,
            database = "temporary SQLite file seeded by SyntheticCanaryFactory",
            provider = "fake HttpMessageHandler; no Gemini network"
        });
        if (!string.IsNullOrWhiteSpace(readyFile))
        {
            var readyPath = Path.GetFullPath(readyFile);
            var readyDirectory = Path.GetDirectoryName(readyPath);
            if (!string.IsNullOrWhiteSpace(readyDirectory))
                Directory.CreateDirectory(readyDirectory);
            await File.WriteAllTextAsync(readyPath, ready);
        }
        Console.WriteLine($"BROWSER_E2E_READY {ready}");

        // The parent Node process owns lifecycle and terminates this process
        // after the browser run. Keep the host alive without opening a shell or
        // accepting any external shutdown command.
        await Task.Delay(Timeout.InfiniteTimeSpan);
        return 0;
    }

    private static void ApplyCorsHeaders(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin)) return;

        context.Response.Headers.AccessControlAllowOrigin = origin;
        context.Response.Headers.AccessControlAllowCredentials = "true";
        context.Response.Headers.AccessControlAllowMethods = "GET,POST,PUT,PATCH,DELETE,OPTIONS";
        context.Response.Headers.AccessControlAllowHeaders =
            context.Request.Headers.AccessControlRequestHeaders.ToString() is { Length: > 0 } requested
                ? requested
                : "Authorization,Content-Type,X-Requested-With";
        context.Response.Headers.Vary = "Origin";
    }

    private static string? Option(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        return null;
    }

    private static int ParseInt(string[] args, string name, int fallback)
    {
        var value = Option(args, name);
        return int.TryParse(value, out var parsed) && parsed is > 0 and <= 65535 ? parsed : fallback;
    }
}

/// <summary>
/// Test-only HTTP seam for browser acceptance. It never opens a socket and
/// never forwards a request. The production GeminiAiProvider still performs
/// serialization, timeout, retry, response parsing, and planner validation.
/// </summary>
internal sealed class FakeGeminiHttpHandler : HttpMessageHandler
{
    private readonly string _mode;
    private int _requestCount;

    public FakeGeminiHttpHandler(string mode) => _mode = mode.Trim().ToLowerInvariant();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var requestNumber = Interlocked.Increment(ref _requestCount);
        switch (_mode)
        {
            case "disabled":
                throw new InvalidOperationException("Disabled mode must not call the fake provider.");
            case "timeout":
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new OperationCanceledException(cancellationToken);
            case "delayed":
                await Task.Delay(750, cancellationToken);
                return ValidResponse(request);
            case "rate-limited":
                return Response(HttpStatusCode.TooManyRequests, "retry-after", "0");
            case "server-error":
                return Response(HttpStatusCode.ServiceUnavailable);
            case "recover":
                return requestNumber == 1
                    ? Response(HttpStatusCode.ServiceUnavailable)
                    : ValidResponse(request);
            case "invalid-json":
                return InvalidJsonResponse();
            default:
                return ValidResponse(request);
        }
    }

    private static HttpResponseMessage ValidResponse(HttpRequestMessage request)
    {
        var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
        var toolName = SelectTool(body);
        var planner = new
        {
            plannerSchemaVersion = "1.0",
            plannerConfidence = 0.99m,
            reply = "Tôi đã kiểm tra dữ liệu synthetic trong phạm vi được cấp.",
            responseMode = "tool_result",
            toolCalls = new[] { new { name = toolName, version = "1.0", arguments = new { } } },
            clarification = (string?)null,
            safety = (string?)null,
            suggestedSpecialtyCodes = Array.Empty<string>(),
            urgency = "ROUTINE",
            primaryIntent = "ViewAppointments",
            secondaryIntent = (string?)null,
            isClear = true,
            clarificationPrompt = (string?)null,
            extractedSpecialtyCode = (string?)null,
            extractedDoctorName = (string?)null,
            extractedDate = (string?)null,
            extractedTimePreference = (string?)null,
            wantsEarliest = false,
            requestedActionType = "ViewMyAppointments",
            extractedReason = (string?)null,
            isCorrection = false,
            negatedDoctorName = (string?)null,
            negatedSymptom = (string?)null,
            correctionTarget = (string?)null
        };
        var envelope = new
        {
            candidates = new[]
            {
                new { content = new { parts = new[] { new { text = JsonSerializer.Serialize(planner) } } } }
            }
        };
        return Response(HttpStatusCode.OK, content: JsonSerializer.Serialize(envelope));
    }

    private static HttpResponseMessage InvalidJsonResponse() =>
        Response(HttpStatusCode.OK, content: "{not-json");

    private static string SelectTool(string body)
    {
        // The full provider prompt contains the global tool catalog. Select
        // from the synthetic actor's user message instead of the catalog, or
        // the fake could accidentally return a tool outside that actor's
        // role-specific allowlist.
        var searchable = body;
        try
        {
            using var document = JsonDocument.Parse(body);
            searchable = string.Join(
                "\n",
                document.RootElement.GetProperty("contents")
                    .EnumerateArray()
                    .SelectMany(content => content.GetProperty("parts").EnumerateArray())
                    .Select(part => part.GetProperty("text").GetString() ?? string.Empty));
        }
        catch (JsonException)
        {
            // Keep the raw body as a safe fallback for this synthetic seam.
        }

        if (searchable.Contains("quầy tiếp đón", StringComparison.OrdinalIgnoreCase))
            return "reception.get_today_appointments";
        if (searchable.Contains("lượt đang chờ được phân công", StringComparison.OrdinalIgnoreCase))
            return "doctor.get_my_queue";
        if (searchable.Contains("yêu cầu xét nghiệm", StringComparison.OrdinalIgnoreCase))
            return "technician.get_worklist";
        if (searchable.Contains("toa đang chờ xử lý", StringComparison.OrdinalIgnoreCase))
            return "pharmacist.get_prescription_queue";
        if (searchable.Contains("tình hình vận hành", StringComparison.OrdinalIgnoreCase))
            return "admin.get_dashboard_metrics";
        return "patient.get_my_appointments";
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string? header = null, string? value = null, string? content = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(content ?? "", Encoding.UTF8, "application/json")
        };
        if (header is not null && value is not null)
            response.Headers.TryAddWithoutValidation(header, value);
        return response;
    }
}
