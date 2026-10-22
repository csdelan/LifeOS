using System.Security.Cryptography;
using System.Text;

namespace LifeOs.Infrastructure.Integrations;

/// <summary>
/// Encrypts integration tokens at rest (migration 0036, activity plan §3.5): AES-256-GCM under
/// <c>Integrations:TokenKey</c>, 32 random bytes in base64 held outside the database (<c>.env</c>
/// and a Fly secret). A sealed value is a version byte, a 12-byte nonce, the 16-byte tag and
/// the ciphertext. It's bound to where it's kept (service and column), so a value copied to
/// another column or service won't open.
/// </summary>
public sealed class TokenCipher
{
    public const string KeyKey = "Integrations:TokenKey";

    /// <summary>A PowerShell line that prints a new key.</summary>
    public const string MakeKeyCommand =
        "[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))";

    private const byte Version = 1;
    private const int KeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int HeaderBytes = 1 + NonceBytes + TagBytes;

    private readonly byte[] key;

    public TokenCipher(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != KeyBytes)
        {
            throw new ArgumentException($"A token key is {KeyBytes} bytes, got {key.Length}.", nameof(key));
        }

        this.key = (byte[])key.Clone();
    }

    /// <summary>
    /// The cipher for <c>Integrations:TokenKey</c>, read through <paramref name="get"/>, or null
    /// when it isn't set. Throws <see cref="InvalidOperationException"/> when it isn't 32 bytes
    /// of base64, so a bad deploy fails at startup.
    /// </summary>
    public static TokenCipher? Resolve(Func<string, string?> get)
    {
        var text = get(KeyKey)?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                $"{KeyKey} isn't base64. It's 32 random bytes in base64; make one with {MakeKeyCommand}");
        }

        if (bytes.Length != KeyBytes)
        {
            throw new InvalidOperationException(
                $"{KeyKey} is {bytes.Length} bytes; it must be {KeyBytes}. Make one with {MakeKeyCommand}");
        }

        return new TokenCipher(bytes);
    }

    /// <summary>Seals <paramref name="plaintext"/> for <paramref name="context"/> (e.g. <c>oura.access_token</c>).</summary>
    public byte[] Encrypt(string plaintext, string context)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var data = Encoding.UTF8.GetBytes(plaintext);
        var sealedValue = new byte[HeaderBytes + data.Length];
        sealedValue[0] = Version;
        var nonce = sealedValue.AsSpan(1, NonceBytes);
        var tag = sealedValue.AsSpan(1 + NonceBytes, TagBytes);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagBytes);
        aes.Encrypt(nonce, data, sealedValue.AsSpan(HeaderBytes), tag, AssociatedData(context));
        return sealedValue;
    }

    /// <summary>
    /// Opens a value <see cref="Encrypt"/> sealed for <paramref name="context"/>. Throws
    /// <see cref="CryptographicException"/> when it was sealed under another key or context,
    /// or has been changed.
    /// </summary>
    public string Decrypt(byte[] sealedValue, string context)
    {
        ArgumentNullException.ThrowIfNull(sealedValue);
        if (sealedValue.Length < HeaderBytes || sealedValue[0] != Version)
        {
            throw new CryptographicException("Not a sealed token, or one from a newer version of LifeOS.");
        }

        var data = new byte[sealedValue.Length - HeaderBytes];
        using var aes = new AesGcm(key, TagBytes);
        aes.Decrypt(
            sealedValue.AsSpan(1, NonceBytes),
            sealedValue.AsSpan(HeaderBytes),
            sealedValue.AsSpan(1 + NonceBytes, TagBytes),
            data,
            AssociatedData(context));
        return Encoding.UTF8.GetString(data);
    }

    // Never print the key.
    public override string ToString() => nameof(TokenCipher);

    private static byte[] AssociatedData(string context)
        => Encoding.UTF8.GetBytes("bsk_ops.integration/" + context);
}
