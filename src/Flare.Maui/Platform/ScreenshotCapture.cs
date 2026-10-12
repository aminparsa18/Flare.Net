using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Media;

namespace Flare.Maui;

/// <summary>
/// Captures the current page as JPEG, lowering the quality until it fits the size limit. Masked views
/// (<see cref="ScreenshotMasking"/>) are hidden only while <c>CaptureAsync</c> reads the page.
/// </summary>
internal static class ScreenshotCapture
{
    private static readonly int[] Qualities = [70, 50, 30];

    public static async Task<byte[]?> CaptureAsync(int maxBytes, bool maskTextInputs, CancellationToken cancellationToken)
    {
        var result = await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page is null) return null;
            using (ScreenshotMasking.Apply(page, maskTextInputs))
                return await page.CaptureAsync();
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
