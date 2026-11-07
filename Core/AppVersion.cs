#nullable enable
using System;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace HearthStoner
{
    /// <summary>
    /// Which HearthStoner this is: the major.minor.patch number from the VERSION file, when it was built, and the git
    /// commit it was built from (the SDK adds that to the informational version as "1.2.3+&lt;sha&gt;").
    /// </summary>
    public sealed class AppVersion
    {
        /// <summary>The metadata key the app project stamps the build time under (see HearthStoner.csproj).</summary>
        public const string BuildDateKey = "BuildDate";
        private const int ShortCommit = 7;

        private static readonly Lazy<AppVersion> _current =
            new Lazy<AppVersion>(() => Of(Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly));

        /// <summary>"1.2.3", or "0.0.0" when the build carries no version.</summary>
        public string Number { get; }
        /// <summary>When this exe was built (local time), or null if unknown.</summary>
        public DateTime? BuildDate { get; }
        /// <summary>The first 7 characters of the git commit it was built from, or null if unknown.</summary>
        public string? Commit { get; }

        /// <summary>The running app's version.</summary>
        public static AppVersion Current => _current.Value;

        private AppVersion(string number, DateTime? buildDate, string? commit)
        {
            Number = number;
            BuildDate = buildDate;
            Commit = commit;
        }

        /// <summary>"1.2.3, built Nov 7, 2026 (commit 2c99dcb)": the number with whatever else is known.</summary>
        public string Detail
        {
            get
            {
                var text = Number;
                if (BuildDate is DateTime d) text += ", built " + d.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
                if (Commit != null) text += $" (commit {Commit})";
                return text;
            }
        }

        /// <summary>Reads the version an assembly was built with.</summary>
        /// <param name="assembly">Usually the app's exe; must not be null.</param>
        public static AppVersion Of(Assembly assembly)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                                ?? assembly.GetName().Version?.ToString(3);
            var buildDate = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == BuildDateKey)?.Value;
            return Parse(informational, buildDate);
        }

        /// <summary>Builds a version from the raw strings.</summary>
        /// <param name="informationalVersion">"1.2.3" or "1.2.3+&lt;full sha&gt;"; null or blank means unknown.</param>
        /// <param name="buildDate">"yyyy-MM-ddTHH:mm" as the project stamps it; anything else means unknown.</param>
        public static AppVersion Parse(string? informationalVersion, string? buildDate)
        {
            var raw = (informationalVersion ?? "").Trim();
            var plus = raw.IndexOf('+');
            var number = (plus >= 0 ? raw.Substring(0, plus) : raw).Trim();
            if (number.Length == 0) number = "0.0.0";
            var sha = plus >= 0 ? raw.Substring(plus + 1).Trim() : "";
            string? commit = sha.Length == 0 ? null : sha.Substring(0, Math.Min(ShortCommit, sha.Length));
            DateTime? built = DateTime.TryParseExact(buildDate, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d
                : null;
            return new AppVersion(number, built, commit);
        }
    }
}
