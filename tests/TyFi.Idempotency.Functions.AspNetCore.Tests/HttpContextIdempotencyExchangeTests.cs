using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TyFi.Idempotency.Functions.AspNetCore.Tests;

/// <summary>
/// Reproduces the ASP.NET Core integration response-capture bug: under that integration the
/// function's <c>IActionResult</c> is executed to the response by the framework *after* this
/// middleware unwinds, so buffering <c>Response.Body</c> during <c>next</c> captures nothing.
/// The exchange must instead capture and replace <c>context.GetInvocationResult().Value</c>.
/// </summary>
public sealed class HttpContextIdempotencyExchangeTests
{
    private sealed record SampleDto(int Id, string Name);

    [Fact]
    public async Task InvokeAndCapture_captures_the_action_results_real_status_and_body()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();
        using ServiceProvider provider = services.BuildServiceProvider();

        var clientBody = new MemoryStream();
        var http = new DefaultHttpContext { RequestServices = provider };
        http.Response.Body = clientBody;

        var invocationResult = new FakeInvocationResultAccessor();
        var exchange = new HttpContextIdempotencyExchange(invocationResult, http);

        var dto = new SampleDto(42, "Therapy");

        IdempotencyCapturedResponse captured = await exchange.InvokeAndCaptureAsync(
            () =>
            {
                // Faithful to the real host: the function sets the invocation result and does
                // NOT write Response.Body. The framework executes the result after the middleware.
                invocationResult.Value = new ObjectResult(dto) { StatusCode = 201 };
                return Task.CompletedTask;
            },
            CancellationToken.None);

        // The captured (stored + replayed) status and body must be the real 201 + JSON,
        // not the default 200 with an empty body that a Response.Body swap would see.
        Assert.Equal(201, captured.StatusCode);
        Assert.Contains("\"id\":42", Encoding.UTF8.GetString(captured.Body));

        // The invocation result must have been replaced so the host delivers exactly the
        // captured bytes once, with the correct status, and no "response already started".
        var finalResult = Assert.IsAssignableFrom<IActionResult>(invocationResult.Value);
        await finalResult.ExecuteResultAsync(
            new ActionContext(http, new RouteData(), new ActionDescriptor()));

        Assert.Equal(201, http.Response.StatusCode);
        Assert.Equal(
            Encoding.UTF8.GetString(captured.Body),
            Encoding.UTF8.GetString(clientBody.ToArray()));
    }

    [Fact]
    public async Task ShortCircuit_replays_via_the_invocation_result_without_writing_the_response_body()
    {
        var clientBody = new MemoryStream();
        var http = new DefaultHttpContext();
        http.Response.Body = clientBody;

        var invocationResult = new FakeInvocationResultAccessor();
        var exchange = new HttpContextIdempotencyExchange(invocationResult, http);

        await exchange.ShortCircuitAsync(
            409,
            Encoding.UTF8.GetBytes("{\"message\":\"conflict\"}"),
            "application/json",
            retryAfterSeconds: 1,
            CancellationToken.None);

        // The middleware must not write the real response itself; it stages a result instead.
        Assert.Empty(clientBody.ToArray());

        var result = Assert.IsAssignableFrom<IActionResult>(invocationResult.Value);
        await result.ExecuteResultAsync(
            new ActionContext(http, new RouteData(), new ActionDescriptor()));

        Assert.Equal(409, http.Response.StatusCode);
        Assert.Equal("1", http.Response.Headers.RetryAfter.ToString());
        Assert.Contains("conflict", Encoding.UTF8.GetString(clientBody.ToArray()));
    }

    [Fact]
    public async Task InvokeAndCapture_captures_an_IResult_so_the_ledger_is_not_poisoned_with_an_empty_200()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        using ServiceProvider provider = services.BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = provider };
        http.Response.Body = new MemoryStream();

        var invocationResult = new FakeInvocationResultAccessor();
        var exchange = new HttpContextIdempotencyExchange(invocationResult, http);

        var dto = new SampleDto(7, "Minimal");

        IdempotencyCapturedResponse captured = await exchange.InvokeAndCaptureAsync(
            () =>
            {
                // A minimal-API style function returns IResult, not IActionResult.
                invocationResult.Value = Results.Json(dto, statusCode: 201);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        // The real 201 + body must be captured, not the default 200 + empty body that would
        // poison the store for every subsequent replay.
        Assert.Equal(201, captured.StatusCode);
        Assert.Contains("\"id\":7", Encoding.UTF8.GetString(captured.Body));
    }

    [Fact]
    public async Task InvokeAndCapture_preserves_response_headers_such_as_location()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();
        using ServiceProvider provider = services.BuildServiceProvider();

        var clientBody = new MemoryStream();
        var http = new DefaultHttpContext { RequestServices = provider };
        http.Response.Body = clientBody;

        var invocationResult = new FakeInvocationResultAccessor();
        var exchange = new HttpContextIdempotencyExchange(invocationResult, http);

        await exchange.InvokeAndCaptureAsync(
            () =>
            {
                invocationResult.Value = new CreatedResult(
                    "/api/practice/services/42", new SampleDto(42, "Therapy"));
                return Task.CompletedTask;
            },
            CancellationToken.None);

        var finalResult = Assert.IsAssignableFrom<IActionResult>(invocationResult.Value);
        await finalResult.ExecuteResultAsync(
            new ActionContext(http, new RouteData(), new ActionDescriptor()));

        Assert.Equal(201, http.Response.StatusCode);
        Assert.Equal("/api/practice/services/42", http.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task InvokeAndCapture_does_not_invent_a_content_type_for_an_empty_204()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        using ServiceProvider provider = services.BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = provider };
        http.Response.Body = new MemoryStream();

        var invocationResult = new FakeInvocationResultAccessor();
        var exchange = new HttpContextIdempotencyExchange(invocationResult, http);

        IdempotencyCapturedResponse captured = await exchange.InvokeAndCaptureAsync(
            () =>
            {
                invocationResult.Value = new NoContentResult();
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(204, captured.StatusCode);
        Assert.Empty(captured.Body);

        var finalResult = Assert.IsAssignableFrom<IActionResult>(invocationResult.Value);
        await finalResult.ExecuteResultAsync(
            new ActionContext(http, new RouteData(), new ActionDescriptor()));

        Assert.Equal(204, http.Response.StatusCode);
        Assert.True(string.IsNullOrEmpty(http.Response.ContentType));
        Assert.Null(http.Response.ContentLength);
    }

    [Fact]
    public async Task InvokeAndCapture_rejects_an_unsupported_result_rather_than_caching_an_empty_200()
    {
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        http.Response.Body = new MemoryStream();

        var invocationResult = new FakeInvocationResultAccessor();
        var exchange = new HttpContextIdempotencyExchange(invocationResult, http);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            exchange.InvokeAndCaptureAsync(
                () =>
                {
                    // Neither IActionResult nor IResult: must fail loudly so the claim rolls
                    // back instead of storing a fabricated cacheable 200.
                    invocationResult.Value = new SampleDto(1, "poco");
                    return Task.CompletedTask;
                },
                CancellationToken.None));
    }
}
