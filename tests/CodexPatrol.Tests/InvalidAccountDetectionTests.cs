using CodexPatrol.Models;
using CodexPatrol.Services;
using Xunit;

namespace CodexPatrol.Tests;

public sealed class InvalidAccountDetectionTests
{
    [Fact]
    public void TryGetInvalidCredentialFailureReason_ShouldMatch401Error()
    {
        var quota = new CodexQuotaSnapshot
        {
            StatusCode = 401,
            Success = false,
            ErrorMessage = "Your authentication token has been invalidated. Please try signing in again.",
        };

        var matched = InspectionEngine.TryGetInvalidCredentialFailureReason(quota, out var reason);

        Assert.True(matched);
        Assert.Equal("额度获取失败：401 Your authentication token has been invalidated. Please try signing in again.", reason);
    }

    [Fact]
    public void TryGetInvalidCredentialFailureReason_ShouldMatchInvalidatedTokenMessage()
    {
        var quota = new CodexQuotaSnapshot
        {
            StatusCode = 500,
            Success = false,
            ErrorMessage = "Your authentication token has been invalidated. Please try signing in again.",
        };

        var matched = InspectionEngine.TryGetInvalidCredentialFailureReason(quota, out var reason);

        Assert.True(matched);
        Assert.Equal("额度获取失败：500 Your authentication token has been invalidated. Please try signing in again.", reason);
    }

    [Fact]
    public void TryGetInvalidCredentialFailureReason_ShouldIgnoreOtherErrors()
    {
        var quota = new CodexQuotaSnapshot
        {
            StatusCode = 429,
            Success = false,
            ErrorMessage = "rate limit exceeded",
        };

        var matched = InspectionEngine.TryGetInvalidCredentialFailureReason(quota, out var reason);

        Assert.False(matched);
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void FilterAutoActionItems_ShouldAlwaysIncludeInvalidCredentialDisable()
    {
        var decisions = new List<InspectionDecision>
        {
            new()
            {
                AccountName = "invalid-a",
                Action = InspectionAction.Disable,
                DisableReason = DisableReason.ErrorDisabled,
                Reason = "额度获取失败：401 Your authentication token has been invalidated. Please try signing in again.，建议禁用账号",
            },
        };

        var result = InspectionEngine.FilterAutoActionItems(AutoActionMode.None, autoEnable: false, priorityRoutingEnabled: false, decisions);

        var item = Assert.Single(result);
        Assert.Equal("invalid-a", item.AccountName);
        Assert.Equal(InspectionAction.Disable, item.Action);
        Assert.Equal(DisableReason.ErrorDisabled, item.DisableReason);
    }
}
