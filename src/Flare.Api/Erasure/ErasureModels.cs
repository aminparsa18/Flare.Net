using MemoryPack;

namespace Flare.Api.Erasure;

/// <summary>Lifecycle of one erasure request; stored in <c>erasure_operations.Status</c>. Whether the queued mutations have finished is read live, see <see cref="ErasureStatus"/>.</summary>
public static class ErasureOperationStatus
{
    public const string Pending = "pending";
    public const string Submitted = "submitted";
    public const string Failed = "failed";
}

/// <summary>The state <c>GET /api/erasure/{id}</c> reports, derived from the stored status plus <c>system.mutations</c>.</summary>
public static class ErasureStatus
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Success = "success";
    public const string Failed = "failed";
}

/// <summary>One personal-data identifier: an attribute key (<c>user.id</c>, <c>user.email</c>, <c>client.address</c>, ...) and the value that identifies the data subject.</summary>
[MemoryPackable]
public sealed partial record ErasureIdentifier
{
    public required string Key { get; init; }

    public required string Value { get; init; }
}

/// <summary>Body of <c>POST /api/erasure/preview</c>, <c>/export</c> and <c>/api/erasure</c>. A row belongs to the subject when any identifier matches.</summary>
[MemoryPackable]
public sealed partial record ErasureRequest
{
    public IReadOnlyList<ErasureIdentifier>? Identifiers { get; init; }

    /// <summary>Only <c>POST /api/erasure</c>: must equal the first identifier's value, so a script or a stray click can't erase by accident.</summary>
    public string? Confirm { get; init; }

    public string? Validate(bool requireConfirm)
    {
        if (Identifiers is not { Count: > 0 })
        {
            return "At least one identifier is required.";
        }

        if (Identifiers.Count > ErasureSql.MaxIdentifiers)
        {
            return $"At most {ErasureSql.MaxIdentifiers} identifiers are allowed.";
        }

        foreach (var identifier in Identifiers)
        {
            if (ErasureSql.ValidateIdentifier(identifier.Key, identifier.Value) is { } error)
            {
                return error;
            }
        }

        if (requireConfirm && !string.Equals(Confirm, Identifiers[0].Value, StringComparison.Ordinal))
        {
            return "confirm must equal the first identifier's value.";
        }

        return null;
    }
}

/// <summary>Rows matching the subject in one table.</summary>
[MemoryPackable]
public sealed partial record ErasureTableCount
{
    public required string Table { get; init; }

    public required long Rows { get; init; }
}

/// <summary>Dry-run result: what an erasure would delete.</summary>
[MemoryPackable]
public sealed partial record ErasurePreviewResponse
{
    public required IReadOnlyList<ErasureTableCount> Tables { get; init; }

    public required long TotalRows { get; init; }

    /// <summary>Sessions found through the identifiers; screenshots and feedback of those sessions are included in the counts.</summary>
    public required int SessionCount { get; init; }

    /// <summary>True when more sessions matched than are followed (<see cref="ErasureSql.MaxSessions"/>); the rest of their screenshots and feedback are not covered.</summary>
    public bool SessionsTruncated { get; init; }
}

[MemoryPackable]
public sealed partial record StartErasureResponse
{
    public required Guid TransactionId { get; init; }
}

/// <summary>Mutation progress for one table.</summary>
[MemoryPackable]
public sealed partial record ErasureTableProgress
{
    public required string Table { get; init; }

    /// <summary>Rows that matched when the request was made.</summary>
    public required long Rows { get; init; }

    /// <summary><c>pending</c>, <c>running</c> or <c>success</c> (the mutation finished), or <c>failed</c>.</summary>
    public required string Status { get; init; }

    /// <summary>Data parts the mutation still has to rewrite (summed over replicas in cluster mode).</summary>
    public long PartsToDo { get; init; }

    public string? Error { get; init; }
}

[MemoryPackable]
public sealed partial record ErasureOperation
{
    public required Guid TransactionId { get; init; }

    public required string Status { get; init; }

    public string? Error { get; init; }

    public required IReadOnlyList<string> IdentifierKeys { get; init; }

    /// <summary>Short SHA-256 prefix of the identifier values, to correlate repeated requests without storing them.</summary>
    public required string SubjectHash { get; init; }

    public required string RequestedBy { get; init; }

    public required DateTimeOffset RequestedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required IReadOnlyList<ErasureTableProgress> Tables { get; init; }
}

[MemoryPackable]
public sealed partial record ErasureListResponse
{
    public required IReadOnlyList<ErasureOperation> Operations { get; init; }
}
