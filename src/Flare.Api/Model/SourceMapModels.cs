namespace Flare.Api.Model;

/// <summary>One stored source map (ADR-0152). <see cref="SizeBytes"/> is the uncompressed size.</summary>
public sealed record SourceMapDto
{
    public required string ServiceName { get; init; }

    public required string Version { get; init; }

    public required string Bundle { get; init; }

    public required long SizeBytes { get; init; }

    public required DateTimeOffset UploadedAt { get; init; }
}

public sealed record SourceMapListResponse
{
    public required IReadOnlyList<SourceMapDto> Maps { get; init; }
}
