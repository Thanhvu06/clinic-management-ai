using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Planning;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClinicManagement.IntegrationTests;

public sealed class GeminiAiProviderResilienceTests
{
    [Fact]
    public async Task Returns_valid_200_json_without_retrying()
    {
        var handler = new SequenceHandler(_ => Task.FromResult(Response(HttpStatusCode.OK, ValidEnvelope("Đã hiểu yêu cầu."))));
        var provider = CreateProvider(handler);

        var result = await provider.ChatWithAiAsync("xin chào", new(), new(), "{}", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Success", result.Status);
        Assert.Equal("Đã hiểu yêu cầu.", result.Reply);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Respects_retry_after_for_429_when_budget_allows_a_second_attempt()
    {
        var first = Response(HttpStatusCode.TooManyRequests, "{}", TimeSpan.FromMilliseconds(80));
        var handler = new SequenceHandler(
            _ => Task.FromResult(first),
            _ => Task.FromResult(Response(HttpStatusCode.OK, ValidEnvelope("Đã thử lại."))));
        var provider = CreateProvider(handler, timeoutSeconds: 2, maxAttempts: 2, retryBaseDelayMilliseconds: 0);
        var clock = Stopwatch.StartNew();

        var result = await provider.ChatWithAiAsync("xin chào", new(), new(), "{}", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.CallCount);
        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(65), $"Retry-After was not respected: {clock.Elapsed}");
    }

    [Fact]
    public async Task Retries_503_once_and_recovers()
    {
        var handler = new SequenceHandler(
            _ => Task.FromResult(Response(HttpStatusCode.ServiceUnavailable, "{}", TimeSpan.Zero)),
            _ => Task.FromResult(Response(HttpStatusCode.OK, ValidEnvelope("Đã hồi phục."))));
        var provider = CreateProvider(handler, timeoutSeconds: 2, maxAttempts: 2, retryBaseDelayMilliseconds: 0);

        var result = await provider.ChatWithAiAsync("xin chào", new(), new(), "{}", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Does_not_retry_auth_or_model_configuration_errors()
    {
        var handler = new SequenceHandler(_ => Task.FromResult(Response(HttpStatusCode.Unauthorized, "{}")));
        var provider = CreateProvider(handler, timeoutSeconds: 2, maxAttempts: 3);

        var result = await provider.ChatWithAiAsync("xin chào", new(), new(), "{}", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AuthFailure", result.Status);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Returns_timeout_after_the_bounded_total_budget()
    {
        var handler = new SequenceHandler(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response(HttpStatusCode.OK, ValidEnvelope("không được dùng"));
        });
        var provider = CreateProvider(handler, timeoutSeconds: 1, maxAttempts: 1);

        var result = await provider.ChatWithAiAsync("xin chào", new(), new(), "{}", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Timeout", result.Status);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Rejects_invalid_json_without_retrying_or_fabricating_a_reply()
    {
        var handler = new SequenceHandler(_ => Task.FromResult(Response(HttpStatusCode.OK, InvalidJsonEnvelope())));
        var provider = CreateProvider(handler);

        var result = await provider.ChatWithAiAsync("xin chào", new(), new(), "{}", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("InvalidResponse", result.Status);
        Assert.Empty(result.Reply);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Distinguishes_client_cancellation_and_does_not_retry_it()
    {
        var handler = new SequenceHandler(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response(HttpStatusCode.OK, ValidEnvelope("không được dùng"));
        });
        var provider = CreateProvider(handler, timeoutSeconds: 5, maxAttempts: 3);
        using var cancellation = new CancellationTokenSource();
        var request = provider.ChatWithAiAsync("xin chào", new(), new(), "{}", cancellation.Token);
        await Task.Delay(50);
        cancellation.Cancel();

        var result = await request;

        Assert.False(result.IsSuccess);
        Assert.Equal("Cancelled", result.Status);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Circuit_opens_after_provider_failures_and_recovers_without_losing_the_model_call()
    {
        var handler = new SequenceHandler(
            _ => Task.FromResult(Response(HttpStatusCode.ServiceUnavailable, "{}", TimeSpan.Zero)),
            _ => Task.FromResult(Response(HttpStatusCode.ServiceUnavailable, "{}", TimeSpan.Zero)),
            _ => Task.FromResult(Response(HttpStatusCode.ServiceUnavailable, "{}", TimeSpan.Zero)),
            _ => Task.FromResult(Response(HttpStatusCode.OK, ValidEnvelope("Đã hồi phục."))));
        var provider = CreateProvider(handler, timeoutSeconds: 2, maxAttempts: 1, retryBaseDelayMilliseconds: 0);
        var health = new AiProviderHealth(TimeSpan.FromMilliseconds(60));
        var planner = new GeminiStructuredPlanner(provider, health, NullLogger<GeminiStructuredPlanner>.Instance);
        var request = new AiStructuredPlannerRequest
        {
            Role = AiActorRole.Doctor,
            Message = "yêu cầu mơ hồ",
            AllowedToolNames = new[] { "doctor.get_my_queue" }
        };

        for (var i = 0; i < 3; i++)
        {
            var failed = await planner.PlanAsync(request);
            Assert.False(failed.IsSuccess);
            Assert.Equal(AiProviderStatusContract.Degraded, failed.ProviderState);
        }

        var blocked = await planner.PlanAsync(request);
        Assert.False(blocked.IsSuccess);
        Assert.Equal(AiProviderStatusContract.Unavailable, blocked.ProviderState);
        Assert.Equal(3, handler.CallCount);

        await Task.Delay(100);
        var recovered = await planner.PlanAsync(request);

        Assert.True(recovered.IsSuccess);
        Assert.Equal(AiProviderStatusContract.Online, recovered.ProviderState);
        Assert.Equal(4, handler.CallCount);
    }

    private static GeminiAiProvider CreateProvider(
        HttpMessageHandler handler,
        int timeoutSeconds = 2,
        int maxAttempts = 3,
        int retryBaseDelayMilliseconds = 1) => new(
        new HttpClient(handler),
        Options.Create(new AiProviderOptions
        {
            IsEnabled = true,
            ApiKey = "test-only-key",
            ProviderUrl = "https://fake-gemini.test",
            ModelName = "gemini-3.5-flash-lite",
            TimeoutSeconds = timeoutSeconds,
            MaxAttempts = maxAttempts,
            RetryBaseDelayMilliseconds = retryBaseDelayMilliseconds
        }),
        NullLogger<GeminiAiProvider>.Instance);

    private static HttpResponseMessage Response(HttpStatusCode status, string body, TimeSpan? retryAfter = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (retryAfter.HasValue)
            response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter.Value);
        return response;
    }

    private static string ValidEnvelope(string reply) => JsonSerializer.Serialize(new
    {
        candidates = new[]
        {
            new
            {
                content = new
                {
                    parts = new[]
                    {
                        new { text = JsonSerializer.Serialize(new
                        {
                            plannerSchemaVersion = "1.0",
                            plannerConfidence = 0.9m,
                            reply,
                            primaryIntent = "Greeting",
                            isClear = true
                        }) }
                    }
                }
            }
        }
    });

    private static string InvalidJsonEnvelope() => JsonSerializer.Serialize(new
    {
        candidates = new[]
        {
            new
            {
                content = new
                {
                    parts = new[] { new { text = "not-json" } }
                }
            }
        }
    });

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _responses;

        public SequenceHandler(params Func<CancellationToken, Task<HttpResponseMessage>>[] responses) =>
            _responses = new Queue<Func<CancellationToken, Task<HttpResponseMessage>>>(responses);

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            var response = _responses.Count > 0
                ? _responses.Dequeue()
                : (_ => Task.FromResult(Response(HttpStatusCode.InternalServerError, "{}")));
            return response(cancellationToken);
        }
    }
}
