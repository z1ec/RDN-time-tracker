using WorkTimeTracker.App.Services;

namespace WorkTimeTracker.App.Tests;

public class CommandLineOptionsTests
{
    [Fact]
    public void Empty()
    {
        var o = CommandLineOptions.Parse([]);
        Assert.False(o.ShowVersion);
        Assert.False(o.SelfTest);
        Assert.Null(o.DataDir);
        Assert.Empty(o.Errors);
    }

    [Fact]
    public void AllFlags()
    {
        var o = CommandLineOptions.Parse(["--self-test", "--data-dir", "/tmp/x", "--version", "--unknown"]);
        Assert.True(o.ShowVersion);
        Assert.True(o.SelfTest);
        Assert.Equal("/tmp/x", o.DataDir);
    }

    [Fact]
    public void DataDirWithoutValue_IsError()
    {
        var o = CommandLineOptions.Parse(["--data-dir"]);
        Assert.Null(o.DataDir);
        Assert.Single(o.Errors);
    }

    [Fact]
    public void Version_IsSemVer() => Assert.Matches(@"^\d+\.\d+\.\d+", AppInfo.Version);
}
