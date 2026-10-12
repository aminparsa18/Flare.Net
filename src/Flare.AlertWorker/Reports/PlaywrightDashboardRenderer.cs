using Flare.Api.Model;
using Flare.Identity.Auth;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace Flare.AlertWorker.Reports;

/// <summary>
/// <see cref="IDashboardRenderer"/> over Playwright's Chromium: launches a local browser, or connects to a
/// Playwright server when <see cref="ReportsOptions.BrowserWsEndpoint"/> is set. One browser per render, so a
/// hung page can never leak into the next report.
/// </summary>
public sealed class PlaywrightDashboardRenderer(IOptions<ReportsOptions> options, IOptions<AuthOptions> authOptions) : IDashboardRenderer
{
    // Reports whether the dashboard SPA has loaded the dashboard and every panel has been asked to render.
    // See the dashboard's /dashboards/[id] route, which sets this attribute in report mode.
    private const string ReadySelector = "html[data-report-ready]";

    // The page's tallest scroll container: the dashboard scrolls inside its own wrapper, so the document
    // height alone would cut the report at one screen.
    private const string MeasureHeightScript = """
        () => {
            let height = document.documentElement.scrollHeight;
            for (const el of document.querySelectorAll('*')) {
                const style = getComputedStyle(el);
                if ((style.overflowY === 'auto' || style.overflowY === 'scroll') && el.scrollHeight > el.clientHeight) {
                    height = Math.max(height, el.scrollHeight + Math.max(0, el.getBoundingClientRect().top));
                }
            }
            return Math.ceil(height);
        }
        """;

    public async Task<RenderedReport> RenderAsync(string pageUrl, string? renderToken, string format, CancellationToken cancellationToken)
    {
        var opts = options.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(opts.RenderTimeout);
        var timeoutMs = (float)opts.RenderTimeout.TotalMilliseconds;

        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await OpenBrowserAsync(playwright, opts);
            await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = opts.ViewportWidth, Height = 900 },
                ColorScheme = ColorScheme.Light,
                DeviceScaleFactor = 1,
            });
            context.SetDefaultTimeout(timeoutMs);

            if (renderToken is not null)
            {
                await context.AddCookiesAsync([new Cookie
                {
                    Name = authOptions.Value.CookieName,
                    Value = renderToken,
                    Url = opts.ApiUrl,
                    HttpOnly = true,
                }]);
            }

            var page = await context.NewPageAsync();
            await page.EmulateMediaAsync(new PageEmulateMediaOptions { Media = Media.Screen });
            await page.GotoAsync(pageUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = timeoutMs });
            await page.WaitForSelectorAsync(ReadySelector, new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = timeoutMs });
            await SettleAsync(page, opts, timeout.Token);

            // Grow the viewport to the whole dashboard so panels that sit below the fold are laid out and
            // drawn, then let them settle again before capturing.
            var height = Math.Clamp(await page.EvaluateAsync<int>(MeasureHeightScript), 400, opts.MaxPageHeight);
            await page.SetViewportSizeAsync(opts.ViewportWidth, height);
            await SettleAsync(page, opts, timeout.Token);

            if (string.Equals(format, DashboardScheduleRequest.PngFormat, StringComparison.Ordinal))
            {
                var png = await page.ScreenshotAsync(new PageScreenshotOptions { Type = ScreenshotType.Png, FullPage = true });
                return new RenderedReport(png, "image/png", "png");
            }

            var pdf = await page.PdfAsync(new PagePdfOptions
            {
                Width = $"{opts.ViewportWidth}px",
                Height = $"{height}px",
                PrintBackground = true,
                PageRanges = "1",
            });
            return new RenderedReport(pdf, "application/pdf", "pdf");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Rendering took longer than {opts.RenderTimeout.TotalSeconds:0} seconds.");
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"Rendering took longer than {opts.RenderTimeout.TotalSeconds:0} seconds: {FirstLine(ex.Message)}");
        }
    }

    private static async Task SettleAsync(IPage page, ReportsOptions opts, CancellationToken cancellationToken)
    {
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(opts.SettleDelay, cancellationToken);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private static async Task<IBrowser> OpenBrowserAsync(IPlaywright playwright, ReportsOptions opts)
    {
        if (opts.BrowserWsEndpoint.Length > 0)
        {
            return await playwright.Chromium.ConnectAsync(opts.BrowserWsEndpoint);
        }

        var executable = opts.ChromiumPath.Length > 0 ? opts.ChromiumPath : null;
        try
        {
            return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                ExecutablePath = executable,
                // A container usually runs as root without the user namespaces Chromium's sandbox needs.
                Args = ["--no-sandbox", "--disable-dev-shm-usage", .. opts.ChromiumArgs],
            });
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "No Chromium to render with. Set Reports:ChromiumPath to one, point Reports:BrowserWsEndpoint at a Playwright server, "
                + "or build the worker image with INSTALL_CHROMIUM=true.");
        }
    }


    private static string FirstLine(string message) => message.Split('\n', 2)[0].Trim();
}
