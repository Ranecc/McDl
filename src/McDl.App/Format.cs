using System;
using System.Globalization;

namespace McDl.App
{
    /// <summary>界面上的文案格式化：字节、日期、版本类型。集中一处，避免每页各写一套。</summary>
    internal static class Format
    {
        public static string Bytes(long value)
        {
            if (value < 0) return "0 B";
            if (value < 1024) return value + " B";

            var kb = value / 1024.0;
            if (kb < 1024) return kb.ToString("0.0", CultureInfo.InvariantCulture) + " KB";

            var mb = kb / 1024.0;
            if (mb < 1024) return mb.ToString("0.0", CultureInfo.InvariantCulture) + " MB";

            var gb = mb / 1024.0;
            return gb.ToString("0.00", CultureInfo.InvariantCulture) + " GB";
        }

        public static string Date(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return string.Empty;

            DateTime parsed;
            if (DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
            {
                return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            return iso.Length >= 10 ? iso.Substring(0, 10) : iso;
        }

        public static string VersionType(string type)
        {
            switch ((type ?? string.Empty).ToLowerInvariant())
            {
                case "release": return "正式版";
                case "snapshot": return "快照";
                case "old_beta": return "远古 Beta";
                case "old_alpha": return "远古 Alpha";
                default: return string.IsNullOrEmpty(type) ? "未知" : type;
            }
        }

        public static string MirrorName(string name)
        {
            switch ((name ?? string.Empty).ToLowerInvariant())
            {
                case "official": return "官方";
                case "bmclapi": return "BMCLAPI";
                default: return name;
            }
        }
    }
}
