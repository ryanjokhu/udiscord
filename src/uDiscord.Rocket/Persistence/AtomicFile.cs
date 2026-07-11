using System;
using System.IO;
using System.Text;

namespace UDiscord.Rocket.Persistence
{
    internal static class AtomicFile
    {
        public static void WriteAllText(string path, string content)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            string temporaryPath = path + ".tmp";
            string backupPath = path + ".bak";
            File.WriteAllText(temporaryPath, content ?? string.Empty, new UTF8Encoding(false));

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temporaryPath, path, backupPath, true);
                    return;
                }
                catch (PlatformNotSupportedException)
                {
                }
                catch (IOException)
                {
                }

                File.Copy(path, backupPath, true);
                File.Delete(path);
            }

            File.Move(temporaryPath, path);
        }

        public static string ReadAllTextOrQuarantine(string path, Action<string> warning)
        {
            if (!File.Exists(path)) return null;
            try
            {
                return File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception exception)
            {
                string quarantine = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                try
                {
                    File.Copy(path, quarantine, true);
                }
                catch
                {
                }

                warning?.Invoke("Unable to read " + Path.GetFileName(path) + "; preserved a quarantine copy. " + exception.Message);
                return null;
            }
        }
    }
}
