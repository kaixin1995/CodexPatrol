using CodexPatrol.Models;
using CodexPatrol.Services;
using Xunit;

namespace CodexPatrol.Tests;

public sealed class QuotaCachePolicyTests
{
    [Fact]
    public void ExtractAuthIndex_ShouldReadAuthIndexField()
    {
        var result = UsageQueueMonitor.ExtractAuthIndex("""{"auth_index":"auth-1","model":"gpt-test"}""");
        Assert.Equal("auth-1", result);
    }

    [Fact]
    public void ExtractAuthIndex_ShouldReadCamelCaseAuthIndex()
    {
        var result = UsageQueueMonitor.ExtractAuthIndex("""{"authIndex":"auth-2","timestamp":"2026-05-22T00:00:00Z"}""");
        Assert.Equal("auth-2", result);
    }

    [Fact]
    public void ExtractAuthIndex_ShouldReturnEmpty_WhenNoAuthIndex()
    {
        var result = UsageQueueMonitor.ExtractAuthIndex("""{"model":"gpt-test"}""");
        Assert.Equal("", result);
    }

    [Fact]
    public void ExtractAuthIndex_ShouldReturnEmpty_WhenInvalidJson()
    {
        var result = UsageQueueMonitor.ExtractAuthIndex("not json");
        Assert.Equal("", result);
    }

    [Fact]
    public void BuildLastUsageByAuthIndex_ShouldPickLatestTimestampPerAuthIndex()
    {
        const string rawJson = """
        {
          "apis": {
            "chatgpt": {
              "models": {
                "codex": {
                  "details": [
                    { "auth_index": "auth-a", "timestamp": "2026-05-20T08:00:00Z" },
                    { "authIndex": "auth-a", "timestamp": "2026-05-20T09:30:00Z" },
                    { "auth_index": "auth-b", "timestamp": "2026-05-20T07:00:00+08:00" }
                  ]
                }
              }
            }
          }
        }
        """;

        var result = UsageActivityAnalyzer.BuildLastUsageByAuthIndex(rawJson);

        Assert.Equal(2, result.Count);
        Assert.Equal(new DateTime(2026, 5, 20, 9, 30, 0, DateTimeKind.Utc), result["auth-a"]);
        Assert.Equal(new DateTime(2026, 5, 19, 23, 0, 0, DateTimeKind.Utc), result["auth-b"]);
    }

    [Fact]
    public void BuildLastUsageByAuthIndex_ShouldScanNestedPayloadRecursively()
    {
        const string rawJson = """
        {
          "records": [
            {
              "group": {
                "details": [
                  { "auth_index": "auth-a", "timestamp": "2026-05-20T08:00:00Z" }
                ]
              }
            },
            {
              "items": [
                {
                  "meta": {
                    "authIndex": "auth-b",
                    "timestamp": "2026-05-20T10:30:00+08:00"
                  }
                }
              ]
            }
          ]
        }
        """;

        var result = UsageActivityAnalyzer.BuildLastUsageByAuthIndex(rawJson);

        Assert.Equal(2, result.Count);
        Assert.Equal(new DateTime(2026, 5, 20, 8, 0, 0, DateTimeKind.Utc), result["auth-a"]);
        Assert.Equal(new DateTime(2026, 5, 20, 2, 30, 0, DateTimeKind.Utc), result["auth-b"]);
    }

