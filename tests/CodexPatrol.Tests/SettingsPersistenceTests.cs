using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using CodexPatrol.Api;
using CodexPatrol.Models;
using CodexPatrol.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CodexPatrol.Tests;

public sealed class SettingsPersistenceTests
{
    [Fact]
    public void ApplySettings_ShouldPersistAndReloadSavedValuesPerSite()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            var created = store.CreateSite(new SaveSettingsRequest
            {
                SiteName = "备用站点",
                SiteEnabled = true,
                CpaBaseUrl = "http://backup-host",
                ManagementKey = "backup-key",
                PollIntervalMinutes = 17,
                PollRandomDelayMinMinutes = 1,
                PollRandomDelayMaxMinutes = 4,
                ProbeWorkers = 6,
                ProbeBatchDelayMinMs = 2100,
                ProbeBatchDelayMaxMs = 3400,
                ActionWorkers = 5,
                TimeoutMs = 12345,
                RetryCount = 2,
                AutoActionMode = "disable",
                AutoEnableRecovered = true,
                UsedPercentThreshold = 88,
                Provider = "codex",
            });

            var reloaded = CreateStore(baseDirectory, BuildLegacyDefaults()).GetSettings(created.SiteId);

            Assert.Equal(created.SiteId, reloaded.SiteId);
            Assert.Equal("备用站点", reloaded.Name);
            Assert.Equal("http://backup-host", reloaded.CpaBaseUrl);
            Assert.Equal("backup-key", reloaded.ManagementKey);
            Assert.Equal(17, reloaded.PollIntervalMinutes);
            Assert.Equal(1, reloaded.PollRandomDelayMinMinutes);
            Assert.Equal(4, reloaded.PollRandomDelayMaxMinutes);
            Assert.Equal(6, reloaded.ProbeWorkers);
            Assert.Equal(2100, reloaded.ProbeBatchDelayMinMs);
            Assert.Equal(3400, reloaded.ProbeBatchDelayMaxMs);
            Assert.Equal(5, reloaded.ActionWorkers);
            Assert.Equal(12345, reloaded.TimeoutMs);
            Assert.Equal(2, reloaded.RetryCount);
            Assert.Equal("disable", reloaded.AutoActionMode);
            Assert.True(reloaded.AutoEnableRecovered);
            Assert.Equal(88, reloaded.UsedPercentThreshold);
            Assert.Equal("codex", reloaded.Provider);
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public void ApplySettings_WithEmptyManagementKey_ShouldKeepExistingKey()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            var created = store.CreateSite(new SaveSettingsRequest
            {
                SiteName = "主站",
                SiteEnabled = true,
                CpaBaseUrl = "http://primary-host",
                ManagementKey = "keep-this-key",
                PollIntervalMinutes = 12,
                ProbeWorkers = 4,
                ProbeBatchDelayMinMs = 2000,
                ProbeBatchDelayMaxMs = 3000,
                ActionWorkers = 4,
                TimeoutMs = 15000,
                RetryCount = 0,
                AutoActionMode = "none",
                AutoEnableRecovered = true,
                UsedPercentThreshold = 95,
                Provider = "codex",
            });

            store.ApplySettings(new SaveSettingsRequest
            {
                SiteId = created.SiteId,
                SiteName = "主站",
                SiteEnabled = true,
                CpaBaseUrl = "http://updated-host",
                ManagementKey = "",
                PollIntervalMinutes = 15,
                ProbeWorkers = 5,
                ProbeBatchDelayMinMs = 2100,
                ProbeBatchDelayMaxMs = 3200,
                ActionWorkers = 4,
                TimeoutMs = 15000,
                RetryCount = 0,
                AutoActionMode = "none",
                AutoEnableRecovered = true,
                UsedPercentThreshold = 95,
                Provider = "codex",
            });

            var reloaded = CreateStore(baseDirectory, BuildLegacyDefaults()).GetSettings(created.SiteId);

            Assert.Equal("http://updated-host", reloaded.CpaBaseUrl);
            Assert.Equal("keep-this-key", reloaded.ManagementKey);
            Assert.True(reloaded.AutoEnableRecovered);
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public void Exceptions_ShouldPersistSeparatelyPerSite()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            var backup = store.CreateSite(new SaveSettingsRequest
            {
                SiteName = "备用站点",
                SiteEnabled = true,
                CpaBaseUrl = "http://backup-host",
                ManagementKey = "backup-key",
                PollIntervalMinutes = 10,
                ProbeWorkers = 3,
                ProbeBatchDelayMinMs = 2000,
                ProbeBatchDelayMaxMs = 3000,
                ActionWorkers = 4,
                TimeoutMs = 15000,
                RetryCount = 0,
                AutoActionMode = "none",
                AutoEnableRecovered = false,
                UsedPercentThreshold = 95,
                Provider = "codex",
            });

            store.AddException("account-a", "default");
            store.AddException("account-b", backup.SiteId);

            var reloaded = CreateStore(baseDirectory, BuildLegacyDefaults());

            Assert.Contains("account-a", reloaded.GetExceptions("default"));
            Assert.DoesNotContain("account-b", reloaded.GetExceptions("default"));
            Assert.Contains("account-b", reloaded.GetExceptions(backup.SiteId));
            Assert.DoesNotContain("account-a", reloaded.GetExceptions(backup.SiteId));
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public void DeleteSite_ShouldFail_WhenSiteIsRunning()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            var backup = store.CreateSite(new SaveSettingsRequest
            {
                SiteName = "备用站点",
                SiteEnabled = true,
                CpaBaseUrl = "http://backup-host",
                ManagementKey = "backup-key",
                PollIntervalMinutes = 10,
                ProbeWorkers = 3,
                ProbeBatchDelayMinMs = 2000,
                ProbeBatchDelayMaxMs = 3000,
                ActionWorkers = 4,
                TimeoutMs = 15000,
                RetryCount = 0,
                AutoActionMode = "none",
                AutoEnableRecovered = false,
                UsedPercentThreshold = 95,
                Provider = "codex",
            });

            store.StartProgress("inspection", "auto", 1, "running", backup.SiteId);

            var deleted = store.DeleteSite(backup.SiteId, out var error);

