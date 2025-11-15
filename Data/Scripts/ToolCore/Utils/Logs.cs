using Sandbox.ModAPI;
using System;
using System.IO;
using ToolCore.Session;

namespace ToolCore.Utils
{
    internal static class Logs
    {
        internal const string LOG_PREFIX = "ToolCore";
        internal const string LOG_SUFFIX = ".log";
        internal const int LOGS_TO_KEEP = 5;
        internal static int LOGGED_LINES = 0;
        internal static TextWriter TextWriter;
        internal static bool PAUSED = false;

        internal static void InitLogs()
        {
            int last = LOGS_TO_KEEP - 1;
            string lastName = LOG_PREFIX + last + LOG_SUFFIX;
            if (MyAPIGateway.Utilities.FileExistsInLocalStorage(lastName, typeof(Logs)))
                MyAPIGateway.Utilities.DeleteFileInLocalStorage(lastName, typeof(Logs));

            if (last > 0)
            {
                for (int i = last; i > 0; i--)
                {
                    string oldName = LOG_PREFIX + (i - 1) + LOG_SUFFIX;
                    string newName = LOG_PREFIX + i + LOG_SUFFIX;
                    RenameFileInLocalStorage(oldName, newName, typeof(Logs));
                }
            }

            string fileName = LOG_PREFIX + 0 + LOG_SUFFIX;
            TextWriter = MyAPIGateway.Utilities.WriteFileInLocalStorage(fileName, typeof(Logs));

            var message = $"{DateTime.Now:dd-MM-yy HH-mm-ss} - Logging Started";
            TextWriter.WriteLine(message);
            TextWriter.Flush();

        }

        internal static void RenameFileInLocalStorage(string oldName, string newName, Type anyObjectInYourMod)
        {
            if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(oldName, anyObjectInYourMod))
                return;

            if (MyAPIGateway.Utilities.FileExistsInLocalStorage(newName, anyObjectInYourMod))
                return;

            using (var read = MyAPIGateway.Utilities.ReadFileInLocalStorage(oldName, anyObjectInYourMod))
            {
                using (var write = MyAPIGateway.Utilities.WriteFileInLocalStorage(newName, anyObjectInYourMod))
                {
                    write.Write(read.ReadToEnd());
                    write.Flush();
                    write.Dispose();
                }
            }
            MyAPIGateway.Utilities.DeleteFileInLocalStorage(oldName, anyObjectInYourMod);
        }

        internal static void WriteLine(string text)
        {
            if (PAUSED) return;
            if (LOGGED_LINES < 500)
            {
                string line = $"{DateTime.Now:dd-MM-yy HH-mm-ss} - " + text;
                lock (TextWriter)
                {
                    LOGGED_LINES++;
                    TextWriter.WriteLine(line);
                    TextWriter.Flush();
                }
            }
            else
                Pause();
        }
        internal static void Pause()
        {
            lock (TextWriter)
            {
                var message = $"{DateTime.Now:dd-MM-yy HH-mm-ss} - Logging Stopped at 500 lines for overflow protection";
                TextWriter.WriteLine(message);
                TextWriter.Flush();
            }
            PAUSED = true;
        }

        internal static void Close()
        {
            var message = $"{DateTime.Now:dd-MM-yy HH-mm-ss} - Logging Stopped";
            TextWriter.WriteLine(message);

            TextWriter.Flush();
            TextWriter.Close();
            TextWriter.Dispose();
        }

        internal static void LogException(Exception ex)
        {
            var hasInner = ex.InnerException != null;
            var text = !hasInner ? $"{ex.Message}\n{ex.StackTrace}" : ex.Message;
            WriteLine(text);
            if (hasInner)
                LogException(ex.InnerException);
        }

    }
}
