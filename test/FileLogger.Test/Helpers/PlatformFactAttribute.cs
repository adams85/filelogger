using System;
using System.Linq;
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
    public OSPlatformEnum[]? AssertOn
    {
        get;
        set
        {
            var currentPlatform =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OSPlatformEnum.Windows
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? OSPlatformEnum.Linux
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? OSPlatformEnum.OSX
#if NETCOREAPP3_0_OR_GREATER
                : RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD) ? OSPlatformEnum.FreeBSD
#endif
                : OSPlatformEnum.Unknown;

            Skip = value is not null && Array.IndexOf(value, currentPlatform) >= 0
                ? null
                : $"Skipped on {currentPlatform}";
            field = value;
        }
    }
}
