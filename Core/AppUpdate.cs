#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HearthStoner
{
    /// <summary>A published HearthStoner release on GitHub.</summary>
    /// <param name="Version">The release's number, from its tag (v1.2.3).</param>
    /// <param name="ExeUrl">Download of the single HearthStoner.exe.</param>
    /// <param name="Sha256Url">Download of HearthStoner.exe.sha256 ("&lt;hex&gt;  HearthStoner.exe").</param>
    /// <param name="PageUrl">The release's page on GitHub.</param>
    /// <param name="Notes">The release notes (that version's CHANGELOG section).</param>
    public sealed record AppRelease(Version Version, string ExeUrl, string Sha256Url, string PageUrl, string Notes);

    /// <summary>What an update check found: a newer release, nothing newer (both null), or why it failed.</summary>
    public sealed record AppUpdateStatus(AppRelease? Newer, string? Error);

    /// <summary>
    /// Updates HearthStoner itself from the GitHub Releases that .github/workflows/release.yml publishes. Not to be
    /// confused with <see cref="UpdateCheck"/>, which watches UnitySpy and the Hearthstone client.
    /// The new exe is downloaded, checked against its published SHA-256, and then its own 'install' command replaces
    /// the running copy (waiting for a game in progress to end first).
    /// </summary>
    public static class AppUpdate
    {
        /// <summary>The GitHub repository releases come from.</summary>
        public const string Repo = "bucckevin/HearthStoner";
        /// <summary>The release asset with the app.</summary>
        public const string ExeName = "HearthStoner.exe";
        /// <summary>The release asset with the app's SHA-256.</summary>
        public const string Sha256Name = ExeName + ".sha256";

        private static string LatestUrl => $"https://api.github.com/repos/{Repo}/releases/latest";

        /// <summary>Asks GitHub for the latest release and compares it with the running version.</summary>
        /// <param name="currentNumber">The running version, e.g. "1.2.0" (<see cref="AppVersion.Number"/>).</param>
        public static AppUpdateStatus Check(string currentNumber)
        {
            string json;
            try { json = Web.GetString(LatestUrl, 20); }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new AppUpdateStatus(null, "No releases on GitHub yet.");
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                return new AppUpdateStatus(null, "Couldn't reach GitHub: " + ex.Message);
            }
            var latest = ParseRelease(json);
            if (latest == null) return new AppUpdateStatus(null, "The latest release has no HearthStoner.exe.");
            return new AppUpdateStatus(IsNewer(latest, currentNumber) ? latest : null, null);
        }

        /// <summary>Reads a release from GitHub's releases API JSON. Null if it isn't a usable app release.</summary>
        /// <param name="json">One release object as GitHub returns it.</param>
        public static AppRelease? ParseRelease(string json)
        {
            JObject release;
            try { release = JObject.Parse(json); }
            catch (JsonReaderException) { return null; }
            if (!TryParseVersion((string?)release["tag_name"], out var version)) return null;
            var assets = release["assets"] as JArray ?? new JArray();
            string? Url(string name) => assets.FirstOrDefault(a => (string?)a["name"] == name)?.Value<string>("browser_download_url");
            var exe = Url(ExeName);
            var sha = Url(Sha256Name);
            if (exe == null || sha == null) return null;
            return new AppRelease(version, exe, sha, (string?)release["html_url"] ?? "", (string?)release["body"] ?? "");
        }

        /// <summary>Reads "1.2.3" or "v1.2.3" (exactly three numbers).</summary>
        public static bool TryParseVersion(string? text, out Version version)
        {
            version = new Version(0, 0, 0);
            var raw = (text ?? "").Trim().TrimStart('v', 'V');
            if (raw.Count(c => c == '.') != 2 || !Version.TryParse(raw, out var parsed)) return false;
            version = parsed;
            return true;
        }

        /// <summary>True when the release is a higher version than the running one (or the running one is unknown).</summary>
        public static bool IsNewer(AppRelease release, string currentNumber) =>
            !TryParseVersion(currentNumber, out var current) || release.Version > current;

        /// <summary>The hash from a .sha256 file ("&lt;64 hex&gt;  name"), lower case, or null if there isn't one.</summary>
        public static string? ParseSha256(string text)
        {
            var first = (text ?? "").Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return first != null && first.Length == 64 && first.All(Uri.IsHexDigit) ? first.ToLowerInvariant() : null;
        }

        /// <summary>True when the bytes have exactly this SHA-256.</summary>
        public static bool HashMatches(byte[] data, string sha256Hex) =>
            string.Equals(Convert.ToHexString(SHA256.HashData(data)), sha256Hex, StringComparison.OrdinalIgnoreCase);

        /// <summary>Downloads the release's exe into a folder after checking it against its published SHA-256.</summary>
        /// <param name="release">From <see cref="Check"/>.</param>
        /// <param name="folder">Where to put HearthStoner.exe (created if missing; an old copy is replaced).</param>
        /// <returns>The path of the checked exe.</returns>
        /// <exception cref="InvalidDataException">The checksum file is unreadable or the download doesn't match it.</exception>
        /// <exception cref="HttpRequestException">A download failed.</exception>
        public static string Download(AppRelease release, string folder)
        {
            var expected = ParseSha256(Web.GetString(release.Sha256Url, 20))
                           ?? throw new InvalidDataException($"{Sha256Name} doesn't contain a SHA-256.");
            var exe = Web.GetBytes(release.ExeUrl, 600);
            if (!HashMatches(exe, expected))
                throw new InvalidDataException($"The downloaded {ExeName} doesn't match its published SHA-256; not installing it.");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, ExeName);
            File.WriteAllBytes(path, exe);
            return path;
        }
    }
}
