using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GameTimeGuard.Core.Security;

public sealed class SignedEnvelope
{
    public string Kind { get; set; } = "";
    public long Sequence { get; set; }
    public string Payload { get; set; } = "";
    public string Signature { get; set; } = "";
}

public static class Integrity
{
    public static string Seal(string kind, string payloadJson, byte[] key)
    {
        if (string.IsNullOrWhiteSpace(kind))
            throw new ArgumentException("签名类型不能为空", nameof(kind));
        if (key is null || key.Length < 32)
            throw new ArgumentException("签名密钥长度不足", nameof(key));

        using var document = JsonDocument.Parse(payloadJson);
        if (!document.RootElement.TryGetProperty("sequence", out var sequenceProperty) ||
            sequenceProperty.ValueKind != JsonValueKind.Number ||
            !sequenceProperty.TryGetInt64(out var sequence))
        {
            throw new InvalidDataException("被签名的数据缺少 sequence");
        }

        var envelope = new SignedEnvelope
        {
            Kind = kind,
            Sequence = sequence,
            Payload = payloadJson,
            Signature = Compute(kind, sequence, payloadJson, key)
        };
        return GuardJson.Serialize(envelope);
    }

    public static bool TryOpen(string? envelopeJson, byte[] key, string expectedKind, out string payload, out long sequence)
    {
        payload = "";
        sequence = 0;
        if (string.IsNullOrWhiteSpace(envelopeJson) || key is null || key.Length == 0)
            return false;

        SignedEnvelope? envelope;
        try
        {
            envelope = GuardJson.Deserialize<SignedEnvelope>(envelopeJson);
        }
        catch (JsonException)
        {
            return false;
        }

        if (envelope is null || envelope.Payload is null)
            return false;
        if (!envelope.Kind.Equals(expectedKind, StringComparison.Ordinal))
            return false;

        var expected = Compute(envelope.Kind, envelope.Sequence, envelope.Payload, key);
        if (!FixedEquals(expected, envelope.Signature))
            return false;

        try
        {
            using var document = JsonDocument.Parse(envelope.Payload);
            if (!document.RootElement.TryGetProperty("sequence", out var sequenceProperty) ||
                !sequenceProperty.TryGetInt64(out var payloadSequence) ||
                payloadSequence != envelope.Sequence)
            {
                return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        payload = envelope.Payload;
        sequence = envelope.Sequence;
        return true;
    }

    public static string Compute(string kind, long sequence, string payload, byte[] key)
    {
        var material = Encoding.UTF8.GetBytes(kind + "\n" + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + payload);
        var hash = HMACSHA256.HashData(key, material);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool FixedEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        if (a.Length != b.Length)
        {
            CryptographicOperations.FixedTimeEquals(a, a);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
