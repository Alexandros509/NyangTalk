using System.IO;

namespace NyangTalk.Shared
{
    /// <summary>Optional face-only companion image; old connectors without it remain readable.</summary>
    public static class PortraitFiles
    {
        public const string FaceSuffix = "-face";
        public static string FaceVariant(string portrait)
        {
            return Path.Combine(Path.GetDirectoryName(portrait) ?? "", Path.GetFileNameWithoutExtension(portrait) + FaceSuffix + Path.GetExtension(portrait));
        }
        public static bool IsFaceKey(string key) { return key.EndsWith(FaceSuffix, System.StringComparison.Ordinal); }
        public static string FullKey(string key) { return IsFaceKey(key) ? key.Substring(0, key.Length - FaceSuffix.Length) : key; }
        public static string RequestPair(PictureQueue queue, string key)
        {
            var full = queue.Request(key);
            queue.Request(key + FaceSuffix);
            return full;
        }
    }
}