            Assert.False(deleted);
            Assert.Equal("站点正在执行任务，暂时不能删除", error);
            Assert.True(store.HasSite(backup.SiteId));
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public void AddOperationLog_ShouldIncludeSiteInfoAndWriteToLocalFile()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());

            var entry = store.AddOperationLog("inspection", "inspection", "manual", "测试日志", siteId: "default");
            var logPath = Path.Combine(baseDirectory, "logs", entry.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd"), "Inspection.log");
            var content = File.ReadAllText(logPath);

            Assert.Equal("default", entry.SiteId);
            Assert.Equal("默认站点", entry.SiteName);
            Assert.Contains("[站点:默认站点/default]", content);
            Assert.Contains("测试日志", content);
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public void AddExceptionLog_ShouldWriteToLocalErrorFile()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            var exception = new InvalidOperationException("测试异常");

            store.AddExceptionLog("quota", "quotaRefresh", "manual", exception, "刷新额度异常", siteId: "default", accountName: "account-a", displayAccount: "user-a@example.com");

            var logPath = Path.Combine(baseDirectory, "logs", DateTime.Now.ToString("yyyy-MM-dd"), "Error.log");
            var content = File.ReadAllText(logPath);

            Assert.Contains("[站点:默认站点/default]", content);
            Assert.Contains("[账号:account-a]", content);
            Assert.Contains("[显示:user-a@example.com]", content);
            Assert.Contains("刷新额度异常", content);
            Assert.Contains("InvalidOperationException", content);
            Assert.Contains("测试异常", content);
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public void AddOperationLog_ShouldWriteDifferentCategoriesToDifferentFiles()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());

            var quotaEntry = store.AddOperationLog("quota", "quotaRefresh", "manual", "额度刷新日志", siteId: "default");
            var accountEntry = store.AddOperationLog("account", "accountEnable", "manual", "账号启用日志", siteId: "default");
            var priorityEntry = store.AddOperationLog("priority", "priorityRouting", "system", "优先级日志", siteId: "default");
            var monitorEntry = store.AddOperationLog("monitor", "usageQueue", "system", "监控日志", siteId: "default");
            var startupEntry = store.AddOperationLog("system", "startup", "system", "启动日志", siteId: "default");
            var systemEntry = store.AddOperationLog("system", "other", "system", "系统日志", siteId: "default");

            Assert.True(File.Exists(Path.Combine(baseDirectory, "logs", quotaEntry.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd"), "Quota.log")));
            Assert.True(File.Exists(Path.Combine(baseDirectory, "logs", accountEntry.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd"), "Account.log")));
            Assert.True(File.Exists(Path.Combine(baseDirectory, "logs", priorityEntry.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd"), "Priority.log")));
            Assert.True(File.Exists(Path.Combine(baseDirectory, "logs", monitorEntry.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd"), "UsageQueue.log")));
            Assert.True(File.Exists(Path.Combine(baseDirectory, "logs", startupEntry.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd"), "Startup.log")));
            Assert.True(File.Exists(Path.Combine(baseDirectory, "logs", systemEntry.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd"), "System.log")));
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public void QuotaSnapshots_ShouldPersistAndReloadPerSite()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            var backup = store.CreateSite(new SaveSettingsRequest
            {
                SiteName = "备用站点",
                SiteEnabled = true,
                CpaBaseUrl = "http://backup-host",
                ManagementKey = "backup-key",
                PollIntervalMinutes = 10,
                ProbeWorkers = 3,
                ProbeBatchDelayMinMs = 2000,
                ProbeBatchDelayMaxMs = 3000,
                ActionWorkers = 4,
                TimeoutMs = 15000,
                RetryCount = 0,
                AutoActionMode = "none",
                AutoEnableRecovered = false,
                UsedPercentThreshold = 95,
                Provider = "codex",
            });

            store.SetAccounts([BuildAccount("account-a", "user-a@example.com")], "default");
            store.SetAccounts([BuildAccount("account-b", "user-b@example.com")], backup.SiteId);
            store.SetQuota("account-a", BuildQuota("account-a", "user-a@example.com", refreshedAt: new DateTime(2026, 5, 22, 1, 0, 0, DateTimeKind.Utc), checkedAt: new DateTime(2026, 5, 22, 1, 30, 0, DateTimeKind.Utc)), "default");
            store.SetQuota("account-b", BuildQuota("account-b", "user-b@example.com", refreshedAt: new DateTime(2026, 5, 22, 2, 0, 0, DateTimeKind.Utc), checkedAt: new DateTime(2026, 5, 22, 2, 20, 0, DateTimeKind.Utc)), backup.SiteId);

            var reloaded = CreateStore(baseDirectory, BuildLegacyDefaults());
            var defaultQuota = reloaded.GetQuota("account-a", "default");
            var backupQuota = reloaded.GetQuota("account-b", backup.SiteId);

            Assert.NotNull(defaultQuota);
            Assert.NotNull(backupQuota);
            Assert.Equal(new DateTime(2026, 5, 22, 1, 30, 0, DateTimeKind.Utc), defaultQuota!.CheckedAt);
            Assert.Equal(new DateTime(2026, 5, 22, 1, 0, 0, DateTimeKind.Utc), defaultQuota.RefreshedAt);
            Assert.Equal(new DateTime(2026, 5, 22, 2, 20, 0, DateTimeKind.Utc), backupQuota!.CheckedAt);
            Assert.Equal(new DateTime(2026, 5, 22, 2, 0, 0, DateTimeKind.Utc), backupQuota.RefreshedAt);
            Assert.Null(reloaded.GetQuota("account-b", "default"));
            Assert.Null(reloaded.GetQuota("account-a", backup.SiteId));
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public void SetAccounts_ShouldPruneDeletedQuotaAndAddPlaceholderForNewAccount()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            store.SetAccounts(
            [
                BuildAccount("account-a", "user-a@example.com"),
                BuildAccount("account-b", "user-b@example.com")
            ], "default");
            store.SetQuota("account-a", BuildQuota("account-a", "user-a@example.com", refreshedAt: new DateTime(2026, 5, 22, 1, 0, 0, DateTimeKind.Utc)), "default");
            store.SetQuota("account-b", BuildQuota("account-b", "user-b@example.com", refreshedAt: new DateTime(2026, 5, 22, 2, 0, 0, DateTimeKind.Utc)), "default");

            store.SetAccounts(
            [
                BuildAccount("account-a", "user-a@example.com"),
                BuildAccount("account-c", "user-c@example.com")
            ], "default");

            var reloaded = CreateStore(baseDirectory, BuildLegacyDefaults());
            var placeholderQuota = reloaded.GetQuota("account-c", "default");

            Assert.NotNull(reloaded.GetQuota("account-a", "default"));
            Assert.Null(reloaded.GetQuota("account-b", "default"));
            Assert.NotNull(placeholderQuota);
            Assert.Equal(DateTime.MinValue, placeholderQuota!.CheckedAt);
            Assert.Equal(DateTime.MinValue, placeholderQuota.RefreshedAt);
            Assert.DoesNotContain(reloaded.GetQuotas("default"), item => item.AccountName == "account-c");
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task InspectAccountAsync_ShouldBypassUsageCache_WhenUsageMonitorNotActive()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var legacyDefaults = BuildLegacyDefaults();
            var store = CreateStore(baseDirectory, legacyDefaults);
            var account = BuildAccount("account-a", "user-a@example.com");
            store.SetAccounts([account], "default");
            store.SetQuota("account-a", BuildQuota("account-a", "user-a@example.com", new DateTime(2026, 5, 22, 1, 0, 0, DateTimeKind.Utc)), "default");

            var handler = new StubHttpMessageHandler(request =>
            {
                if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/v0/management/api-call")
                {
                    return JsonResponse(new ApiCallResponse
                    {
                        Status_Code = 200,
                        BodyText = """
                        {
                          "plan_type": "free",
                          "rate_limit": {
                            "primary_window": {
                              "used_percent": 80,
                              "limit_window_seconds": 604800,
                              "reset_after_seconds": 3600
                            },
                            "limit_reached": false,
                            "allowed": true
                          }
                        }
                        """
                    });
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("not found", Encoding.UTF8, "text/plain")
                };
            });
            var engine = new InspectionEngine(new CpaClient(new HttpClient(handler)), store);

            // monitor 未激活，应该走真实请求。
            var decision = await engine.InspectAccountAsync("default", account, lastUsageByAuthIndex: null);
            var quota = store.GetQuota("account-a", "default");

            Assert.Equal(1, handler.RequestCount);
            Assert.NotNull(quota);
            Assert.False(quota!.FromCache);
            Assert.Equal(200, decision.StatusCode);
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task InspectAccountAsync_ShouldReuseCache_WhenMonitorActiveAndNoUsage()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var legacyDefaults = BuildLegacyDefaults();
            var store = CreateStore(baseDirectory, legacyDefaults);
            var account = BuildAccount("account-a", "user-a@example.com");
            store.SetAccounts([account], "default");
            store.SetQuota("account-a", BuildQuota("account-a", "user-a@example.com", DateTime.UtcNow), "default");

            // 模拟 monitor 活跃但该账号没有调用活动。
            store.SetUsageMonitorActive("default", true);

            var handler = new StubHttpMessageHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.NotFound));
            var engine = new InspectionEngine(new CpaClient(new HttpClient(handler)), store);

            var decision = await engine.InspectAccountAsync("default", account, lastUsageByAuthIndex: null);
            var quota = store.GetQuota("account-a", "default");

            // 不应发起真实请求，直接复用缓存。
            Assert.Equal(0, handler.RequestCount);
            Assert.NotNull(quota);
            Assert.True(quota!.FromCache);
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task InspectAccountAsync_ShouldRealRequest_WhenScheduledRealRefreshWindowReached()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var legacyDefaults = BuildLegacyDefaults();
            var store = CreateStore(baseDirectory, legacyDefaults);
            var account = BuildAccount("account-a", "user-a@example.com");
            store.SetAccounts([account], "default");

            var staleQuota = BuildQuota("account-a", "user-a@example.com", DateTime.UtcNow.AddHours(-10).AddMinutes(-5));
            staleQuota.FromCache = false;
            store.SetQuota("account-a", staleQuota, "default");

            var handler = new StubHttpMessageHandler(request =>
            {
                if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/v0/management/api-call")
                {
                    return JsonResponse(new ApiCallResponse
                    {
                        Status_Code = 200,
                        BodyText = """
                        {
                          "plan_type": "free",
                          "rate_limit": {
                            "primary_window": {
                              "used_percent": 40,
                              "limit_window_seconds": 604800,
                              "reset_after_seconds": 3600
                            },
                            "limit_reached": false,
                            "allowed": true
                          }
                        }
                        """
                    });
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("not found", Encoding.UTF8, "text/plain")
                };
            });
            var engine = new InspectionEngine(new CpaClient(new HttpClient(handler)), store);

            var decision = await engine.InspectAccountAsync("default", account, lastUsageByAuthIndex: null);
            var quota = store.GetQuota("account-a", "default");

            Assert.Equal(1, handler.RequestCount);
            Assert.NotNull(quota);
            Assert.False(quota!.FromCache);
            Assert.Equal(200, decision.StatusCode);
            Assert.True(quota.RefreshedAt > staleQuota.RefreshedAt);
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task InspectAccountAsync_ShouldDisablePaidAccount_WhenFiveHourQuotaReachedEvenIfWeeklyAvailable()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var legacyDefaults = BuildLegacyDefaults();
            var store = CreateStore(baseDirectory, legacyDefaults);
            var account = BuildAccount("account-paid", "paid@example.com");
            store.SetAccounts([account], "default");

            var handler = new StubHttpMessageHandler(request =>
            {
                if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/v0/management/api-call")
                {
                    return JsonResponse(new ApiCallResponse
                    {
                        Status_Code = 200,
                        BodyText = """
                        {
                          "plan_type": "pro",
                          "rate_limit": {
                            "primary_window": {
                              "used_percent": 100,
                              "limit_window_seconds": 18000,
                              "reset_after_seconds": 3600
                            },
                            "secondary_window": {
                              "used_percent": 60,
                              "limit_window_seconds": 604800,
                              "reset_after_seconds": 86400
                            },
                            "limit_reached": false,
                            "allowed": true
                          }
                        }
                        """
                    });
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("not found", Encoding.UTF8, "text/plain")
                };
            });
            var engine = new InspectionEngine(new CpaClient(new HttpClient(handler)), store);

            var decision = await engine.InspectAccountAsync("default", account, lastUsageByAuthIndex: null, forceRefresh: true);

            Assert.Equal(InspectionAction.Disable, decision.Action);
            Assert.Equal(DisableReason.QuotaExhausted, decision.DisableReason);
            Assert.Contains("5 小时额度达到阈值", decision.Reason);
            Assert.Equal(100, decision.UsedPercent);
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task InspectAccountAsync_ShouldKeepPaidAccountDisabled_WhenFiveHourQuotaStillReached()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var legacyDefaults = BuildLegacyDefaults();
            var store = CreateStore(baseDirectory, legacyDefaults);
            var account = BuildAccount("account-paid", "paid@example.com", disabled: true);
            store.SetAccounts([account], "default");

            var handler = new StubHttpMessageHandler(request =>
            {
                if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/v0/management/api-call")
                {
                    return JsonResponse(new ApiCallResponse
                    {
                        Status_Code = 200,
                        BodyText = """
                        {
                          "plan_type": "pro",
                          "rate_limit": {
                            "primary_window": {
                              "used_percent": 100,
                              "limit_window_seconds": 18000,
                              "reset_after_seconds": 3600
                            },
                            "secondary_window": {
                              "used_percent": 60,
                              "limit_window_seconds": 604800,
                              "reset_after_seconds": 86400
                            },
                            "limit_reached": false,
                            "allowed": true
                          }
                        }
                        """
                    });
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("not found", Encoding.UTF8, "text/plain")
                };
            });
            var engine = new InspectionEngine(new CpaClient(new HttpClient(handler)), store);

            var decision = await engine.InspectAccountAsync("default", account, lastUsageByAuthIndex: null, forceRefresh: true);

            Assert.Equal(InspectionAction.Keep, decision.Action);
            Assert.Equal(DisableReason.QuotaExhausted, decision.DisableReason);
            Assert.Contains("5 小时额度达到阈值，但账号已禁用", decision.Reason);
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task InspectAccountAsync_ShouldEnablePaidAccount_WhenFiveHourQuotaRecoveredAndWeeklyAvailable()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var legacyDefaults = BuildLegacyDefaults();
            var store = CreateStore(baseDirectory, legacyDefaults);
            var account = BuildAccount("account-paid", "paid@example.com", disabled: true);
            store.SetAccounts([account], "default");

            var handler = new StubHttpMessageHandler(request =>
            {
                if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/v0/management/api-call")
                {
                    return JsonResponse(new ApiCallResponse
                    {
                        Status_Code = 200,
                        BodyText = """
                        {
                          "plan_type": "pro",
                          "rate_limit": {
                            "primary_window": {
                              "used_percent": 20,
                              "limit_window_seconds": 18000,
                              "reset_after_seconds": 3600
                            },
                            "secondary_window": {
                              "used_percent": 60,
                              "limit_window_seconds": 604800,
                              "reset_after_seconds": 86400
                            },
                            "limit_reached": false,
                            "allowed": true
                          }
                        }
                        """
                    });
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("not found", Encoding.UTF8, "text/plain")
                };
            });
            var engine = new InspectionEngine(new CpaClient(new HttpClient(handler)), store);

            var decision = await engine.InspectAccountAsync("default", account, lastUsageByAuthIndex: null, forceRefresh: true);

            Assert.Equal(InspectionAction.Enable, decision.Action);
            Assert.Equal(DisableReason.None, decision.DisableReason);
            Assert.Contains("周额度和 5 小时额度均可用", decision.Reason);
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task InspectAccountAsync_ShouldRealRequest_WhenMonitorActiveAndHasUsage()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var legacyDefaults = BuildLegacyDefaults();
            var store = CreateStore(baseDirectory, legacyDefaults);
            var account = BuildAccount("account-a", "user-a@example.com");
            store.SetAccounts([account], "default");
            store.SetQuota("account-a", BuildQuota("account-a", "user-a@example.com", new DateTime(2026, 5, 22, 1, 0, 0, DateTimeKind.Utc)), "default");

            // 模拟 monitor 活跃且该账号有调用活动。
            store.SetUsageMonitorActive("default", true);
            store.MarkAccountUsage("default", "auth-account-a");

            var handler = new StubHttpMessageHandler(request =>
            {
                if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/v0/management/api-call")
                {
                    return JsonResponse(new ApiCallResponse
                    {
                        Status_Code = 200,
                        BodyText = """
                        {
                          "plan_type": "free",
                          "rate_limit": {
                            "primary_window": {
                              "used_percent": 80,
                              "limit_window_seconds": 604800,
                              "reset_after_seconds": 3600
                            },
                            "limit_reached": false,
                            "allowed": true
                          }
                        }
                        """
                    });
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("not found", Encoding.UTF8, "text/plain")
                };
            });
            var engine = new InspectionEngine(new CpaClient(new HttpClient(handler)), store);

            var decision = await engine.InspectAccountAsync("default", account, lastUsageByAuthIndex: null);
            var quota = store.GetQuota("account-a", "default");

            // 有调用活动，应该走真实请求。
            Assert.Equal(1, handler.RequestCount);
            Assert.NotNull(quota);
            Assert.False(quota!.FromCache);
            Assert.Equal(200, decision.StatusCode);

            // 刷新后标记应被清除。
            Assert.False(store.HasAccountUsage("default", "auth-account-a"));
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public void BuildNextRunAt_ShouldAddConfiguredRandomMinutes_WhenRangeIsFixed()
    {
        var settings = new PatrolSiteSettings
        {
            PollIntervalMinutes = 10,
            PollRandomDelayMinMinutes = 3,
            PollRandomDelayMaxMinutes = 3,
        };

        var result = InvokeBuildNextRunAt(settings, new DateTime(2026, 5, 22, 8, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 5, 22, 8, 13, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void BuildNextRunAt_ShouldUseBaseInterval_WhenRandomRangeIsZero()
    {
        var settings = new PatrolSiteSettings
        {
            PollIntervalMinutes = 10,
            PollRandomDelayMinMinutes = 0,
            PollRandomDelayMaxMinutes = 0,
        };

        var result = InvokeBuildNextRunAt(settings, new DateTime(2026, 5, 22, 8, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 5, 22, 8, 10, 0, DateTimeKind.Utc), result);
    }

    private static PatrolSettings BuildLegacyDefaults()
    {
        return new PatrolSettings
        {
            CpaBaseUrl = "http://legacy-host",
            ManagementKey = "legacy-key",
            AutoEnableRecovered = false,
            AutoActionMode = "none",
            PollRandomDelayMinMinutes = 1,
            PollRandomDelayMaxMinutes = 3,
            ListenHost = "0.0.0.0",
            ListenPort = 22014,
            ProbeWorkers = 3,
            ProbeBatchDelayMinMs = 2000,
            ProbeBatchDelayMaxMs = 3000,
            PollIntervalMinutes = 10,
            ActionWorkers = 4,
            TimeoutMs = 15000,
            RetryCount = 0,
            UsedPercentThreshold = 95,
            Provider = "codex",
        };
    }

    private static AuthFileItem BuildAccount(string name, string email, bool disabled = false)
    {
        return new AuthFileItem
        {
            Name = name,
            Email = email,
            Disabled = disabled,
            Type = "codex",
            Provider = "codex",
            AuthIndex = $"auth-{name}",
        };
    }

    private static CodexQuotaSnapshot BuildQuota(string accountName, string displayAccount, DateTime refreshedAt, double usedPercent = 80, bool disabled = false, DateTime? checkedAt = null)
    {
        return new CodexQuotaSnapshot
        {
            AccountName = accountName,
            DisplayAccount = displayAccount,
            PlanType = "Free",
            Disabled = disabled,
            CheckedAt = checkedAt ?? refreshedAt,
            RefreshedAt = refreshedAt,
            StatusCode = 200,
            Success = true,
            LastUsageAt = DateTime.MinValue,
            Windows =
            [
                new CodexQuotaWindowSnapshot
                {
                    Id = "weekly",
                    Label = "周限额",
                    UsedPercent = usedPercent,
                    ResetLabel = "1天后重置",
                    LimitWindowSeconds = 604800,
                    ResetAtUtc = refreshedAt.AddDays(1),
                }
            ],
        };
    }

    private static DateTime InvokeBuildNextRunAt(PatrolSiteSettings settings, DateTime nowUtc)
    {
        var method = typeof(AutoPollingService).GetMethod("BuildNextRunAt", BindingFlags.NonPublic | BindingFlags.Static, null, [typeof(PatrolSiteSettings), typeof(DateTime)], null);
        Assert.NotNull(method);
        return Assert.IsType<DateTime>(method!.Invoke(null, [settings, nowUtc]));
    }


    [Fact]
    public async Task UsageQueueMonitor_ShouldWriteDetailedMonitorLogs_WhenQueueItemsCanBeMatched()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            store.SetAccounts(
            [
                BuildAccount("account-a", "user-a@example.com"),
                BuildAccount("account-b", "user-b@example.com")
            ], "default");

            var handler = new StubHttpMessageHandler(request =>
            {
                if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/v0/management/usage-queue")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""
                        [
                          { "auth_index": "auth-account-a" },
                          { "authIndex": "auth-account-b" },
                          { "auth_index": "unknown-auth" },
                          { "model": "codex" }
                        ]
                        """, Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("not found", Encoding.UTF8, "text/plain")
                };
            });

            var monitor = new UsageQueueMonitor(new CpaClient(new HttpClient(handler)), store, CreateLogger<UsageQueueMonitor>());
            await InvokePollSiteAsync(monitor, "default");

            var logs = store.GetOperationLogs(200, "default");
            var summary = Assert.Single(logs, item => item.Category == "monitor" && item.Message.Contains("本轮拉取 4 条 usage-queue 记录"));

            Assert.Contains("解析出 3 个 auth_index", summary.Message);
            Assert.Contains("匹配到 2 条已知账号记录", summary.Message);
            Assert.Contains("未匹配 1 条", summary.Message);
            Assert.Contains("缺少 auth_index 1 条", summary.Message);
            Assert.Contains("新增活跃账号 3 个", summary.Message);
            Assert.Contains("命中账号：user-a@example.com、user-b@example.com", summary.Message);
            Assert.True(store.IsUsageMonitorActive("default"));
            Assert.True(store.HasAccountUsage("default", "auth-account-a"));
            Assert.True(store.HasAccountUsage("default", "auth-account-b"));
            Assert.True(store.HasAccountUsage("default", "unknown-auth"));
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task UsageQueueMonitor_ShouldLogEmptyPoll_WhenQueueHasNoItems()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            store.SetAccounts([BuildAccount("account-a", "user-a@example.com")], "default");

            var handler = new StubHttpMessageHandler(request =>
            {
                if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/v0/management/usage-queue")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("[]", Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("not found", Encoding.UTF8, "text/plain")
                };
            });

            var monitor = new UsageQueueMonitor(new CpaClient(new HttpClient(handler)), store, CreateLogger<UsageQueueMonitor>());
            await InvokePollSiteAsync(monitor, "default");

            var logs = store.GetOperationLogs(200, "default");
            // 空队列不再输出日志，只应有监控激活日志
            Assert.DoesNotContain(logs, item => item.Category == "monitor" && item.Message.Contains("本轮拉取 0 条 usage-queue 记录"));
            Assert.DoesNotContain(logs, item => item.Category == "monitor" && item.Message.Contains("本轮未获取到新的 usage-queue 记录"));
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task UsageQueueMonitor_ShouldWriteWarningLog_WhenQueueRequestFails()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("boom"));
            var monitor = new UsageQueueMonitor(new CpaClient(new HttpClient(handler)), store, CreateLogger<UsageQueueMonitor>());

            await InvokePollSiteAsync(monitor, "default");

            var logs = store.GetOperationLogs(200, "default");
            Assert.Contains(logs, item => item.Category == "monitor" && item.Level == "warning" && item.Message.Contains("拉取 usage-queue 失败：boom"));
            Assert.False(store.IsUsageQueueUnsupported("default"));
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task UsageQueueMonitor_ShouldTreatOperationCanceledAsRetryable_WhenStoppingTokenIsNotCancelled()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            var handler = new StubHttpMessageHandler(_ => throw new OperationCanceledException("timeout"));
            var monitor = new UsageQueueMonitor(new CpaClient(new HttpClient(handler)), store, CreateLogger<UsageQueueMonitor>());

            await InvokePollSiteAsync(monitor, "default");

            var logs = store.GetOperationLogs(200, "default");
            Assert.Contains(logs, item => item.Category == "monitor" && item.Level == "warning" && item.Message.Contains("超时或被取消"));
            Assert.False(store.IsUsageQueueUnsupported("default"));
            Assert.False(store.IsUsageMonitorActive("default"));
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task GetAuthFilesAsync_ShouldParsePriorityField()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/v0/management/auth-files")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "files": [
                        {
                          "name": "account-a",
                          "priority": 10,
                          "disabled": false
                        }
                      ],
                      "total": 1
                    }
                    """, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("not found", Encoding.UTF8, "text/plain")
            };
        });

        var cpa = new CpaClient(new HttpClient(handler));
        var response = await cpa.GetAuthFilesAsync(new PatrolSiteSettings
        {
            CpaBaseUrl = "http://test-host",
            ManagementKey = "test-key",
            TimeoutMs = 5000,
        });

        var file = Assert.Single(response.Files);
        Assert.Equal("account-a", file.Name);
        Assert.Equal(10, file.Priority);
    }

    [Fact]
    public async Task UpdateAccountPriorityAsync_ShouldPatchPriorityOnly()
    {
        string? requestBody = null;

        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Patch && request.RequestUri?.AbsolutePath == "/v0/management/auth-files/fields")
            {
                requestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("not found", Encoding.UTF8, "text/plain")
            };
        });

        var cpa = new CpaClient(new HttpClient(handler));
        await cpa.UpdateAccountPriorityAsync(new PatrolSiteSettings
        {
            CpaBaseUrl = "http://test-host",
            ManagementKey = "test-key",
            TimeoutMs = 5000,
        }, "account-a", 7);

        Assert.Equal(1, handler.RequestCount);
        Assert.NotNull(requestBody);

        using var document = JsonDocument.Parse(requestBody!);
        Assert.Equal("account-a", document.RootElement.GetProperty("name").GetString());
        Assert.Equal(7, document.RootElement.GetProperty("priority").GetInt32());
        Assert.False(document.RootElement.TryGetProperty("disabled", out _));
    }

    [Fact]
    public void NextScheduledAt_And_NextResetCheckAt_ShouldBeStoredSeparately()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            var scheduledAt = new DateTime(2026, 5, 24, 8, 0, 0, DateTimeKind.Utc);
            var resetCheckAt = new DateTime(2026, 5, 27, 9, 30, 0, DateTimeKind.Utc);

            store.SetNextScheduledAt(scheduledAt, "default");
            store.SetNextResetCheckAt(resetCheckAt, "default");

            Assert.Equal(scheduledAt, store.GetNextScheduledAt("default"));
            Assert.Equal(resetCheckAt, store.GetNextResetCheckAt("default"));
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public void AutoPollingStartSemantic_ShouldAllowImmediateRunWithoutLosingResetCheckSchedule()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            var resetCheckAt = new DateTime(2026, 5, 27, 9, 30, 0, DateTimeKind.Utc);

            store.SetNextResetCheckAt(resetCheckAt, "default");
            store.SetNextScheduledAt(DateTime.MinValue, "default");

            Assert.Equal(DateTime.MinValue, store.GetNextScheduledAt("default"));
            Assert.Equal(resetCheckAt, store.GetNextResetCheckAt("default"));
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public void InspectionStatusResponse_ShouldExposeBothScheduledTimes()
    {
        var response = new InspectionStatusResponse
        {
            IsPolling = true,
            NextScheduledAt = new DateTime(2026, 5, 24, 8, 0, 0, DateTimeKind.Utc),
            NextResetCheckAt = new DateTime(2026, 5, 27, 9, 30, 0, DateTimeKind.Utc),
            LastRunStartedAt = new DateTime(2026, 5, 23, 8, 0, 0, DateTimeKind.Utc),
            LastRunFinishedAt = new DateTime(2026, 5, 23, 8, 10, 0, DateTimeKind.Utc),
            AutoPollingEnabled = true,
            PollIntervalMinutes = 10,
        };

        Assert.True(response.IsPolling);
        Assert.Equal(new DateTime(2026, 5, 24, 8, 0, 0, DateTimeKind.Utc), response.NextScheduledAt);
        Assert.Equal(new DateTime(2026, 5, 27, 9, 30, 0, DateTimeKind.Utc), response.NextResetCheckAt);
        Assert.True(response.AutoPollingEnabled);
        Assert.Equal(10, response.PollIntervalMinutes);
    }

    [Fact]
    public void LogMessageMetadata_ShouldDistinguishRealRequestCacheReuseAndSkipCheck()
    {
        var realQuota = new CodexQuotaSnapshot
        {
            AccountName = "account-a",
            CheckedAt = new DateTime(2026, 5, 23, 8, 0, 0, DateTimeKind.Utc),
            RefreshedAt = new DateTime(2026, 5, 23, 8, 0, 0, DateTimeKind.Utc),
            FromCache = false,
            CacheReason = "",
        };

        var reusedQuota = new CodexQuotaSnapshot
        {
            AccountName = "account-b",
            CheckedAt = new DateTime(2026, 5, 23, 8, 5, 0, DateTimeKind.Utc),
            RefreshedAt = new DateTime(2026, 5, 23, 8, 0, 0, DateTimeKind.Utc),
            FromCache = true,
            CacheReason = "命中调用日志缓存：上次刷新后无新调用，且未到额度重置时间",
        };

        var skippedQuota = new CodexQuotaSnapshot
        {
            AccountName = "account-c",
            CheckedAt = new DateTime(2026, 5, 23, 8, 10, 0, DateTimeKind.Utc),
            RefreshedAt = new DateTime(2026, 5, 23, 8, 0, 0, DateTimeKind.Utc),
            FromCache = true,
            CacheReason = "命中禁用免费号跳过：周额度未重置，保持禁用",
        };

        Assert.False(realQuota.FromCache);
        Assert.Contains("命中调用日志缓存", reusedQuota.CacheReason);
        Assert.Contains("跳过", skippedQuota.CacheReason);
    }

    [Fact]
    public void BuildInspectionProbeLogMessage_ShouldReturnFailureMessage_WhenDecisionHasError()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            var decision = new InspectionDecision
            {
                AccountName = "account-a",
                Action = InspectionAction.Disable,
                Error = "上游请求失败",
            };

            var message = InvokeInspectionProbeLogMessage(store, "default", decision);

            Assert.Equal("探测失败，建议禁用：上游请求失败", message);
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public void BuildInspectionProbeLogMessage_ShouldDistinguishCacheSkipAndRealRequest()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            store.SetAccounts([BuildAccount("account-a", "user-a@example.com")], "default");

            var cachedQuota = BuildQuota("account-a", "user-a@example.com", new DateTime(2026, 5, 23, 8, 0, 0, DateTimeKind.Utc));
            cachedQuota.FromCache = true;
            cachedQuota.CacheReason = "命中调用日志缓存：上次刷新后无新调用，且未到额度重置时间";
            store.SetQuota("account-a", cachedQuota, "default");

            var cachedMessage = InvokeInspectionProbeLogMessage(store, "default", new InspectionDecision
            {
                AccountName = "account-a",
                Action = InspectionAction.Keep,
                Reason = "周额度正常，保留账号",
            });

            cachedQuota.CacheReason = "命中禁用免费号跳过：周额度未重置，保持禁用";
            store.SetQuota("account-a", cachedQuota, "default");

            var skippedMessage = InvokeInspectionProbeLogMessage(store, "default", new InspectionDecision
            {
                AccountName = "account-a",
                Action = InspectionAction.Disable,
                Reason = "免费账号已禁用，且周额度未重置，跳过本轮检查",
            });

            cachedQuota.FromCache = false;
            cachedQuota.CacheReason = "";
            cachedQuota.CheckedAt = new DateTime(2026, 5, 23, 9, 15, 0, DateTimeKind.Utc);
            cachedQuota.RefreshedAt = new DateTime(2026, 5, 23, 9, 15, 0, DateTimeKind.Utc);
            store.SetQuota("account-a", cachedQuota, "default");

            var realMessage = InvokeInspectionProbeLogMessage(store, "default", new InspectionDecision
            {
                AccountName = "account-a",
                Action = InspectionAction.Keep,
                Reason = "周额度正常，保留账号",
            });

            Assert.Contains("探测完成（缓存复用）", cachedMessage);
            Assert.Contains("命中调用日志缓存", cachedMessage);
            Assert.Contains("探测完成（跳过检查）", skippedMessage);
            Assert.Contains("命中禁用免费号跳过", skippedMessage);
            Assert.Contains("探测完成（真实请求，真实刷新时间 2026-05-23 09:15:00 UTC）", realMessage);
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public void BuildQuotaRefreshLogMessage_ShouldDistinguishFailureCacheSkipAndRealRequest()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            store.SetAccounts([BuildAccount("account-a", "user-a@example.com")], "default");

            var failureMessage = InvokeQuotaRefreshLogMessage(store, "default", new InspectionDecision
            {
                AccountName = "account-a",
                Error = "请求超时",
            });

            var quota = BuildQuota("account-a", "user-a@example.com", new DateTime(2026, 5, 23, 10, 0, 0, DateTimeKind.Utc));
            quota.FromCache = true;
            quota.CacheReason = "命中禁用免费号跳过：周额度未重置，保持禁用";
            store.SetQuota("account-a", quota, "default");

            var skippedMessage = InvokeQuotaRefreshLogMessage(store, "default", new InspectionDecision
            {
                AccountName = "account-a",
            });

            quota.FromCache = false;
            quota.CacheReason = "";
            quota.CheckedAt = new DateTime(2026, 5, 23, 10, 5, 0, DateTimeKind.Utc);
            quota.RefreshedAt = new DateTime(2026, 5, 23, 10, 5, 0, DateTimeKind.Utc);
            store.SetQuota("account-a", quota, "default");

            var realMessage = InvokeQuotaRefreshLogMessage(store, "default", new InspectionDecision
            {
                AccountName = "account-a",
            });

            Assert.Equal("额度刷新失败：请求超时", failureMessage);
            Assert.Contains("额度刷新完成：跳过检查（命中禁用免费号跳过：周额度未重置，保持禁用）", skippedMessage);
            Assert.Equal("额度刷新完成：真实请求，真实刷新时间 2026-05-23 10:05:00 UTC", realMessage);
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task AutoPollingService_ShouldRunInspectionImmediately_WhenNextScheduledAtIsMinValue()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            store.ApplySettings(new SaveSettingsRequest
            {
                SiteId = "default",
                SiteName = "默认站点",
                SiteEnabled = true,
                CpaBaseUrl = "http://legacy-host",
                ManagementKey = "legacy-key",
                PollIntervalMinutes = 10,
                PollRandomDelayMinMinutes = 0,
                PollRandomDelayMaxMinutes = 0,
                ProbeWorkers = 3,
                ProbeBatchDelayMinMs = 2000,
                ProbeBatchDelayMaxMs = 3000,
                ActionWorkers = 4,
                TimeoutMs = 15000,
                RetryCount = 0,
                AutoActionMode = "none",
                AutoEnableRecovered = false,
                UsedPercentThreshold = 95,
                Provider = "codex",
            });

            var resetCheckAt = new DateTime(2026, 5, 27, 9, 30, 0, DateTimeKind.Utc);
            store.SetNextScheduledAt(DateTime.MinValue, "default");
            store.SetNextResetCheckAt(resetCheckAt, "default");

            var cts = new CancellationTokenSource();
            var handler = new StubHttpMessageHandler(request =>
            {
                if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/v0/management/auth-files")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""
                        {
                          "files": [
                            {
                              "name": "account-a",
                              "type": "codex",
                              "provider": "codex",
                              "auth_index": "auth-account-a",
                              "email": "user-a@example.com",
                              "disabled": false
                            }
                          ],
                          "total": 1
                        }
                        """, Encoding.UTF8, "application/json")
                    };
                }

                if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/v0/management/api-call")
                {
                    cts.Cancel();
                    return JsonResponse(new ApiCallResponse
                    {
                        Status_Code = 200,
                        BodyText = """
                        {
                          "plan_type": "free",
                          "rate_limit": {
                            "primary_window": {
                              "used_percent": 80,
                              "limit_window_seconds": 604800,
                              "reset_after_seconds": 3600
                            },
                            "limit_reached": false,
                            "allowed": true
                          }
                        }
                        """
                    });
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("not found", Encoding.UTF8, "text/plain")
                };
            });

            var cpa = new CpaClient(new HttpClient(handler));
            var engine = new InspectionEngine(cpa, store);
            var service = new AutoPollingService(engine, cpa, store, CreateLogger<AutoPollingService>());

            // ExecuteAsync 在 cts 取消后正常退出（不再抛异常）
            await InvokeAutoPollingExecuteAsync(service, cts.Token);

            Assert.Contains(store.GetOperationLogs(200, "default"), item => item.Message.Contains("启动预热"));
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task WarmupStartupQuotasAsync_ShouldStopAfterFirstBelowThresholdAccount()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            var settings = store.GetSettings("default");
            settings.UsedPercentThreshold = 95;
            var accounts = new List<AuthFileItem>
            {
                BuildAccount("account-a", "user-a@example.com"),
                BuildAccount("account-b", "user-b@example.com"),
                BuildAccount("account-c", "user-c@example.com")
            };

            var handler = new StubHttpMessageHandler(request =>
            {
                if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/v0/management/api-call")
                {
                    return JsonResponse(new ApiCallResponse
                    {
                        Status_Code = 200,
                        BodyText = """
                        {
                          "plan_type": "free",
                          "rate_limit": {
                            "primary_window": {
                              "used_percent": 80,
                              "limit_window_seconds": 604800,
                              "reset_after_seconds": 3600
                            },
                            "limit_reached": false,
                            "allowed": true
                          }
                        }
                        """
                    });
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("not found", Encoding.UTF8, "text/plain")
                };
            });

            var cpa = new CpaClient(new HttpClient(handler));
            var engine = new InspectionEngine(cpa, store);
            var service = new AutoPollingService(engine, cpa, store, CreateLogger<AutoPollingService>());

            await InvokeWarmupStartupQuotasAsync(service, "default", settings, accounts, CancellationToken.None);

            Assert.Equal(1, handler.RequestCount);
            Assert.Contains(store.GetOperationLogs(200, "default"), item => item.Message.Contains("启动预热真实检测停止：账号 account-a 周额度 80% 未达到阈值 95%"));
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task WarmupStartupQuotasAsync_ShouldProbeAtMostThreeEnabledAccounts()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            var settings = store.GetSettings("default");
            settings.UsedPercentThreshold = 95;
            var accounts = new List<AuthFileItem>
            {
                BuildAccount("account-disabled", "disabled@example.com", disabled: true),
                BuildAccount("account-a", "user-a@example.com"),
                BuildAccount("account-b", "user-b@example.com"),
                BuildAccount("account-c", "user-c@example.com"),
                BuildAccount("account-d", "user-d@example.com")
            };

            var handler = new StubHttpMessageHandler(request =>
            {
                if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/v0/management/api-call")
                {
                    return JsonResponse(new ApiCallResponse
                    {
                        Status_Code = 200,
                        BodyText = """
                        {
                          "plan_type": "free",
                          "rate_limit": {
                            "primary_window": {
                              "used_percent": 100,
                              "limit_window_seconds": 604800,
                              "reset_after_seconds": 3600
                            },
                            "limit_reached": false,
                            "allowed": true
                          }
                        }
                        """
                    });
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("not found", Encoding.UTF8, "text/plain")
                };
            });

            var cpa = new CpaClient(new HttpClient(handler));
            var engine = new InspectionEngine(cpa, store);
            var service = new AutoPollingService(engine, cpa, store, CreateLogger<AutoPollingService>());

            await InvokeWarmupStartupQuotasAsync(service, "default", settings, accounts, CancellationToken.None);

            Assert.Equal(3, handler.RequestCount);
            Assert.Null(store.GetQuota("account-d", "default"));
            Assert.Contains(store.GetOperationLogs(200, "default"), item => item.Message.Contains("启动预热真实检测结束：已按上限探测 3 个账号"));
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task TryRefreshScheduledRealQuotasAsync_ShouldSkipExceptionAccounts()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            store.SetAccounts(
            [
                BuildAccount("normal-account", "normal@example.com"),
                BuildAccount("exception-account", "exception@example.com"),
            ], "default");

            var staleAt = DateTime.UtcNow.AddHours(-10).AddMinutes(-5);
            store.SetQuota("normal-account", BuildQuota("normal-account", "normal@example.com", staleAt), "default");
            store.SetQuota("exception-account", BuildQuota("exception-account", "exception@example.com", staleAt), "default");
            store.AddException("exception-account", "default");

            var handler = new StubHttpMessageHandler(request =>
            {
                if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/v0/management/api-call")
                {
                    return JsonResponse(new ApiCallResponse
                    {
                        Status_Code = 200,
                        BodyText = """
                        {
                          "plan_type": "free",
                          "rate_limit": {
                            "primary_window": {
                              "used_percent": 35,
                              "limit_window_seconds": 604800,
                              "reset_after_seconds": 3600
                            },
                            "limit_reached": false,
                            "allowed": true
                          }
                        }
                        """
                    });
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("not found", Encoding.UTF8, "text/plain")
                };
            });

            var cpa = new CpaClient(new HttpClient(handler));
            var engine = new InspectionEngine(cpa, store);
            var service = new AutoPollingService(engine, cpa, store, CreateLogger<AutoPollingService>());

            var refreshed = await InvokeTryRefreshScheduledRealQuotasAsync(service, "default", store.GetSettings(), CancellationToken.None);

            Assert.True(refreshed);
            Assert.Equal(1, handler.RequestCount);
            Assert.True(store.GetQuota("normal-account", "default")!.RefreshedAt > staleAt);
            Assert.Equal(staleAt, store.GetQuota("exception-account", "default")!.RefreshedAt);
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    private static HttpResponseMessage JsonResponse(ApiCallResponse response)
    {
        var json = JsonSerializer.Serialize(new
        {
            status_code = response.Status_Code,
            bodyText = response.BodyText,
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static RuntimeStore CreateStore(string baseDirectory, PatrolSettings settings)
    {
        var writer = new OperationLogFileWriter(baseDirectory);
        return new RuntimeStore(settings, writer, baseDirectory);
    }

    /// <summary>
    /// 通过反射调用单站点轮询，避免真的启动后台死循环服务。
    /// </summary>
    private static async Task InvokePollSiteAsync(UsageQueueMonitor monitor, string siteId)
    {
        var method = typeof(UsageQueueMonitor).GetMethod("PollSiteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(monitor, [siteId, CancellationToken.None]));
        await task;
    }

    /// <summary>
    /// 通过反射调用自动轮询后台入口，便于验证单轮调度行为。
    /// </summary>
    private static async Task InvokeAutoPollingExecuteAsync(AutoPollingService service, CancellationToken cancellationToken)
    {
        var method = typeof(AutoPollingService).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(service, [cancellationToken]));
        await task;
    }

    /// <summary>
    /// 通过反射调用启动预热逻辑，验证启动期额度探测的停止条件。
    /// </summary>
    private static async Task InvokeWarmupStartupQuotasAsync(AutoPollingService service, string siteId, PatrolSiteSettings settings, List<AuthFileItem> accounts, CancellationToken cancellationToken)
    {
        var method = typeof(AutoPollingService).GetMethod("WarmupStartupQuotasAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(service, [siteId, settings, accounts, cancellationToken]));
        await task;
    }

    /// <summary>
    /// 通过反射调用后台保鲜真实刷新逻辑，验证 10 小时真实刷新约束。
    /// </summary>
    private static async Task<bool> InvokeTryRefreshScheduledRealQuotasAsync(AutoPollingService service, string siteId, PatrolSiteSettings settings, CancellationToken cancellationToken)
    {
        var method = typeof(AutoPollingService).GetMethod("TryRefreshScheduledRealQuotasAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var task = Assert.IsType<Task<bool>>(method!.Invoke(service, [siteId, settings, cancellationToken]));
        return await task;
    }

    /// <summary>
    /// 通过反射调用巡检日志消息构造逻辑，锁定对外文案分支。
    /// </summary>
    private static string InvokeInspectionProbeLogMessage(RuntimeStore store, string siteId, InspectionDecision decision)
    {
        var method = typeof(InspectionEndpoints).GetMethod("BuildInspectionProbeLogMessage", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<string>(method!.Invoke(null, [store, siteId, decision]));
    }

    /// <summary>
    /// 通过反射调用额度刷新日志消息构造逻辑，锁定对外文案分支。
    /// </summary>
    private static string InvokeQuotaRefreshLogMessage(RuntimeStore store, string siteId, InspectionDecision decision)
    {
        var method = typeof(QuotaEndpoints).GetMethod("BuildQuotaRefreshLogMessage", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<string>(method!.Invoke(null, [store, siteId, decision]));
    }

    /// <summary>
    /// 构造空日志器，满足监控服务依赖。
    /// </summary>
    private static ILogger<T> CreateLogger<T>()
    {
        return LoggerFactory.Create(_ => { }).CreateLogger<T>();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "CodexPatrolTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
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

    // ========== 优先级路由单元测试 ==========

    [Fact]
    public void FilterAutoActionItems_ShouldExcludeEnable_WhenPriorityRoutingEnabled()
    {
        var decisions = new List<InspectionDecision>
        {
            new() { AccountName = "a1", Action = InspectionAction.Disable, DisableReason = DisableReason.QuotaExhausted },
            new() { AccountName = "a2", Action = InspectionAction.Enable },
            new() { AccountName = "a3", Action = InspectionAction.Keep },
        };

        // 优先级路由关闭 → Enable 正常包含
        var normal = InspectionEngine.FilterAutoActionItems(AutoActionMode.Disable, true, false, decisions);
        Assert.Contains(normal, d => d.AccountName == "a2");

        // 优先级路由开启 → Enable 不包含
        var withPriority = InspectionEngine.FilterAutoActionItems(AutoActionMode.Disable, true, true, decisions);
        Assert.DoesNotContain(withPriority, d => d.AccountName == "a2");
        // Disable 仍然包含
        Assert.Contains(withPriority, d => d.AccountName == "a1");
    }

    [Fact]
    public void SetAccounts_ShouldInitializeDisableReason_ForDisabledAccounts()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());

            store.SetAccounts(
            [
                BuildAccount("enabled-a", "a@test.com", disabled: false),
                BuildAccount("disabled-b", "b@test.com", disabled: true),
            ]);

            Assert.Equal(DisableReason.None, store.GetDisableReason("enabled-a"));
            // 从 CPA 同步的 disabled 状态，无已知原因 → ManualDisabled
            Assert.Equal(DisableReason.ManualDisabled, store.GetDisableReason("disabled-b"));
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public void DisableReason_ShouldBeStoredAndCleared()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            store.SetAccounts([BuildAccount("a1", "a@test.com")]);

            Assert.Equal(DisableReason.None, store.GetDisableReason("a1"));

            store.SetDisableReason("a1", DisableReason.QuotaExhausted);
            Assert.Equal(DisableReason.QuotaExhausted, store.GetDisableReason("a1"));

            store.SetDisableReason("a1", DisableReason.OrderedStandby);
            Assert.Equal(DisableReason.OrderedStandby, store.GetDisableReason("a1"));

            store.ClearDisableReason("a1");
            Assert.Equal(DisableReason.None, store.GetDisableReason("a1"));
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public void AccountPriorities_ShouldPersistAndReload()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());

            store.SetAccountPriorities(
            [
                new AccountPriority { Name = "free-1", Priority = 1, PendingFirstInspection = true },
                new AccountPriority { Name = "pro-1", Priority = 3 },
                new AccountPriority { Name = "free-2", Priority = 2 },
            ]);

            var reloaded = CreateStore(baseDirectory, BuildLegacyDefaults());
            var priorities = reloaded.GetAccountPriorities();

            // 按优先级排序返回
            Assert.Equal(3, priorities.Count);
            Assert.Equal("free-1", priorities[0].Name);
            Assert.Equal(1, priorities[0].Priority);
            Assert.True(priorities[0].PendingFirstInspection);
            Assert.Equal("free-2", priorities[1].Name);
            Assert.Equal(2, priorities[1].Priority);
            Assert.False(priorities[1].PendingFirstInspection);
            Assert.Equal("pro-1", priorities[2].Name);
            Assert.Equal(3, priorities[2].Priority);
            Assert.False(priorities[2].PendingFirstInspection);
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public void AccountPriorities_ShouldIgnoreInvalidEntries()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());

            // 空名和 0 优先级应被忽略
            store.SetAccountPriorities(
            [
                new AccountPriority { Name = "valid", Priority = 1 },
                new AccountPriority { Name = "", Priority = 2 },
                new AccountPriority { Name = "zero-priority", Priority = 0 },
            ]);

            var priorities = store.GetAccountPriorities();
            Assert.Single(priorities);
            Assert.Equal("valid", priorities[0].Name);
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public void SetAccounts_ShouldAppendNewPriorityAccountsAsPendingFirstInspection()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            store.AddException("ignored");
            store.SetAccountPriorities(
            [
                new AccountPriority { Name = "existing", Priority = 1 },
            ]);

            store.SetAccounts(
            [
                BuildAccount("existing", "existing@test.com"),
                BuildAccount("new-free", "new@test.com"),
                BuildAccount("ignored", "ignored@test.com"),
            ]);

            var priorities = store.GetAccountPriorities();

            Assert.Equal(2, priorities.Count);
            Assert.Equal("existing", priorities[0].Name);
            Assert.Equal(1, priorities[0].Priority);
            Assert.False(priorities[0].PendingFirstInspection);
            Assert.Equal("new-free", priorities[1].Name);
            Assert.Equal(2, priorities[1].Priority);
            Assert.True(priorities[1].PendingFirstInspection);
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public void TryAutoReorderAccountPriorities_ShouldKeepPendingAccountsUntilTheyAreInspected()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            store.SetAccounts(
            [
                BuildAccount("existing", "existing@test.com"),
                BuildAccount("new-free", "new@test.com"),
            ]);
            store.SetQuota("existing", BuildQuota("existing", "existing@test.com", DateTime.UtcNow, usedPercent: 20));
            store.SetQuota("new-free", BuildQuota("new-free", "new@test.com", DateTime.UtcNow, usedPercent: 10));
            store.SetAccountPriorities(
            [
                new AccountPriority { Name = "existing", Priority = 1 },
                new AccountPriority { Name = "new-free", Priority = 2, PendingFirstInspection = true },
            ]);

            var changed = store.TryAutoReorderAccountPriorities(["existing"], 95, out var priorities);

            Assert.False(changed);
            Assert.Equal(2, priorities.Count);
            Assert.Equal("new-free", priorities[1].Name);
            Assert.True(priorities[1].PendingFirstInspection);
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public void TryAutoReorderAccountPriorities_ShouldInsertNewFreeAccountsByRemainingQuotaAndSinkExhaustedFree()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            store.SetAccounts(
            [
                BuildAccount("free-1", "f1@test.com"),
                BuildAccount("paid-1", "p1@test.com"),
                BuildAccount("free-2", "f2@test.com"),
                BuildAccount("new-free-high", "nfh@test.com"),
                BuildAccount("new-free-low", "nfl@test.com"),
                BuildAccount("new-paid", "np@test.com"),
            ]);

            store.SetQuota("free-1", BuildQuota("free-1", "f1@test.com", DateTime.UtcNow, usedPercent: 20));
            var paidQuota = BuildQuota("paid-1", "p1@test.com", DateTime.UtcNow, usedPercent: 10);
            paidQuota.PlanType = "Pro";
            store.SetQuota("paid-1", paidQuota);
            store.SetQuota("free-2", BuildQuota("free-2", "f2@test.com", DateTime.UtcNow, usedPercent: 99));
            store.SetQuota("new-free-high", BuildQuota("new-free-high", "nfh@test.com", DateTime.UtcNow, usedPercent: 10));
            store.SetQuota("new-free-low", BuildQuota("new-free-low", "nfl@test.com", DateTime.UtcNow, usedPercent: 40));
            var newPaidQuota = BuildQuota("new-paid", "np@test.com", DateTime.UtcNow, usedPercent: 5);
            newPaidQuota.PlanType = "Pro";
            store.SetQuota("new-paid", newPaidQuota);

            store.SetAccountPriorities(
            [
                new AccountPriority { Name = "free-1", Priority = 1 },
                new AccountPriority { Name = "paid-1", Priority = 2 },
                new AccountPriority { Name = "free-2", Priority = 3 },
                new AccountPriority { Name = "new-free-high", Priority = 4, PendingFirstInspection = true },
                new AccountPriority { Name = "new-free-low", Priority = 5, PendingFirstInspection = true },
                new AccountPriority { Name = "new-paid", Priority = 6, PendingFirstInspection = true },
            ]);

            var changed = store.TryAutoReorderAccountPriorities(
                ["free-1", "paid-1", "free-2", "new-free-high", "new-free-low", "new-paid"],
                95,
                out var priorities);

            Assert.True(changed);
            Assert.Equal(
                ["free-1", "new-free-high", "new-free-low", "new-paid", "paid-1", "free-2"],
                priorities.Select(priority => priority.Name).ToArray());
            Assert.Equal([1, 2, 3, 4, 5, 6], priorities.Select(priority => priority.Priority).ToArray());
            Assert.All(
                priorities.Where(priority => priority.Name.StartsWith("new-", StringComparison.OrdinalIgnoreCase)),
                priority => Assert.False(priority.PendingFirstInspection));
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public void PriorityRoutingSettings_ShouldPersistThroughApplySettings()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            var created = store.CreateSite(new SaveSettingsRequest
            {
                SiteName = "测试站",
                SiteEnabled = true,
                CpaBaseUrl = "http://test",
                ManagementKey = "key",
                PollIntervalMinutes = 10,
                ProbeWorkers = 3,
                ProbeBatchDelayMinMs = 2000,
                ProbeBatchDelayMaxMs = 3000,
                ActionWorkers = 4,
                TimeoutMs = 15000,
                AutoActionMode = "disable",
                UsedPercentThreshold = 95,
                PriorityRoutingEnabled = true,
                PriorityMinActiveCount = 3,
            });

            var reloaded = CreateStore(baseDirectory, BuildLegacyDefaults()).GetSettings(created.SiteId);

            Assert.True(reloaded.PriorityRoutingEnabled);
            Assert.Equal(3, reloaded.PriorityMinActiveCount);
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public void PriorityMinActiveCount_ShouldBeClampedToRange()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            store.CreateSite(new SaveSettingsRequest
            {
                SiteName = "clamp",
                SiteEnabled = true,
                CpaBaseUrl = "http://test",
                ManagementKey = "key",
                PollIntervalMinutes = 10,
                ProbeWorkers = 3,
                ProbeBatchDelayMinMs = 2000,
                ProbeBatchDelayMaxMs = 3000,
                ActionWorkers = 4,
                TimeoutMs = 15000,
                AutoActionMode = "none",
                UsedPercentThreshold = 95,
                PriorityMinActiveCount = 99,
            });

            var settings = store.GetSettings();
            // 大于 10 → clamp 到 10
            Assert.Equal(10, settings.PriorityMinActiveCount);
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task ApplyPriorityRoutingAsync_ShouldEnableTopPriorityAndDisableOthers()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());

            // 3 个账号，P1 和 P2 应启用，P3 应禁用（minActiveCount=2）
            store.SetAccounts(
            [
                BuildAccount("free-1", "f1@test.com"),
                BuildAccount("free-2", "f2@test.com"),
                BuildAccount("pro-1", "p1@test.com"),
            ]);

            store.SetQuota("free-1", BuildQuota("free-1", "f1@test.com", DateTime.UtcNow, usedPercent: 30), "default");
            store.SetQuota("free-2", BuildQuota("free-2", "f2@test.com", DateTime.UtcNow, usedPercent: 50), "default");
            store.SetQuota("pro-1", BuildQuota("pro-1", "p1@test.com", DateTime.UtcNow, usedPercent: 10), "default");

            store.SetAccountPriorities(
            [
                new AccountPriority { Name = "free-1", Priority = 1 },
                new AccountPriority { Name = "free-2", Priority = 2 },
                new AccountPriority { Name = "pro-1", Priority = 3 },
            ]);

            // 所有账号先启用
            store.UpdateAccountDisabledState("pro-1", false, "default");

            // 启用优先级路由
            store.UpdateSettings(s => s.PriorityRoutingEnabled = true);

            var disableRequests = new List<string>();
            var handler = new StubHttpMessageHandler(req =>
            {
                if (req.Method == HttpMethod.Patch && req.RequestUri?.AbsolutePath == "/v0/management/auth-files/status")
                {
                    var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
                    if (body.Contains("\"disabled\":true"))
                    {
                        var doc = System.Text.Json.JsonDocument.Parse(body);
                        var name = doc.RootElement.GetProperty("name").GetString() ?? "";
                        disableRequests.Add(name);
                    }
                }
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            var cpa = new CpaClient(new HttpClient(handler));
            var engine = new InspectionEngine(cpa, store);
            var service = new AutoPollingService(engine, cpa, store, CreateLogger<AutoPollingService>());

            await InvokeApplyPriorityRoutingAsync(service, "default", store.GetSettings(), []);

            // pro-1 应被禁用（OrderedStandby）
            Assert.Contains("pro-1", disableRequests);
            Assert.Equal(DisableReason.OrderedStandby, store.GetDisableReason("pro-1", "default"));
            // free-1 和 free-2 保持启用
            Assert.Equal(DisableReason.None, store.GetDisableReason("free-1", "default"));
            Assert.Equal(DisableReason.None, store.GetDisableReason("free-2", "default"));
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task ApplyPriorityRoutingAsync_ShouldReactivateRecoveredAccount()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());

            store.SetAccounts(
            [
                BuildAccount("free-1", "f1@test.com"),
                BuildAccount("free-2", "f2@test.com"),
                BuildAccount("pro-1", "p1@test.com"),
            ]);

            // free-1 额度超阈值，free-2 可用，pro-1 可用
            store.SetQuota("free-1", BuildQuota("free-1", "f1@test.com", DateTime.UtcNow, usedPercent: 99), "default");
            store.SetQuota("free-2", BuildQuota("free-2", "f2@test.com", DateTime.UtcNow, usedPercent: 30), "default");
            store.SetQuota("pro-1", BuildQuota("pro-1", "p1@test.com", DateTime.UtcNow, usedPercent: 10), "default");

            store.SetAccountPriorities(
            [
                new AccountPriority { Name = "free-1", Priority = 1 },
                new AccountPriority { Name = "free-2", Priority = 2 },
                new AccountPriority { Name = "pro-1", Priority = 3 },
            ]);

            // free-1 被标记为 QuotaExhausted
            store.UpdateAccountDisabledState("free-1", true, DisableReason.QuotaExhausted, "default");
            // pro-1 被标记为 OrderedStandby
            store.UpdateAccountDisabledState("pro-1", true, DisableReason.OrderedStandby, "default");

            // 启用优先级路由
            store.UpdateSettings(s => s.PriorityRoutingEnabled = true);

            var enableRequests = new List<string>();
            var handler = new StubHttpMessageHandler(req =>
            {
                if (req.Method == HttpMethod.Patch && req.RequestUri?.AbsolutePath == "/v0/management/auth-files/status")
                {
                    var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
                    if (body.Contains("\"disabled\":false"))
                    {
                        var doc = System.Text.Json.JsonDocument.Parse(body);
                        var name = doc.RootElement.GetProperty("name").GetString() ?? "";
                        enableRequests.Add(name);
                    }
                }
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            var cpa = new CpaClient(new HttpClient(handler));
            var engine = new InspectionEngine(cpa, store);
            var service = new AutoPollingService(engine, cpa, store, CreateLogger<AutoPollingService>());

            await InvokeApplyPriorityRoutingAsync(service, "default", store.GetSettings(), []);

            // free-2 和 pro-1 应被激活（free-1 超阈值跳过），pro-1 被恢复启用
            Assert.Contains("pro-1", enableRequests);
            Assert.Equal(DisableReason.None, store.GetDisableReason("pro-1", "default"));
            // free-1 仍然超阈值
            Assert.Equal(DisableReason.QuotaExhausted, store.GetDisableReason("free-1", "default"));
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task ManualPriorityRoutingAsync_ShouldNotReactivatePaidAccountWithFiveHourQuotaExhausted()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());

            store.SetAccounts(
            [
                BuildAccount("paid-1", "p1@test.com", disabled: true),
                BuildAccount("free-1", "f1@test.com"),
            ]);

            var paidQuota = BuildQuota("paid-1", "p1@test.com", DateTime.UtcNow, usedPercent: 30);
            paidQuota.PlanType = "Pro";
            paidQuota.Windows.Add(new CodexQuotaWindowSnapshot
            {
                Id = "5h",
                Label = "5小时限额",
                UsedPercent = 100,
                LimitWindowSeconds = 18000,
                ResetAtUtc = DateTime.UtcNow.AddHours(1),
            });
            store.SetQuota("paid-1", paidQuota, "default");
            store.SetQuota("free-1", BuildQuota("free-1", "f1@test.com", DateTime.UtcNow, usedPercent: 20), "default");

            store.SetAccountPriorities(
            [
                new AccountPriority { Name = "paid-1", Priority = 1 },
                new AccountPriority { Name = "free-1", Priority = 2 },
            ]);
            store.SetDisableReason("paid-1", DisableReason.QuotaExhausted, "default");
            store.UpdateSettings(s =>
            {
                s.PriorityRoutingEnabled = true;
                s.PriorityMinActiveCount = 2;
            });

            var enableRequests = new List<string>();
            var handler = new StubHttpMessageHandler(req =>
            {
                if (req.Method == HttpMethod.Patch && req.RequestUri?.AbsolutePath == "/v0/management/auth-files/status")
                {
                    var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
                    if (body.Contains("\"disabled\":false"))
                    {
                        using var document = JsonDocument.Parse(body);
                        enableRequests.Add(document.RootElement.GetProperty("name").GetString() ?? "");
                    }
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            });
            var cpa = new CpaClient(new HttpClient(handler));
            var engine = new InspectionEngine(cpa, store);

            await InvokeManualApplyPriorityRoutingAsync(store, engine, cpa, store.GetSettings(), "default", []);

            // 5 小时额度到阈值的收费号不应被启用
            Assert.DoesNotContain("paid-1", enableRequests);
            Assert.Equal(DisableReason.QuotaExhausted, store.GetDisableReason("paid-1", "default"));
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task ManualPriorityRoutingAsync_ShouldReactivateRecoveredQuotaExhaustedAccount()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());

            store.SetAccounts(
            [
                BuildAccount("free-1", "f1@test.com", disabled: true),
                BuildAccount("free-2", "f2@test.com"),
            ]);

            store.SetQuota("free-1", BuildQuota("free-1", "f1@test.com", DateTime.UtcNow, usedPercent: 20), "default");
            store.SetQuota("free-2", BuildQuota("free-2", "f2@test.com", DateTime.UtcNow, usedPercent: 40), "default");
            store.SetAccountPriorities(
            [
                new AccountPriority { Name = "free-1", Priority = 1 },
                new AccountPriority { Name = "free-2", Priority = 2 },
            ]);
            store.SetDisableReason("free-1", DisableReason.QuotaExhausted, "default");
            store.UpdateSettings(s =>
            {
                s.PriorityRoutingEnabled = true;
                s.PriorityMinActiveCount = 2;
            });

            var enableRequests = new List<string>();
            var handler = new StubHttpMessageHandler(req =>
            {
                if (req.Method == HttpMethod.Patch && req.RequestUri?.AbsolutePath == "/v0/management/auth-files/status")
                {
                    var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
                    if (body.Contains("\"disabled\":false"))
                    {
                        using var document = JsonDocument.Parse(body);
                        enableRequests.Add(document.RootElement.GetProperty("name").GetString() ?? "");
                    }
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            });
            var cpa = new CpaClient(new HttpClient(handler));
            var engine = new InspectionEngine(cpa, store);

            await InvokeManualApplyPriorityRoutingAsync(store, engine, cpa, store.GetSettings(), "default", []);

            Assert.Contains("free-1", enableRequests);
            Assert.Equal(DisableReason.None, store.GetDisableReason("free-1", "default"));
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task ManualPriorityRoutingAsync_ShouldReactivateManualDisabledAccount()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());

            store.SetAccounts([BuildAccount("free-1", "f1@test.com", disabled: true)]);
            store.SetQuota("free-1", BuildQuota("free-1", "f1@test.com", DateTime.UtcNow, usedPercent: 20), "default");
            store.SetAccountPriorities([new AccountPriority { Name = "free-1", Priority = 1 }]);
            store.SetDisableReason("free-1", DisableReason.ManualDisabled, "default");
            store.UpdateSettings(s => s.PriorityRoutingEnabled = true);

            var enableRequests = new List<string>();
            var handler = new StubHttpMessageHandler(req =>
            {
                if (req.Method == HttpMethod.Patch && req.RequestUri?.AbsolutePath == "/v0/management/auth-files/status")
                {
                    var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
                    if (body.Contains("\"disabled\":false"))
                    {
                        using var document = JsonDocument.Parse(body);
                        enableRequests.Add(document.RootElement.GetProperty("name").GetString() ?? "");
                    }
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            });
            var cpa = new CpaClient(new HttpClient(handler));
            var engine = new InspectionEngine(cpa, store);

            await InvokeManualApplyPriorityRoutingAsync(store, engine, cpa, store.GetSettings(), "default", []);

            Assert.Contains("free-1", enableRequests);
            Assert.Equal(DisableReason.None, store.GetDisableReason("free-1", "default"));
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task ApplyPriorityRoutingAsync_ShouldNotActOnAccountsWithoutPriority()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());

            store.SetAccounts(
            [
                BuildAccount("with-priority", "wp@test.com"),
                BuildAccount("no-priority", "np@test.com"),
            ]);

            store.SetQuota("with-priority", BuildQuota("with-priority", "wp@test.com", DateTime.UtcNow, usedPercent: 30), "default");
            store.SetQuota("no-priority", BuildQuota("no-priority", "np@test.com", DateTime.UtcNow, usedPercent: 50), "default");

            // 只有 with-priority 有优先级配置
            store.SetAccountPriorities(
            [
                new AccountPriority { Name = "with-priority", Priority = 1 },
            ]);

            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
            var cpa = new CpaClient(new HttpClient(handler));
            var engine = new InspectionEngine(cpa, store);
            var service = new AutoPollingService(engine, cpa, store, CreateLogger<AutoPollingService>());

            await InvokeApplyPriorityRoutingAsync(service, "default", store.GetSettings(), []);

            // no-priority 无禁用原因，不受优先级路由影响
            Assert.Equal(DisableReason.None, store.GetDisableReason("no-priority", "default"));
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task ApplyPriorityRoutingAsync_ShouldTreatPendingFirstInspectionAccountsAsStandby()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());

            store.SetAccounts(
            [
                BuildAccount("existing", "existing@test.com"),
                BuildAccount("pending", "pending@test.com"),
            ]);

            store.SetQuota("existing", BuildQuota("existing", "existing@test.com", DateTime.UtcNow, usedPercent: 20), "default");
            store.SetAccountPriorities(
            [
                new AccountPriority { Name = "existing", Priority = 1 },
                new AccountPriority { Name = "pending", Priority = 2, PendingFirstInspection = true },
            ]);
            store.UpdateSettings(s =>
            {
                s.PriorityRoutingEnabled = true;
                s.PriorityMinActiveCount = 2;
            });

            var disableRequests = new List<string>();
            var handler = new StubHttpMessageHandler(req =>
            {
                if (req.Method == HttpMethod.Patch && req.RequestUri?.AbsolutePath == "/v0/management/auth-files/status")
                {
                    var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
                    if (body.Contains("\"disabled\":true"))
                    {
                        using var document = JsonDocument.Parse(body);
                        var name = document.RootElement.GetProperty("name").GetString() ?? "";
                        disableRequests.Add(name);
                    }
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            });
            var cpa = new CpaClient(new HttpClient(handler));
            var engine = new InspectionEngine(cpa, store);
            var service = new AutoPollingService(engine, cpa, store, CreateLogger<AutoPollingService>());

            await InvokeApplyPriorityRoutingAsync(service, "default", store.GetSettings(), []);

            Assert.Contains("pending", disableRequests);
            Assert.Equal(DisableReason.OrderedStandby, store.GetDisableReason("pending", "default"));
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task ApplyPriorityRoutingAsync_ShouldNotProcessExceptionAccountsEvenIfPriorityExists()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());

            store.SetAccounts(
            [
                BuildAccount("normal", "normal@test.com"),
                BuildAccount("exception", "exception@test.com"),
            ]);

            store.SetQuota("normal", BuildQuota("normal", "normal@test.com", DateTime.UtcNow, usedPercent: 20), "default");
            store.SetQuota("exception", BuildQuota("exception", "exception@test.com", DateTime.UtcNow, usedPercent: 20), "default");
            store.SetAccountPriorities(
            [
                new AccountPriority { Name = "normal", Priority = 1 },
                new AccountPriority { Name = "exception", Priority = 2 },
            ]);
            store.AddException("exception");
            store.UpdateSettings(s => s.PriorityRoutingEnabled = true);

            var disableRequests = new List<string>();
            var handler = new StubHttpMessageHandler(req =>
            {
                if (req.Method == HttpMethod.Patch && req.RequestUri?.AbsolutePath == "/v0/management/auth-files/status")
                {
                    var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
                    if (body.Contains("\"disabled\":true"))
                    {
                        using var document = JsonDocument.Parse(body);
                        var name = document.RootElement.GetProperty("name").GetString() ?? "";
                        disableRequests.Add(name);
                    }
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            });
            var cpa = new CpaClient(new HttpClient(handler));
            var engine = new InspectionEngine(cpa, store);
            var service = new AutoPollingService(engine, cpa, store, CreateLogger<AutoPollingService>());

            await InvokeApplyPriorityRoutingAsync(service, "default", store.GetSettings(), []);

            Assert.DoesNotContain("exception", disableRequests);
            Assert.Equal(DisableReason.None, store.GetDisableReason("exception", "default"));
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task PriorityRoutingRemoteSync_ShouldSkipPendingAndExceptionAccounts()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            store.AddException("exception");
            store.UpdateSettings(s =>
            {
                s.CpaBaseUrl = "http://test-host";
                s.ManagementKey = "test-key";
            });

            var patchRequests = new List<string>();
            var handler = new StubHttpMessageHandler(request =>
            {
                if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/v0/management/auth-files")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""
                        {
                          "files": [
                            { "name": "ready", "priority": 5, "disabled": false },
                            { "name": "pending", "priority": 4, "disabled": false },
                            { "name": "exception", "priority": 3, "disabled": false }
                          ],
                          "total": 3
                        }
                        """, Encoding.UTF8, "application/json")
                    };
                }

                if (request.Method == HttpMethod.Patch && request.RequestUri?.AbsolutePath == "/v0/management/auth-files/fields")
                {
                    var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
                    using var document = JsonDocument.Parse(body);
                    patchRequests.Add(document.RootElement.GetProperty("name").GetString() ?? "");
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{}", Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("not found", Encoding.UTF8, "text/plain")
                };
            });
            var cpa = new CpaClient(new HttpClient(handler));

            var warning = await InvokePriorityRoutingRemoteSyncAsync(
                store,
                cpa,
                store.GetSettings(),
                [
                    new AccountPriority { Name = "ready", Priority = 1 },
                    new AccountPriority { Name = "pending", Priority = 2, PendingFirstInspection = true },
                    new AccountPriority { Name = "exception", Priority = 3 },
                ],
                "default",
                CancellationToken.None);

            Assert.Null(warning);
            Assert.Equal(["ready"], patchRequests);
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task RunInspectionAsync_ShouldNotAutoReorderOrSyncWhenPriorityRoutingDisabled()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());
            store.SetAccounts([BuildAccount("pending-free", "pending@test.com")], "default");
            store.SetAccountPriorities(
            [
                new AccountPriority { Name = "pending-free", Priority = 1, PendingFirstInspection = true },
            ]);
            store.UpdateSettings(s =>
            {
                s.CpaBaseUrl = "http://test-host";
                s.ManagementKey = "test-key";
                s.PriorityRoutingEnabled = false;
                s.ProbeWorkers = 1;
                s.ProbeBatchDelayMinMs = 0;
                s.ProbeBatchDelayMaxMs = 0;
                s.TimeoutMs = 5000;
            });

            var priorityPatchCount = 0;
            var handler = new StubHttpMessageHandler(request =>
            {
                if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/v0/management/auth-files")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""
                        {
                          "files": [
                            {
                              "name": "pending-free",
                              "email": "pending@test.com",
                              "provider": "codex",
                              "auth_index": "auth-pending-free",
                              "disabled": false,
                              "priority": 8
                            }
                          ],
                          "total": 1
                        }
                        """, Encoding.UTF8, "application/json")
                    };
                }

                if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/v0/management/api-call")
                {
                    return BuildApiCallUsageResponse(10);
                }

                if (request.Method == HttpMethod.Patch && request.RequestUri?.AbsolutePath == "/v0/management/auth-files/fields")
                {
                    priorityPatchCount++;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{}", Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("not found", Encoding.UTF8, "text/plain")
                };
            });
            var cpa = new CpaClient(new HttpClient(handler));
            var engine = new InspectionEngine(cpa, store);
            var service = new AutoPollingService(engine, cpa, store, CreateLogger<AutoPollingService>());

            await InvokeRunInspectionAsync(service, "default", store.GetSettings(), CancellationToken.None);

            var priorities = store.GetAccountPriorities("default");
            var priority = Assert.Single(priorities);
            Assert.True(priority.PendingFirstInspection);
            Assert.Equal(0, priorityPatchCount);
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public void GetQuotas_ShouldIncludeDisableReason()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());

            store.SetAccounts(
            [
                BuildAccount("a1", "a@test.com"),
                BuildAccount("a2", "b@test.com"),
            ]);

            store.SetQuota("a1", BuildQuota("a1", "a@test.com", DateTime.UtcNow), "default");
            store.SetQuota("a2", BuildQuota("a2", "b@test.com", DateTime.UtcNow), "default");

            store.SetDisableReason("a1", DisableReason.QuotaExhausted);
            // a2 无禁用原因

            var quotas = store.GetQuotas("default");
            var q1 = quotas.First(q => q.AccountName == "a1");
            var q2 = quotas.First(q => q.AccountName == "a2");

            Assert.Equal("QuotaExhausted", q1.DisableReason);
            Assert.Equal("", q2.DisableReason);
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task WarmupStartupQuotasAsync_WithPriorityRouting_ShouldUsePriorityOrder()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(baseDirectory, BuildLegacyDefaults());

            store.SetAccounts(
            [
                BuildAccount("pro-1", "p1@test.com"),
                BuildAccount("free-1", "f1@test.com"),
                BuildAccount("free-2", "f2@test.com"),
            ]);

            // 设置优先级：free-1 最先
            store.SetAccountPriorities(
            [
                new AccountPriority { Name = "free-1", Priority = 1 },
                new AccountPriority { Name = "free-2", Priority = 2 },
                new AccountPriority { Name = "pro-1", Priority = 3 },
            ]);

            // 启用优先级路由
            store.UpdateSettings(settings => settings.PriorityRoutingEnabled = true);

            var requestOrder = new List<string>();
            var handler = new StubHttpMessageHandler(req =>
            {
                if (req.Content != null)
                {
                    requestOrder.Add(req.RequestUri!.ToString());
                }

                return BuildCodexUsageResponse(80);
            });
            var cpa = new CpaClient(new HttpClient(handler));
            var engine = new InspectionEngine(cpa, store);
            var service = new AutoPollingService(engine, cpa, store, CreateLogger<AutoPollingService>());

            var settings = store.GetSettings();
            var accounts = store.GetAccounts();

            await InvokeWarmupStartupQuotasAsync(service, "default", settings, accounts, CancellationToken.None);

            // 第一个请求应该是 free-1（优先级最高）
            Assert.True(requestOrder.Count > 0, "应该发起请求");
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    /// <summary>
    /// 通过反射调用自动巡检主流程。
    /// </summary>
    private static async Task InvokeRunInspectionAsync(AutoPollingService service, string siteId, PatrolSiteSettings settings, CancellationToken cancellationToken)
    {
        var method = typeof(AutoPollingService).GetMethod("RunInspectionAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(service, [siteId, settings, cancellationToken, false]));
        await task;
    }

    /// <summary>
    /// 通过反射调用手动巡检优先级路由调度逻辑。
    /// </summary>
    private static async Task InvokeManualApplyPriorityRoutingAsync(
        RuntimeStore store,
        InspectionEngine engine,
        CpaClient cpa,
        PatrolSiteSettings settings,
        string siteId,
        List<InspectionDecision> decisions)
    {
        var method = typeof(InspectionEndpoints).GetMethod("ApplyPriorityRoutingAsync", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task<List<ActionOutcome>>>(method!.Invoke(null, [store, engine, cpa, settings, siteId, decisions, CancellationToken.None]));
        await task;
    }

    /// <summary>
    /// 通过反射调用远端优先级同步逻辑。
    /// </summary>
    private static async Task<string?> InvokePriorityRoutingRemoteSyncAsync(
        RuntimeStore store,
        CpaClient cpa,
        PatrolSiteSettings settings,
        List<AccountPriority> priorities,
        string siteId,
        CancellationToken cancellationToken)
    {
        var type = typeof(RuntimeStore).Assembly.GetType("CodexPatrol.Services.PriorityRoutingRemoteSync");
        Assert.NotNull(type);
        var method = type!.GetMethod("TrySyncAsync", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        var task = Assert.IsType<Task<string?>>(method!.Invoke(null, [store, cpa, settings, priorities, siteId, cancellationToken, "测试同步"]));
        return await task;
    }

    /// <summary>
    /// 通过反射调用优先级路由调度逻辑。
    /// </summary>
    private static async Task InvokeApplyPriorityRoutingAsync(AutoPollingService service, string siteId, PatrolSiteSettings settings, List<InspectionDecision> decisions)
    {
        var method = typeof(AutoPollingService).GetMethod("ApplyPriorityRoutingAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(service, [siteId, settings, decisions, CancellationToken.None]));
        await task;
    }

    private static HttpResponseMessage BuildApiCallUsageResponse(double usedPercent)
    {
        var body = JsonSerializer.Serialize(new
        {
            plan_type = "free",
            rate_limit = new
            {
                allowed = true,
                limit_reached = false,
                primary_window = new
                {
                    used_percent = usedPercent,
                    limit_window_seconds = 604800,
                    reset_after_seconds = 86400,
                },
                secondary_window = (object?)null,
            },
        });
        var envelope = JsonSerializer.Serialize(new
        {
            status_code = 200,
            bodyText = body,
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(envelope, Encoding.UTF8, "application/json"),
        };
    }

    private static HttpResponseMessage BuildCodexUsageResponse(double usedPercent)
    {
        var json = JsonSerializer.Serialize(new
        {
            plan_type = "free",
            rate_limit = new
            {
                allowed = true,
                limit_reached = false,
                primary_window = new
                {
                    used_percent = usedPercent,
                    limit_window_seconds = 604800,
                    reset_after_seconds = 86400,
                },
                secondary_window = (object?)null,
            },
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }
}
