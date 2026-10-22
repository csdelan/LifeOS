using System.Security.Cryptography;
using System.Text;
using LifeOs.Infrastructure.Integrations;

namespace LifeOs.Tests;

/// <summary>
/// Integration tokens are sealed at rest (migration 0036, activity plan §3.5): AES-GCM under the
/// token key, bound to the service and column they're kept in.
/// </summary>
public sealed class TokenCipherTests
{
    private const string Token = "oura-access-token-1234567890";

    private static TokenCipher NewCipher() => new(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void A_sealed_token_opens_again()
    {
        var cipher = NewCipher();

        var sealedValue = cipher.Encrypt(Token, "oura.access_token");

        Assert.Equal(Token, cipher.Decrypt(sealedValue, "oura.access_token"));
        Assert.False(Encoding.UTF8.GetString(sealedValue).Contains(Token, StringComparison.Ordinal));
    }

    [Fact]
    public void Each_seal_differs()
    {
        var cipher = NewCipher();

        Assert.NotEqual(cipher.Encrypt(Token, "oura.access_token"), cipher.Encrypt(Token, "oura.access_token"));
    }

    [Fact]
    public void Another_column_another_key_or_a_changed_byte_wont_open()
    {
        var cipher = NewCipher();
        var sealedValue = cipher.Encrypt(Token, "oura.access_token");
        var changed = (byte[])sealedValue.Clone();
        changed[^1] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => cipher.Decrypt(sealedValue, "oura.refresh_token"));
        Assert.ThrowsAny<CryptographicException>(() => NewCipher().Decrypt(sealedValue, "oura.access_token"));
        Assert.ThrowsAny<CryptographicException>(() => cipher.Decrypt(changed, "oura.access_token"));
        Assert.ThrowsAny<CryptographicException>(() => cipher.Decrypt([1, 2, 3], "oura.access_token"));
    }
}
