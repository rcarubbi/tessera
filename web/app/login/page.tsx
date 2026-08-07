"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { useAuth } from "@/components/AuthContext";
import { API_BASE } from "@/lib/api";
import { card, field } from "@/lib/ui";

export default function LoginPage() {
  const { login, logout, refreshUser } = useAuth();
  const router = useRouter();
  const [key, setKey] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [checking, setChecking] = useState(false);
  const [githubEnabled, setGithubEnabled] = useState(true);

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const token = params.get("token");
    const oauthError = params.get("error");
    const reason = params.get("reason");
    if (token) {
      login(token);
      router.replace("/repos");
    } else if (oauthError === "oauth_failed") {
      if (reason === "invalid_state") {
        setError("GitHub sign-in session expired or state was invalid. Please try again.");
      } else if (reason) {
        setError(`GitHub sign-in failed: ${reason}`);
      } else {
        setError("GitHub sign-in failed. Please try again or use the access key.");
      }
    }
    fetch(`${API_BASE}/api/auth/config`)
      .then((r) => (r.ok ? r.json() : null))
      .then((cfg) => {
        if (cfg) setGithubEnabled(cfg.githubEnabled === true);
      })
      .catch(() => setGithubEnabled(false));
  }, [login, router]);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!key.trim()) return;
    setChecking(true);
    setError(null);
    login(key.trim());
    const user = await refreshUser();
    if (!user) {
      logout();
      setError("Invalid access key.");
      setChecking(false);
      return;
    }
    router.replace("/repos");
    setChecking(false);
  };

  const startGithub = () => {
    window.location.href = `${API_BASE}/api/auth/login`;
  };

  return (
    <div className="flex min-h-screen items-center justify-center bg-bg px-5">
      <div className="w-full max-w-[400px]">
        <div className="mb-6 text-center">
          <div className="mx-auto mb-3 flex h-12 w-12 items-center justify-center rounded-xl bg-accent/15 text-2xl font-black text-accent">
            T
          </div>
          <h1 className="text-2xl font-bold">Tessera</h1>
          <p className="mt-1 text-sm text-dim">
            Architecture knowledge graph for legacy systems.
          </p>
        </div>

        <div className={`${card} flex flex-col gap-4`}>
          {githubEnabled && (
            <>
              <button
                className="inline-flex h-10 w-full cursor-pointer items-center justify-center gap-2 rounded-lg border border-border bg-inset px-4 text-sm font-medium text-fg transition-colors select-none hover:border-accent disabled:cursor-not-allowed disabled:opacity-50"
                type="button"
                onClick={startGithub}
                disabled={checking}
              >
                <svg viewBox="0 0 16 16" width="16" height="16" fill="currentColor" aria-hidden="true">
                  <path d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27s1.36.09 2 .27c1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.01 8.01 0 0 0 16 8c0-4.42-3.58-8-8-8Z" />
                </svg>
                Sign in with GitHub
              </button>
              <div className="flex items-center gap-3 text-xs text-dim">
                <span className="h-px flex-1 bg-border" />
                or with access key
                <span className="h-px flex-1 bg-border" />
              </div>
            </>
          )}

          <form onSubmit={submit} className="flex flex-col gap-3">
            <div>
              <label htmlFor="key" className="mb-1 block text-xs font-medium text-dim">
                Dashboard access key
              </label>
              <input
                id="key"
                className={`${field} ${error ? "border-danger/60" : ""}`}
                type="password"
                value={key}
                onChange={(e) => {
                  setKey(e.target.value);
                  if (error) setError(null);
                }}
                placeholder="Enter your access key"
                autoFocus
              />
            </div>
            {error && <div className="rounded-lg border border-danger/30 bg-danger/10 px-3 py-2 text-xs text-danger">{error}</div>}
            <button
              className="inline-flex h-10 w-full cursor-pointer items-center justify-center gap-2 rounded-lg border border-accent bg-accent px-4 text-sm font-semibold text-bg transition-colors select-none hover:bg-accent disabled:cursor-not-allowed disabled:opacity-50"
              type="submit"
              disabled={checking || !key.trim()}
            >
              {checking ? "Signing in…" : "Sign in"}
            </button>
          </form>
        </div>

        <p className="mt-4 text-center text-xs text-dim">
          The key is configured on the API as <code>Dashboard:ApiKey</code>.
        </p>
      </div>
    </div>
  );
}
