using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security;

namespace NyangTalk.Shared
{
    public enum CatalogCompatibilityState { Applied, AlreadyCompatible, Refused, Failed }

    public sealed class CatalogCompatibilityResult
    {
        public CatalogCompatibilityState State { get; internal set; }
        public string Reason { get; internal set; }
        public Exception Error { get; internal set; }
        public bool Applied { get { return State == CatalogCompatibilityState.Applied; } }
        public bool Compatible { get { return Applied || State == CatalogCompatibilityState.AlreadyCompatible; } }
    }

    /// <summary>
    /// The game's version-2 catalog contains its original runtime's string-hash magic. A different Mono profile must
    /// accept that exact known format without changing catalog bytes or the runtime's global string-hash behavior.
    /// </summary>
    public static class CatalogCompatibility
    {
        public const int LegacyMagic = 233015618;
        public const int SupportedVersion = 2;
        public const int HeaderBytes = 32;
        private const string CatalogTypeName = "UnityEngine.AddressableAssets.ResourceLocators.ContentCatalogData";
        private static readonly object Gate = new object();

        public static CatalogCompatibilityResult TryApplyToAssembly(Assembly catalogAssembly, string catalogPath)
        {
            if (catalogAssembly == null) return Refuse("catalog-assembly-missing");
            return TryApply(catalogAssembly.GetType(CatalogTypeName, false), catalogPath);
        }

        public static CatalogCompatibilityResult TryApply(Type catalogType, string catalogPath)
        {
            if (catalogType == null) return Refuse("catalog-type-missing");
            if (string.IsNullOrEmpty(catalogPath)) return Refuse("catalog-path-missing");
            try
            {
                using (var stream = new FileStream(catalogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    return TryApply(catalogType, stream, "ContentCatalogData".GetHashCode());
            }
            catch (Exception ex) { if (!IsExpectedFailure(ex)) throw; return Failure(ex); }
        }

        /// <summary>Bounded header validation; the caller owns the stream and its original position is preserved.</summary>
        public static CatalogCompatibilityResult TryApply(Type catalogType, Stream catalog, int expectedCurrentRuntimeMagic)
        {
            try
            {
                if (catalogType == null || catalogType.FullName != CatalogTypeName) return Refuse("catalog-type-unrecognized");
                var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                var magicField = catalogType.GetField("kMagic", flags);
                if (magicField == null || magicField.FieldType != typeof(int) || !magicField.IsStatic || !magicField.IsPrivate || magicField.IsLiteral || magicField.IsInitOnly)
                    return Refuse("catalog-magic-field-unrecognized");
                var versionField = catalogType.GetField("kVersion", flags);
                if (versionField == null || versionField.FieldType != typeof(int) || !versionField.IsStatic || !versionField.IsLiteral || (int)versionField.GetRawConstantValue() != SupportedVersion)
                    return Refuse("catalog-version-field-unrecognized");
                if (catalog == null || !catalog.CanRead || !catalog.CanSeek) return Refuse("catalog-stream-unreadable");
                if (catalog.Length < HeaderBytes) return Refuse("catalog-header-truncated");
                byte[] header = new byte[HeaderBytes];
                long originalPosition = catalog.Position;
                try
                {
                    catalog.Position = 0;
                    int read = 0;
                    while (read < header.Length)
                    {
                        int count = catalog.Read(header, read, header.Length - read);
                        if (count == 0) return Refuse("catalog-header-truncated");
                        read += count;
                    }
                }
                finally { catalog.Position = originalPosition; }
                if (Int32LittleEndian(header, 0) != LegacyMagic) return Refuse("catalog-header-magic-unrecognized");
                if (Int32LittleEndian(header, 4) != SupportedVersion) return Refuse("catalog-header-version-unrecognized");
                lock (Gate)
                {
                    RuntimeHelpers.RunClassConstructor(catalogType.TypeHandle);
                    int current = (int)magicField.GetValue(null);
                    if (current == LegacyMagic) return Result(CatalogCompatibilityState.AlreadyCompatible, "catalog-magic-already-compatible", null);
                    if (current != expectedCurrentRuntimeMagic) return Refuse("catalog-runtime-magic-unrecognized");
                    magicField.SetValue(null, LegacyMagic);
                    if ((int)magicField.GetValue(null) != LegacyMagic) return Result(CatalogCompatibilityState.Failed, "catalog-magic-write-not-observed", null);
                    return Result(CatalogCompatibilityState.Applied, "catalog-legacy-magic-restored", null);
                }
            }
            catch (Exception ex) { if (!IsExpectedFailure(ex)) throw; return Failure(ex); }
        }

        private static int Int32LittleEndian(byte[] bytes, int offset)
        {
            return bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24;
        }

        private static CatalogCompatibilityResult Refuse(string reason) { return Result(CatalogCompatibilityState.Refused, reason, null); }
        private static CatalogCompatibilityResult Result(CatalogCompatibilityState state, string reason, Exception error)
        {
            return new CatalogCompatibilityResult { State = state, Reason = reason, Error = error };
        }

        private static bool IsExpectedFailure(Exception ex)
        {
            return ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException
                || ex is InvalidOperationException || ex is MemberAccessException || ex is TypeInitializationException
                || ex is TypeLoadException || ex is TargetInvocationException || ex is SecurityException;
        }

        private static CatalogCompatibilityResult Failure(Exception ex)
        {
            return Result(CatalogCompatibilityState.Failed, "catalog-compatibility-failed: " + ex.GetType().Name + ": " + ex.Message, ex);
        }
    }
}
