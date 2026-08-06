"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { useAuth } from "@/components/AuthContext";
import { API_BASE } from "@/lib/api";

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
    if (token) {
      login(token);
      router.replace("/repos");
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
    <div className="container" style={{ maxWidth: 420, marginTop: 80 }}>
      <div className="card">
        <h1 style={{ marginTop: 0 }}>Tessera</h1>
        <p className="muted">Architecture knowledge graph for legacy systems. Sign in with GitHub or use the dashboard access key.</p>
        {githubEnabled && (
          <button className="btn" type="button" onClick={startGithub} disabled={checking}>
            Sign in with GitHub
          </button>
        )}
        {githubEnabled && (
          <div style={{ textAlign: "center", color: "var(--muted, #888)", margin: "12px 0", fontSize: 12 }}>or</div>
        )}
        <form onSubmit={submit} style={{ display: "flex", flexDirection: "column", gap: 12 }}>
          <input
            type="password"
            value={key}
            onChange={(e) => setKey(e.target.value)}
            placeholder="Access key"
            autoFocus
          />
          {error && <div className="badge red">{error}</div>}
          <button className="btn primary" type="submit" disabled={checking || !key.trim()}>
            {checking ? "Signing in…" : "Sign in"}
          </button>
        </form>
        <p className="muted" style={{ fontSize: 12 }}>
          The key is configured on the API as <code>Dashboard:ApiKey</code>.
        </p>
      </div>
    </div>
  );
}
