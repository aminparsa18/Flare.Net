using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Flare.Api.Synthetic;

/// <summary>
/// A minimal DNS client for the record types the system resolver API cannot return (MX, TXT, CNAME): builds one
/// query, sends it over UDP (retrying over TCP when the reply is truncated) and decodes the answer section.
/// See <c>docs-internal/adr/0135-synthetic-dns-mx-txt-cname.md</c>.
/// </summary>
public static class SyntheticDnsWire
{
    public const ushort TypeCname = 5;
    public const ushort TypeMx = 15;
    public const ushort TypeTxt = 16;

    /// <summary>The DNS type code for <paramref name="record"/> (MX, TXT or CNAME), or null for anything else.</summary>
    public static ushort? TypeFor(string record) => record.ToUpperInvariant() switch
    {
        "CNAME" => TypeCname,
        "MX" => TypeMx,
        "TXT" => TypeTxt,
        _ => null,
    };

    public static byte[] BuildQuery(ushort id, string host, ushort type)
    {
        var message = new List<byte>(32);
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header, id);
        BinaryPrimitives.WriteUInt16BigEndian(header[2..], 0x0100); // recursion desired
        BinaryPrimitives.WriteUInt16BigEndian(header[4..], 1);      // one question
        message.AddRange(header.ToArray());
        foreach (var label in host.Trim().TrimEnd('.').Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            if (bytes.Length is 0 or > 63)
            {
                throw new InvalidOperationException("invalid DNS label");
            }

            message.Add((byte)bytes.Length);
            message.AddRange(bytes);
        }

        message.Add(0);
        message.Add((byte)(type >> 8));
        message.Add((byte)type);
        message.Add(0);
        message.Add(1); // class IN
        return [.. message];
    }

    /// <summary>The answers of <paramref name="type"/> in <paramref name="response"/>, as text. Throws on a malformed or error reply.</summary>
    public static (List<string> Answers, bool Truncated) ParseAnswers(byte[] response, ushort id, ushort type)
    {
        if (response.Length < 12 || BinaryPrimitives.ReadUInt16BigEndian(response) != id)
        {
            throw new InvalidOperationException("malformed DNS reply");
        }

        var flags = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(2));
        var truncated = (flags & 0x0200) != 0;
        var rcode = flags & 0xF;
        if (rcode == 3)
        {
            return ([], truncated); // NXDOMAIN: no answers, not an error of the probe itself
        }

        if (rcode != 0)
        {
            throw new InvalidOperationException($"DNS server returned RCODE {rcode}");
        }

        var questions = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(4));
        var answers = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(6));
        var pos = 12;
        for (var i = 0; i < questions; i++)
        {
            ReadName(response, ref pos);
            pos += 4;
        }

        var result = new List<string>();
        for (var i = 0; i < answers; i++)
        {
            ReadName(response, ref pos);
            if (pos + 10 > response.Length)
            {
                throw new InvalidOperationException("malformed DNS reply");
            }

            var recordType = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(pos));
            var length = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(pos + 8));
            pos += 10;
            var end = pos + length;
            if (end > response.Length)
            {
                throw new InvalidOperationException("malformed DNS reply");
            }

            if (recordType == type)
            {
                result.Add(type switch
                {
                    TypeMx => $"{BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(pos))} {ReadNameAt(response, pos + 2)}",
                    TypeCname => ReadNameAt(response, pos),
                    _ => ReadTxt(response, pos, end),
                });
            }

            pos = end;
        }

        return (result, truncated);
    }

    /// <summary>Resolves <paramref name="host"/> against the system's configured name servers (first to answer wins).</summary>
    public static async Task<List<string>> ResolveAsync(string host, ushort type, CancellationToken cancellationToken)
    {
        var servers = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().DnsAddresses)
            .Where(a => !a.IsIPv6LinkLocal && !a.IsIPv6SiteLocal)
            .Distinct()
            .ToList();
        if (servers.Count == 0)
        {
            throw new InvalidOperationException("no DNS servers configured");
        }

        Exception? last = null;
        foreach (var server in servers)
        {
            try
            {
                return await ResolveAsync(new IPEndPoint(server, 53), host, type, cancellationToken);
            }
            catch (Exception ex) when (ex is SocketException or InvalidOperationException or IOException)
            {
                last = ex;
            }
        }

        throw last!;
    }

    public static async Task<List<string>> ResolveAsync(IPEndPoint server, string host, ushort type, CancellationToken cancellationToken)
    {
        var id = (ushort)Random.Shared.Next(ushort.MaxValue + 1);
        var query = BuildQuery(id, host, type);
        using var udp = new UdpClient(server.AddressFamily);
        udp.Connect(server);
        await udp.SendAsync(query, cancellationToken);
        var reply = (await udp.ReceiveAsync(cancellationToken)).Buffer;
        var (answers, truncated) = ParseAnswers(reply, id, type);
        if (!truncated)
        {
            return answers;
        }

        using var tcp = new TcpClient(server.AddressFamily);
        await tcp.ConnectAsync(server, cancellationToken);
        var stream = tcp.GetStream();
        var framed = new byte[query.Length + 2];
        BinaryPrimitives.WriteUInt16BigEndian(framed, (ushort)query.Length);
        query.CopyTo(framed, 2);
        await stream.WriteAsync(framed, cancellationToken);
        var lengthBytes = new byte[2];
        await stream.ReadExactlyAsync(lengthBytes, cancellationToken);
        var body = new byte[BinaryPrimitives.ReadUInt16BigEndian(lengthBytes)];
        await stream.ReadExactlyAsync(body, cancellationToken);
        return ParseAnswers(body, id, type).Answers;
    }

    private static string ReadNameAt(byte[] message, int pos) => ReadName(message, ref pos);

    private static string ReadName(byte[] message, ref int pos)
    {
        var labels = new List<string>();
        var cursor = pos;
        var jumped = false;
        for (var hops = 0; hops < 128; hops++)
        {
            if (cursor >= message.Length)
            {
                throw new InvalidOperationException("malformed DNS name");
            }

            var len = message[cursor];
            if (len == 0)
            {
                if (!jumped)
                {
                    pos = cursor + 1;
                }

                return string.Join('.', labels);
            }

            if ((len & 0xC0) == 0xC0)
            {
                if (cursor + 1 >= message.Length)
                {
                    throw new InvalidOperationException("malformed DNS name");
                }

                if (!jumped)
                {
                    pos = cursor + 2;
                }

                jumped = true;
                cursor = ((len & 0x3F) << 8) | message[cursor + 1];
                continue;
            }

            if (cursor + 1 + len > message.Length)
            {
                throw new InvalidOperationException("malformed DNS name");
            }

            labels.Add(Encoding.ASCII.GetString(message, cursor + 1, len));
            cursor += 1 + len;
        }

        throw new InvalidOperationException("DNS name compression loop");
    }

    private static string ReadTxt(byte[] message, int pos, int end)
    {
        var text = new StringBuilder();
        while (pos < end)
        {
            var len = message[pos++];
            if (pos + len > end)
            {
                throw new InvalidOperationException("malformed TXT record");
            }

            text.Append(Encoding.UTF8.GetString(message, pos, len));
            pos += len;
        }

        return text.ToString();
    }
}
