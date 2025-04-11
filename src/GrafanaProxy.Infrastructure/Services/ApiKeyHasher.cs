using Sodium;
using System.Text;

namespace GrafanaProxy.Infrastructure.Services
{
    public interface IApiKeyHasher
    {
        string HashApiKey(string apiKey);
        bool VerifyApiKey(string providedApiKey, string storedHash);
    }

    public class ApiKeyHasher : IApiKeyHasher
    {
        // Using Argon2 with default interactive difficulty settings.
        // These are generally a good balance and recommended over Scrypt.
        // Consider PasswordStorage.Strength.Sensitive if needed.
        
        public string HashApiKey(string apiKey)
        {
            // Use ScryptHashString - expects string password
            return PasswordHash.ScryptHashString(apiKey, PasswordHash.Strength.Interactive);
        }

        public bool VerifyApiKey(string providedApiKey, string storedHash)
        {
            // Use ScryptHashStringVerify - expects string password
            return PasswordHash.ScryptHashStringVerify(storedHash, providedApiKey);
        }
    }
} 