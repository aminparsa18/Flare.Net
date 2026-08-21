namespace Flare.Api.HostStats;

/// <summary>
/// Reads total/used disk space for the Resources page's Host overview panel - just
/// <see cref="System.IO.DriveInfo"/> against <c>/</c>, no bind mount involved.
/// </summary>
/// <remarks>
/// <para>
/// An earlier version of this bind-mounted the Docker host's real <c>/</c> into the
/// container at <c>/hostfs:ro</c> specifically to read from, on the assumption that a
/// container's own <c>/</c> is an isolated overlay/union filesystem with no visibility
/// into the host's real disk (unlike CPU/memory/uptime, which come "for free" from
/// Docker's unnamespaced <c>/proc</c> - see <see cref="ProcFileReader"/>'s remarks).
/// That assumption turned out to be backwards in practice, confirmed live against a real
/// <c>docker compose up --build</c>: on the overlay2 storage driver (the modern default
/// almost everywhere - devicemapper's per-container loopback disks, where the old
/// "container sees its own small virtual disk" concern actually held, is legacy/removed
/// from current Docker Engine), there's no separate per-container storage pool, so
/// <c>df -h /</c> from *inside* any container already reports the real host disk's
/// total/used - confirmed reading <c>458G / 40G</c>, matching the actual Docker Desktop
/// VM disk. The bind mount, meanwhile, made things *worse*: on Docker Desktop's LinuxKit
/// VM, <c>/</c> itself is already an overlay
/// (<c>lowerdir=/,upperdir=/run/rootfs.upper</c>), so mounting it a second time at
/// <c>/hostfs</c> lands on that overlay's own tiny writable upper layer -
/// <c>statvfs</c> reported a bogus <c>784M</c> total there, not the real disk.
/// </para>
/// <para>
/// Net effect: no bind mount needed at all, in docker-compose *or* under Aspire's dev
/// loop (<c>AddProject</c> runs <c>Flare.Api</c> as a bare <c>dotnet</c> process there -
/// see <c>Flare.AppHost/AppHost.cs</c> - where <c>/</c> already *is* the real machine).
/// This is still best-effort: a storage driver/quota that genuinely partitions disk
/// per-container (rare today, but possible) would report that partition instead of the
/// full host disk - there's no fully general fix for that without host cooperation.
/// </para>
/// </remarks>
public sealed class HostDiskReader
{
    private readonly string _path;

    public HostDiskReader() : this("/")
    {
    }

    /// <summary>Test-only constructor - lets <c>Flare.Api.Tests</c> point this at a scratch directory instead of depending on <c>/</c> being statable in the test environment.</summary>
    internal HostDiskReader(string path) => _path = path;

    public (long TotalBytes, long UsedBytes) Read()
    {
        var drive = new DriveInfo(_path);
        var total = drive.TotalSize;
        var used = total - drive.AvailableFreeSpace;
        return (total, used);
    }
}
