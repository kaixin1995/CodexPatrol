using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CodexPatrol.Models;
using CodexPatrol.Services;
using Xunit;

namespace CodexPatrol.Tests;

public sealed class CpaClientUnitTests
{
    private static PatrolSiteSettings CreateSite() => new()
    {
        CpaBaseUrl = "http://test-host",
        ManagementKey = "test-key",
        TimeoutMs = 5000,
    };

    [Fact]
    public async Task GetAuthFilesAsync_ShouldUseExpectedRouteAndParseFields()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "files": [
                    {
                      "name": "account-a",
                      "provider": "codex",
                      "auth_index": "auth-a",
                      "disabled": false,
                      "priority": 9
                    }
                  ],
                  "total": 1
                }
                """, Encoding.UTF8, "application/json")
            };
        });

        var client = new CpaClient(new HttpClient(handler));
        var result = await client.GetAuthFilesAsync(CreateSite());

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Get, capturedRequest!.Method);
        Assert.Equal("http://test-host/v0/management/auth-files", capturedRequest.RequestUri?.ToString());
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization?.Scheme);
        Assert.Equal("test-key", capturedRequest.Headers.Authorization?.Parameter);

        var file = Assert.Single(result.Files);
        Assert.Equal("account-a", file.Name);
        Assert.Equal("codex", file.Provider);
        Assert.Equal("auth-a", file.Auth_Index);
        Assert.False(file.Disabled);
        Assert.Equal(9, file.Priority);
    }

    [Fact]
    public async Task ApiCallAsync_ShouldPostExpectedPayloadAndParseResponse()
    {
        string? requestBody = null;
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            requestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "status_code": 207,
                  "bodyText": "ok"
                }
                """, Encoding.UTF8, "application/json")
            };
        });

        var client = new CpaClient(new HttpClient(handler));
        var response = await client.ApiCallAsync(CreateSite(), new ApiCallRequest
        {
            AuthIndex = "auth-a",
            Method = "POST",
            Url = "https://example.test/demo",
            Header = new Dictionary<string, string> { ["X-Test"] = "1" },
            Data = "{}",
        });

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("http://test-host/v0/management/api-call", capturedRequest.RequestUri?.ToString());
        Assert.Equal("application/json", capturedRequest.Content?.Headers.ContentType?.MediaType);
        Assert.NotNull(requestBody);

        using var document = JsonDocument.Parse(requestBody!);
        Assert.Equal("auth-a", document.RootElement.GetProperty("authIndex").GetString());
        Assert.Equal("POST", document.RootElement.GetProperty("method").GetString());
        Assert.Equal("https://example.test/demo", document.RootElement.GetProperty("url").GetString());
        Assert.Equal(207, response.Status_Code);
        Assert.Equal("ok", response.BodyText);
    }

    [Fact]
    public async Task DisableAccountAsync_ShouldPatchDisabledTrue()
    {
        string? requestBody = null;
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            requestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });

        var client = new CpaClient(new HttpClient(handler));
        await client.DisableAccountAsync(CreateSite(), "account-a");

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Patch, capturedRequest!.Method);
        Assert.Equal("http://test-host/v0/management/auth-files/status", capturedRequest.RequestUri?.ToString());
        using var document = JsonDocument.Parse(requestBody!);
        Assert.Equal("account-a", document.RootElement.GetProperty("name").GetString());
        Assert.True(document.RootElement.GetProperty("disabled").GetBoolean());
    }

    [Fact]
    public async Task EnableAccountAsync_ShouldPatchDisabledFalse()
    {
        string? requestBody = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            requestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });

        var client = new CpaClient(new HttpClient(handler));
        await client.EnableAccountAsync(CreateSite(), "account-a");

        using var document = JsonDocument.Parse(requestBody!);
        Assert.Equal("account-a", document.RootElement.GetProperty("name").GetString());
        Assert.False(document.RootElement.GetProperty("disabled").GetBoolean());
    }

    [Fact]
    public async Task DeleteAccountAsync_ShouldUseDeleteRouteWithEncodedName()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });

        var client = new CpaClient(new HttpClient(handler));
        await client.DeleteAccountAsync(CreateSite(), "account a.json");

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Delete, capturedRequest!.Method);
        Assert.Equal("http://test-host/v0/management/auth-files", capturedRequest.RequestUri?.GetLeftPart(UriPartial.Path));
        Assert.Equal("?name=account%20a.json", capturedRequest.RequestUri?.Query);
    }


    [Fact]
    public async Task PopUsageQueueAsync_ShouldParseObjectsAndJsonStrings()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  { "auth_index": "a" },
                  "{\"auth_index\":\"b\"}",
                  null,
                  "not-json"
                ]
                """, Encoding.UTF8, "application/json")
            };
        });

        var client = new CpaClient(new HttpClient(handler));
        var items = await client.PopUsageQueueAsync(CreateSite(), 0);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Get, capturedRequest!.Method);
        Assert.Equal("http://test-host/v0/management/usage-queue?count=1", capturedRequest.RequestUri?.ToString());
        Assert.Equal(2, items.Count);

        using var first = JsonDocument.Parse(items[0]);
        using var second = JsonDocument.Parse(items[1]);
        Assert.Equal("a", first.RootElement.GetProperty("auth_index").GetString());
        Assert.Equal("b", second.RootElement.GetProperty("auth_index").GetString());
    }

    [Fact]
    public async Task RequestCodexUsageAsync_ShouldComposeApiCallAndIncludeAccountHeader()
    {
        string? requestBody = null;
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            requestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "status_code": 200,
                  "bodyText": "usage-body"
                }
                """, Encoding.UTF8, "application/json")
            };
        });

        var client = new CpaClient(new HttpClient(handler));
        var result = await client.RequestCodexUsageAsync(CreateSite(), "auth-a", "acct-1");

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("http://test-host/v0/management/api-call", capturedRequest.RequestUri?.ToString());
        Assert.NotNull(requestBody);

        using var document = JsonDocument.Parse(requestBody!);
        Assert.Equal("auth-a", document.RootElement.GetProperty("authIndex").GetString());
        Assert.Equal("GET", document.RootElement.GetProperty("method").GetString());
        Assert.Equal("https://chatgpt.com/backend-api/wham/usage", document.RootElement.GetProperty("url").GetString());
        Assert.Equal("acct-1", document.RootElement.GetProperty("header").GetProperty("Chatgpt-Account-Id").GetString());
        Assert.Equal("Bearer $TOKEN$", document.RootElement.GetProperty("header").GetProperty("Authorization").GetString());
        Assert.Equal(200, result.statusCode);
        Assert.Equal("usage-body", result.body);
    }

    [Fact]
    public async Task RequestCodexUsageAsync_ShouldOmitAccountHeaderWhenAccountIdMissing()
    {
        string? requestBody = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            requestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "statusCode": 201,
                  "body": "fallback-body"
                }
                """, Encoding.UTF8, "application/json")
            };
        });

        var client = new CpaClient(new HttpClient(handler));
        var result = await client.RequestCodexUsageAsync(CreateSite(), "auth-a", null);

        Assert.NotNull(requestBody);
        using var document = JsonDocument.Parse(requestBody!);
        Assert.False(document.RootElement.GetProperty("header").TryGetProperty("Chatgpt-Account-Id", out _));
        Assert.Equal(201, result.statusCode);
        Assert.Equal("fallback-body", result.body);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(responder(request));
        }
    }
}
