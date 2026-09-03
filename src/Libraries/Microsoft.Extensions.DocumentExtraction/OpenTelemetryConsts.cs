// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.DocumentExtraction;

#pragma warning disable S4041 // Type names should not match namespaces

/// <summary>Provides constants used by various telemetry services.</summary>
internal static class OpenTelemetryConsts
{
    public const string DefaultSourceName = "Experimental.Microsoft.Extensions.DocumentExtraction";

    public const string SecondsUnit = "s";

    /// <summary>Environment variable name for controlling whether sensitive content should be captured in telemetry by default.</summary>
    public const string GenAICaptureMessageContentEnvVar = "OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT";

    public const string TypeText = "text";

    public static class Error
    {
        public const string Type = "error.type";
    }

    public static class GenAI
    {
        public const string GenerateContentName = "generate_content";

        public static class Client
        {
            public static class OperationDuration
            {
                public const string Description = "Measures the duration of a GenAI operation";
                public const string Name = "gen_ai.client.operation.duration";
                public static readonly double[] ExplicitBucketBoundaries = [0.01, 0.02, 0.04, 0.08, 0.16, 0.32, 0.64, 1.28, 2.56, 5.12, 10.24, 20.48, 40.96, 81.92];
            }

        }

        public static class Operation
        {
            public const string Name = "gen_ai.operation.name";
        }

        public static class Output
        {
            public const string Type = "gen_ai.output.type";
        }

        public static class Provider
        {
            public const string Name = "gen_ai.provider.name";
        }

        public static class Request
        {
            public const string Model = "gen_ai.request.model";
        }

    }

    public static class Server
    {
        public const string Address = "server.address";
        public const string Port = "server.port";
    }
}
