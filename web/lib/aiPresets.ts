export type AiPreset = {
  name: string;
  label: string;
  baseUrl: string;
  defaultModel: string;
};

export const AI_PRESETS: AiPreset[] = [
  {
    name: "gemini",
    label: "Google Gemini",
    baseUrl: "https://generativelanguage.googleapis.com/v1beta/openai",
    defaultModel: "gemini-3.5-flash-lite",
  },
  {
    name: "openai",
    label: "OpenAI",
    baseUrl: "https://api.openai.com/v1",
    defaultModel: "gpt-4o-mini",
  },
  {
    name: "openrouter",
    label: "OpenRouter",
    baseUrl: "https://openrouter.ai/api/v1",
    defaultModel: "openrouter/auto",
  },
  {
    name: "ollama",
    label: "Ollama (local)",
    baseUrl: "http://localhost:11434/v1",
    defaultModel: "qwen2.5-coder:7b",
  },
  {
    name: "custom",
    label: "Custom (OpenAI-compatible)",
    baseUrl: "",
    defaultModel: "",
  },
];
