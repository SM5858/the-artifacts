# Guardian extension artifact

Planned location of the adaptive Guardian's executable configuration (Configuration Document, Section 2.3):

- `manifest.json` — common manifest fields (Configuration Document C1)
- `system_prompt.md` — system instructions for `configure_guardian`
- `configure_guardian.schema.json` — the single tool the model may call

The model is called server-side only; API keys never go in this repository or in the game client.
