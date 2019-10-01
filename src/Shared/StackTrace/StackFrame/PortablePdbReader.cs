// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Microsoft.Extensions.StackTrace.Sources
{
    internal class PortablePdbReader : IDisposable
    {
        private readonly Dictionary<string, MetadataReaderProvider> _cache =
            new Dictionary<string, MetadataReaderProvider>(StringComparer.Ordinal);

        public void PopulateStackFrame(StackFrameInfo frameInfo, MethodBase method, int IlOffset)
        {
            if (method.Module.Assembly.IsDynamic)
            {
                return;
            }

            var metadataReader = GetMetadataReader(method.Module.Assembly.Location);

            if (metadataReader == null)
            {
                return;
            }

            var methodToken = MetadataTokens.Handle(method.MetadataToken);

            Debug.Assert(methodToken.Kind == HandleKind.MethodDefinition);

            var handle = ((MethodDefinitionHandle)methodToken).ToDebugInformationHandle();

            if (!handle.IsNil)
            {
                var methodDebugInfo = metadataReader.GetMethodDebugInformation(handle);
                var sequencePoints = methodDebugInfo.GetSequencePoints();
                SequencePoint? bestPointSoFar = null;

                foreach (var point in sequencePoints)
                {
                    if (point.Offset > IlOffset)
                    {
                        break;
                    }

                    if (point.StartLine != SequencePoint.HiddenLine)
                    {
                        bestPointSoFar = point;
                    }
                }

                if (bestPointSoFar.HasValue)
                {
                    frameInfo.LineNumber = bestPointSoFar.Value.StartLine;
                    frameInfo.FilePath = metadataReader.GetString(metadataReader.GetDocument(bestPointSoFar.Value.Document).Name);
                }
            }
        }

        private MetadataReader GetMetadataReader(string assemblyPath)
        {
            if (string.IsNullOrEmpty(assemblyPath))
            {
                return null;
            }

            if (_cache.TryGetValue(assemblyPath, out var provider))
            {
                return provider.GetMetadataReader();
            }

            if (TryGetPdbPath(assemblyPath, out var pdbPath) && File.Exists(pdbPath))
            {
                var pdbStream = File.OpenRead(pdbPath);

                if (IsPortable(pdbStream))
                {
                    provider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);

                    _cache[assemblyPath] = provider;

                    return provider.GetMetadataReader();
                }
            }

            return null;
        }

        private static bool TryGetPdbPath(string assemblyPath, out string pdbPath)
        {
            if (File.Exists(assemblyPath))
            {
                var peStream = File.OpenRead(assemblyPath);

                using (var peReader = new PEReader(peStream))
                {
                    try
                    {
                        foreach (var entry in peReader.ReadDebugDirectory())
                        {
                            if (entry.Type == DebugDirectoryEntryType.CodeView)
                            {
                                var codeViewData = peReader.ReadCodeViewDebugDirectoryData(entry);
                                var peDirectory = Path.GetDirectoryName(assemblyPath);
                                pdbPath = Path.Combine(peDirectory, Path.GetFileName(codeViewData.Path));
                                return true;
                            }
                        }
                    }
                    catch
                    {
                        pdbPath = default;
                        return false;
                    }
                }
            }

            pdbPath = default;
            return false;
        }

        private static bool IsPortable(Stream pdbStream)
        {
            var result =
                pdbStream.ReadByte() == 'B' &&
                pdbStream.ReadByte() == 'S' &&
                pdbStream.ReadByte() == 'J' &&
                pdbStream.ReadByte() == 'B';

            pdbStream.Seek(0, SeekOrigin.Begin);

            return result;
        }

        public void Dispose()
        {
            foreach (var entry in _cache)
            {
                entry.Value?.Dispose();
            }

            _cache.Clear();
        }
    }
}
