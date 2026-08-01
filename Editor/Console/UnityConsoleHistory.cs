using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace Agxmeister.Uplink.Console
{
    /// <summary>
    /// Reads the Editor's Console window through <c>UnityEditor.LogEntries</c>, which is internal. That is the
    /// only way to see messages logged before Uplink loaded, and it is used for exactly that — once, to seed
    /// the buffer. Everything here is reflective and every failure answers null, so a Unity release that
    /// renames a field costs the package its history and nothing else.
    /// </summary>
    public sealed class UnityConsoleHistory : IConsoleHistory
    {
        // UnityEditor.LogMessageFlags, which is internal too. A bit this does not know about reads as a plain
        // log, which is the harmless way to be wrong.
        private const int ErrorBits =
            (1 << 0) |  // Error
            (1 << 1) |  // Assert
            (1 << 4) |  // Fatal
            (1 << 6) |  // AssetImportError
            (1 << 8) |  // ScriptingError
            (1 << 11) | // ScriptCompileError
            (1 << 17) | // ScriptingException
            (1 << 21);  // ScriptingAssertion

        private const int WarningBits =
            (1 << 7) |  // AssetImportWarning
            (1 << 9) |  // ScriptingWarning
            (1 << 12);  // ScriptCompileWarning

        public IList<ConsoleEntry> Read()
        {
            try
            {
                return ReadEntries();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static IList<ConsoleEntry> ReadEntries()
        {
            var editor = typeof(EditorWindow).Assembly;
            var logEntries = editor.GetType("UnityEditor.LogEntries");
            var logEntry = editor.GetType("UnityEditor.LogEntry");
            if (logEntries == null || logEntry == null)
            {
                return null;
            }

            const BindingFlags anyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var start = logEntries.GetMethod("StartGettingEntries", anyStatic);
            var finish = logEntries.GetMethod("EndGettingEntries", anyStatic);
            var get = logEntries.GetMethod(
                "GetEntryInternal", anyStatic, null, new[] { typeof(int), logEntry }, null);

            const BindingFlags anyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            // Renamed from `condition` in older Editors.
            var messageField = logEntry.GetField("message", anyInstance) ?? logEntry.GetField("condition", anyInstance);
            var modeField = logEntry.GetField("mode", anyInstance);

            if (start == null || finish == null || get == null || messageField == null || modeField == null)
            {
                return null;
            }

            var count = (int)start.Invoke(null, null);
            try
            {
                var buffer = Activator.CreateInstance(logEntry);
                var arguments = new object[2];
                var entries = new List<ConsoleEntry>(count);

                for (var row = 0; row < count; row++)
                {
                    arguments[0] = row;
                    arguments[1] = buffer;
                    get.Invoke(null, arguments);

                    entries.Add(new ConsoleEntry
                    {
                        Level = LevelOf((int)modeField.GetValue(buffer)),
                        // The Console stores message and callstack as one blob and does not say where the
                        // split is, so it is reported whole rather than guessed apart.
                        Message = (string)messageField.GetValue(buffer) ?? string.Empty,
                    });
                }

                return entries;
            }
            finally
            {
                // The Console stays locked for as long as entries are being read.
                finish.Invoke(null, null);
            }
        }

        private static string LevelOf(int mode)
        {
            if ((mode & ErrorBits) != 0)
            {
                return ConsoleLevel.Error;
            }
            return (mode & WarningBits) != 0 ? ConsoleLevel.Warning : ConsoleLevel.Log;
        }
    }
}
