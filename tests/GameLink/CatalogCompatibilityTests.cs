using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using NyangTalk.Shared;
using CatalogFixture = UnityEngine.AddressableAssets.ResourceLocators.ContentCatalogData;

namespace NyangTalk.Tests
{
    public static class CatalogCompatibilityTests
    {
        private const int RuntimeMagic = -1444555298;
        private static byte[] Header(int magic = CatalogCompatibility.LegacyMagic, int version = 2, int length = 32)
        {
            byte[] bytes = new byte[Math.Max(8, length)];
            for (int i = 0; i < 4; i++) { bytes[i] = (byte)(magic >> (8 * i)); bytes[4 + i] = (byte)(version >> (8 * i)); }
            if (length != bytes.Length) Array.Resize(ref bytes, length);
            return bytes;
        }
        private static CatalogCompatibilityResult Apply(byte[] bytes, int expected = RuntimeMagic)
        {
            using (var stream = new MemoryStream(bytes, false)) return CatalogCompatibility.TryApply(typeof(CatalogFixture), stream, expected);
        }

        [Test("catalog,compatibility")]
        public static void KnownRuntimeMismatchChangesOnlyTheCatalogMagic()
        {
            CatalogFixture.SetMagic(RuntimeMagic);
            string input = "ContentCatalogData";
            int hashBefore = input.GetHashCode();
            var bytes = Header();
            var before = Convert.ToBase64String(bytes);
            using (var stream = new MemoryStream(bytes, false))
            {
                stream.Position = 11;
                var result = CatalogCompatibility.TryApply(typeof(CatalogFixture), stream, RuntimeMagic);
                A.Eq(CatalogCompatibilityState.Applied, result.State, "known replacement-runtime mismatch is repaired");
                A.True(result.Applied && result.Compatible && result.Error == null, "success is explicit");
                A.Eq(CatalogCompatibility.LegacyMagic, CatalogFixture.Magic, "only catalog's private magic is corrected");
                A.Eq(11L, stream.Position, "caller stream position preserved");
            }
            A.Eq(before, Convert.ToBase64String(bytes), "catalog input bytes unchanged");
            A.Eq(hashBefore, input.GetHashCode(), "global string hashing unchanged");
        }

        [Test("catalog,compatibility")]
        public static void AlreadyCompatibleCatalogAndUnexpectedPatchesRemainUntouched()
        {
            CatalogFixture.SetMagic(CatalogCompatibility.LegacyMagic);
            var already = Apply(Header());
            A.Eq(CatalogCompatibilityState.AlreadyCompatible, already.State, "original runtime is a no-op");
            A.True(already.Compatible && !already.Applied, "compatible does not imply a write");
            CatalogFixture.SetMagic(1234);
            var patched = Apply(Header());
            A.Eq(CatalogCompatibilityState.Refused, patched.State, "unexpected third-party or changed-runtime magic is not overwritten");
            A.Eq("catalog-runtime-magic-unrecognized", patched.Reason, "refusal identifies guard");
            A.Eq(1234, CatalogFixture.Magic, "unrecognized value untouched");
        }

        [Test("catalog,compatibility")]
        public static void UnknownHeadersVersionsAndShortInputsFailClosed()
        {
            foreach (var bytes in new[] { Header(7), Header(CatalogCompatibility.LegacyMagic, 3), Header(length: 31), Header(length: 0), Header(length: 7) })
            {
                CatalogFixture.SetMagic(RuntimeMagic);
                var result = Apply(bytes);
                A.Eq(CatalogCompatibilityState.Refused, result.State, "unknown catalog format is refused");
                A.False(result.Compatible, "not presented as compatible");
                A.Eq(RuntimeMagic, CatalogFixture.Magic, "refused format never changes runtime field");
            }
        }

        private static Type FieldFixture(string typeName, Type magicType, FieldAttributes magicFlags, int version = 2)
        {
            var name = new AssemblyName("CatalogFixture" + Guid.NewGuid().ToString("N"));
            var assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
            var type = assembly.DefineDynamicModule(name.Name).DefineType(typeName, TypeAttributes.Public);
            var field = type.DefineField("kMagic", magicType, magicFlags);
            if ((magicFlags & FieldAttributes.Literal) != 0) field.SetConstant(RuntimeMagic);
            type.DefineField("kVersion", typeof(int), FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.Literal).SetConstant(version);
            return type.CreateType();
        }

