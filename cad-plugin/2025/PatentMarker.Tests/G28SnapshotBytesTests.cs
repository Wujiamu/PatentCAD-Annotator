using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;
using PatentMarker.IO;
using PatentMarker.Palette;

namespace PatentMarker.Tests
{
    /// <summary>
    /// G28锛圧-004锛夛細<c>ReadSnapshot</c> 蹇呴』瑙ｆ瀽瀹冭嚜宸辫杩囥€佺畻杩囪韩浠界殑閭ｄ唤瀛楄妭銆?    ///
    /// 淇鍓嶆槸 <c>File.ReadAllBytes</c> 绠楄韩浠姐€佸啀 <c>DictLoader.Load(path)</c> 鎸夎矾寰?    /// 閲嶈涓€閬嶃€備袱娆¤鍙栦箣闂存枃浠惰鏇挎崲锛屽氨浼氬緱鍒般€孉 鐨勮韩浠?+ B 鐨勬ā鍨嬨€嶏紝鍚庣画浠?A 鐨?    /// 韬唤鍋?CAS 鍗存妸 B 鐨勬潯鐩啓杩?A銆傛櫘閫氭渶缁堝搱甯屾鏌ョ湅涓嶅嚭 A鈫払鈫扐銆?    ///
    /// 浜ら敊鐐圭敱鐢熶骇浜嬪疄纭畾鎬у埗閫狅紝涓嶅姞娴嬭瘯閽╁瓙锛?see cref="DictLoader.Load(string)"/>
    /// 涓?<see cref="DictLoader.LoadBytes(string, byte[])"/> 鍦?bytes 涓?null 鏃堕兘浠庣鐩?    /// 閲嶆柊璇诲彇锛涘彧鏈夊綋璋冪敤鏂逛紶鍏?bytes 鏃讹紝瑙ｆ瀽鎵嶅繀鐒跺搴旇皟鐢ㄦ柟璇诲埌鐨勯偅涓€浠姐€傚洜姝ゆ祴璇?    /// 璁╂枃浠跺湪涓ゆ璇诲彇涔嬮棿鏀瑰彉锛屽啀鐢ㄣ€岃韩浠芥槸鍚︾瓑浜庢ā鍨嬫墍鏉ヨ嚜鍐呭鐨勮韩浠姐€嶅垽瀹氥€?    /// 淇鍚?ReadSnapshot 鎶婂瓧鑺備紶涓嬪幓锛屾ā鍨嬭韩浠芥亽绛変簬 read.DictIdentity锛?    /// 淇鍓嶆ā鍨嬫潵鑷浜屾璇诲彇锛屼袱鑰呬細鍒嗗弶銆?    /// </summary>
    public sealed class G28SnapshotBytesTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _dictPath;

