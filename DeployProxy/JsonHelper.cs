using System;
using Newtonsoft.Json;

namespace DeployProxy
{
    public static class JsonHelper
    {
        public static T FromJson<T>(this string value)
        {
            try
            {
                return JsonConvert.DeserializeObject<T>(value);
            }
            catch (Exception ex)
            {
                return default;
            }
        }
        public static string ToJson(this object value)
        {
            JsonSerializerSettings settings = new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            };

            return JsonConvert.SerializeObject(value, settings);
        }
    }
}