        [Test("catalog,compatibility")]
        public static void OnlyTheExactKnownMutableCatalogFieldIsEligible()
        {
            string typeName = typeof(CatalogFixture).FullName;
            var flags = FieldAttributes.Private | FieldAttributes.Static;
            var cases = new[]
            {
                typeof(string), FieldFixture("Other.ContentCatalogData", typeof(int), flags),
                FieldFixture(typeName, typeof(long), flags), FieldFixture(typeName, typeof(int), flags | FieldAttributes.InitOnly),
                FieldFixture(typeName, typeof(int), flags | FieldAttributes.Literal),
                FieldFixture(typeName, typeof(int), FieldAttributes.Public | FieldAttributes.Static),
                FieldFixture(typeName, typeof(int), flags, 3)
            };
            foreach (var fixture in cases)
                using (var stream = new MemoryStream(Header(), false))
                {
                    var result = CatalogCompatibility.TryApply(fixture, stream, RuntimeMagic);
                    A.Eq(CatalogCompatibilityState.Refused, result.State, "changed type or field layout is refused: " + fixture);
                    A.False(result.Applied, "no write to unfamiliar layout");
                }
        }

        private sealed class ReadLimitStream : MemoryStream
        {
            public int BytesRead;
            public ReadLimitStream(byte[] bytes) : base(bytes, false) { }
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (BytesRead + count > CatalogCompatibility.HeaderBytes) throw new InvalidOperationException("reader exceeded header boundary");
                int read = base.Read(buffer, offset, Math.Min(3, count)); BytesRead += read; return read;
            }
        }

        [Test("catalog,compatibility")]
        public static void InspectionReadsOnlyTheHeaderAndHandlesShortReads()
        {
            var bytes = Header(length: 2 * 1024 * 1024);
            CatalogFixture.SetMagic(RuntimeMagic);
            using (var stream = new ReadLimitStream(bytes))
            {
                var result = CatalogCompatibility.TryApply(typeof(CatalogFixture), stream, RuntimeMagic);
                A.True(result.Applied, "partial stream reads still produce complete bounded header");
                A.Eq(CatalogCompatibility.HeaderBytes, stream.BytesRead, "reads exactly 32 bytes of large catalog");
            }
        }

        [Test("catalog,compatibility")]
        public static void ProductionPathNeverChangesFilesAndSurfacesReadErrors()
        {
            var dir = Ctx.NewDir("catalog-compatibility");
            var path = Path.Combine(dir, "catalog.bin");
            var bytes = Header(); File.WriteAllBytes(path, bytes);
            CatalogFixture.SetMagic("ContentCatalogData".GetHashCode());
            var result = CatalogCompatibility.TryApplyToAssembly(typeof(CatalogFixture).Assembly, path);
            A.True(result.Compatible, "production computes expected magic from current runtime");
            A.Eq(Convert.ToBase64String(bytes), Convert.ToBase64String(File.ReadAllBytes(path)), "on-disk bytes unchanged");
            var missing = CatalogCompatibility.TryApply(typeof(CatalogFixture), Path.Combine(dir, "missing.bin"));
            A.Eq(CatalogCompatibilityState.Failed, missing.State, "missing catalog is an explicit read failure");
            A.True(missing.Error is FileNotFoundException, "original read exception is retained for caller logging");
            A.True(missing.Reason.Contains("FileNotFoundException"), "failure reason is reviewable");
        }
    }
}

namespace UnityEngine.AddressableAssets.ResourceLocators
{
    // Shape fixture only: no game or Addressables assembly is loaded by these tests.
    public sealed class ContentCatalogData
    {
        private static int kMagic = NyangTalk.Shared.CatalogCompatibility.LegacyMagic;
        private const int kVersion = 2;
        public static int Magic { get { return kMagic; } }
        public static void SetMagic(int magic) { kMagic = magic; }
    }
}
