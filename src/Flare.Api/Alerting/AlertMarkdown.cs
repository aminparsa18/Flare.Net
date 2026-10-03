using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Flare.Api.Alerting;

/// <summary>Which wire format <see cref="AlertMarkdown.Render"/> produces. See ADR-0090.</summary>
public enum AlertMarkupFormat
{
    /// <summary>Markers stripped; used for webhook (non-Slack), PagerDuty, the email subject and the email text part.</summary>
    Plain,

    /// <summary>Telegram's <c>parse_mode: HTML</c> subset (<c>b</c>, <c>i</c>, <c>code</c>, <c>a</c>).</summary>
    TelegramHtml,

    /// <summary>Slack <c>mrkdwn</c> (<c>*bold*</c>, <c>_italic_</c>, <c>&lt;url|label&gt;</c>).</summary>
    SlackMrkdwn,

    /// <summary>An HTML fragment for the email's <c>text/html</c> part.</summary>
    EmailHtml,
}

/// <summary>
/// Renders an alert notification template (<see cref="Model.AlertRule.NotificationBodyTemplate"/>
/// and the title) as a small CommonMark subset in each channel's own format: <c>**bold**</c>,
/// <c>*italic*</c>/<c>_italic_</c>, <c>`code`</c>, <c>[label](url)</c>, <c>-</c>/<c>*</c>
/// bullet lists and <c>1.</c> numbered lists, with <c>\</c> escapes. Nothing else is
/// interpreted (no headings, tables, raw HTML), so a template can't inject markup a channel
/// wouldn't accept.
/// </summary>
/// <remarks>
/// <c>{{placeholder}}</c> values are substituted <em>before</em> parsing as opaque text atoms:
/// they are never scanned for markers, so a service called <c>my_*_svc</c> or a log message
/// containing <c>**</c> can't change the formatting, and they are escaped for the target
/// format. A link whose URL resolves empty (<c>{{rule_url}}</c> without
/// <c>Alerting:PublicUrl</c>) collapses to its label, and only <c>http</c>, <c>https</c> and
/// <c>mailto</c> targets become links. See <c>docs-internal/adr/0090-alert-template-markdown.md</c>.
/// </remarks>
public static partial class AlertMarkdown
{
    private const string Specials = "\\*_`[]";

    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_.\-]+)\s*\}\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"^\s*[-*+]\s+(.*)$")]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"^\s*(\d{1,9})[.)]\s+(.*)$")]
    private static partial Regex OrderedRegex();

    [GeneratedRegex(@"https?://[^\s<>""]+")]
    private static partial Regex BareUrlRegex();

    public static string Render(string template, IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, string> labels, AlertMarkupFormat format)
    {
        var lines = template.Replace("\r\n", "\n").Split('\n');
        var output = new StringBuilder();
        var html = format == AlertMarkupFormat.EmailHtml;
        string? openList = null;

        void CloseList()
        {
            if (openList is not null)
            {
                output.Append("</").Append(openList).Append('>');
                openList = null;
            }
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var bullet = BulletRegex().Match(line);
            var ordered = bullet.Success ? Match.Empty : OrderedRegex().Match(line);
            if (bullet.Success || ordered.Success)
            {
                var content = bullet.Success ? bullet.Groups[1].Value : ordered.Groups[2].Value;
                var inline = RenderInline(content, values, labels, format);
                if (html)
                {
                    var tag = bullet.Success ? "ul" : "ol";
                    if (openList != tag)
                    {
                        CloseList();
                        output.Append('<').Append(tag).Append('>');
                        openList = tag;
                    }

                    output.Append("<li>").Append(inline).Append("</li>");
                }
                else
                {
                    var marker = bullet.Success ? "•" : ordered.Groups[1].Value + ".";
                    output.Append(marker).Append(' ').Append(inline);
                    if (i < lines.Length - 1)
                    {
                        output.Append('\n');
                    }
                }

                continue;
            }

            CloseList();
            output.Append(RenderInline(line, values, labels, format));
            if (i < lines.Length - 1)
            {
                output.Append(html ? "<br>\n" : "\n");
            }
        }

        CloseList();
        return html ? $"<div>{output}</div>" : output.ToString();
    }

    // ---- inline ----

    /// <summary>One source character, or a resolved placeholder value (<see cref="Atom"/>), or an escaped character that must stay literal.</summary>
    private readonly record struct Tok(char Ch, string? Atom, bool Escaped)
    {
        public bool IsSpecial(char c) => Atom is null && !Escaped && Ch == c;

        public bool IsWhitespace => Atom is null && char.IsWhiteSpace(Ch);

        public bool IsWordChar => Atom is not null || char.IsLetterOrDigit(Ch);
    }

    private abstract record Node;

    private sealed record TextNode(string Text) : Node;

    private sealed record CodeNode(string Text) : Node;

    private sealed record StyleNode(char Kind, List<Node> Children) : Node; // 'b' bold, 'i' italic

    private sealed record LinkNode(List<Node> Label, string Url) : Node;

    private static string RenderInline(string line, IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, string> labels, AlertMarkupFormat format)
    {
        var tokens = Tokenize(line, values, labels);
        var nodes = ParseRange(tokens, 0, tokens.Count, values, labels);
        var sb = new StringBuilder();
        Emit(nodes, format, sb);
        return sb.ToString();
    }

    private static List<Tok> Tokenize(string line, IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, string> labels)
    {
        var tokens = new List<Tok>(line.Length);
        var pos = 0;
        while (pos < line.Length)
        {
            if (line[pos] == '{' && PlaceholderRegex().Match(line, pos) is { Success: true, Index: var at } m && at == pos)
            {
                tokens.Add(new Tok('\0', AlertTemplateRenderer.Render(m.Value, values, labels), false));
                pos += m.Length;
            }
            else if (line[pos] == '\\' && pos + 1 < line.Length && Specials.Contains(line[pos + 1]))
            {
                tokens.Add(new Tok(line[pos + 1], null, true));
                pos += 2;
            }
            else
            {
                tokens.Add(new Tok(line[pos], null, false));
                pos++;
            }
        }

        return tokens;
    }

    private static string Flatten(List<Tok> tokens, int start, int end)
    {
        var sb = new StringBuilder();
        for (var i = start; i < end; i++)
        {
            sb.Append(tokens[i].Atom ?? tokens[i].Ch.ToString());
        }

        return sb.ToString();
    }

    private static List<Node> ParseRange(List<Tok> t, int start, int end, IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, string> labels)
    {
        var nodes = new List<Node>();
        var text = new StringBuilder();

        void FlushText()
        {
            if (text.Length > 0)
            {
                nodes.Add(new TextNode(text.ToString()));
                text.Clear();
            }
        }

        var i = start;
        while (i < end)
        {
            var tok = t[i];

            if (tok.IsSpecial('`'))
            {
                var close = IndexOfSpecial(t, '`', i + 1, end);
                if (close > i + 1)
                {
                    FlushText();
                    nodes.Add(new CodeNode(Flatten(t, i + 1, close)));
                    i = close + 1;
                    continue;
                }
            }
            else if (tok.IsSpecial('['))
            {
                var parsed = TryParseLink(t, i, end, values, labels);
                if (parsed is not null)
                {
                    FlushText();
                    nodes.Add(parsed.Value.Node);
                    i = parsed.Value.Next;
                    continue;
                }
            }
            else if ((tok.IsSpecial('*') || tok.IsSpecial('_')) && i + 1 < end)
            {
                var marker = tok.Ch;
                var bold = marker == '*' && t[i + 1].IsSpecial('*');
                var width = bold ? 2 : 1;
                var contentStart = i + width;
                var leftOk = marker == '*' || i == start || !t[i - 1].IsWordChar;
                if (leftOk && contentStart < end && !t[contentStart].IsWhitespace)
                {
                    var close = FindEmphasisClose(t, marker, width, contentStart, end);
                    if (close > contentStart)
                    {
                        FlushText();
                        nodes.Add(new StyleNode(bold ? 'b' : 'i', ParseRange(t, contentStart, close, values, labels)));
                        i = close + width;
                        continue;
                    }
                }
            }

            text.Append(tok.Atom ?? tok.Ch.ToString());
            i++;
        }

        FlushText();
        return nodes;
    }

    private static int IndexOfSpecial(List<Tok> t, char c, int from, int end)
    {
        for (var i = from; i < end; i++)
        {
            if (t[i].IsSpecial(c))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindEmphasisClose(List<Tok> t, char marker, int width, int from, int end)
    {
        for (var i = from + 1; i + width <= end; i++)
        {
            if (!t[i].IsSpecial(marker))
            {
                continue;
            }

            if (width == 2)
            {
                if (!t[i + 1].IsSpecial(marker))
                {
                    continue;
                }
            }
            else if ((i + 1 < end && t[i + 1].IsSpecial(marker)) || (i > from && t[i - 1].IsSpecial(marker)))
            {
                continue;
            }

            if (t[i - 1].IsWhitespace)
            {
                continue;
            }

            if (marker == '_' && i + 1 < end && t[i + 1].IsWordChar)
            {
                continue;
            }

            return i;
        }

        return -1;
    }

    private static (LinkNode Node, int Next)? TryParseLink(List<Tok> t, int open, int end, IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, string> labels)
    {
        var closeLabel = -1;
        for (var i = open + 1; i + 1 < end; i++)
        {
            if (t[i].IsSpecial(']') && t[i + 1].Atom is null && !t[i + 1].Escaped && t[i + 1].Ch == '(')
            {
                closeLabel = i;
                break;
            }
        }

        if (closeLabel < 0)
        {
            return null;
        }

        var closeUrl = -1;
        for (var i = closeLabel + 2; i < end; i++)
        {
            if (t[i].Atom is null && !t[i].Escaped && t[i].Ch == ')')
            {
                closeUrl = i;
                break;
            }
        }

        if (closeUrl < 0)
        {
            return null;
        }

        var url = Flatten(t, closeLabel + 2, closeUrl).Trim();
        var safe = url.Length == 0
            || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
        if (!safe)
        {
            return null;
        }

        return (new LinkNode(ParseRange(t, open + 1, closeLabel, values, labels), url), closeUrl + 1);
    }

    // ---- emit ----

    private static void Emit(List<Node> nodes, AlertMarkupFormat format, StringBuilder sb)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextNode text:
                    sb.Append(EscapeText(text.Text, format));
                    break;
                case CodeNode code:
                    sb.Append(format switch
                    {
                        AlertMarkupFormat.Plain => code.Text,
                        AlertMarkupFormat.SlackMrkdwn => $"`{EscapeText(code.Text, format)}`",
                        _ => $"<code>{EscapeText(code.Text, format, autoLink: false)}</code>",
                    });
                    break;
                case StyleNode style:
                    {
                        var (open, close) = (format, style.Kind) switch
                        {
                            (AlertMarkupFormat.Plain, _) => ("", ""),
                            (AlertMarkupFormat.SlackMrkdwn, 'b') => ("*", "*"),
                            (AlertMarkupFormat.SlackMrkdwn, _) => ("_", "_"),
                            (AlertMarkupFormat.EmailHtml, 'b') => ("<strong>", "</strong>"),
                            (AlertMarkupFormat.EmailHtml, _) => ("<em>", "</em>"),
                            (_, 'b') => ("<b>", "</b>"),
                            _ => ("<i>", "</i>"),
                        };
                        sb.Append(open);
                        Emit(style.Children, format, sb);
                        sb.Append(close);
                        break;
                    }

                case LinkNode link:
                    EmitLink(link, format, sb);
                    break;
            }
        }
    }

    private static void EmitLink(LinkNode link, AlertMarkupFormat format, StringBuilder sb)
    {
        if (link.Url.Length == 0)
        {
            // {{rule_url}} etc. unresolved - keep the label, drop the dead link.
            Emit(link.Label, format, sb);
            return;
        }

        switch (format)
        {
            case AlertMarkupFormat.Plain:
                {
                    var label = new StringBuilder();
                    Emit(link.Label, format, label);
                    sb.Append(label.Length == 0 || label.ToString() == link.Url ? link.Url : $"{label} ({link.Url})");
                    break;
                }

            case AlertMarkupFormat.SlackMrkdwn:
                sb.Append('<').Append(link.Url.Replace("&", "&amp;").Replace("<", "%3C").Replace(">", "%3E").Replace("|", "%7C")).Append('|');
                Emit(link.Label, format, sb);
                sb.Append('>');
                break;

            default:
                sb.Append("<a href=\"").Append(WebUtility.HtmlEncode(link.Url)).Append("\">");
                var inner = new StringBuilder();
                Emit(link.Label, format, inner);
                sb.Append(inner.Length == 0 ? WebUtility.HtmlEncode(link.Url) : inner.ToString());
                sb.Append("</a>");
                break;
        }
    }

    private static string EscapeText(string text, AlertMarkupFormat format, bool autoLink = true)
    {
        switch (format)
        {
            case AlertMarkupFormat.Plain:
                return text;
            case AlertMarkupFormat.SlackMrkdwn:
                return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            case AlertMarkupFormat.TelegramHtml:
                // Telegram linkifies a bare URL on its own.
                return WebUtility.HtmlEncode(text);
            default:
                if (!autoLink)
                {
                    return WebUtility.HtmlEncode(text);
                }

                // Email clients vary on linkifying plain URLs in HTML, so do it here.
                var sb = new StringBuilder();
                var last = 0;
                foreach (Match m in BareUrlRegex().Matches(text))
                {
                    var url = m.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')');
                    sb.Append(WebUtility.HtmlEncode(text[last..m.Index]));
                    sb.Append("<a href=\"").Append(WebUtility.HtmlEncode(url)).Append("\">").Append(WebUtility.HtmlEncode(url)).Append("</a>");
                    last = m.Index + url.Length;
                }

                sb.Append(WebUtility.HtmlEncode(text[last..]));
                return sb.ToString();
        }
    }
}
