"use client";

import { createContext, useContext, useMemo, useState, type ReactNode } from "react";
import { clearToken, getToken, setToken } from "@/lib/api";

type AuthContextValue = {
  token: string | null;
  login: (token: string) => void;
  logout: () => void;
};

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [token, setTokenState] = useState<string | null>(() => getToken());

  const value = useMemo<AuthContextValue>(
    () => ({
      token,
      login: (t: string) => {
        setToken(t);
        setTokenState(t);
      },
      logout: () => {
        clearToken();
        setTokenState(null);
      },
    }),
    [token],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within AuthProvider");
  return ctx;
}
