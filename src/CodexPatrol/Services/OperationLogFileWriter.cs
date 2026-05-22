using System.Text;
using CodexPatrol.Models;

namespace CodexPatrol.Services;

/// <summary>
/// 将运行时操作日志额外落到本地文件，启动时不会反向加载。
/// </summary>
public sealed class OperationLogFileWriter
{
    /// <summary>
    /// 文件写入锁，保证同一时刻只有一个线程在写文件。
    /// </summary>
    private readonly object _fileLock = new();

    /// <summary>
    /// 日志文件根目录。
    /// </summary>
    private readonly string _baseDirectory;

    /// <summary>
    /// 构造 OperationLogFileWriter，默认使用应用基目录。
    /// </summary>
    public OperationLogFileWriter(string? baseDirectory = null)
    {
        _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;
    }

    /// <summary>
    /// 将操作日志条目追加到当天日志文件。
    /// </summary>
    public void Write(OperationLogEntry entry)
    {
        try
        {
            var filePath = BuildLogFilePath(entry.CreatedAt, "Ope.log");
            var line = BuildLine(entry);

            lock (_fileLock)
            {
                File.AppendAllText(filePath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // 本地落盘失败不影响主流程。
        }
    }

    /// <summary>
    /// 将异常信息追加到当天错误日志文件，包含完整堆栈。
    /// </summary>
    public void WriteException(
        Exception exception,
        string category,
        string operationType,
        string source,
        string level = "error",
        string message = "",
        string siteId = "",
        string siteName = "",
        string accountName = "",
        string displayAccount = "")
    {
        try
        {
            var createdAt = DateTime.UtcNow;
            var filePath = BuildLogFilePath(createdAt, "Error.log");
            var line = BuildExceptionLine(createdAt, exception, category, operationType, source, level, message, siteId, siteName, accountName, displayAccount);

            lock (_fileLock)
            {
                File.AppendAllText(filePath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // 异常日志写入失败时不再抛出，避免二次异常影响主流程。
        }
    }

    /// <summary>
    /// 根据日期生成日志文件路径，按天建目录。
    /// </summary>
    private string BuildLogFilePath(DateTime createdAtUtc, string fileName)
    {
        var logDir = Path.Combine(_baseDirectory, "logs", createdAtUtc.ToLocalTime().ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(logDir);
        return Path.Combine(logDir, fileName);
    }

    /// <summary>
    /// 将操作日志条目格式化为一行文本。
    /// </summary>
    private static string BuildLine(OperationLogEntry entry)
    {
        return $"[{entry.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff}] [{entry.Level.ToUpperInvariant()}] [{entry.Category}/{entry.Source}/{entry.OperationType}]{BuildContextSegments(entry.SiteId, entry.SiteName, entry.AccountName, entry.DisplayAccount)} {entry.Message}{Environment.NewLine}";
    }

    /// <summary>
    /// 将异常信息格式化为一行文本，包含完整堆栈跟踪。
    /// </summary>
    private static string BuildExceptionLine(
        DateTime createdAtUtc,
        Exception exception,
        string category,
        string operationType,
        string source,
        string level,
        string message,
        string siteId,
        string siteName,
        string accountName,
        string displayAccount)
    {
        var summary = string.IsNullOrWhiteSpace(message) ? exception.Message : message;
        return $"[{createdAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff}] [{level.ToUpperInvariant()}] [{category}/{source}/{operationType}]{BuildContextSegments(siteId, siteName, accountName, displayAccount)} {summary}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}";
    }

    /// <summary>
    /// 拼接日志行中的上下文片段（站点、账号、显示名），为空时省略。
    /// </summary>
    private static string BuildContextSegments(string siteId, string siteName, string accountName, string displayAccount)
    {
        var accountSegment = string.IsNullOrWhiteSpace(accountName)
            ? ""
            : $" [账号:{accountName}]";
        var displaySegment = string.IsNullOrWhiteSpace(displayAccount)
            ? ""
            : $" [显示:{displayAccount}]";
        var siteSegment = string.IsNullOrWhiteSpace(siteId)
            ? ""
            : $" [站点:{(string.IsNullOrWhiteSpace(siteName) ? siteId : $"{siteName}/{siteId}")}]";
        return $"{siteSegment}{accountSegment}{displaySegment}";
    }
}
