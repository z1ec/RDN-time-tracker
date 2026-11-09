using System.Runtime.InteropServices;

namespace WorkTimeTracker.App.Services;

/// <summary>
/// WinExe на Windows не имеет консоли. Для CLI-режимов подключаемся к консоли родительского процесса,
/// чтобы вывод <c>--version</c> был виден в терминале.
/// </summary>
internal static partial class ConsoleAttacher
{
    private const int AttachParentProcess = -1;

    public static void TryAttachToParent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            if (AttachConsole(AttachParentProcess))
            {
                var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                Console.SetOut(stdout);
            }
        }
        catch (Exception)
        {
            // Консоль — не критична: отчёт self-test всё равно пишется в файл.
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);
}