    [Fact]
    public void TryReuseQuota_ShouldCloneCachedQuota_WhenNoNewUsageAndWindowNotReset()
    {
        var existing = CreateSnapshot(
            refreshedAt: new DateTime(2026, 5, 20, 8, 0, 0, DateTimeKind.Utc),
            resetAtUtc: new DateTime(2026, 5, 21, 8, 0, 0, DateTimeKind.Utc));
        var nowUtc = new DateTime(2026, 5, 20, 9, 0, 0, DateTimeKind.Utc);
        var lastUsageAtUtc = new DateTime(2026, 5, 20, 7, 30, 0, DateTimeKind.Utc);

        var reused = QuotaCachePolicy.TryReuseQuota(
            existing,
            displayAccount: "new-display",
            disabled: true,
            nowUtc,
            lastUsageAtUtc,
            out var snapshot,
            out var reason);

        Assert.True(reused);
        Assert.NotNull(snapshot);
        Assert.True(snapshot!.FromCache);
        Assert.Equal("new-display", snapshot.DisplayAccount);
        Assert.True(snapshot.Disabled);
        Assert.Equal(nowUtc, snapshot.CheckedAt);
        Assert.Equal(existing.RefreshedAt, snapshot.RefreshedAt);
        Assert.Equal(lastUsageAtUtc, snapshot.LastUsageAt);
        Assert.Contains("命中调用日志缓存", snapshot.CacheReason);
        Assert.Equal(snapshot.CacheReason, reason);
    }

    [Fact]
    public void TryReuseQuota_ShouldReuse_WhenNoUsageRecordExists()
    {
        var existing = CreateSnapshot(
            refreshedAt: new DateTime(2026, 5, 20, 8, 0, 0, DateTimeKind.Utc),
            resetAtUtc: new DateTime(2026, 5, 21, 8, 0, 0, DateTimeKind.Utc));
        var nowUtc = new DateTime(2026, 5, 20, 9, 0, 0, DateTimeKind.Utc);

        var reused = QuotaCachePolicy.TryReuseQuota(
            existing,
            displayAccount: "display",
            disabled: false,
            nowUtc,
            lastUsageAtUtc: null,
            out var snapshot,
            out var reason);

        Assert.True(reused);
        Assert.NotNull(snapshot);
        Assert.True(snapshot!.FromCache);
        Assert.Equal(nowUtc, snapshot.CheckedAt);
        Assert.Equal(DateTime.MinValue, snapshot.LastUsageAt);
        Assert.Contains("未发现调用记录", reason);
    }

    [Fact]
    public void HasReachedScheduledRealRefreshAt_ShouldUseStableSpreadWindow()
    {
        var existing = CreateSnapshot(
            refreshedAt: new DateTime(2026, 5, 20, 8, 0, 0, DateTimeKind.Utc),
            resetAtUtc: new DateTime(2026, 5, 21, 8, 0, 0, DateTimeKind.Utc));

        var scheduledAt = QuotaCachePolicy.GetScheduledRealRefreshAt(existing, "default", "account-1");

        Assert.NotNull(scheduledAt);
        var age = scheduledAt!.Value - existing.RefreshedAt;
        Assert.InRange(age, TimeSpan.FromHours(8), TimeSpan.FromHours(9).Add(TimeSpan.FromMinutes(50)));
        Assert.False(QuotaCachePolicy.HasReachedScheduledRealRefreshAt(existing, "default", "account-1", scheduledAt.Value.AddMinutes(-1)));
        Assert.True(QuotaCachePolicy.HasReachedScheduledRealRefreshAt(existing, "default", "account-1", scheduledAt.Value));
    }

    [Fact]
    public void TryReuseQuota_ShouldReturnFalse_WhenUsageExistsAfterRefresh()
    {
        var existing = CreateSnapshot(
            refreshedAt: new DateTime(2026, 5, 20, 8, 0, 0, DateTimeKind.Utc),
            resetAtUtc: new DateTime(2026, 5, 21, 8, 0, 0, DateTimeKind.Utc));
        var nowUtc = new DateTime(2026, 5, 20, 9, 0, 0, DateTimeKind.Utc);
        var lastUsageAtUtc = new DateTime(2026, 5, 20, 8, 30, 0, DateTimeKind.Utc);

        var reused = QuotaCachePolicy.TryReuseQuota(
            existing,
            displayAccount: "display",
            disabled: false,
            nowUtc,
            lastUsageAtUtc,
            out var snapshot,
            out var reason);

        Assert.False(reused);
        Assert.Null(snapshot);
        Assert.Equal("上次额度刷新后存在新的调用记录", reason);
    }

