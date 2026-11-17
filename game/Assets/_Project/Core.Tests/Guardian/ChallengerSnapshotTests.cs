using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TheArtifacts.Core.Guardian;

namespace TheArtifacts.Core.Tests.Guardian
{
    public class ChallengerSnapshotTests
    {
        // Configuration Document 2.2: the allowed challenger snapshot, and nothing else.
        static readonly string[] AllowedProperties =
        {
            nameof(ChallengerSnapshot.Stats),
            nameof(ChallengerSnapshot.SkillIds),
            nameof(ChallengerSnapshot.EquippedArtifactIds),
            nameof(ChallengerSnapshot.AttemptsOnThisGuardian),
        };

        [Test]
        public void Snapshot_ExposesOnlyAllowedFields()
        {
            var members = typeof(ChallengerSnapshot)
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.MemberType == MemberTypes.Property || m.MemberType == MemberTypes.Field)
                .Select(m => m.Name);

            Assert.That(members, Is.EquivalentTo(AllowedProperties),
                "Adding a field to ChallengerSnapshot sends it to the Guardian model. " +
                "Update Configuration Document 2.2 first, then this test.");
        }

        [Test]
        public void Snapshot_CopiesInputs()
        {
            var skills = new[] { "skill-a" };
            var snapshot = new ChallengerSnapshot(null, skills, null, 2);
            skills[0] = "changed";

            Assert.That(snapshot.SkillIds, Is.EqualTo(new[] { "skill-a" }));
            Assert.That(snapshot.Stats, Is.Empty);
            Assert.That(snapshot.EquippedArtifactIds, Is.Empty);
            Assert.That(snapshot.AttemptsOnThisGuardian, Is.EqualTo(2));
        }
    }
}
