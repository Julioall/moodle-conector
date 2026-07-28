using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using MoodleConnector.Application.MoodleApi;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleEndpointValidator : IMoodleEndpointValidator
{
    private readonly ILogger<MoodleEndpointValidator> _logger;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolveAddressesAsync;

    public MoodleEndpointValidator(ILogger<MoodleEndpointValidator> logger)
        : this(
            logger,
            static async (host, cancellationToken) =>
                await Dns.GetHostAddressesAsync(host, cancellationToken))
    {
    }

    internal MoodleEndpointValidator(
        ILogger<MoodleEndpointValidator> logger,
        Func<string, CancellationToken, Task<IPAddress[]>> resolveAddressesAsync)
    {
        _logger = logger;
        _resolveAddressesAsync = resolveAddressesAsync;
    }

    public async Task<Uri> ValidateAsync(string baseUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw Failure("The Moodle endpoint must be an absolute HTTPS URL without user info.");
        }

        var host = uri.DnsSafeHost.TrimEnd('.').ToLowerInvariant();
        if (host is "localhost" or "local" or "internal" or "home.arpa" or "lan" ||
            host.EndsWith(".localhost", StringComparison.Ordinal) ||
            host.EndsWith(".local", StringComparison.Ordinal) ||
            host.EndsWith(".internal", StringComparison.Ordinal) ||
            host.EndsWith(".home.arpa", StringComparison.Ordinal) ||
            host.EndsWith(".lan", StringComparison.Ordinal))
        {
            throw Failure("The Moodle endpoint host is not public.");
        }

        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(host, out var literalAddress)
                ? [literalAddress]
                : await _resolveAddressesAsync(host, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException)
        {
            throw Failure("The Moodle endpoint host could not be resolved.", exception);
        }

        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
        {
            throw Failure("The Moodle endpoint resolves to a private, local, or reserved address.");
        }

        return new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty,
            UserName = string.Empty,
            Password = string.Empty
        }.Uri;
    }

    private MoodleApiException Failure(string internalMessage, Exception? innerException = null)
    {
        var failure = new MoodleApiException(
            MoodleErrorContract.NetworkError,
            internalMessage,
            innerException: innerException,
            stage: MoodleIntegrationStage.UrlValidation);
        _logger.LogWarning(
            innerException,
            "Moodle endpoint validation failed. AuditId={AuditId} ErrorCode={ErrorCode}",
            failure.AuditId,
            failure.ErrorCode);
        return failure;
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.None) ||
            address.Equals(IPAddress.IPv6None) ||
            address.IsIPv6LinkLocal ||
            address.IsIPv6SiteLocal ||
            address.IsIPv6Multicast)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !MatchesPrefix(bytes, [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00], 96) &&
                   !MatchesPrefix(bytes, [0x00, 0x64, 0xff, 0x9b, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00], 96) &&
                   !MatchesPrefix(bytes, [0x00, 0x64, 0xff, 0x9b, 0x00, 0x01], 48) &&
                   !MatchesPrefix(bytes, [0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00], 64) &&
                   !MatchesPrefix(bytes, [0x20, 0x01, 0x00, 0x00], 32) &&
                   !MatchesPrefix(bytes, [0x20, 0x01, 0x00, 0x02, 0x00, 0x00], 48) &&
                   !MatchesPrefix(bytes, [0x20, 0x01, 0x00, 0x10], 28) &&
                   !MatchesPrefix(bytes, [0x20, 0x01, 0x00, 0x20], 28) &&
                   !MatchesPrefix(bytes, [0x20, 0x01, 0x0d, 0xb8], 32) &&
                   !MatchesPrefix(bytes, [0x20, 0x02], 16) &&
                   !MatchesPrefix(bytes, [0xfc], 7);
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        return bytes[0] switch
        {
            0 or 10 or 127 => false,
            100 when bytes[1] is >= 64 and <= 127 => false,
            169 when bytes[1] == 254 => false,
            172 when bytes[1] is >= 16 and <= 31 => false,
            192 when bytes[1] == 0 && bytes[2] is 0 or 2 => false,
            192 when bytes[1] == 88 && bytes[2] == 99 => false,
            192 when bytes[1] == 168 => false,
            198 when bytes[1] is 18 or 19 => false,
            198 when bytes[1] == 51 && bytes[2] == 100 => false,
            203 when bytes[1] == 0 && bytes[2] == 113 => false,
            >= 224 => false,
            _ => true
        };
    }

    private static bool MatchesPrefix(ReadOnlySpan<byte> address, ReadOnlySpan<byte> prefix, int prefixLength)
    {
        var wholeBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;
        if (address.Length < wholeBytes || prefix.Length < wholeBytes)
        {
            return false;
        }

        if (!address[..wholeBytes].SequenceEqual(prefix[..wholeBytes]))
        {
            return false;
        }

        if (remainingBits == 0)
        {
            return true;
        }

        if (address.Length <= wholeBytes || prefix.Length <= wholeBytes)
        {
            return false;
        }

        var mask = (byte)(0xff << (8 - remainingBits));
        return (address[wholeBytes] & mask) == (prefix[wholeBytes] & mask);
    }
}
