using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
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
            var logPath = Path.Combine(baseDirectory, "logs", entry.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd"), "Ope.log");
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
            store.SetQuota("account-a", BuildQuota("account-a", "user-a@example.com", refreshedAt: new DateTime(2026, 5, 22, 1, 0, 0, DateTimeKind.Utc)), "default");
            store.SetQuota("account-b", BuildQuota("account-b", "user-b@example.com", refreshedAt: new DateTime(2026, 5, 22, 2, 0, 0, DateTimeKind.Utc)), backup.SiteId);

            var reloaded = CreateStore(baseDirectory, BuildLegacyDefaults());
            var defaultQuota = reloaded.GetQuota("account-a", "default");
            var backupQuota = reloaded.GetQuota("account-b", backup.SiteId);

            Assert.NotNull(defaultQuota);
            Assert.NotNull(backupQuota);
            Assert.Equal(new DateTime(2026, 5, 22, 1, 0, 0, DateTimeKind.Utc), defaultQuota!.RefreshedAt);
            Assert.Equal(new DateTime(2026, 5, 22, 2, 0, 0, DateTimeKind.Utc), backupQuota!.RefreshedAt);
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
            Assert.Equal(DateTime.MinValue, placeholderQuota!.RefreshedAt);
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
            store.SetQuota("account-a", BuildQuota("account-a", "user-a@example.com", new DateTime(2026, 5, 22, 1, 0, 0, DateTimeKind.Utc)), "default");

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

    private static CodexQuotaSnapshot BuildQuota(string accountName, string displayAccount, DateTime refreshedAt, bool disabled = false)
    {
        return new CodexQuotaSnapshot
        {
            AccountName = accountName,
            DisplayAccount = displayAccount,
            PlanType = "Free",
            Disabled = disabled,
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
                    UsedPercent = 80,
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
            Assert.Contains(logs, item => item.Category == "monitor" && item.Message.Contains("本轮未获取到新的 usage-queue 记录"));
            Assert.Contains(logs, item => item.Category == "monitor" && item.Message.Contains("本轮拉取 0 条 usage-queue 记录"));
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
}
