using Danslicer.Core.Diagnostics;

namespace Danslicer.Tests;

/// <summary>The crash log: a daily file that records exceptions with their stack and prunes old files.</summary>
[Collection("CrashLog")]
public sealed class CrashLogTests : IDisposable
{
    private readonly string _original = CrashLog.Directory;
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "danslicer-crashlog-" + Guid.NewGuid().ToString("N"));

    public CrashLogTests() => CrashLog.Directory = _scratch;

    public void Dispose()
    {
        CrashLog.Directory = _original;
        if (Directory.Exists(_scratch)) Directory.Delete(_scratch, recursive: true);
    }

    [Fact]
    public void WritesATimestampedLineToTodaysFile()
    {
        CrashLog.Write("hello");

        var text = File.ReadAllText(CrashLog.CurrentPath);
        Assert.Contains("hello", text);
        Assert.Contains(DateTime.Now.ToString("yyyy-MM-dd"), text);
    }

    [Fact]
    public void RecordsTheExceptionTypeMessageStackAndInnerException()
    {
        Exception caught;
        try
        {
            try { throw new InvalidOperationException("inner detail"); }
            catch (Exception inner) { throw new KeyNotFoundException("outer failure", inner); }
        }
        catch (Exception e) { caught = e; }

        CrashLog.WriteException("Test context", caught);

        var text = File.ReadAllText(CrashLog.CurrentPath);
        Assert.Contains("Test context", text);
        Assert.Contains("KeyNotFoundException: outer failure", text);
        Assert.Contains("InvalidOperationException: inner detail", text);
        Assert.Contains(nameof(RecordsTheExceptionTypeMessageStackAndInnerException), text);
        Assert.Contains("Danslicer", text);
    }

    [Fact]
    public void OldLogsArePrunedAndRecentOnesKept()
    {
        Directory.CreateDirectory(_scratch);
        var old = Path.Combine(_scratch, "danslicer-20200101.log");
        var recent = Path.Combine(_scratch, "danslicer-recent.log");
        File.WriteAllText(old, "old");
        File.WriteAllText(recent, "recent");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-CrashLog.RetainDays - 1));

        CrashLog.Write("prune trigger");

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }
}
