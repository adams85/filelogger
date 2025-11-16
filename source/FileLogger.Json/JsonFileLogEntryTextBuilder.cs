using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Karambolo.Extensions.Logging.File.Json;

// based on: https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.Extensions.Logging.Console/src/JsonConsoleFormatter.cs
public class JsonFileLogEntryTextBuilder : StructuredFileLogEntryTextBuilder
{
    public static readonly JsonFileLogEntryTextBuilder Default = new();

    private readonly JsonWriterOptions _jsonWriterOptions;
    private readonly string _entrySeparator;

    protected JsonFileLogEntryTextBuilder()
        : this(jsonWriterOptions: null, entrySeparator: null) { }

    [Obsolete("This constructor will be removed in a future major version. Please use the other overload which accepts an instance of " + nameof(JsonFileLogFormatOptions) + ".")]
    public JsonFileLogEntryTextBuilder(JsonWriterOptions jsonWriterOptions)
        : this(jsonWriterOptions, entrySeparator: null) { }

    public JsonFileLogEntryTextBuilder(JsonFileLogFormatOptions formatOptions)
        : this((formatOptions ?? throw new ArgumentNullException(nameof(formatOptions))).JsonWriterOptions, formatOptions.EntrySeparator) { }

    private JsonFileLogEntryTextBuilder(JsonWriterOptions? jsonWriterOptions, string? entrySeparator)
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
            _ => throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, message: null)
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

    protected virtual void WriteMessage(Utf8JsonWriter writer, string? message)
    {
        writer.WriteString("Message", message);
    }

    protected virtual void WriteException(Utf8JsonWriter writer, Exception exception)
    {
        writer.WriteString("Exception", exception.ToString());
    }

    protected virtual void WriteNonPrimitiveValue(Utf8JsonWriter writer, object obj)
    {
        string? stringValue = obj switch
        {
            byte[] byteArray => Convert.ToBase64String(byteArray),
            _ => Convert.ToString(obj, CultureInfo.InvariantCulture),
        };

        writer.WriteStringValue(stringValue);
    }

    protected virtual void WriteValue(Utf8JsonWriter writer, object? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        switch (Type.GetTypeCode(value.GetType()))
        {
            case TypeCode.String:
                writer.WriteStringValue((string)value!);
                break;
            case TypeCode.Boolean:
                writer.WriteBooleanValue((bool)value!);
                break;
            case TypeCode.Byte:
                writer.WriteNumberValue((byte)value!);
                break;
            case TypeCode.SByte:
                writer.WriteNumberValue((sbyte)value!);
                break;
            case TypeCode.Char:
                char charValue = (char)value!;
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
                writer.WriteStringValue(MemoryMarshal.CreateSpan(ref charValue, 1));
#else
                writer.WriteStringValue(charValue.ToString());
#endif
                break;
            case TypeCode.Decimal:
                writer.WriteNumberValue((decimal)value!);
                break;
            case TypeCode.Double:
                writer.WriteNumberValue((double)value!);
                break;
            case TypeCode.Single:
                writer.WriteNumberValue((float)value!);
                break;
            case TypeCode.Int32:
                writer.WriteNumberValue((int)value!);
                break;
            case TypeCode.UInt32:
                writer.WriteNumberValue((uint)value!);
                break;
            case TypeCode.Int64:
                writer.WriteNumberValue((long)value!);
                break;
            case TypeCode.UInt64:
                writer.WriteNumberValue((ulong)value!);
                break;
            case TypeCode.Int16:
                writer.WriteNumberValue((short)value!);
                break;
            case TypeCode.UInt16:
                writer.WriteNumberValue((ushort)value!);
                break;
            default:
                WriteNonPrimitiveValue(writer, value!);
                break;
        }
    }

    protected virtual void WriteState<TState>(Utf8JsonWriter writer, [DisallowNull] TState state, string? message)
    {
        writer.WriteStartObject("State");

        string? stateMessage = state.ToString();

        if (!string.Equals(message, stateMessage))
        {
            writer.WriteString("Message", stateMessage);
        }

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
            {
                WriteValue(writer, scope);
            }
        }, writer);

        writer.WriteEndArray();
    }

    protected virtual void WriteEntryObject<TState>(Utf8JsonWriter writer, string categoryName, LogLevel logLevel, EventId eventId, string? message, TState state, Exception? exception,
        IExternalScopeProvider? scopeProvider, DateTimeOffset timestamp)
    {
        writer.WriteStartObject();

        WriteTimestamp(writer, timestamp);
        WriteEventId(writer, eventId);
        WriteLogLevel(writer, logLevel);
        WriteCategoryName(writer, categoryName);

        WriteMessage(writer, message);

        if (exception is not null)
            WriteException(writer, exception);

        if (state is not null)
            WriteState(writer, state, message);

        if (scopeProvider is not null)
            WriteLogScopeInfo(writer, scopeProvider);

        writer.WriteEndObject();
    }

    protected virtual void AppendEntrySeparator(StringBuilder sb)
    {
        sb.Append(_entrySeparator).Append(Environment.NewLine);
    }

    public override void BuildEntryText<TState>(StringBuilder sb, string categoryName, LogLevel logLevel, EventId eventId, string? message, TState state, Exception? exception,
        IExternalScopeProvider? scopeProvider, DateTimeOffset timestamp)
    {
        using (var output = new PooledByteBufferWriter(initialCapacity: 1024))
        {
            using (var writer = new Utf8JsonWriter(output, _jsonWriterOptions))
            {
                WriteEntryObject(writer, categoryName, logLevel, eventId, message, state, exception, scopeProvider, timestamp);

                writer.Flush();
            }

#if NET9_0_OR_GREATER
            ReadOnlySpan<byte> messageBytes = output.WrittenSpan;
            char[] logMessageBuffer = ArrayPool<char>.Shared.Rent(Encoding.UTF8.GetMaxCharCount(messageBytes.Length));
            try
            {
                int charsWritten = Encoding.UTF8.GetChars(messageBytes, logMessageBuffer);
                sb.Append(logMessageBuffer.AsSpan(0, charsWritten));
            }
            finally
            {
                ArrayPool<char>.Shared.Return(logMessageBuffer);
            }
#elif NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            sb.Append(Encoding.UTF8.GetString(output.WrittenMemory.AsSpan()));
#else
            sb.Append(Encoding.UTF8.GetString(output.WrittenMemory.Array, output.WrittenMemory.Offset, output.WrittenMemory.Count));
#endif
        }

        AppendEntrySeparator(sb);
    }
}
