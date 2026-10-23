using System;
using System.Globalization;
using System.IO;
using System.Text;
using PatentMarker.Diagnostics;
using Xunit;

namespace PatentMarker.Tests
{
    public class PatReleaseManifestTests
    {
        [Fact]
        public void MatchingManifest_ReturnsPass()
        {
            PatReleaseManifest.AssemblyIdentity identity =
                PatReleaseManifest.InspectCurrentAssembly();
            string path = WriteManifest(identity, null, null);
            try
            {
                PatReleaseManifest.ValidationResult result =
                    PatReleaseManifest.Validate(identity, path);
                Assert.Equal(PatReleaseManifest.ValidationResult.Pass, result.Status);
                Assert.Contains(identity.Sha256, result.Detail);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void MissingManifest_ReturnsSkip()
        {
            PatReleaseManifest.AssemblyIdentity identity =
                PatReleaseManifest.InspectCurrentAssembly();
            string path = Path.Combine(Path.GetTempPath(),
                "pm-missing-" + Guid.NewGuid().ToString("N") + ".txt");
            PatReleaseManifest.ValidationResult result =
                PatReleaseManifest.Validate(identity, path);
            Assert.Equal(PatReleaseManifest.ValidationResult.Skip, result.Status);
        }

        [Fact]
        public void HashMismatch_ReturnsFail()
        {
            PatReleaseManifest.AssemblyIdentity identity =
                PatReleaseManifest.InspectCurrentAssembly();
            string path = WriteManifest(identity, "artifact.sha256", new string('0', 64));
            try
            {
                PatReleaseManifest.ValidationResult result =
                    PatReleaseManifest.Validate(identity, path);
                Assert.Equal(PatReleaseManifest.ValidationResult.Fail, result.Status);
                Assert.Contains("SHA-256 mismatch", result.Detail);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void MvidMismatch_ReturnsFail()
        {
            PatReleaseManifest.AssemblyIdentity identity =
                PatReleaseManifest.InspectCurrentAssembly();
            string path = WriteManifest(identity, "artifact.mvid",
                "00000000-0000-0000-0000-000000000000");
            try
            {
                PatReleaseManifest.ValidationResult result =
                    PatReleaseManifest.Validate(identity, path);
                Assert.Equal(PatReleaseManifest.ValidationResult.Fail, result.Status);
                Assert.Contains("MVID mismatch", result.Detail);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void MalformedManifest_ReturnsFail()
        {
            PatReleaseManifest.AssemblyIdentity identity =
                PatReleaseManifest.InspectCurrentAssembly();
            string path = Path.Combine(Path.GetTempPath(),
                "pm-malformed-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(path, "this is not a manifest\n", Encoding.UTF8);
            try
            {
                PatReleaseManifest.ValidationResult result =
                    PatReleaseManifest.Validate(identity, path);
                Assert.Equal(PatReleaseManifest.ValidationResult.Fail, result.Status);
            }
            finally { File.Delete(path); }
        }

        private static string WriteManifest(
            PatReleaseManifest.AssemblyIdentity identity,
            string keyToReplace,
            string replacement)
        {
            string[] lines =
            {
                "format=1",
                "product=PatentMarker",
                "artifact.file=" + identity.FileName,
                "artifact.size=" + identity.Size.ToString(CultureInfo.InvariantCulture),
                "artifact.sha256=" + identity.Sha256,
                "artifact.mvid=" + identity.Mvid,
                "artifact.informational_version=" + identity.InformationalVersion
            };
            if (keyToReplace != null)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith(keyToReplace + "=", StringComparison.Ordinal))
                        lines[i] = keyToReplace + "=" + replacement;
                }
            }
            string path = Path.Combine(Path.GetTempPath(),
                "pm-manifest-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
            return path;
        }
    }
}
