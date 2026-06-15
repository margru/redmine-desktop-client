using System;
using System.Configuration;
using System.IO;
using System.Windows.Forms;

namespace Redmine.Client
{
    public enum DialogType
    {
        New,
        Edit,
    };

    /// <summary>
    /// Protects the user settings file (user.config) against the corruption that .NET leaves
    /// behind when the process is killed mid-write (e.g. a PC crash while the timer is ticking,
    /// which used to rewrite the file every second). On startup a corrupt file is restored from
    /// a last-known-good backup (or removed so the app can start with defaults); the backup is
    /// refreshed whenever the config is known to be in a clean state.
    /// </summary>
    internal static class ConfigProtection
    {
        private static string UserConfigPath()
        {
            return ConfigurationManager
                .OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal).FilePath;
        }

        /// <summary>Call once, before any setting is read, to recover from a corrupt user.config.</summary>
        public static void RepairIfCorrupt()
        {
            try
            {
                // Forces the framework to parse user.config; a corrupt file throws here.
                ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal);
            }
            catch (ConfigurationErrorsException ex)
            {
                string corrupt = ex.Filename;
                if (string.IsNullOrEmpty(corrupt))
                    return;
                try
                {
                    string backup = corrupt + ".bak";
                    if (File.Exists(backup))
                        File.Copy(backup, corrupt, true);   // restore the last known-good config
                    else if (File.Exists(corrupt))
                        File.Delete(corrupt);               // nothing to restore -> start with defaults
                }
                catch { /* best effort - never block startup */ }
            }
        }

        /// <summary>Snapshot the current (clean) user.config as the restore point.</summary>
        public static void Backup()
        {
            try
            {
                string path = UserConfigPath();
                if (File.Exists(path))
                    File.Copy(path, path + ".bak", true);
            }
            catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Persists the running-timer state (is it ticking, and the elapsed seconds) so it can be
    /// resumed after the app is closed or crashes. Kept in its own tiny file - separate from
    /// user.config, which holds the URL/credentials/preferences - and written atomically (temp
    /// file swapped in with File.Replace) so a crash mid-write can never corrupt it. This is the
    /// frequently-changed value, so isolating it keeps the precious config out of harm's way.
    /// </summary>
    internal static class TimerState
    {
        private static string FilePath
        {
            get { return Path.Combine(Application.CommonAppDataPath, "TimerState.txt"); }
        }

        public static void Save(bool ticking, int ticks)
        {
            try
            {
                string path = FilePath;
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0};{1}", ticking ? 1 : 0, ticks));
                if (File.Exists(path))
                    File.Replace(tmp, path, null);  // atomic swap on NTFS - never a torn file
                else
                    File.Move(tmp, path);           // first write: nothing to corrupt yet
            }
            catch { /* best effort - losing the resume value is harmless */ }
        }

        /// <summary>Reads the saved timer state. Returns false (and defaults) if none/unreadable.</summary>
        public static bool TryLoad(out bool ticking, out int ticks)
        {
            ticking = false;
            ticks = 0;
            try
            {
                string path = FilePath;
                if (!File.Exists(path))
                    return false;
                string[] parts = File.ReadAllText(path).Split(';');
                if (parts.Length != 2)
                    return false;
                ticking = parts[0] == "1";
                ticks = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                ticking = false;
                ticks = 0;
                return false;
            }
        }
    }

    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Must be set before the first HTTP request creates the host's ServicePoint,
            // otherwise the parallel issue-page fetch is throttled to the default 2
            // concurrent connections per host. See MainFormData.GetIssuesParallel.
            System.Net.ServicePointManager.DefaultConnectionLimit = 20;
            // Recover a corrupt settings file before the first setting is read (in the form ctor).
            ConfigProtection.RepairIfCorrupt();
            try
            {
                AppDomain.CurrentDomain.UnhandledException += new
                    UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
            }
            catch (Exception)
            { }
            try
            {
                Application.Run(RedmineClientForm.Instance);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Start Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Handler for the weird exceptions we are not handling in our code and especially for the CLR20r3 stuff
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = (Exception)e.ExceptionObject;
            Console.WriteLine("Observed unhandled exception: {0}", ex.ToString());
        }

    }
}
