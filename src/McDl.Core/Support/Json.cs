using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace McDl.Core
{
    /// <summary>Thin JSON helpers so every model goes through one serializer configuration.</summary>
    public static class Json
    {
        private static readonly JsonSerializerSettings ReadSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
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
