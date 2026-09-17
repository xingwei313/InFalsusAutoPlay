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

        /// <summary>`noscore`. Read on the same occasions as the first key; written by nothing.</summary>
        private const string NoScoreKey = "noscore";

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

            WriteFresh();
            Diagnostics.Info($"wrote default config to {Config.Path}");
        }

        /// <summary>
        /// Re-reads the file into the settings. Called on entering a chart rather than only at startup,
        /// so a switch can be flipped without restarting the game.
        ///
        /// Everything downstream reads a setting at the moment it needs it, so a new value applies from
        /// the next tick. A file that is there but cannot be read leaves the settings already in force
        /// alone — falling back to a default because of a transient read failure would silently turn
        /// autoplay off in the middle of a run.
        ///
        /// A key the file does not name is a different case and goes back to its default, so that
        /// removing a line does what it looks like it does. The two settings differ in which way that
        /// errs: an `autoplay` line that is deleted returns the switch to on, and a `noscore=0` line
        /// that is deleted returns the record to being skipped — which is the state the key exists for.
        /// </summary>
        internal static void Load()
        {
            try
            {
                if (!File.Exists(Config.Path))
                {
                    // Nothing is there, so nothing names a key: the rule below applies, not the
                    // read-failure rule. Deleting the file is how someone says "I have no settings", and
                    // leaving the last file's answers in force would contradict that until a restart.
                    Config.Autoplay = Config.AutoplayDefault;
                    Config.NoScore = Config.NoScoreDefault;
                    return;
                }

                // Read into a local before assigning anything: a file that cannot be read has to leave
                // the settings in force, and a reset placed above this line would have undone that on
                // the way to the exception. Once the lines are in hand the read has happened.
                string[] lines = File.ReadAllLines(Config.Path);

                Config.Autoplay = Config.AutoplayDefault;
                Config.NoScore = Config.NoScoreDefault;

                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();

                    if (key.Equals(Key, StringComparison.OrdinalIgnoreCase))
                        Config.Autoplay = Truthy(value);
                    else if (key.Equals(NoScoreKey, StringComparison.OrdinalIgnoreCase))
                        Config.NoScore = Truthy(value);
                }
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"could not read config, keeping the settings in force: " +
                                 Diagnostics.Describe(e));
            }
        }

        /// <summary>
        /// Writes the `autoplay` switch into the file, and into the setting in force, and reports
        /// whether the file was written. This key only: it is what the settings page's borrowed row
        /// stands for.
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
                    WriteFresh();
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

                    // Every match, not the first. <see cref="Load"/> applies a file's lines in order, so
                    // the last line to name the key is the one in force; editing an earlier one would
                    // leave the file saying one thing and the game doing another.
                    lines[i] = Line(Key, on);
                    replaced = true;
                }

                if (!replaced)
                {
                    // No line to edit: the file was written by something else, or by a hand that
                    // removed it. Appending keeps the file's own shape; the next read takes it like any
                    // other line.
                    var grown = new string[lines.Length + 1];
                    Array.Copy(lines, grown, lines.Length);
                    grown[lines.Length] = Line(Key, on);
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
        /// The file is the one key and nothing else — no header explaining it, and not the other key
        /// either: a fresh file names only what has already been decided by hand, and
        /// <see cref="NoScoreKey"/> is written by no code path at all.
        ///
        /// What a key means belongs where the key is read (<see cref="Config.Autoplay"/> and
        /// <see cref="Config.NoScore"/>, and the AUTO row on the settings page), not copied into a
        /// file that then has to be kept in step with it.
        /// </summary>
        private static void WriteFresh()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(Config.Path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                File.WriteAllText(Config.Path, Line(Key, Config.Autoplay) + "\n");
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"could not write config: {Diagnostics.Describe(e)}");
            }
        }

        private static string Line(string key, bool on) => $"{key}={(on ? 1 : 0)}";

        private static bool Truthy(string v) =>
            v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}
