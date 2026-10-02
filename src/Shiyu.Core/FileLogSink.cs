using System.Globalization;
using System.Text;

namespace Shiyu.Core;

/// <summary>
/// 默认出口：追加写 <c>{数据目录}/logs/shiyu-yyyyMMdd.log</c>，按天滚动，
/// 保留 7 天，单文件超过 1MB 轮转为 <c>.1</c>。写入是同步的——致命异常
/// 的最后一行必须在进程倒下之前落到盘上。
/// </summary>
public sealed class FileLogSink : ILogSink
{
    public const int RetentionDays = 7;
    public const long MaxFileBytes = 1024 * 1024;

    private readonly string _directory;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    /// <summary>已做过保留清理的日期（yyyyMMdd）。一天清一次，不为每行 IO。</summary>
    private string _sweptOn = string.Empty;

    public FileLogSink(string dataDirectory, TimeProvider? clock = null)
    {
        _directory = Path.Combine(dataDirectory, "logs");
        _clock = clock ?? TimeProvider.System;
    }

    public void WriteLine(string line)
    {
        lock (_gate)
        {
            try
            {
                WriteUnderLock(line);
            }
            catch (Exception)
            {
                // expected: 磁盘满、目录被删、权限被收走——放弃这一行，
                // 日志绝不能成为它本要诊断的那种崩溃。
            }
        }
    }

    private void WriteUnderLock(string line)
    {
        Directory.CreateDirectory(_directory);

        var today = _clock.GetLocalNow();
        var date = today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        SweepOldFiles(date);

        var path = Path.Combine(_directory, "shiyu-" + date + ".log");
        var bytes = new FileInfo(path) is { Exists: true } info
            ? info.Length + Encoding.UTF8.GetByteCount(line) + 2
            : 0;

        if (bytes > MaxFileBytes)
        {
            // 超限轮转为 .1（顶掉上一个 .1）而不是截头：一次原子改名保住
            // 整行完整的记录，也不必把整个文件读进内存重写——最热的写入
            // 路径上做重活，日志会先于宿主成为问题。
            File.Move(path, path + ".1", overwrite: true);
        }

        File.AppendAllText(path, line + Environment.NewLine);
    }

    /// <summary>
    /// 删除保留期之外的日志（含轮转的 <c>.1</c>）。首次写入和日期翻页时
    /// 各跑一次；改名不出的日期（解析不了）不动——不确定的文件宁可留下。
    /// </summary>
    private void SweepOldFiles(string today)
    {
        if (_sweptOn == today)
        {
            return;
        }

        _sweptOn = today;
        var horizon = DateTime.ParseExact(today, "yyyyMMdd", CultureInfo.InvariantCulture)
            .Date.AddDays(-RetentionDays);

        foreach (var file in Directory.EnumerateFiles(_directory, "shiyu-*.log*"))
        {
            var name = Path.GetFileName(file);
            if (name.Length >= "shiyu-".Length + 8
                && DateTime.TryParseExact(name.Substring(6, 8), "yyyyMMdd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var stamped)
                && stamped < horizon)
            {
                File.Delete(file);
            }
        }
    }
}
