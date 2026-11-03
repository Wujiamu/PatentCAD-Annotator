using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace PatentMarker.Diagnostics
{
    /// <summary>
    /// Reads the release manifest shipped beside PatentMarker.dll and checks it
    /// against the assembly that is actually executing. The manifest is a
    /// dependency-free key=value text file so the same implementation compiles
    /// for every supported CLR, including .NET 2.0.
    /// </summary>
    public static class PatReleaseManifest
    {
        public const string FileName = "PatentMarker-release.txt";
        public const string FormatVersion = "1";

        public sealed class AssemblyIdentity
        {
            public string Path = "";
            public string FileName = "";
            public long Size;
            public string Sha256 = "";
            public string Mvid = "";
            public string AssemblyVersion = "";
            public string InformationalVersion = "";
        }

        public sealed class ValidationResult
        {
            public const string Pass = "PASS";
            public const string Fail = "FAIL";
            public const string Skip = "SKIP";

            public string Status = Skip;
            public string Detail = "";

            public ValidationResult(string status, string detail)
            {
                Status = status ?? Skip;
                Detail = detail ?? "";
            }
        }

        public static AssemblyIdentity InspectCurrentAssembly()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string path = assembly.Location ?? "";
            return InspectAssembly(path, assembly);
        }

        public static AssemblyIdentity InspectAssembly(string path)
        {
            return InspectAssembly(path, null);
        }

        private static AssemblyIdentity InspectAssembly(string path, Assembly loadedAssembly)
        {
            AssemblyIdentity identity = new AssemblyIdentity();
            identity.Path = path ?? "";
            identity.FileName = identity.Path.Length > 0
                ? Path.GetFileName(identity.Path) : "";
            try
            {
                if (identity.Path.Length > 0 && File.Exists(identity.Path))
                {
                    identity.Size = new FileInfo(identity.Path).Length;
                    identity.Sha256 = ComputeSha256(identity.Path);
                }
            }
            catch
            {
                identity.Sha256 = "";
            }

            Assembly assembly = loadedAssembly;
            if (assembly == null && identity.Path.Length > 0)
            {
                try { assembly = Assembly.LoadFrom(identity.Path); }
                catch { assembly = null; }
            }
            if (assembly != null)
            {
                try { identity.AssemblyVersion = assembly.GetName().Version.ToString(); }
                catch { }
                try { identity.Mvid = assembly.ManifestModule.ModuleVersionId.ToString("D"); }
                catch { }
                try
                {
                    object[] attrs = assembly.GetCustomAttributes(
                        typeof(AssemblyInformationalVersionAttribute), false);
                    if (attrs != null && attrs.Length > 0)
                    {
                        identity.InformationalVersion =
                            ((AssemblyInformationalVersionAttribute)attrs[0]).InformationalVersion;
                    }
                }
                catch { }
            }
            return identity;
        }

        public static ValidationResult ValidateCurrentAssembly()
        {
            AssemblyIdentity identity = InspectCurrentAssembly();
            string manifestPath = "";
            try
            {
                string directory = Path.GetDirectoryName(identity.Path);
                if (directory != null && directory.Length > 0)
                    manifestPath = Path.Combine(directory, FileName);
            }
            catch { }
            return Validate(identity, manifestPath);
        }

        public static ValidationResult Validate(
            AssemblyIdentity identity, string manifestPath)
        {
            if (identity == null)
                return new ValidationResult(ValidationResult.Fail, "assembly identity unavailable");
            if (manifestPath == null || manifestPath.Length == 0 || !File.Exists(manifestPath))
                return new ValidationResult(ValidationResult.Skip,
                    "release manifest not found beside " + identity.FileName);

            Dictionary<string, string> values;
            try { values = ReadManifest(manifestPath); }
            catch (Exception ex)
            {
                return new ValidationResult(ValidationResult.Fail,
                    "release manifest unreadable: " + ex.Message);
            }

            string format = Get(values, "format");
            if (format != FormatVersion)
                return new ValidationResult(ValidationResult.Fail,
                    "unsupported manifest format: " + (format.Length == 0 ? "(missing)" : format));

            string expectedFile = Get(values, "artifact.file");
            if (!string.Equals(expectedFile, identity.FileName, StringComparison.OrdinalIgnoreCase))
                return new ValidationResult(ValidationResult.Fail,
                    "manifest artifact.file='" + expectedFile + "' loaded='" + identity.FileName + "'");

            string size = Get(values, "artifact.size");
            long expectedSize;
            if (size.Length == 0 || !long.TryParse(size, out expectedSize))
                return new ValidationResult(ValidationResult.Fail, "manifest artifact.size missing or invalid");
            if (expectedSize != identity.Size)
                return new ValidationResult(ValidationResult.Fail,
                    "size mismatch expected=" + expectedSize + " actual=" + identity.Size);

            string expectedSha = Get(values, "artifact.sha256");
            if (expectedSha.Length == 0 || identity.Sha256.Length == 0 ||
                !string.Equals(expectedSha, identity.Sha256, StringComparison.OrdinalIgnoreCase))
                return new ValidationResult(ValidationResult.Fail,
                    "SHA-256 mismatch expected=" + expectedSha + " actual=" + identity.Sha256);

            string expectedMvid = Get(values, "artifact.mvid");
            if (expectedMvid.Length == 0 ||
                !string.Equals(expectedMvid, identity.Mvid, StringComparison.OrdinalIgnoreCase))
                return new ValidationResult(ValidationResult.Fail,
                    "MVID mismatch expected=" + expectedMvid + " actual=" + identity.Mvid);

            string expectedVersion = Get(values, "artifact.informational_version");
            if (expectedVersion.Length > 0 &&
                !string.Equals(expectedVersion, identity.InformationalVersion, StringComparison.Ordinal))
                return new ValidationResult(ValidationResult.Fail,
                    "informational version mismatch expected=" + expectedVersion
                    + " actual=" + identity.InformationalVersion);

            return new ValidationResult(ValidationResult.Pass,
                "identity matched: " + identity.FileName + " | MVID " + identity.Mvid
                + " | SHA-256 " + identity.Sha256);
        }

        public static Dictionary<string, string> ReadManifest(string path)
        {
            Dictionary<string, string> values =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] lines = File.ReadAllLines(path);
            foreach (string raw in lines)
            {
                string line = raw == null ? "" : raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                int equals = line.IndexOf('=');
                if (equals <= 0) continue;
                string key = line.Substring(0, equals).Trim();
                string value = line.Substring(equals + 1).Trim();
                if (key.Length > 0) values[key] = value;
            }
            return values;
        }

        public static string ComputeSha256(string path)
        {
            using (FileStream stream = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(stream);
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static string Get(Dictionary<string, string> values, string key)
        {
            string value;
            return values.TryGetValue(key, out value) ? value : "";
        }
    }
}
