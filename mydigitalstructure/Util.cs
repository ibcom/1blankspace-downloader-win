using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using ServiceStack.Text;

namespace mydigitalspace
{
    public class Util
    {
        public static string SerializeObject<T>(T objectToSerialize)
        {
            string json = JsonSerializer.SerializeToString<T>(objectToSerialize);

            return json;
        }

        public static object DeSerializeObject<T>(string data)
        {
            T fromjson = (T)JsonSerializer.DeserializeFromString<T>(data);

            return fromjson;
        }

        public static string DictionaryToParameters(Dictionary<string, object> dict)
        {
            StringBuilder sb = new StringBuilder();
            string param_string = string.Empty;

            foreach (KeyValuePair<string, object> kv in dict)
            {
                sb.Append(string.Format("&{0}={1}", kv.Key, kv.Value.ToString()));
            }
            param_string = sb.ToString();

            if (sb.ToString().StartsWith("&"))
            {
                param_string = param_string.Remove(0, 1);
            }

            return param_string;
        }

        public static float GetEpochTime()
        {
            return (DateTime.Now.ToUniversalTime().Ticks - 621355968000000000) / 10000000;
        }

        public static string GetSubstringByString(string start, string end, string value)
        {
            return value.Substring((value.IndexOf(start) + start.Length), (value.IndexOf(end) - value.IndexOf(start) - start.Length));
        }
        
    }
}
