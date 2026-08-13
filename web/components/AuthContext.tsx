"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { apiGet, apiPost } from "@/lib/api";

export type AuthUser = {
  login: string;
  name: string;
  avatarUrl: string;
  isAdmin: boolean;
};

type AuthContextValue = {
  user: AuthUser | null;
  hydrated: boolean;
  login: (apiKey: string) => Promise<boolean>;
  logout: () => void;
  refreshUser: () => Promise<AuthUser | null>;
};

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [hydrated, setHydrated] = useState(false);

  const refreshUser = useCallback(async (): Promise<AuthUser | null> => {
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
    let cancelled = false;
    void refreshUser().finally(() => {
      if (!cancelled) setHydrated(true);
    });
    return () => {
      cancelled = true;
    };
  }, [refreshUser]);

  const login = useCallback(
    async (apiKey: string): Promise<boolean> => {
      try {
        await apiPost<{ ok: boolean }>("/api/auth/key", { apiKey });
      } catch {
        return false;
      }
      return (await refreshUser()) !== null;
    },
    [refreshUser],
  );

  const logout = useCallback(() => {
    void apiPost<{ status: string }>("/api/auth/logout").catch(() => {});
    setUser(null);
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({ user, hydrated, login, logout, refreshUser }),
    [user, hydrated, login, logout, refreshUser],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within AuthProvider");
  return ctx;
}
