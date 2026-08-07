"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { TopBar } from "@/components/TopBar";
import { useAuth } from "@/components/AuthContext";
import { apiGet, apiPut, ApiError } from "@/lib/api";
import { mergePresets } from "@/lib/aiPresets";
import type { AiSettings, AiSettingsRequest } from "@/lib/types";

export default function SettingsPage() {
  const { token, user, logout } = useAuth();
  const router = useRouter();

  const [settings, setSettings] = useState<AiSettings | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const [providerName, setProviderName] = useState("");
  const [baseUrl, setBaseUrl] = useState("");
  const [model, setModel] = useState("");
  const [apiKey, setApiKey] = useState("");
  const [fallback, setFallback] = useState("");

  const presets = useMemo(
    () => mergePresets(settings?.availableProviders ?? []),
    [settings],
  );

  const load = useCallback(() => {
    setError(null);
    apiGet<AiSettings>("/api/settings/ai")
      .then((s) => {
        setSettings(s);
        setProviderName(s.providerName);
        setBaseUrl(s.baseUrl);
        setModel(s.model);
        setFallback(s.fallbackProviderName ?? "");
      })
      .catch((e) => {
        if (e instanceof ApiError && e.status === 401) {
          logout();
          router.replace("/login");
          return;
        }
        setError(e.message);
      })
      .finally(() => setLoading(false));
  }, [logout, router]);

  useEffect(() => {
    if (!token) {
      router.replace("/login");
      return;
    }
    load();
  }, [token, router, load]);

  const handleProviderChange = (value: string) => {
    setProviderName(value);
    const preset = presets.find((p) => p.name === value);
    if (preset) {
      setBaseUrl(preset.baseUrl);
      setModel(preset.defaultModel);
    }
  };

  const handleSave = async () => {
    setError(null);
    setSaved(null);
    const request: AiSettingsRequest = {
      providerName,
      baseUrl,
      model,
      fallbackProviderName: fallback || null,
      apiKey: apiKey.trim() ? apiKey.trim() : null,
    };
    setSaving(true);
    try {
      const updated = await apiPut<AiSettings>("/api/settings/ai", request);
      setSettings(updated);
      setApiKey("");
      setSaved("AI settings saved. Chat and analysis will use the new provider within seconds.");
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Failed to save settings.");
    } finally {
      setSaving(false);
    }
  };

  const isAdmin = user?.isAdmin === true;

  return (
    <div className="app-shell">
      <TopBar />
      <main className="app-main">
        <div className="mx-auto max-w-[680px] px-5 py-6">
          <div className="mb-6">
            <h1 className="text-2xl font-bold">Settings</h1>
            <p className="mt-1 text-sm text-dim">Configure which LLM powers chat and code analysis.</p>
          </div>

          {loading && <div className="text-dim">Loading settings…</div>}

          {!loading && (
            <div className="card flex flex-col gap-4">
              {!isAdmin && (
                <div className="rounded-md border border-border bg-inset px-3 py-2 text-[13px] text-dim">
                  Only administrators can change AI settings.
                </div>
              )}

              {error && <div className="card card-error text-danger">{error}</div>}
              {saved && <div className="rounded-md border border-good/40 bg-good/10 px-3 py-2 text-[13px] text-good">{saved}</div>}

              <label className="flex flex-col gap-1.5 text-sm">
                <span className="text-dim">Provider</span>
                <select
                  className="field"
                  value={providerName}
                  onChange={(e) => handleProviderChange(e.target.value)}
                >
                  <option value="" disabled>
                    Select a provider…
                  </option>
                  {presets.map((p) => (
                    <option key={p.name} value={p.name}>
                      {p.label}
                    </option>
                  ))}
                </select>
              </label>

              <label className="flex flex-col gap-1.5 text-sm">
                <span className="text-dim">Base URL</span>
                <input
                  className="field"
                  value={baseUrl}
                  onChange={(e) => setBaseUrl(e.target.value)}
                  placeholder="https://api.openai.com/v1"
                />
              </label>

              <label className="flex flex-col gap-1.5 text-sm">
                <span className="text-dim">Model</span>
                <input
                  className="field"
                  value={model}
                  onChange={(e) => setModel(e.target.value)}
                  placeholder="gpt-4o-mini"
                />
              </label>

              <label className="flex flex-col gap-1.5 text-sm">
                <span className="flex items-center gap-2 text-dim">
                  API key
                  {settings?.hasApiKey && <span className="badge badge-green">stored · {settings.apiKeyMasked}</span>}
                </span>
                <input
                  className="field"
                  type="password"
                  value={apiKey}
                  onChange={(e) => setApiKey(e.target.value)}
                  placeholder={settings?.hasApiKey ? "Leave blank to keep the existing key" : "Enter API key"}
                  autoComplete="new-password"
                />
              </label>

              <label className="flex flex-col gap-1.5 text-sm">
                <span className="text-dim">Fallback provider (optional)</span>
                <select
                  className="field"
                  value={fallback}
                  onChange={(e) => setFallback(e.target.value)}
                >
                  <option value="">None</option>
                  {settings?.availableProviders.map((p) => (
                    <option key={p.name} value={p.name}>
                      {p.name}
                    </option>
                  ))}
                </select>
              </label>

              {settings?.updatedAt && (
                <div className="text-xs text-dim">
                  Last updated {new Date(settings.updatedAt).toLocaleString()}
                </div>
              )}

              <div className="flex items-center gap-3 border-t border-border pt-4">
                <button
                  className="btn btn-primary"
                  disabled={!isAdmin || saving || !providerName || !baseUrl || !model}
                  onClick={handleSave}
                >
                  {saving ? "Saving…" : "Save settings"}
                </button>
              </div>
            </div>
          )}
        </div>
      </main>
    </div>
  );
}
