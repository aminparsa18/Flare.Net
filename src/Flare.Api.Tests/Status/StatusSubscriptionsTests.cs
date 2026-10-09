using Flare.Api.Alerting;
using Flare.Api.Model;
using Flare.Api.Status;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flare.Api.Tests.Status;

public class StatusSubscriptionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid PageId = Guid.NewGuid();

    [Theory]
    [InlineData("  Ada@Example.COM ", "ada@example.com")]
    [InlineData("a.b+tag@sub.example.org", "a.b+tag@sub.example.org")]
    public void TryNormalizeEmail_AcceptsAndLowercases(string raw, string expected)
    {
        Assert.True(StatusSubscriptions.TryNormalizeEmail(raw, out var email));
        Assert.Equal(expected, email);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-at-sign")]
    [InlineData("Ada <ada@example.com>")]
    [InlineData("a@example.com, b@example.com")]
    [InlineData("a b@example.com")]
    [InlineData("ada@localhost")]
    [InlineData("ada@.example.com")]
    [InlineData("\"quoted\"@example.com")]
    public void TryNormalizeEmail_RejectsMalformed(string? raw) => Assert.False(StatusSubscriptions.TryNormalizeEmail(raw, out _));

    [Fact]
    public void TryNormalizeEmail_RejectsOverlong() =>
        Assert.False(StatusSubscriptions.TryNormalizeEmail(new string('a', 250) + "@example.com", out _));

    [Fact]
    public void IdFor_IsStableAndPerPage()
    {
        Assert.Equal(StatusSubscriptions.IdFor(PageId, "a@example.com"), StatusSubscriptions.IdFor(PageId, "a@example.com"));
        Assert.NotEqual(StatusSubscriptions.IdFor(PageId, "a@example.com"), StatusSubscriptions.IdFor(PageId, "b@example.com"));
        Assert.NotEqual(StatusSubscriptions.IdFor(PageId, "a@example.com"), StatusSubscriptions.IdFor(Guid.NewGuid(), "a@example.com"));
    }

    [Fact]
    public void Subscribe_NewAddress_SavesUnverifiedAndSendsConfirmation()
    {
        var (save, send) = StatusSubscriptions.Subscribe(null, PageId, "a@example.com", [], Now);

        Assert.True(send);
        Assert.NotNull(save);
        Assert.False(save.Verified);
        Assert.Equal(StatusSubscriptions.IdFor(PageId, "a@example.com"), save.Id);
    }

    [Fact]
    public void Subscribe_VerifiedAddress_DoesNothing()
    {
        var existing = Row(verified: true, updated: Now.AddDays(-1));

        Assert.Equal((null, false), StatusSubscriptions.Subscribe(existing, PageId, existing.Email, [], Now));
    }

    [Fact]
    public void Subscribe_UnverifiedInsideCooldown_DoesNothing()
    {
        var existing = Row(verified: false, updated: Now.AddMinutes(-2));

        Assert.Equal((null, false), StatusSubscriptions.Subscribe(existing, PageId, existing.Email, [], Now));
    }

    [Fact]
    public void Subscribe_UnverifiedAfterCooldown_ResendsAndKeepsCreatedAt()
    {
        var existing = Row(verified: false, updated: Now.AddMinutes(-30));

        var (save, send) = StatusSubscriptions.Subscribe(existing, PageId, existing.Email, [], Now);

        Assert.True(send);
        Assert.Equal(Now, save!.UpdatedAt);
        Assert.Equal(existing.CreatedAt, save.CreatedAt);
    }

    private static readonly Guid RefA = Guid.NewGuid();
    private static readonly Guid RefB = Guid.NewGuid();
    private static readonly Guid RefC = Guid.NewGuid();

    private static StatusPage Page() => new()
    {
        Id = PageId, Slug = "acme", Title = "Acme", CreatedAt = Now, UpdatedAt = Now,
        Components = [new("A", StatusComponentKind.Monitor, RefA), new("B", StatusComponentKind.Slo, RefB), new("C", StatusComponentKind.Slo, RefC)],
    };

    [Fact]
    public void ComponentKey_IsStableAndPerPage()
    {
        Assert.Equal(StatusSubscriptions.ComponentKey(PageId, RefA), StatusSubscriptions.ComponentKey(PageId, RefA));
        Assert.NotEqual(StatusSubscriptions.ComponentKey(PageId, RefA), StatusSubscriptions.ComponentKey(PageId, RefB));
        Assert.NotEqual(StatusSubscriptions.ComponentKey(PageId, RefA), StatusSubscriptions.ComponentKey(Guid.NewGuid(), RefA));
        Assert.NotEqual(RefA, StatusSubscriptions.ComponentKey(PageId, RefA));
    }

    [Fact]
    public void TryNormalizeComponents_NoneOrAll_MeansEverything()
    {
        var page = Page();
        Assert.True(StatusSubscriptions.TryNormalizeComponents(page, null, out var none));
        Assert.Empty(none);
        var all = page.Components.Select(c => StatusSubscriptions.ComponentKey(PageId, c.RefId)).ToList();
        Assert.True(StatusSubscriptions.TryNormalizeComponents(page, all, out var everything));
        Assert.Empty(everything);
    }

    [Fact]
    public void TryNormalizeComponents_SubsetIsKeptDistinct_UnknownIsRejected()
    {
        var page = Page();
        var a = StatusSubscriptions.ComponentKey(PageId, RefA);
        Assert.True(StatusSubscriptions.TryNormalizeComponents(page, [a, a], out var subset));
        Assert.Equal([a], subset);
        Assert.False(StatusSubscriptions.TryNormalizeComponents(page, [a, Guid.NewGuid()], out _));
        Assert.False(StatusSubscriptions.TryNormalizeComponents(page, [RefA], out _));
    }

    [Fact]
    public void Wants_FiltersByComponentButNeverDropsUnscopedIncidents()
    {
        var onlyA = Row(true, Now) with { Components = [StatusSubscriptions.ComponentKey(PageId, RefA)] };
        Assert.True(StatusSubscriptions.Wants(Row(true, Now), PageId, [RefB]));
        Assert.True(StatusSubscriptions.Wants(onlyA, PageId, [RefA, RefB]));
        Assert.False(StatusSubscriptions.Wants(onlyA, PageId, [RefB]));
        Assert.True(StatusSubscriptions.Wants(onlyA, PageId, []));
    }

    [Fact]
    public void Subscribe_PendingAddress_TakesNewComponents()
    {
        var existing = Row(verified: false, updated: Now.AddMinutes(-30));
        var picked = new[] { StatusSubscriptions.ComponentKey(PageId, RefA) };

        var (save, _) = StatusSubscriptions.Subscribe(existing, PageId, existing.Email, picked, Now);

        Assert.Equal(picked, save!.Components);
    }

    [Fact]
    public void Signer_ConfirmAndUnsubscribeLinksAreNotInterchangeable()
    {
        var signer = new StatusSubscriptionLinkSigner(new EphemeralDataProtectionProvider(), Options.Create(new AlertLinkOptions { PublicUrl = "https://flare.example.com/" }));

        var confirm = Token(signer.ConfirmUrl(PageId, "a@example.com"));
        var unsubscribe = Token(signer.UnsubscribeUrl(PageId, "a@example.com"));

        Assert.Equal(new StatusSubscriptionClaims(PageId, "a@example.com"), signer.ValidateConfirm(confirm));
        Assert.Equal(new StatusSubscriptionClaims(PageId, "a@example.com"), signer.ValidateUnsubscribe(unsubscribe));
        Assert.Null(signer.ValidateUnsubscribe(confirm));
        Assert.Null(signer.ValidateConfirm(unsubscribe));
        Assert.Null(signer.ValidateConfirm("garbage"));
        Assert.Null(signer.ValidateConfirm(null));
    }

    [Fact]
    public void Signer_NoPublicUrl_IssuesNoLinks()
    {
        var signer = new StatusSubscriptionLinkSigner(new EphemeralDataProtectionProvider(), Options.Create(new AlertLinkOptions()));

        Assert.Null(signer.ConfirmUrl(PageId, "a@example.com"));
        Assert.Null(signer.UnsubscribeUrl(PageId, "a@example.com"));
    }

    [Fact]
    public void Mailer_IncidentEmail_CarriesUnsubscribeHeadersAndLinks()
    {
        var page = new StatusPage { Id = PageId, Slug = "acme", Title = "Acme", CreatedAt = Now, UpdatedAt = Now };
        var incident = new StatusIncident { Id = Guid.NewGuid(), PageId = PageId, Title = "API down", CreatedAt = Now, UpdatedAt = Now };
        var update = new StatusIncidentUpdate(Now, StatusIncidentStatus.Monitoring, "Fix is out.");

        var message = StatusSubscriberMailer.BuildIncident("flare@example.com", page, incident, update, ["API"], "https://flare.example.com/", new StatusMailRecipient("a@example.com", "https://flare.example.com/subscribe/unsubscribe?token=t"));

        Assert.Equal("[Acme] Monitoring: API down", message.Subject);
        Assert.Equal("<https://flare.example.com/subscribe/unsubscribe?token=t>", message.Headers["List-Unsubscribe"]);
        Assert.Equal("List-Unsubscribe=One-Click", message.Headers["List-Unsubscribe-Post"]);
        var text = message.TextBody;
        Assert.Contains("Fix is out.", text);
        Assert.Contains("Affected: API", text);
        Assert.Contains("https://flare.example.com/status/acme", text);
        Assert.Contains("Unsubscribe: https://flare.example.com/subscribe/unsubscribe?token=t", text);
    }

    private static string? Token(string? url) => AlertAckLinkSigner.TokenFromUrl(url);

    private static StatusSubscriber Row(bool verified, DateTimeOffset updated) => new()
    {
        Id = StatusSubscriptions.IdFor(PageId, "a@example.com"), PageId = PageId, Email = "a@example.com",
        Verified = verified, CreatedAt = Now.AddDays(-2), UpdatedAt = updated,
    };
}
