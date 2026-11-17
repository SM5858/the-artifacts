using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Models.Messages;
using TheArtifacts.Core.Guardian;

namespace GuardianModule
{
    /// <summary>
    /// Claude Haiku 5.5 behind <see cref="IGuardianModel"/> (Configuration Document 2.7):
    /// thinking disabled, 300 output tokens, one forced strict tool call, no SDK retries.
    /// </summary>
    public sealed class ClaudeGuardianModel : IGuardianModel
    {
        public const int MaxOutputTokens = 300;

        readonly AnthropicClient _client;

        public ClaudeGuardianModel(string apiKey)
        {
            _client = new AnthropicClient { ApiKey = apiKey, MaxRetries = 0 };
        }

        public string ModelId => "claude-haiku-5-5";

        public async Task<ModelAnswer> ProposeAsync(GuardianRequest request, CancellationToken cancellationToken)
        {
            // Pass the whole schema through, so strict mode sees additionalProperties: false and the enums.
            var rawSchema = request.ToolInputSchema
                .ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value));

            var parameters = new MessageCreateParams
            {
                Model = ModelId,
                MaxTokens = MaxOutputTokens,
                Thinking = new ThinkingConfigDisabled(),
                System = request.SystemPrompt,
                Tools =
                [
                    new Tool
                    {
                        Name = GuardianRequest.ToolName,
                        Description = request.ToolDescription,
                        Strict = true,
                        InputSchema = InputSchema.FromRawUnchecked(rawSchema),
                    },
                ],
                ToolChoice = new ToolChoiceTool { Name = GuardianRequest.ToolName },
                Messages = [new UserMessageParam { Content = request.UserMessage }],
            };

            var response = await _client.Messages.Create(parameters, cancellationToken).ConfigureAwait(false);

            GuardianSetup proposal = null;
            foreach (var block in response.Content)
            {
                if (block.TryPickToolUse(out ToolUseBlock toolUse) && toolUse.Name == GuardianRequest.ToolName)
                {
                    proposal = Parse(toolUse.Input);
                    break;
                }
            }

            return new ModelAnswer
            {
                // No tool call (e.g. a refusal) is a malformed answer, not a missing service.
                Proposal = proposal ?? new GuardianSetup(null, null, null),
                InputTokens = response.Usage.InputTokens,
                OutputTokens = response.Usage.OutputTokens,
                StopReason = response.StopReason?.ToString(),
            };
        }

        /// <summary>Reads the tool input; any wrong type leaves that field null so the validator reports Malformed.</summary>
        public static GuardianSetup Parse(IReadOnlyDictionary<string, JsonElement> input)
        {
            string Str(string key) =>
                input.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            List<string> skills = null;
            if (input.TryGetValue("skill_ids", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                skills = arr.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : null)
                    .ToList();
            }

            return new GuardianSetup(Str("stat_tier"), skills, Str("strategy_id"));
        }
    }
}
