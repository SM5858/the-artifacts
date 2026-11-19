using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TheArtifacts.Core.Combat;
using TheArtifacts.Core.Guardian;

namespace GuardianModule
{
    /// <summary>
    /// Everything sent to the model for one attempt: the system prompt, the user message
    /// (allowed challenger snapshot + allowed options), and the tool schema with enums filled in.
    /// Built only from the allowed snapshot fields (Configuration Document 2.2).
    /// </summary>
    public sealed class GuardianRequest
    {
        public const string ToolName = "configure_guardian";

        public string SystemPrompt { get; }
        public string UserMessage { get; }
        public JsonObject ToolInputSchema { get; }
        public string ToolDescription { get; }
        public string SnapshotHash { get; }
        public AllowedSetupSpace Space { get; }

        GuardianRequest(string system, string user, JsonObject schema, string description, AllowedSetupSpace space)
        {
            SystemPrompt = system;
            UserMessage = user;
            ToolInputSchema = schema;
            ToolDescription = description;
            Space = space;
            SnapshotHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user))).ToLowerInvariant()[..16];
        }

        /// <summary>The allowed snapshot for a challenger wearing these artifacts.</summary>
        public static ChallengerSnapshot Snapshot(PlayerBuild build, int attemptsOnThisGuardian) =>
            new ChallengerSnapshot(
                new Dictionary<string, int>
                {
                    ["max_hp"] = (int)Math.Round(build.MaxHp),
                    ["max_mp"] = (int)Math.Round(build.MaxMp),
                    ["strength"] = (int)Math.Round(build.Strength),
                    ["intelligence"] = (int)Math.Round(build.Intelligence),
                    ["agility"] = (int)Math.Round(build[StatKind.Agility]),
                    ["def"] = (int)Math.Round(build[StatKind.Def]),
                    ["res"] = (int)Math.Round(build[StatKind.Res]),
                    ["precision"] = (int)Math.Round(build[StatKind.Precision]),
                    ["speed"] = (int)Math.Round(build[StatKind.Speed]),
                },
                new[] { "heavy_strike", "guard", "lunge", "punish", "lightning_bolt" },
                build.Artifacts.Select(a => a.Id),
                attemptsOnThisGuardian);

        public static GuardianRequest Build(ChallengerSnapshot snapshot, AllowedSetupSpace space)
        {
            var equipped = snapshot.EquippedArtifactIds
                .Select(PrototypeContent.Artifact)
                .Select(a => new { id = a.Id, name = a.Name, slot = a.Slot.ToString().ToLowerInvariant(), rarity = a.Rarity.ToString().ToLowerInvariant() });

            // Tiers in content order (weakest first), restricted to what this challenger may face.
            var tiers = PrototypeContent.Tiers.Where(t => space.StatTiers.Contains(t.Id)).ToArray();
            var skills = PrototypeContent.Skills.Where(s => space.SkillPool.Contains(s.Id)).ToArray();
            var strategies = PrototypeContent.StrategyIds.Where(space.Strategies.Contains).ToArray();

            var message = new
            {
                challenger = new
                {
                    stats = snapshot.Stats,
                    abilities = snapshot.SkillIds,
                    equipped_artifacts = equipped,
                    attempts_on_this_guardian = snapshot.AttemptsOnThisGuardian,
                },
                allowed = new
                {
                    stat_tiers = tiers.Select(t => new { id = t.Id, max_hp = t.MaxHp, damage_multiplier = t.DamageMultiplier, def = t.Def, res = t.Res }),
                    skills = skills.Select(s => new { id = s.Id, name = s.Name, tell_seconds = s.Tell, base_damage = s.BaseDamage, damage_type = s.DamageType.ToString().ToLowerInvariant(), recovery_seconds = s.Recovery, avoid = HowToAvoid(s) }),
                    strategies = strategies.Select(id => new { id, behavior = StrategyBehavior(id) }),
                    min_skills = space.MinSkills,
                    max_skills = space.MaxSkills,
                },
            };
            var user = JsonSerializer.Serialize(message);

            var tool = JsonNode.Parse(ReadResource("Guardian.configure_guardian.schema.json"))!.AsObject();
            var schema = tool["input_schema"]!.AsObject();
            var props = schema["properties"]!.AsObject();
            props["stat_tier"]!["enum"] = new JsonArray(tiers.Select(t => (JsonNode)t.Id).ToArray());
            props["skill_ids"]!["items"]!["enum"] = new JsonArray(skills.Select(s => (JsonNode)s.Id).ToArray());
            props["strategy_id"]!["enum"] = new JsonArray(strategies.Select(s => (JsonNode)s).ToArray());

            return new GuardianRequest(ReadResource("Guardian.system_prompt.md"), user,
                (JsonObject)schema.DeepClone(), tool["description"]!.GetValue<string>(), space);
        }

        static string HowToAvoid(GuardianSkillDef s) => s.Id switch
        {
            "cleave" => "step to the side or behind",
            "slam" => "run out of the circle",
            "charge" => "sidestep the line",
            "rings" => "move in close to the Guardian",
            "embers" => "keep moving",
            "fury" => "guard and back off while the Guardian's attacks are stronger",
            _ => "",
        };

        static string StrategyBehavior(string id) => id switch
        {
            "steady" => "one skill every 8 s in a fixed rotation (most predictable)",
            "aggressive" => "one skill every 5.5 s, favoring Cleave and Charge",
            "combo" => "every 12 s, two skills back to back",
            "reactive" => "every 7 s, picks by distance to the player",
            _ => "",
        };

        static string ReadResource(string name)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Missing embedded resource {name}.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
