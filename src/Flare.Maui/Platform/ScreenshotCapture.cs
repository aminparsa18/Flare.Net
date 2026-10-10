using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Media;

namespace Flare.Maui;

/// <summary>Captures the current page as JPEG, lowering the quality until it fits the size limit.</summary>
internal static class ScreenshotCapture
{
    private static readonly int[] Qualities = [70, 50, 30];

    public static async Task<byte[]?> CaptureAsync(int maxBytes, CancellationToken cancellationToken)
    {
        var result = await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            return page is null ? null : await page.CaptureAsync();
        }).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (result is null) return null;

        foreach (var quality in Qualities)
        {
            using var stream = await result.OpenReadAsync(ScreenshotFormat.Jpeg, quality).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (buffer.Length <= maxBytes) return buffer.ToArray();
        }
        return null;
    }
}
