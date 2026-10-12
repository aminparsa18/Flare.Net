namespace Flare.AlertWorker.Reports;

/// <summary>A rendered dashboard: the file bytes and their MIME type.</summary>
public sealed record RenderedReport(byte[] Content, string ContentType, string FileExtension);

/// <summary>Renders a dashboard URL to a PDF or PNG. The seam between the worker and a real browser.</summary>
public interface IDashboardRenderer
{
    /// <param name="pageUrl">The dashboard URL to open (see <see cref="DashboardReportUrl"/>).</param>
    /// <param name="renderToken">The render credential to present as the session cookie, or null when Flare's auth is off.</param>
    /// <param name="format"><c>pdf</c> or <c>png</c>.</param>
    Task<RenderedReport> RenderAsync(string pageUrl, string? renderToken, string format, CancellationToken cancellationToken);
}
