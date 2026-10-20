using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace McDl.Core
{
    /// <summary>统一的 JSON 读写，保证所有模型用同一套序列化设置。</summary>
    public static class Json
    {
        private static readonly JsonSerializerSettings ReadSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            // 不能开日期推断：版本号 "1.21" 这类字符串会被 Newtonsoft 当成日期。
            DateParseHandling = DateParseHandling.None
        };

        private static readonly JsonSerializerSettings WriteSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            DateParseHandling = DateParseHandling.None,
            Formatting = Formatting.Indented
        };

        public static T Parse<T>(string text) where T : class
        {
            if (string.IsNullOrEmpty(text)) return null;
            return JsonConvert.DeserializeObject<T>(text, ReadSettings);
        }

        public static T Load<T>(string path) where T : class
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            return Parse<T>(File.ReadAllText(path, Encoding.UTF8));
        }

        public static void Save(string path, object value)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonConvert.SerializeObject(value, WriteSettings), new UTF8Encoding(false));
        }
    }
}
