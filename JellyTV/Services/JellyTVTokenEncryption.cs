using System;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyTV.Services;

/// <summary>
/// Service for encrypting and decrypting APNs device tokens.
/// </summary>
public sealed class JellyTVTokenEncryption
{
    private const string Purpose = "Jellyfin.Plugin.JellyTV.ApnsTokens.v1";

    private readonly IDataProtector _protector;
    private readonly ILogger<JellyTVTokenEncryption> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyTVTokenEncryption"/> class.
    /// </summary>
    /// <param name="provider">The data protection provider.</param>
    /// <param name="logger">The logger.</param>
    public JellyTVTokenEncryption(IDataProtectionProvider provider, ILogger<JellyTVTokenEncryption> logger)
    {
        _protector = provider.CreateProtector(Purpose);
        _logger = logger;
    }

    /// <summary>
    /// Encrypts a plaintext APNs token.
    /// </summary>
    /// <param name="plainToken">The plaintext token.</param>
    /// <returns>The encrypted token.</returns>
    public string Encrypt(string plainToken)
    {
        if (string.IsNullOrWhiteSpace(plainToken))
        {
            return plainToken;
        }

        try
        {
            return _protector.Protect(plainToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt token");
            throw;
        }
    }

    /// <summary>
    /// Attempts to decrypt an encrypted token. Returns null if decryption fails.
    /// </summary>
    /// <param name="encryptedToken">The encrypted token.</param>
    /// <returns>The decrypted token, or null if decryption failed.</returns>
    public string? TryDecrypt(string encryptedToken)
    {
        if (string.IsNullOrWhiteSpace(encryptedToken))
        {
            return encryptedToken;
        }

        if (IsPlaintextToken(encryptedToken))
        {
            return encryptedToken;
        }

        try
        {
            return _protector.Unprotect(encryptedToken);
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt token (corrupted or wrong key)");
            return null;
        }
    }

    /// <summary>
    /// Checks if a token appears to be a plaintext APNs token (64 hex chars).
    /// Used for migration from unencrypted storage.
    /// </summary>
    /// <param name="token">The token to check.</param>
    /// <returns>True if the token appears to be plaintext.</returns>
    public static bool IsPlaintextToken(string token)
    {
        return !string.IsNullOrWhiteSpace(token) &&
               token.Length == 64 &&
               token.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));
    }
}
