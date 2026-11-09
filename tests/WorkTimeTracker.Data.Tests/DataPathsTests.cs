namespace WorkTimeTracker.Data.Tests;

public class DataPathsTests
{
    [Fact]
    public void CommandLine_HasPriority()
    {
        using var tmp = new TempDirectory();
        File.WriteAllText(tmp.Combine("portable.txt"), "");
        var paths = DataPaths.Resolve(tmp.Combine("custom"), tmp.Path, tmp.Combine("local"));
        Assert.Equal(DataLocationKind.CommandLine, paths.Kind);
        Assert.Equal(tmp.Combine("custom"), paths.DataDirectory);
        Assert.Equal(tmp.Combine("custom", "worktime.db"), paths.DatabasePath);
    }

    [Fact]
    public void PortableMarker_UsesDataNextToExe()
    {
        using var tmp = new TempDirectory();
        File.WriteAllText(tmp.Combine("portable.txt"), "");
        var paths = DataPaths.Resolve(null, tmp.Path, tmp.Combine("local"));
        Assert.Equal(DataLocationKind.Portable, paths.Kind);
        Assert.Equal(tmp.Combine("data"), paths.DataDirectory);
    }

    [Fact]
    public void Default_UsesLocalAppData()
    {
        using var tmp = new TempDirectory();
        var paths = DataPaths.Resolve("  ", tmp.Path, tmp.Combine("local"));
        Assert.Equal(DataLocationKind.Default, paths.Kind);
        Assert.Equal(tmp.Combine("local", "WorkTimeTracker"), paths.DataDirectory);
        Assert.Equal(tmp.Combine("local", "WorkTimeTracker", "backups"), paths.BackupsDirectory);
        Assert.Equal(tmp.Combine("local", "WorkTimeTracker", "logs"), paths.LogsDirectory);
    }

    [Fact]
    public void EnsureCreated_CreatesFolders()
    {
        using var tmp = new TempDirectory();
        var paths = new DataPaths(tmp.Combine("d"), DataLocationKind.CommandLine);
        paths.EnsureCreated();
        Assert.True(Directory.Exists(paths.BackupsDirectory));
        Assert.True(Directory.Exists(paths.LogsDirectory));
    }
}
