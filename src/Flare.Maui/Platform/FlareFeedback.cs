using Microsoft.Maui.Controls;

namespace Flare.Maui;

/// <summary>
/// A built-in "report a problem" prompt (ADR-0193): two native dialogs, one for the message and, optionally, one for an
/// e-mail address so a developer can reply. Apps wanting their own form call <see cref="FlareMaui.SendFeedbackAsync"/>.
/// </summary>
public static class FlareFeedback
{
    /// <summary>
    /// Asks the user what went wrong and sends the answer with the current session. Returns true when it was stored, and false
    /// when the user cancelled or the upload failed (the user is told about a failed upload).
    /// </summary>
    public static async Task<bool> PromptAsync(
        Page page,
        string title = "Report a problem",
        string messagePrompt = "What went wrong?",
        bool askForEmail = false,
        string emailPrompt = "Your e-mail, if you want a reply (optional)",
        string sentMessage = "Thanks, your report was sent.",
        string failedMessage = "The report could not be sent. Please try again later.")
    {
        var message = await page.DisplayPromptAsync(title, messagePrompt, "Send", "Cancel", maxLength: 4000, keyboard: Keyboard.Chat);
        if (string.IsNullOrWhiteSpace(message)) return false;

        string? email = null;
        if (askForEmail)
            email = await page.DisplayPromptAsync(title, emailPrompt, "Send", "Skip", keyboard: Keyboard.Email);

        var sent = await FlareMaui.SendFeedbackAsync(message, email);
        await page.DisplayAlertAsync(title, sent ? sentMessage : failedMessage, "OK");
        return sent;
    }
}
