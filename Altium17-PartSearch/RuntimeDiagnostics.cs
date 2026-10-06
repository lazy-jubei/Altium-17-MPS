using System;
using System.IO;

namespace Altium17PartSearch
{
    internal static class RuntimeDiagnostics
    {
        internal static void Trace(string message)
        {
            if (Environment.GetEnvironmentVariable("ALTIUM_PART_SEARCH_TRACE") != "1") return;
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Altium17PartSearch");
                Directory.CreateDirectory(root);
                File.AppendAllText(Path.Combine(root, "cloud-download.log"), $"{DateTime.UtcNow:u} {message}\n");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        internal static void Error(string operation, Exception error)
        {
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Altium17PartSearch");
                Directory.CreateDirectory(root);
                File.AppendAllText(Path.Combine(root, "runtime-errors.log"), $"{DateTime.UtcNow:u} {operation}\n{error}\n\n");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
