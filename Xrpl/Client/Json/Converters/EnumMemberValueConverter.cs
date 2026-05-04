using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xrpl.Client.Json.Converters
{
    /// <summary>
    /// Serializes/deserializes enums using <see cref="EnumMemberAttribute.Value"/>
    /// when present, falling back to the enum member name (case-insensitive read).
    /// Replaces <c>JsonStringEnumConverter</c> for enums decorated with
    /// <c>[EnumMember(Value = "...")]</c>.
    /// </summary>
    public class EnumMemberValueConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        private static readonly Dictionary<T, string> EnumToString;
        private static readonly Dictionary<string, T> StringToEnum;

        static EnumMemberValueConverter()
        {
            T[] values = Enum.GetValues<T>();
            EnumToString = new Dictionary<T, string>(values.Length);
            StringToEnum = new Dictionary<string, T>(values.Length, StringComparer.OrdinalIgnoreCase);

            foreach (T value in values)
            {
                FieldInfo field = typeof(T).GetField(value.ToString())!;
                EnumMemberAttribute attr = field.GetCustomAttribute<EnumMemberAttribute>();
                string name = attr?.Value ?? value.ToString();
                EnumToString[value] = name;
                StringToEnum[name] = value;
                string memberName = value.ToString();
                if (!StringToEnum.ContainsKey(memberName))
                    StringToEnum[memberName] = value;
            }
        }

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string str = reader.GetString();
            if (str != null && StringToEnum.TryGetValue(str, out T result))
                return result;
            if (str != null && Enum.TryParse(str, ignoreCase: true, out T parsed))
                return parsed;
            throw new JsonException($"Unknown {typeof(T).Name} value: '{str}'.");
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(EnumToString.TryGetValue(value, out string str) ? str : value.ToString());
        }
    }
}
