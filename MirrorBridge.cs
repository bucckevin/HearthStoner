using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace HearthStoner
{
    /// <summary>
    /// Loads HearthMirror.dll from a Hearthstone Deck Tracker install and calls it via .NET reflection.
    ///
    /// Why reflection instead of a normal reference: HearthMirror's API has changed shape over the years
    /// (older builds expose static methods like Reflection.GetCollection(); newer builds go through
    /// Reflection.Client.GetCollection()). Binding by name at runtime works with either, and keeps
    /// working when HDT auto-updates its copy of the DLL.
    /// </summary>
    public sealed class MirrorBridge
    {
        private readonly Assembly _asm;
        private readonly Type _reflectionType;
        private readonly object _target;   // Client instance (new API) or null for static calls (old API)
        private readonly Type _targetType;
        private readonly string _dllDir;

        public string DllPath { get; }

        private MirrorBridge(string dllPath)
        {
            DllPath = dllPath;
            _dllDir = Path.GetDirectoryName(dllPath);

            // HearthMirror may depend on other DLLs that live next to it in the HDT folder.
            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromHdtFolder;

            _asm = Assembly.LoadFrom(dllPath);
            _reflectionType = _asm.GetType("HearthMirror.Reflection")
                ?? _asm.GetTypes().FirstOrDefault(t => t.Name == "Reflection")
                ?? throw new InvalidOperationException("Couldn't find the HearthMirror.Reflection type in " + dllPath);

            // New API: static property `Client` returning an object that holds the Get* methods.
            var clientProp = _reflectionType.GetProperty("Client", BindingFlags.Public | BindingFlags.Static);
            if (clientProp != null)
            {
                _target = clientProp.GetValue(null);
                _targetType = _target?.GetType() ?? clientProp.PropertyType;
            }
            else
            {
                _target = null;
                _targetType = _reflectionType;
            }
        }

        private Assembly ResolveFromHdtFolder(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name + ".dll";
            var candidate = Path.Combine(_dllDir, name);
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        }

        public static MirrorBridge Load(string explicitPath = null)
        {
            var path = explicitPath
                       ?? Environment.GetEnvironmentVariable("HEARTHMIRROR_DLL")
                       ?? FindInHdtInstall();
            if (path == null || !File.Exists(path))
                throw new FileNotFoundException(
                    "HearthMirror.dll not found. Install Hearthstone Deck Tracker (https://hsreplay.net/downloads/), " +
                    "or pass --mirror <path to HearthMirror.dll>, or set the HEARTHMIRROR_DLL environment variable.");
            return new MirrorBridge(path);
        }

        /// <summary>HDT installs to %LocalAppData%\HearthstoneDeckTracker\app-x.y.z\ (Squirrel). Pick the newest.</summary>
        private static string FindInHdtInstall()
        {
            var roots = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HearthstoneDeckTracker"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HearthstoneDeckTracker"),
            };
            foreach (var root in roots.Where(Directory.Exists))
            {
                var hit = Directory.GetDirectories(root, "app-*")
                    .OrderByDescending(d => ParseVersion(Path.GetFileName(d).Substring(4)))
                    .Select(d => Path.Combine(d, "HearthMirror.dll"))
                    .FirstOrDefault(File.Exists);
                if (hit != null) return hit;

                var flat = Path.Combine(root, "HearthMirror.dll");
                if (File.Exists(flat)) return flat;
            }
            return null;
        }

        private static Version ParseVersion(string s) => Version.TryParse(s, out var v) ? v : new Version(0, 0);

        /// <summary>All zero-argument Get*/Is* methods HearthMirror exposes in this build.</summary>
        public IEnumerable<string> ListMethods()
        {
            var flags = BindingFlags.Public | (_target == null ? BindingFlags.Static : BindingFlags.Instance);
            return _targetType.GetMethods(flags)
                .Where(m => m.GetParameters().Length == 0 && m.ReturnType != typeof(void) && !m.IsSpecialName)
                .Where(m => m.DeclaringType != typeof(object))
                .Select(m => m.Name)
                .Distinct()
                .OrderBy(n => n);
        }

        public bool Has(string method) => FindMethod(method) != null;

        /// <summary>Calls a zero-argument HearthMirror method by name. Returns null if Hearthstone isn't ready.</summary>
        public object Call(string method)
        {
            var m = FindMethod(method)
                    ?? throw new MissingMethodException($"This HearthMirror build has no method '{method}'. Try the 'methods' command.");
            try
            {
                return m.Invoke(_target, null);
            }
            catch (TargetInvocationException ex)
            {
                // HearthMirror throws while Hearthstone is starting, loading, or not running.
                throw new InvalidOperationException($"{method} failed: {ex.InnerException?.Message ?? ex.Message}", ex.InnerException ?? ex);
            }
        }

        public object TryCall(string method)
        {
            try { return Has(method) ? Call(method) : null; }
            catch { return null; }
        }

        private MethodInfo FindMethod(string name)
        {
            var flags = BindingFlags.Public | (_target == null ? BindingFlags.Static : BindingFlags.Instance);
            return _targetType.GetMethods(flags)
                .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == 0);
        }

        // ---------- Helpers for reading HearthMirror result objects without compile-time types ----------

        public static object Get(object obj, params string[] names)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            foreach (var n in names)
            {
                var p = t.GetProperty(n, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(obj);
                var f = t.GetField(n, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (f != null) return f.GetValue(obj);
            }
            return null;
        }

        public static IEnumerable<object> AsList(object obj)
        {
            if (obj == null || obj is string) return Enumerable.Empty<object>();
            if (obj is IEnumerable e) return e.Cast<object>();
            return Enumerable.Empty<object>();
        }

        /// <summary>Turns any HearthMirror object graph into dictionaries/lists so it can be printed as JSON.</summary>
        public static object ToPlain(object obj, int depth = 0)
        {
            if (obj == null) return null;
            if (depth > 8) return obj.ToString();
            var t = obj.GetType();
            if (t.IsPrimitive || obj is string || obj is decimal || obj is DateTime || obj is Guid) return obj;
            if (t.IsEnum) return obj.ToString();
            if (obj is IDictionary dict)
            {
                var d = new Dictionary<string, object>();
                foreach (DictionaryEntry kv in dict) d[kv.Key?.ToString() ?? "null"] = ToPlain(kv.Value, depth + 1);
                return d;
            }
            if (obj is IEnumerable list) return list.Cast<object>().Select(x => ToPlain(x, depth + 1)).ToList();

            var result = new Dictionary<string, object>();
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0))
            {
                try { result[p.Name] = ToPlain(p.GetValue(obj), depth + 1); } catch { /* skip unreadable */ }
            }
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                try { result[f.Name] = ToPlain(f.GetValue(obj), depth + 1); } catch { }
            }
            return result;
        }
    }
}