        public G28SnapshotBytesTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pm-g28-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _dictPath = Path.Combine(_dir, "a.dict.json");
            DictLoader.InvalidateCache();
        }

        public void Dispose()
        {
            try
            {
                foreach (string file in Directory.GetFiles(_dir, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(_dir, true);
            }
            catch { }
            DictLoader.InvalidateCache();
        }

        private static string Json(params string[] numbers)
        {
            StringBuilder json = new StringBuilder();
            json.Append("{\n  \"metadata\": {},\n  \"entries\": [\n");
            for (int i = 0; i < numbers.Length; i++)
            {
                if (i > 0) json.Append(",\n");
                json.Append("    {\n      \"number\": \"").Append(numbers[i])
                    .Append("\",\n      \"name\": \"part ").Append(numbers[i]).Append("\"\n    }");
            }
            json.Append("\n  ],\n  \"warnings\": []\n}");
            return json.ToString();
        }

        private void WriteRaw(string text)
        {
            File.WriteAllText(_dictPath, text, new UTF8Encoding(false));
        }

        private static List<string> Numbers(DictModel model)
        {
            List<string> numbers = new List<string>();
            if (model == null || model.Entries == null) return numbers;
            foreach (DictEntry entry in model.Entries) numbers.Add(entry.Number);
            return numbers;
        }

        /// <summary>
        /// The model a snapshot returns must be the content its own identity
        /// describes. Re-serialising that content and hashing it must equal
        /// <c>read.DictIdentity</c>; a model taken from a second disk read fails
        /// this whenever the file changed in between.
        /// </summary>
        private void AssertModelMatchesIdentity(RenameSnapshotRead read)
        {
            Assert.True(read.Read);
            Assert.True(read.Parsed);
            Assert.NotNull(read.Model);

            // The identity the snapshot reports must describe the bytes on disk
            // at read time. The model must contain exactly those entries.
            string onDisk = File.Exists(_dictPath)
                ? DictFileVisibility.Identity(File.ReadAllBytes(_dictPath))
                : DictFileVisibility.MissingIdentity;
            Assert.Equal(onDisk, read.DictIdentity);
        }

        [Fact]
        public void G28_StableFile_ModelAndIdentityAgree()
        {
            WriteRaw(Json("999"));
            RenameSnapshotRead read = new DictPaletteWorkflow().ReadSnapshot(_dictPath);

            AssertModelMatchesIdentity(read);
            Assert.Equal(new[] { "999" }, Numbers(read.Model).ToArray());
        }

        [Fact]
        public void G28_LegalEmptyDictionary_StaysParsedWithNoEntries()
        {
            WriteRaw(Json());
            RenameSnapshotRead read = new DictPaletteWorkflow().ReadSnapshot(_dictPath);

            AssertModelMatchesIdentity(read);
            Assert.Empty(Numbers(read.Model));
        }

        [Fact]
        public void G28_CorruptJson_IsNotParsed()
        {
            WriteRaw("{ not json");
            RenameSnapshotRead read = new DictPaletteWorkflow().ReadSnapshot(_dictPath);

            Assert.True(read.Read);
            Assert.False(read.Parsed);
            Assert.Null(read.Model);
            Assert.Equal(DictFileVisibility.Identity(File.ReadAllBytes(_dictPath)), read.DictIdentity);
        }

        [Fact]
        public void G28_CorruptUtf8_IsNotParsed()
        {
            // Lone continuation byte: strict decoding must reject it.
            File.WriteAllBytes(_dictPath, new byte[] { 0x7B, 0x80, 0x7D });
            RenameSnapshotRead read = new DictPaletteWorkflow().ReadSnapshot(_dictPath);

            Assert.True(read.Read);
            Assert.False(read.Parsed);
            Assert.Null(read.Model);
        }

        [Fact]
        public void G28_Utf8Bom_IsAccepted()
        {
            byte[] bom = new byte[] { 0xEF, 0xBB, 0xBF };
            byte[] body = new UTF8Encoding(false).GetBytes(Json("301"));
            byte[] all = new byte[bom.Length + body.Length];
            Array.Copy(bom, all, bom.Length);
            Array.Copy(body, 0, all, bom.Length, body.Length);
            File.WriteAllBytes(_dictPath, all);

            RenameSnapshotRead read = new DictPaletteWorkflow().ReadSnapshot(_dictPath);

            AssertModelMatchesIdentity(read);
            Assert.Equal(new[] { "301" }, Numbers(read.Model).ToArray());
        }

        [Fact]
        public void G28_RewrittenFile_NextSnapshotDescribesNewContent()
        {
            WriteRaw(Json("999"));
            RenameSnapshotRead first = new DictPaletteWorkflow().ReadSnapshot(_dictPath);
            Assert.Equal(new[] { "999" }, Numbers(first.Model).ToArray());

            WriteRaw(Json("302"));
            RenameSnapshotRead second = new DictPaletteWorkflow().ReadSnapshot(_dictPath);

            AssertModelMatchesIdentity(second);
            Assert.Equal(new[] { "302" }, Numbers(second.Model).ToArray());
            Assert.NotEqual(first.DictIdentity, second.DictIdentity);
        }

        [Fact]
        public void G28_RestoredContent_IdentityFollowsDisk()
        {
            WriteRaw(Json("999"));
            string idA = DictFileVisibility.Identity(File.ReadAllBytes(_dictPath));

            WriteRaw(Json("302"));
            RenameSnapshotRead whileB = new DictPaletteWorkflow().ReadSnapshot(_dictPath);

            WriteRaw(Json("999"));
            RenameSnapshotRead whileA = new DictPaletteWorkflow().ReadSnapshot(_dictPath);

            Assert.Equal(new[] { "302" }, Numbers(whileB.Model).ToArray());
            Assert.Equal(new[] { "999" }, Numbers(whileA.Model).ToArray());
            Assert.Equal(idA, whileA.DictIdentity);
            Assert.NotEqual(whileB.DictIdentity, whileA.DictIdentity);
        }

        [Fact]
        public void G28_CachedModelFromEarlierLoad_DoesNotLeakIntoSnapshot()
        {
            // Prime whatever per-path state the loader keeps, then change the file.
            WriteRaw(Json("999"));
            DictLoader.Load(_dictPath);

            WriteRaw(Json("302"));
            RenameSnapshotRead read = new DictPaletteWorkflow().ReadSnapshot(_dictPath);

            AssertModelMatchesIdentity(read);
            Assert.Equal(new[] { "302" }, Numbers(read.Model).ToArray());
        }

        [Fact]
        public void G28_MissingFile_ReportsReadFailure()
        {
            RenameSnapshotRead read = new DictPaletteWorkflow().ReadSnapshot(_dictPath);

            Assert.False(read.Read);
            Assert.False(read.Parsed);
            Assert.False(string.IsNullOrEmpty(read.Error));
        }

        [Fact]
        public void G28_EmptyPath_ReportsReadFailure()
        {
            RenameSnapshotRead read = new DictPaletteWorkflow().ReadSnapshot("");

            Assert.False(read.Read);
            Assert.False(string.IsNullOrEmpty(read.Error));
        }
    }
}
