// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.StackTrace.Sources
{
    internal static class LoggerExtensions
    {
        private static readonly Action<ILogger, string, Exception> _unableToReadDebugInfo;

        static LoggerExtensions()
        {
            _unableToReadDebugInfo = LoggerMessage.Define<string>(
                logLevel: LogLevel.Debug,
                eventId: new EventId(1, "InvalidDebugInfo"),
                formatString: "Failed to read debug information from assembly at '{AssemblyPath}'");
        }

        public static void UnableToReadDebugInfo(this ILogger logger, string assemblyPath, Exception exception)
        {
            _unableToReadDebugInfo(logger, assemblyPath, exception);
        }
    }
}
