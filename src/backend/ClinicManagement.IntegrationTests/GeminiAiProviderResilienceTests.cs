using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
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
        Assert.Equal(1, result.ProviderAttemptCount);
    }

    [Fact]
    public async Task Attempt_budget_one_blocks_a_retry_before_a_second_http_attempt()
    {
        var handler = new SequenceHandler(
            _ => Task.FromResult(Response(HttpStatusCode.ServiceUnavailable, "{}", TimeSpan.Zero)),
            _ => Task.FromResult(Response(HttpStatusCode.OK, ValidEnvelope("không được gọi"))));
        var budget = new TestAttemptBudget(1);
        var provider = CreateProvider(handler, timeoutSeconds: 2, maxAttempts: 2, retryBaseDelayMilliseconds: 0, attemptBudget: budget);

        var result = await provider.ChatWithAiAsync("xin chào", new(), new(), "{}", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AiProviderStatusContract.FailureAttemptBudgetExceeded, result.FailureCode);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(1, result.ProviderAttemptCount);
        Assert.Equal(1, budget.Consumed);
    }

    [Fact]
    public async Task Attempt_budget_two_allows_a_single_bounded_retry()
    {
        var handler = new SequenceHandler(
            _ => Task.FromResult(Response(HttpStatusCode.ServiceUnavailable, "{}", TimeSpan.Zero)),
            _ => Task.FromResult(Response(HttpStatusCode.OK, ValidEnvelope("đã hồi phục"))));
        var budget = new TestAttemptBudget(2);
        var provider = CreateProvider(handler, timeoutSeconds: 2, maxAttempts: 2, retryBaseDelayMilliseconds: 0, attemptBudget: budget);

        var result = await provider.ChatWithAiAsync("xin chào", new(), new(), "{}", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.CallCount);
        Assert.Equal(2, result.ProviderAttemptCount);
        Assert.Equal(2, budget.Consumed);
    }

    [Fact]
    public async Task Attempt_budget_twelve_counts_each_attempt_across_six_retrying_cases()
    {
        var handler = new SequenceHandler(Enumerable.Range(0, 6)
            .SelectMany(_ => new[]
            {
                (Func<CancellationToken, Task<HttpResponseMessage>>)(_ => Task.FromResult(Response(HttpStatusCode.ServiceUnavailable, "{}", TimeSpan.Zero))),
                _ => Task.FromResult(Response(HttpStatusCode.OK, ValidEnvelope("đã hồi phục")))
            }).ToArray());
        var budget = new TestAttemptBudget(12);
        var provider = CreateProvider(handler, timeoutSeconds: 2, maxAttempts: 2, retryBaseDelayMilliseconds: 0, attemptBudget: budget);

        for (var index = 0; index < 6; index++)
        {
            var result = await provider.ChatWithAiAsync("xin chào", new(), new(), "{}", CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.Equal(2, result.ProviderAttemptCount);
        }

        Assert.Equal(12, handler.CallCount);
        Assert.Equal(12, budget.Consumed);
    }

    [Fact]
    public async Task Exhausted_503_reports_both_real_attempts_without_retrying_forever()
    {
        var handler = new SequenceHandler(
            _ => Task.FromResult(Response(HttpStatusCode.ServiceUnavailable, "{}", TimeSpan.Zero)),
            _ => Task.FromResult(Response(HttpStatusCode.ServiceUnavailable, "{}", TimeSpan.Zero)));
        var provider = CreateProvider(handler, timeoutSeconds: 2, maxAttempts: 2, retryBaseDelayMilliseconds: 0);

        var result = await provider.ChatWithAiAsync("xin chào", new(), new(), "{}", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AiProviderStatusContract.FailureServerError, result.FailureCode);
        Assert.Equal(2, handler.CallCount);
        Assert.Equal(2, result.ProviderAttemptCount);
    }

    [Fact]
    public async Task Disabled_provider_does_not_make_an_http_attempt()
    {
        var handler = new SequenceHandler(_ => Task.FromResult(Response(HttpStatusCode.OK, ValidEnvelope("không được gọi"))));
        var provider = CreateProvider(handler, enabled: false);

        var result = await provider.ChatWithAiAsync("xin chào", new(), new(), "{}", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, handler.CallCount);
        Assert.Equal(0, result.ProviderAttemptCount);
        Assert.False(result.ProviderWasCalled);
    }

    [Fact]
    public async Task Specialty_suggestion_path_respects_retry_after_and_recovers_within_the_same_budget()
    {
        var handler = new SequenceHandler(
            _ => Task.FromResult(Response(HttpStatusCode.TooManyRequests, "{}", TimeSpan.FromMilliseconds(60))),
            _ => Task.FromResult(Response(HttpStatusCode.OK, ValidSuggestionEnvelope())));
        var provider = CreateProvider(handler, timeoutSeconds: 2, maxAttempts: 2, retryBaseDelayMilliseconds: 0);

        var result = await provider.GetSuggestionsFromAiAsync(
            "đau đầu",
            new List<WhitelistItemDto> { new() { Code = "SP01", Name = "Nội tổng quát" } },
            CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("SP01", result[0].SpecialtyCode);
        Assert.Equal(2, handler.CallCount);
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
        Assert.Equal(2, result.ProviderAttemptCount);
        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(65), $"Retry-After was not respected: {clock.Elapsed}");
    }

    [Fact]
    public async Task Respects_retry_after_http_date_without_resetting_the_total_budget()
    {
        var first = Response(HttpStatusCode.TooManyRequests, "{}");
        first.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMilliseconds(80));
        var handler = new SequenceHandler(
            _ => Task.FromResult(first),
            _ => Task.FromResult(Response(HttpStatusCode.OK, ValidEnvelope("Đã thử lại theo HTTP date."))));
        var provider = CreateProvider(handler, timeoutSeconds: 2, maxAttempts: 2, retryBaseDelayMilliseconds: 0);
        var clock = Stopwatch.StartNew();

        var result = await provider.ChatWithAiAsync("xin chào", new(), new(), "{}", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.CallCount);
        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(55), $"Retry-After HTTP date was not respected: {clock.Elapsed}");
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Retries_transient_http_failures_within_the_same_budget(HttpStatusCode status)
    {
        var handler = new SequenceHandler(
            _ => Task.FromResult(Response(status, "{}")),
            _ => Task.FromResult(Response(HttpStatusCode.OK, ValidEnvelope("Đã hồi phục."))));
        var provider = CreateProvider(handler, timeoutSeconds: 2, maxAttempts: 2, retryBaseDelayMilliseconds: 0);

        var result = await provider.ChatWithAiAsync("xin chào", new(), new(), "{}", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Exhausted_429_preserves_bounded_retry_metadata_without_waiting_past_budget()
    {
        var handler = new SequenceHandler(_ => Task.FromResult(Response(HttpStatusCode.TooManyRequests, "{}", TimeSpan.FromSeconds(30))));
        var provider = CreateProvider(handler, timeoutSeconds: 1, maxAttempts: 2, retryBaseDelayMilliseconds: 0);

        var result = await provider.ChatWithAiAsync("xin chào", new(), new(), "{}", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("RateLimited", result.Status);
        Assert.Equal(AiProviderStatusContract.FailureRateLimited, result.FailureCode);
        Assert.True(result.Retryable);
        Assert.True(result.RetryAfterSeconds >= 29);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(1, result.ProviderAttemptCount);
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
        Assert.Equal(2, result.ProviderAttemptCount);
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
        Assert.Equal(1, result.ProviderAttemptCount);
    }

    [Fact]
    public async Task Does_not_retry_forbidden_authentication_errors()
    {
        var handler = new SequenceHandler(_ => Task.FromResult(Response(HttpStatusCode.Forbidden, "{}")));
        var provider = CreateProvider(handler, timeoutSeconds: 2, maxAttempts: 3);

        var result = await provider.ChatWithAiAsync("xin chào", new(), new(), "{}", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AuthFailure", result.Status);
        Assert.Equal(AiProviderStatusContract.FailureAuthenticationFailed, result.FailureCode);
        Assert.False(result.Retryable);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(1, result.ProviderAttemptCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Does_not_retry_bad_request_or_missing_model(HttpStatusCode status)
    {
        var handler = new SequenceHandler(_ => Task.FromResult(Response(status, "{}")));
        var provider = CreateProvider(handler, timeoutSeconds: 2, maxAttempts: 3);

        var result = await provider.ChatWithAiAsync("xin chào", new(), new(), "{}", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("InvalidModelOrEndpoint", result.Status);
        Assert.Equal(AiProviderStatusContract.FailureModelUnavailable, result.FailureCode);
        Assert.False(result.Retryable);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(1, result.ProviderAttemptCount);
    }

    [Fact]
    public async Task Retries_transient_network_exception_within_the_same_budget()
    {
        var handler = new SequenceHandler(
            _ => throw new HttpRequestException("synthetic network failure"),
            _ => Task.FromResult(Response(HttpStatusCode.OK, ValidEnvelope("đã hồi phục mạng"))));
        var provider = CreateProvider(handler, timeoutSeconds: 2, maxAttempts: 2, retryBaseDelayMilliseconds: 0);

        var result = await provider.ChatWithAiAsync("xin chào", new(), new(), "{}", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.CallCount);
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
    public async Task Rejects_empty_success_body_without_retrying_or_fabricating_a_reply()
    {
        var handler = new SequenceHandler(_ => Task.FromResult(Response(HttpStatusCode.OK, string.Empty)));
        var provider = CreateProvider(handler);

        var result = await provider.ChatWithAiAsync("xin chào", new(), new(), "{}", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AiProviderStatusContract.FailureInvalidResponse, result.FailureCode);
        Assert.Empty(result.Reply);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Does_not_count_invalid_provider_json_as_a_transient_circuit_failure()
    {
        var handler = new SequenceHandler(
            _ => Task.FromResult(Response(HttpStatusCode.OK, InvalidJsonEnvelope())),
            _ => Task.FromResult(Response(HttpStatusCode.OK, InvalidJsonEnvelope())),
            _ => Task.FromResult(Response(HttpStatusCode.OK, InvalidJsonEnvelope())),
            _ => Task.FromResult(Response(HttpStatusCode.OK, ValidEnvelope("đã gọi lại"))));
        var provider = CreateProvider(handler, timeoutSeconds: 2, maxAttempts: 1, retryBaseDelayMilliseconds: 0);
        var health = new AiProviderHealth(TimeSpan.FromSeconds(30), failureThreshold: 1);
        var planner = new GeminiStructuredPlanner(provider, health, NullLogger<GeminiStructuredPlanner>.Instance);
        var request = new AiStructuredPlannerRequest { Role = AiActorRole.Doctor, Message = "câu hỏi", AllowedToolNames = new[] { "doctor.get_my_queue" } };

        for (var i = 0; i < 3; i++)
        {
            var failed = await planner.PlanAsync(request);
            Assert.Equal(AiProviderStatusContract.FailureInvalidResponse, failed.FailureCode);
        }

        Assert.Equal("Closed", health.State);
        var recovered = await planner.PlanAsync(request);
        Assert.True(recovered.IsSuccess);
        Assert.Equal(4, handler.CallCount);
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

    [Fact]
    public async Task Half_open_allows_exactly_one_concurrent_probe_and_client_cancel_does_not_count()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var health = new AiProviderHealth(TimeSpan.FromSeconds(10), failureThreshold: 1, timeProvider: clock);
        Assert.True(health.CanAttempt());
        health.RecordFailure(AiProviderStatusContract.FailureTimeout);
        Assert.Equal("Open", health.State);

        clock.Advance(TimeSpan.FromSeconds(11));
        Assert.Equal("HalfOpen", health.State);
        var attempts = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(health.CanAttempt)));
        Assert.Equal(1, attempts.Count(x => x));

        health.RecordFailure(AiProviderStatusContract.FailureClientCancelled);
        Assert.Equal(1, health.ConsecutiveFailures);
        Assert.Equal("HalfOpen", health.State);
        Assert.True(health.CanAttempt());
    }

    private static GeminiAiProvider CreateProvider(
        HttpMessageHandler handler,
        int timeoutSeconds = 2,
        int maxAttempts = 3,
        int retryBaseDelayMilliseconds = 1,
        bool enabled = true,
        IAiProviderAttemptBudget? attemptBudget = null) => new(
        new HttpClient(handler),
        Options.Create(new AiProviderOptions
        {
            IsEnabled = enabled,
            ApiKey = "test-only-key",
            ProviderUrl = "https://fake-gemini.test",
            ModelName = "gemini-3.5-flash-lite",
            TimeoutSeconds = timeoutSeconds,
            MaxAttempts = maxAttempts,
            RetryBaseDelayMilliseconds = retryBaseDelayMilliseconds
        }),
        NullLogger<GeminiAiProvider>.Instance,
        attemptBudget: attemptBudget);

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

    private static string ValidSuggestionEnvelope() => JsonSerializer.Serialize(new
    {
        candidates = new[]
        {
            new
            {
                content = new
                {
                    parts = new[]
                    {
                        new { text = JsonSerializer.Serialize(new[] { new { specialtyCode = "SP01", reason = "Phù hợp để tra cứu ban đầu." } }) }
                    }
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

    private sealed class TestAttemptBudget : IAiProviderAttemptBudget
    {
        private int _remaining;

        public TestAttemptBudget(int budget) => _remaining = budget;
        public int Consumed { get; private set; }
        public int Remaining => _remaining;

        public bool TryReserveAttempt()
        {
            if (_remaining <= 0) return false;
            _remaining--;
            Consumed++;
            return true;
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;
        private long _timestamp;

        public ManualTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override long GetTimestamp() => _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
            _timestamp += duration.Ticks;
        }
    }
}
