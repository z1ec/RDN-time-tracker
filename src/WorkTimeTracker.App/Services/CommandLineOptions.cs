namespace WorkTimeTracker.App.Services;

/// <summary>Разобранные аргументы командной строки (F7.11).</summary>
public sealed record CommandLineOptions(bool ShowVersion, bool SelfTest, string? DataDir, IReadOnlyList<string> Errors)
{
    /// <summary>Разбирает аргументы. Неизвестные аргументы игнорируются (их может передать ОС или оболочка).</summary>
    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        var showVersion = false;
        var selfTest = false;
        string? dataDir = null;
        var errors = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--version":
                    showVersion = true;
                    break;
                case "--self-test":
                    selfTest = true;
                    break;
                case "--data-dir":
                    if (i + 1 < args.Count && !string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        dataDir = args[++i];
                    }
                    else
                    {
                        errors.Add("После --data-dir должен идти путь к папке");
                    }

                    break;
            }
        }

        return new CommandLineOptions(showVersion, selfTest, dataDir, errors);
    }
}
