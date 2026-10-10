using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using PatentMarker.IO;
using PatentOffice.Shared;
using Xunit;

namespace PatentOffice.Common.CodeTests
{
    public sealed class OfficeDictionaryTests
    {
        [Fact]
        public void Read_ParsesWordDictionaryWithoutChangingItsBytes()
        {
            byte[] bytes = Encoding.UTF8.GetBytes(
                "{\"metadata\":{\"version\":\"1\"},\"entries\":[{" +
                "\"number\":\" 1342A \",\"name\":null,\"occurrences\":2}],\"warnings\":[]}");
            string path = WriteTempFile(bytes);
            try
            {
                DictionarySnapshot snapshot = DictionaryReader.Read(path);

                Assert.Equal(Path.GetFullPath(path), snapshot.Path);
                PatentEntry entry = Assert.Single(snapshot.Dictionary.Entries);
                Assert.Equal(" 1342A ", entry.Number);
                Assert.Equal("", entry.Name);
                Assert.Equal(2, entry.Occurrences);
                Assert.Equal(Sha256(bytes), snapshot.Sha256);
                Assert.Equal(bytes, File.ReadAllBytes(path));
            }
            finally
            {
                DeleteTempFile(path);
            }
        }

        [Fact]
        public void Read_AcceptsAnEmptyDictionary()
        {
            string path = WriteTempFile(Encoding.UTF8.GetBytes("{\"entries\":[]}"));
            try
            {
                DictionarySnapshot snapshot = DictionaryReader.Read(path);

                Assert.Empty(snapshot.Dictionary.Entries);
            }
            finally
            {
                DeleteTempFile(path);
            }
        }

        [Fact]
        public void Read_RejectsAnEntryWithoutANumber()
        {
            string path = WriteTempFile(Encoding.UTF8.GetBytes(
                "{\"entries\":[{\"number\":\" \",\"name\":\"part\"}]}"));
            try
            {
                Assert.Throws<InvalidDataException>(() => DictionaryReader.Read(path));
            }
            finally
            {
                DeleteTempFile(path);
            }
        }

        [Fact]
        public void Read_RejectsInvalidUtf8()
        {
            byte[] bytes = { (byte)'{', (byte)'"', (byte)'x', (byte)'"', (byte)':', (byte)'"', 0xC3, 0x28, (byte)'"', (byte)'}' };
            string path = WriteTempFile(bytes);
            try
            {
                Assert.Throws<DecoderFallbackException>(() => DictionaryReader.Read(path));
            }
            finally
            {
                DeleteTempFile(path);
            }
        }

        [Fact]
        public void NumberIdentityComparer_TrimsAndIgnoresCase()
        {
            var numbers = new System.Collections.Generic.HashSet<string>(NumberIdentity.Comparer);

            Assert.True(numbers.Add(" 1342A "));
            Assert.False(numbers.Add("1342a"));
        }

        [Fact]
        public void ExtensibilityContract_UsesOfficeDispatchGuidAndMemberIds()
        {
            Type contract = typeof(IDTExtensibility2);
            InterfaceTypeAttribute interfaceType = (InterfaceTypeAttribute)Attribute.GetCustomAttribute(
                contract, typeof(InterfaceTypeAttribute));

            Assert.Equal(new Guid("B65AD801-ABAF-11D0-BB8B-00A0C90F2744"), contract.GUID);
            Assert.Equal(ComInterfaceType.InterfaceIsIDispatch, interfaceType.Value);

            string[] methodNames =
            {
                "OnConnection", "OnDisconnection", "OnAddInsUpdate", "OnStartupComplete", "OnBeginShutdown"
            };
            for (int i = 0; i < methodNames.Length; i++)
            {
                MethodInfo method = contract.GetMethod(methodNames[i]);
                DispIdAttribute dispId = (DispIdAttribute)Attribute.GetCustomAttribute(
                    method, typeof(DispIdAttribute));
                Assert.Equal(i + 1, dispId.Value);
            }
        }

        [Fact]
        public void OfficeCommonSources_AreLinkedIntoTheHostAssembly()
        {
            Assembly hostAssembly = typeof(DictionaryReader).Assembly;

            Assert.Contains(hostAssembly.GetName().Name,
                new[] { "PatentOffice.PowerPoint", "PatentOffice.Visio" });
            Assert.NotNull(hostAssembly.GetType("PatentOffice.Shared.PatentDictionary"));
            Assert.NotNull(hostAssembly.GetType("PatentOffice.Shared.IDTExtensibility2"));
            Assert.NotNull(hostAssembly.GetType("PatentOffice.Shared.OfficeDiagnostics"));
            Assert.DoesNotContain(hostAssembly.GetReferencedAssemblies(),
                reference => reference.Name == "PatentOffice.Shared");
        }

        private static string WriteTempFile(byte[] bytes)
        {
            string path = Path.Combine(Path.GetTempPath(),
                "PatentOffice-Shared-" + Guid.NewGuid().ToString("N") + ".dict.json");
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private static void DeleteTempFile(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        private static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                StringBuilder text = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) text.Append(hash[i].ToString("x2"));
                return text.ToString();
            }
        }
    }
}
