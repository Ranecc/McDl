using System;
using System.Linq;

namespace McDl.Core
{
    /// <summary>Maven 校验文件的解析，Forge installer 与 Fabric 库都会用到。</summary>
    internal static class Hashes
    {
        /// <summary>
        /// 处理 Maven 上 .sha1 的两种写法："&lt;hash&gt;" 与 "&lt;hash&gt;  &lt;文件名&gt;"。
        /// 认不出来（返回 HTML 错误页、长度不对、含非十六进制字符）时返回 null，调用方按“没校验”处理。
        /// </summary>
        internal static string ParseSha1Listing(string listing)
        {
            if (string.IsNullOrWhiteSpace(listing)) return null;

            var token = listing.Trim()
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            if (token == null || token.Length != 40) return null;
            foreach (var c in token)
            {
                var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex) return null;
            }

            return token.ToLowerInvariant();
        }
    }
}
