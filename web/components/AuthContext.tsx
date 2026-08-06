"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { API_BASE, apiGet, clearToken, getToken, setToken } from "@/lib/api";

export type AuthUser = {
  login: string;
  name: string;
  avatarUrl: string;
  isAdmin: boolean;
};

type AuthContextValue = {
  token: string | null;
  user: AuthUser | null;
  login: (token: string) => void;
  logout: () => void;
  refreshUser: () => Promise<AuthUser | null>;
};

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [token, setTokenState] = useState<string | null>(() => getToken());
  const [user, setUser] = useState<AuthUser | null>(null);

  const refreshUser = useCallback(async (): Promise<AuthUser | null> => {
    const current = getToken();
    if (!current) {
      setUser(null);
      return null;
    }
    try {
      const u = await apiGet<AuthUser>("/api/auth/me");
      setUser(u);
      return u;
    } catch {
      setUser(null);
      return null;
    }
  }, []);

  useEffect(() => {
    if (token) void refreshUser();
  }, [token, refreshUser]);

  const value = useMemo<AuthContextValue>(
    () => ({
      token,
      user,
      login: (t: string) => {
        setToken(t);
        setTokenState(t);
      },
      logout: () => {
        const t = getToken();
        if (t) {
          fetch(`${API_BASE}/api/auth/logout`, {
            method: "POST",
            headers: { Authorization: `Bearer ${t}` },
          }).catch(() => {});
        }
        clearToken();
        setTokenState(null);
        setUser(null);
      },
      refreshUser,
    }),
    [token, user, refreshUser],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within AuthProvider");
  return ctx;
}
