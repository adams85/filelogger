using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Karambolo.Extensions.Logging.File.Json
{
    // based on: https://github.com/dotnet/runtime/blob/v6.0.4/src/libraries/Microsoft.Extensions.Logging.Console/src/JsonConsoleFormatter.cs
    public class JsonFileLogEntryTextBuilder : StructuredFileLogEntryTextBuilder
    {
        public static readonly JsonFileLogEntryTextBuilder Default = new JsonFileLogEntryTextBuilder();

        private readonly JsonWriterOptions _jsonWriterOptions;
        private readonly string _entrySeparator;

        protected JsonFileLogEntryTextBuilder()
            : this(jsonWriterOptions: null, entrySeparator: null) { }

        [Obsolete("This constructor will be removed in a future major version. Please use the other overload which accepts an instance of " + nameof(JsonFileLogFormatOptions) + ".")]
        public JsonFileLogEntryTextBuilder(JsonWriterOptions jsonWriterOptions)
            : this(jsonWriterOptions, entrySeparator: null) { }

        public JsonFileLogEntryTextBuilder(JsonFileLogFormatOptions formatOptions)
            : this((formatOptions ?? throw new ArgumentNullException(nameof(formatOptions))).JsonWriterOptions, formatOptions.EntrySeparator) { }

        private JsonFileLogEntryTextBuilder(JsonWriterOptions? jsonWriterOptions, string entrySeparator)
        {
            _jsonWriterOptions = jsonWriterOptions ?? new JsonWriterOptions { Indented = true };
            _entrySeparator = entrySeparator ?? ",";
        }

        protected virtual string GetLogLevelString(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Trace => "Trace",
                LogLevel.Debug => "Debug",
                LogLevel.Information => "Information",
                LogLevel.Warning => "Warning",
                LogLevel.Error => "Error",
                LogLevel.Critical => "Critical",
                _ => throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, null)
            };
        }

        protected virtual void WriteTimestamp(Utf8JsonWriter writer, DateTimeOffset timestamp)
        {
            writer.WriteString("Timestamp", timestamp.ToLocalTime().ToString("o", CultureInfo.InvariantCulture));
        }

        protected virtual void WriteEventId(Utf8JsonWriter writer, EventId eventId)
        {
            writer.WriteNumber("EventId", eventId.Id);
        }

        protected virtual void WriteLogLevel(Utf8JsonWriter writer, LogLevel logLevel)
        {
            writer.WriteString("LogLevel", GetLogLevelString(logLevel));
        }

        protected virtual void WriteCategoryName(Utf8JsonWriter writer, string categoryName)
        {
            writer.WriteString("Category", categoryName);
        }

        protected virtual void WriteMessage(Utf8JsonWriter writer, string message)
        {
            writer.WriteString("Message", message);
        }

        protected virtual void WriteException(Utf8JsonWriter writer, Exception exception)
        {
            writer.WriteString("Exception", exception.ToString());
        }

        protected virtual void WriteNonPrimitiveValue(Utf8JsonWriter writer, object obj)
        {
            string stringValue;

            switch (obj)
            {
                case byte[] byteArray:
                    stringValue = Convert.ToBase64String(byteArray);
                    break;
                case IConvertible convertible:
                    stringValue = convertible.ToString(CultureInfo.InvariantCulture);
                    break;
                default:
                    TypeConverter converter = TypeDescriptor.GetConverter(obj.GetType());
                    stringValue = converter.ConvertToInvariantString(obj);
                    break;
            }

            writer.WriteStringValue(stringValue);
        }

        protected virtual void WriteValue(Utf8JsonWriter writer, object value)
        {
            switch (value)
            {
                case null:
                    writer.WriteNullValue();
                    break;
                case string stringValue:
                    writer.WriteStringValue(stringValue);
                    break;
                case bool boolValue:
                    writer.WriteBooleanValue(boolValue);
                    break;
                case byte byteValue:
                    writer.WriteNumberValue(byteValue);
                    break;
                case sbyte sbyteValue:
                    writer.WriteNumberValue(sbyteValue);
                    break;
                case char charValue:
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
                    writer.WriteStringValue(MemoryMarshal.CreateSpan(ref charValue, 1));
#else
                    writer.WriteStringValue(charValue.ToString());
#endif
                    break;
                case decimal decimalValue:
                    writer.WriteNumberValue(decimalValue);
                    break;
                case double doubleValue:
                    writer.WriteNumberValue(doubleValue);
                    break;
                case float floatValue:
                    writer.WriteNumberValue(floatValue);
                    break;
                case int intValue:
                    writer.WriteNumberValue(intValue);
                    break;
                case uint uintValue:
                    writer.WriteNumberValue(uintValue);
                    break;
                case long longValue:
                    writer.WriteNumberValue(longValue);
                    break;
                case ulong ulongValue:
                    writer.WriteNumberValue(ulongValue);
                    break;
                case short shortValue:
                    writer.WriteNumberValue(shortValue);
                    break;
                case ushort ushortValue:
                    writer.WriteNumberValue(ushortValue);
                    break;
                default:
                    WriteNonPrimitiveValue(writer, value);
                    break;
            }
        }

        protected virtual void WriteState<TState>(Utf8JsonWriter writer, TState state)
        {
            writer.WriteStartObject("State");

            writer.WriteString("Message", state.ToString());
            if (state is IEnumerable<KeyValuePair<string, object>> stateProperties)
            {
                foreach (KeyValuePair<string, object> item in stateProperties)
                {
                    writer.WritePropertyName(item.Key);
                    WriteValue(writer, item.Value);
                }
            }

            writer.WriteEndObject();
        }

        protected virtual void WriteLogScopeInfo(Utf8JsonWriter writer, IExternalScopeProvider scopeProvider)
        {
            writer.WriteStartArray("Scopes");

            scopeProvider.ForEachScope((scope, state) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object>> scopeItems)
                {
                    state.WriteStartObject();

                    state.WriteString("Message", scope.ToString());
                    foreach (KeyValuePair<string, object> item in scopeItems)
                    {
                        writer.WritePropertyName(item.Key);
                        WriteValue(writer, item.Value);
                    }

                    state.WriteEndObject();
                }
                else
                    WriteValue(writer, scope);
            }, writer);

            writer.WriteEndArray();
        }

        protected virtual void WriteEntryObject<TState>(Utf8JsonWriter writer, string categoryName, LogLevel logLevel, EventId eventId, string message, TState state, Exception exception,
            IExternalScopeProvider scopeProvider, DateTimeOffset timestamp)
        {
            writer.WriteStartObject();

            WriteTimestamp(writer, timestamp);
            WriteEventId(writer, eventId);
            WriteLogLevel(writer, logLevel);
            WriteCategoryName(writer, categoryName);

            WriteMessage(writer, message);

            if (exception != null)
                WriteException(writer, exception);

            if (state != null)
                WriteState(writer, state);

            if (scopeProvider != null)
                WriteLogScopeInfo(writer, scopeProvider);

            writer.WriteEndObject();
        }

        protected virtual void AppendEntrySeparator(StringBuilder sb)
        {
            sb.Append(_entrySeparator).Append(Environment.NewLine);
        }

        public override void BuildEntryText<TState>(StringBuilder sb, string categoryName, LogLevel logLevel, EventId eventId, string message, TState state, Exception exception,
            IExternalScopeProvider scopeProvider, DateTimeOffset timestamp)
        {
            using (var output = new PooledByteBufferWriter(initialCapacity: 1024))
            {
                using (var writer = new Utf8JsonWriter(output, _jsonWriterOptions))
                {
                    WriteEntryObject(writer, categoryName, logLevel, eventId, message, state, exception, scopeProvider, timestamp);

                    writer.Flush();
                }

#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
                sb.Append(Encoding.UTF8.GetString(output.WrittenMemory.AsSpan()));
#else
                sb.Append(Encoding.UTF8.GetString(output.WrittenMemory.Array, output.WrittenMemory.Offset, output.WrittenMemory.Count));
#endif
            }

            AppendEntrySeparator(sb);
        }
    }
}
