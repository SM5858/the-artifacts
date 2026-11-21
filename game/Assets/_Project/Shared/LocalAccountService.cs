using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TheArtifacts.Core.Account;
using UnityEngine;

namespace TheArtifacts.Shared
{
    /// <summary>
    /// Offline accounts for development and playtests without the network: one JSON profile per username under
    /// <c>Application.persistentDataPath/accounts</c>, with a salted password hash. Not shared between devices.
    /// </summary>
    public sealed class LocalAccountService : IAccountService
    {
        [Serializable]
        sealed class Credentials
        {
            public string Salt;
            public string Hash;
        }

        public bool IsOnline => false;
        public string Username { get; private set; }

        static string Folder => Path.Combine(Application.persistentDataPath, "accounts");
        static string CredentialsPath(string username) => Path.Combine(Folder, username.ToLowerInvariant() + ".credentials.json");
        static string ProfilePath(string username) => Path.Combine(Folder, username.ToLowerInvariant() + ".profile.json");

        public Task SignInAsync(string username, string password)
        {
            var path = CredentialsPath(username ?? "");
            if (string.IsNullOrEmpty(username) || !File.Exists(path)) throw new AccountException("No account with that username on this computer.");
            var credentials = JsonUtility.FromJson<Credentials>(File.ReadAllText(path));
            if (Hash(credentials.Salt, password) != credentials.Hash) throw new AccountException("Wrong password.");
            Username = username;
            return Task.CompletedTask;
        }

        public Task SignUpAsync(string username, string password)
        {
            var problem = AccountRules.Validate(username, password);
            if (problem != null) throw new AccountException(problem);
            if (File.Exists(CredentialsPath(username))) throw new AccountException("That username is taken on this computer.");

            Directory.CreateDirectory(Folder);
            var salt = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            File.WriteAllText(CredentialsPath(username), JsonUtility.ToJson(new Credentials { Salt = salt, Hash = Hash(salt, password) }));
            Username = username;
            return Task.CompletedTask;
        }

        public void SignOut() => Username = null;

        public Task<PlayerProfile> LoadProfileAsync()
        {
            RequireSignedIn();
            var path = ProfilePath(Username);
            var profile = File.Exists(path) ? JsonUtility.FromJson<PlayerProfile>(File.ReadAllText(path)) : null;
            return Task.FromResult(profile ?? new PlayerProfile());
        }

        public Task SaveProfileAsync(PlayerProfile profile)
        {
            RequireSignedIn();
            Directory.CreateDirectory(Folder);
            // Write then move, so a crash mid-write never leaves a broken profile.
            var path = ProfilePath(Username);
            File.WriteAllText(path + ".tmp", JsonUtility.ToJson(profile, prettyPrint: true));
            if (File.Exists(path)) File.Delete(path);
            File.Move(path + ".tmp", path);
            return Task.CompletedTask;
        }

        void RequireSignedIn()
        {
            if (Username == null) throw new AccountException("Not signed in.");
        }

        static string Hash(string salt, string password)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(salt + ":" + (password ?? ""))));
        }
    }
}
