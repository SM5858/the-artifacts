using System.Linq;

namespace TheArtifacts.Core.Account
{
    /// <summary>
    /// Username and password rules, the same as Unity Authentication's, so an offline account could move online
    /// unchanged.
    /// </summary>
    public static class AccountRules
    {
        public const int MinUsername = 3, MaxUsername = 20, MinPassword = 8, MaxPassword = 30;

        /// <summary>Null when both are acceptable; otherwise a message for the player.</summary>
        public static string Validate(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || username.Length < MinUsername || username.Length > MaxUsername)
                return $"Username: {MinUsername}-{MaxUsername} characters.";
            if (!username.All(c => char.IsLetterOrDigit(c) && c < 128 || c == '.' || c == '-' || c == '_' || c == '@'))
                return "Username: letters, digits, and . - _ @ only.";
            if (string.IsNullOrEmpty(password) || password.Length < MinPassword || password.Length > MaxPassword)
                return $"Password: {MinPassword}-{MaxPassword} characters.";
            if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit) ||
                password.All(char.IsLetterOrDigit))
                return "Password: needs an uppercase letter, a lowercase letter, a digit, and a symbol.";
            return null;
        }
    }
}
