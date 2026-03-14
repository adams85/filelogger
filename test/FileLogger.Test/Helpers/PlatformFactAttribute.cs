using System;
using System.Runtime.InteropServices;
using Xunit;

namespace Karambolo.Extensions.Logging.File.Test.Helpers;

public enum OSPlatformEnum
{
    Unknown,
    Windows,
    Linux,
    OSX,
#if NETCOREAPP3_0_OR_GREATER
    FreeBSD,
#endif
}

public sealed class PlatformFactAttribute : FactAttribute
{
    private static readonly OSPlatformEnum s_currentPlatform =
#if NETFRAMEWORK && !NET471_OR_GREATER
            OSPlatformEnum.Windows;
#else
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OSPlatformEnum.Windows
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? OSPlatformEnum.Linux
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? OSPlatformEnum.OSX
#if NETCOREAPP3_0_OR_GREATER
            : RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD) ? OSPlatformEnum.FreeBSD
#endif
            : OSPlatformEnum.Unknown;
#endif

    internal static string? ShouldSkipPlatform(OSPlatformEnum[]? value)
    {
        return value is not null && Array.IndexOf(value, s_currentPlatform) >= 0
            ? null
            : $"Skipped on {s_currentPlatform}";
    }

    public OSPlatformEnum[]? AssertOn
    {
        get;
        set
        {
            Skip = ShouldSkipPlatform(value);
            field = value;
        }
    }
}

public sealed class PlatformTheoryAttribute : TheoryAttribute
{
    public OSPlatformEnum[]? AssertOn
    {
        get;
        set
        {
            Skip = PlatformFactAttribute.ShouldSkipPlatform(value);
            field = value;
        }
    }
}
