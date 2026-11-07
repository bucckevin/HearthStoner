using System;
using System.IO;

namespace HearthStoner
{
    /// <summary>The data folder, %LocalAppData%\HearthStoner.</summary>
    public static class AppPaths
    {
        public static string DataDir { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HearthStoner");

        public static string Data(params string[] parts) => Path.Combine(DataDir, Path.Combine(parts));
    }
}
