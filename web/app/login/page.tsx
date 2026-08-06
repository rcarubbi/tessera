"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useAuth } from "@/components/AuthContext";

export default function LoginPage() {
  const { login } = useAuth();
  const router = useRouter();
  const [key, setKey] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [checking, setChecking] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!key.trim()) return;
    setChecking(true);
    setError(null);
    login(key.trim());
    router.replace("/repos");
    setChecking(false);
  };

  return (
    <div className="container" style={{ maxWidth: 420, marginTop: 80 }}>
      <div className="card">
        <h1 style={{ marginTop: 0 }}>Tessera</h1>
        <p className="muted">Architecture knowledge graph for legacy systems. Enter the dashboard access key to continue.</p>
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
