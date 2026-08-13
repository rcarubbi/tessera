"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { TopBar } from "@/components/TopBar";
import { useAuth } from "@/components/AuthContext";
import { apiGet, apiPost, apiPut, apiDelete, ApiError } from "@/lib/api";
import { AI_PRESETS } from "@/lib/aiPresets";
import type { AiSettings, AiSettingsList, AiSettingsRequest } from "@/lib/types";
import { badge, badgeGreen, btn, btnDanger, btnPrimary, btnSmall, card, cardError, field } from "@/lib/ui";

export default function SettingsPage() {
  const { token, user, logout } = useAuth();
  const router = useRouter();

  const [providers, setProviders] = useState<AiSettings[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const [editing, setEditing] = useState<AiSettings | null>(null);
  const [providerName, setProviderName] = useState("");
  const [baseUrl, setBaseUrl] = useState("");
  const [model, setModel] = useState("");
  const [apiKey, setApiKey] = useState("");
  const [embeddingModel, setEmbeddingModel] = useState("");

  const resetForm = useCallback(() => {
    setEditing(null);
    setProviderName("");
    setBaseUrl("");
    setModel("");
    setApiKey("");
    setEmbeddingModel("");
  }, []);

  const load = useCallback(() => {
    setError(null);
    apiGet<AiSettingsList>("/api/settings/ai")
      .then((list) => setProviders(list.providers))
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
    const preset = AI_PRESETS.find((p) => p.name === value);
    if (preset) {
      setBaseUrl(preset.baseUrl);
      setModel(preset.defaultModel);
    }
  };

  const handleEdit = (p: AiSettings) => {
    setEditing(p);
    setProviderName(p.providerName);
    setBaseUrl(p.baseUrl);
    setModel(p.model);
    setApiKey("");
    setEmbeddingModel(p.embeddingModel ?? "");
  };

  const handleSave = async () => {
    setError(null);
    setSaved(null);
    const request: AiSettingsRequest = {
      providerName,
      baseUrl,
      model,
      embeddingModel: embeddingModel.trim() ? embeddingModel.trim() : null,
      apiKey: apiKey.trim() ? apiKey.trim() : null,
      isPrimary: editing ? editing.isPrimary : providers.length === 0,
    };
    setSaving(true);
    try {
      await apiPut<AiSettings>("/api/settings/ai", request);
      setSaved("AI settings saved. Chat and analysis will use the new provider within seconds.");
      setApiKey("");
      resetForm();
      load();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Failed to save settings.");
    } finally {
      setSaving(false);
    }
  };

  const handleMakePrimary = async (p: AiSettings) => {
    setError(null);
    try {
      await apiPost(`/api/settings/ai/${encodeURIComponent(p.providerName)}/primary`);
      load();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Failed to set primary provider.");
    }
  };

  const handleDelete = async (p: AiSettings) => {
    setError(null);
    try {
      await apiDelete(`/api/settings/ai/${encodeURIComponent(p.providerName)}`);
      if (editing?.providerName === p.providerName) resetForm();
      load();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Failed to delete provider.");
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
            <p className="mt-1 text-sm text-dim">Configure which LLMs power chat and code analysis.</p>
          </div>

          {loading && <div className="text-dim">Loading settings…</div>}

          {!loading && (
            <div className="flex flex-col gap-5">
              {!isAdmin && (
                <div className="rounded-lg border border-border bg-inset px-3 py-2 text-[13px] text-dim">
                  Only administrators can change AI settings.
                </div>
              )}

              {error && <div className={`${card} ${cardError} text-danger`}>{error}</div>}
              {saved && <div className="rounded-lg border border-good/40 bg-good/10 px-3 py-2 text-[13px] text-good">{saved}</div>}

              <div className="flex flex-col gap-2">
                <h2 className="text-sm font-semibold text-dim">Configured providers</h2>
                {providers.length === 0 && (
                  <div className="rounded-lg border border-dashed border-border px-3 py-3 text-[13px] text-dim">
                    No providers configured yet. Add one below.
                  </div>
                )}
                {providers.map((p) => (
                  <div
                    key={p.providerName}
                    className="flex items-center justify-between gap-3 rounded-lg border border-border bg-inset px-3 py-2"
                  >
                    <div className="min-w-0">
                      <div className="flex items-center gap-2">
                        <span className="truncate font-medium">{p.providerName}</span>
                        {p.isPrimary && <span className={`${badge} ${badgeGreen}`}>primary</span>}
                      </div>
                      <div className="truncate text-xs text-dim">
                        {p.model} · {p.baseUrl}
                        {p.embeddingModel ? ` · emb: ${p.embeddingModel}` : ""}
                      </div>
                    </div>
                    <div className="flex shrink-0 items-center gap-2">
                      {!p.isPrimary && (
                        <button
                          className={`${btn} ${btnSmall}`}
                          disabled={!isAdmin}
                          onClick={() => handleMakePrimary(p)}
                        >
                          Set primary
                        </button>
                      )}
                      <button className={`${btn} ${btnSmall}`} disabled={!isAdmin} onClick={() => handleEdit(p)}>
                        Edit
                      </button>
                      <button
                        className={`${btn} ${btnSmall} ${btnDanger}`}
                        disabled={!isAdmin}
                        onClick={() => handleDelete(p)}
                      >
                        Delete
                      </button>
                    </div>
                  </div>
                ))}
              </div>

              <div className={`${card} flex flex-col gap-4`}>
                <h2 className="text-sm font-semibold text-dim">
                  {editing ? `Edit provider: ${editing.providerName}` : "Add a provider"}
                </h2>

                {editing ? (
                  <div className="flex items-center gap-2 text-sm">
                    <span className="text-dim">Provider</span>
                    <span className="font-medium">{editing.providerName}</span>
                    <button className={`${btn} ${btnSmall}`} onClick={resetForm}>
                      New provider
                    </button>
                  </div>
                ) : (
                  <label className="flex flex-col gap-1.5 text-sm">
                    <span className="text-dim">Provider</span>
                    <select
                      className={field}
                      value={providerName}
                      onChange={(e) => handleProviderChange(e.target.value)}
                    >
                      <option value="" disabled>
                        Select a provider…
                      </option>
                      {AI_PRESETS.map((p) => (
                        <option key={p.name} value={p.name}>
                          {p.label}
                        </option>
                      ))}
                    </select>
                  </label>
                )}

                <label className="flex flex-col gap-1.5 text-sm">
                  <span className="text-dim">Base URL</span>
                  <input
                    className={field}
                    value={baseUrl}
                    onChange={(e) => setBaseUrl(e.target.value)}
                    placeholder="https://api.openai.com/v1"
                  />
                </label>

                <label className="flex flex-col gap-1.5 text-sm">
                  <span className="text-dim">Model</span>
                  <input
                    className={field}
                    value={model}
                    onChange={(e) => setModel(e.target.value)}
                    placeholder="gpt-4o-mini"
                  />
                </label>

                <label className="flex flex-col gap-1.5 text-sm">
                  <span className="flex items-center gap-2 text-dim">
                    API key
                    {editing?.hasApiKey && (
                      <span className={`${badge} ${badgeGreen}`}>stored · {editing.apiKeyMasked}</span>
                    )}
                  </span>
                  <input
                    className={field}
                    type="password"
                    value={apiKey}
                    onChange={(e) => setApiKey(e.target.value)}
                    placeholder={
                      editing?.hasApiKey
                        ? "Leave blank to keep the existing key"
                        : "Optional — local bridges (e.g. Copilot CLI) accept any key"
                    }
                    autoComplete="new-password"
                  />
                </label>

                <label className="flex flex-col gap-1.5 text-sm">
                  <span className="text-dim">Embedding model (optional)</span>
                  <input
                    className={field}
                    value={embeddingModel}
                    onChange={(e) => setEmbeddingModel(e.target.value)}
                    placeholder="e.g. text-embedding-3-small for Copilot; enables semantic retrieval"
                  />
                </label>

                {editing?.updatedAt && (
                  <div className="text-xs text-dim">Last updated {new Date(editing.updatedAt).toLocaleString()}</div>
                )}

                <div className="flex items-center gap-3 border-t border-border pt-4">
                  <button
                    className={`${btn} ${btnPrimary}`}
                    disabled={!isAdmin || saving || !providerName || !baseUrl || !model}
                    onClick={handleSave}
                  >
                    {saving ? "Saving…" : editing ? "Save provider" : "Add provider"}
                  </button>
                </div>
              </div>
            </div>
          )}
        </div>
      </main>
    </div>
  );
}
