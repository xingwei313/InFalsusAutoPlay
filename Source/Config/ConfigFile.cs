using System;
using System.IO;

namespace InFalsusAutoPlay
{
    /// <summary>
    /// Reading and writing <see cref="Config"/>. The whole of the file format lives here, so the
    /// settings class stays a plain description of what can be set.
    /// </summary>
    internal static class ConfigFile
    {
        private const string Key = "autoplay";

        /// <summary>
        /// First run, from `OnLateInitializeMelon`. Writes the defaults out when there is no file
        /// yet; otherwise this is just the first <see cref="Load"/>.
        /// </summary>
        internal static void Initialize()
        {
            if (File.Exists(Config.Path))
            {
                Load();
                return;
            }

            WriteFresh(Config.Autoplay);
            Diagnostics.Info($"wrote default config to {Config.Path}");
        }

        /// <summary>
        /// Re-reads the file into <see cref="Config.Autoplay"/>. Called on entering a chart rather than
        /// only at startup, so the switch can be flipped without restarting the game.
        ///
        /// Everything downstream reads <see cref="Config.Autoplay"/> at the moment it needs it, so the
        /// new value applies from the next tick. A file that cannot be read leaves the setting already
        /// in force alone — falling back to a default because of a transient read failure would
        /// silently turn autoplay off in the middle of a run.
        /// </summary>
        internal static void Load()
        {
            try
            {
                if (!File.Exists(Config.Path)) return;

                foreach (string raw in File.ReadAllLines(Config.Path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    if (!line.Substring(0, eq).Trim().Equals(Key, StringComparison.OrdinalIgnoreCase))
                        continue;

                    Config.Autoplay = Truthy(line.Substring(eq + 1).Trim());
                }
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"could not read config, keeping the settings in force: " +
                                 Diagnostics.Describe(e));
            }
        }

        /// <summary>
        /// Writes the switch into the file, and into the setting in force, and reports whether the
        /// file was written.
        ///
        /// A line edit rather than a whole-file write: the file is the user's, it may hold comments and
        /// hand-tuned values, and rewriting it from a copy read at some earlier point would quietly
        /// undo anything edited since. Only the `autoplay` line is replaced; everything else is copied
        /// through byte for byte.
        ///
        /// The in-memory value is updated as well so the running game sees the change at once — the
        /// file alone would only take effect at the next chart entry.
        /// </summary>
        internal static bool Write(bool on)
        {
            Config.Autoplay = on;

            try
            {
                if (!File.Exists(Config.Path))
                {
                    WriteFresh(on);
                    return true;
                }

                string[] lines = File.ReadAllLines(Config.Path);
                bool replaced = false;

                for (int i = 0; i < lines.Length; i++)
                {
                    string trimmed = lines[i].TrimStart();
                    if (trimmed.Length == 0 || trimmed[0] == '#') continue;

                    int eq = trimmed.IndexOf('=');
                    if (eq <= 0) continue;
                    if (!trimmed.Substring(0, eq).Trim().Equals(Key, StringComparison.OrdinalIgnoreCase))
                        continue;

                    lines[i] = Line(on);
                    replaced = true;
                    break;
                }

                if (!replaced)
                {
                    // No line to edit: the file was written by something else, or by a hand that
                    // removed it. Appending keeps the file's own shape; the next read takes it like any
                    // other line.
                    var grown = new string[lines.Length + 1];
                    Array.Copy(lines, grown, lines.Length);
                    grown[lines.Length] = Line(on);
                    lines = grown;
                }

                File.WriteAllLines(Config.Path, lines);
                return true;
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"could not write autoplay to the config: {Diagnostics.Describe(e)}");
                return false;
            }
        }

        /// <summary>
        /// Writes the file from scratch. First run, or after the file is gone.
        ///
        /// The file is the one line and nothing else — no header explaining the key. What the key
        /// means belongs where the key is read (<see cref="Config.Autoplay"/>, and the AUTO row on the
        /// settings page), not copied into a file that then has to be kept in step with it.
        /// </summary>
        private static void WriteFresh(bool on)
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(Config.Path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                File.WriteAllText(Config.Path, Line(on) + "\n");
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"could not write config: {Diagnostics.Describe(e)}");
            }
        }

        private static string Line(bool on) => $"{Key}={(on ? 1 : 0)}";

        private static bool Truthy(string v) =>
            v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}
