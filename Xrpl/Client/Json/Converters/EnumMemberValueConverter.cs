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
                if (StringToEnum.TryGetValue(name, out T existingForName)
                    && !EqualityComparer<T>.Default.Equals(existingForName, value))
                {
                    throw new InvalidOperationException(
                        $"Enum '{typeof(T).Name}' maps multiple members to the same serialization string '{name}': '{existingForName}' and '{value}'.");
                }

                EnumToString[value] = name;
                StringToEnum[name] = value;
                string memberName = value.ToString();
                if (!StringToEnum.ContainsKey(memberName))
                    StringToEnum[memberName] = value;
            }
        }

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"{typeof(T).Name} value must be a JSON string; got token type {reader.TokenType}.");
            }

            string str = reader.GetString();
            if (str != null && StringToEnum.TryGetValue(str, out T result))
                return result;
            throw new JsonException($"Unknown {typeof(T).Name} value: '{str}'.");
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(EnumToString.TryGetValue(value, out string str) ? str : value.ToString());
        }
    }
}