    [Fact]
    public void TryReuseQuota_ShouldReturnFalse_WhenResetTimeHasArrived()
    {
        var existing = CreateSnapshot(
            refreshedAt: new DateTime(2026, 5, 20, 8, 0, 0, DateTimeKind.Utc),
            resetAtUtc: new DateTime(2026, 5, 20, 8, 30, 0, DateTimeKind.Utc));
        var nowUtc = new DateTime(2026, 5, 20, 9, 0, 0, DateTimeKind.Utc);

        var reused = QuotaCachePolicy.TryReuseQuota(
            existing,
            displayAccount: "display",
            disabled: false,
            nowUtc,
            lastUsageAtUtc: null,
            out var snapshot,
            out var reason);

        Assert.False(reused);
        Assert.Null(snapshot);
        Assert.Contains("已到重置时间", reason);
    }

    [Fact]
    public void TrySkipDisabledFreeQuota_ShouldReuseCachedQuota_WhenWeeklyWindowNotReset()
    {
        var existing = CreateSnapshot(
            refreshedAt: new DateTime(2026, 5, 20, 8, 0, 0, DateTimeKind.Utc),
            resetAtUtc: new DateTime(2026, 5, 21, 8, 0, 0, DateTimeKind.Utc),
            planType: "Free",
            weeklyUsedPercent: 100);
        var nowUtc = new DateTime(2026, 5, 20, 9, 0, 0, DateTimeKind.Utc);

        var skipped = QuotaCachePolicy.TrySkipDisabledFreeQuota(
            existing,
            displayAccount: "display",
            disabled: true,
            threshold: 100,
            nowUtc,
            out var snapshot,
            out var reason);

        Assert.True(skipped);
        Assert.NotNull(snapshot);
        Assert.True(snapshot!.FromCache);
        Assert.Equal(nowUtc, snapshot.CheckedAt);
        Assert.Equal(existing.RefreshedAt, snapshot.RefreshedAt);
        Assert.Equal("Free", snapshot.PlanType);
        Assert.Contains("命中禁用免费号跳过", reason);
    }

    [Fact]
    public void TrySkipDisabledFreeQuota_ShouldReturnFalse_WhenWeeklyThresholdNotReached()
    {
        var existing = CreateSnapshot(
            refreshedAt: new DateTime(2026, 5, 20, 8, 0, 0, DateTimeKind.Utc),
            resetAtUtc: new DateTime(2026, 5, 21, 8, 0, 0, DateTimeKind.Utc),
            planType: "Free",
            weeklyUsedPercent: 80);
        var nowUtc = new DateTime(2026, 5, 20, 9, 0, 0, DateTimeKind.Utc);

        var skipped = QuotaCachePolicy.TrySkipDisabledFreeQuota(
            existing,
            displayAccount: "display",
            disabled: true,
            threshold: 100,
            nowUtc,
            out var snapshot,
            out var reason);

        Assert.False(skipped);
        Assert.Null(snapshot);
        Assert.Equal("周额度未达到停用阈值", reason);
    }

    private static CodexQuotaSnapshot CreateSnapshot(DateTime refreshedAt, DateTime resetAtUtc, string planType = "Plus", double weeklyUsedPercent = 50)
    {
        return new CodexQuotaSnapshot
        {
            AccountName = "account-1",
            DisplayAccount = "display-1",
            PlanType = planType,
            Disabled = false,
            CheckedAt = refreshedAt,
            RefreshedAt = refreshedAt,
            StatusCode = 200,
            Success = true,
            ErrorMessage = "",
            Windows =
            [
                new CodexQuotaWindowSnapshot
                {
                    Id = "weekly",
                    Label = "周限额",
                    UsedPercent = weeklyUsedPercent,
                    ResetLabel = "1天后重置",
                    LimitWindowSeconds = 604800,
                    ResetAtUtc = resetAtUtc,
                }
            ]
        };
    }
}
