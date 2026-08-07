"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useAuth } from "@/components/AuthContext";
import { badge, btn, btnSmall } from "@/lib/ui";

export function TopBar() {
  const { token, user, logout, hydrated } = useAuth();
  const pathname = usePathname();
  const authed = hydrated && !!token;

  return (
    <header className="sticky top-0 z-20 border-b border-border bg-panel/95 backdrop-blur">
      <div className="mx-auto flex h-12 max-w-[1400px] items-center gap-4 px-5">
        <Link href={authed ? "/repos" : "/login"} className="flex items-center gap-2 text-fg no-underline">
          <span className="flex h-6 w-6 items-center justify-center rounded-md bg-accent text-xs font-black text-bg">
            T
          </span>
          <span className="text-sm font-bold">Tessera</span>
        </Link>
        {authed && (
          <nav className="flex items-center gap-1">
            <Link
              href="/repos"
              className={`rounded-md px-2.5 py-1.5 text-sm transition-colors ${
                pathname === "/repos" || pathname.startsWith("/repos/")
                  ? "bg-inset text-fg"
                  : "text-dim hover:text-fg"
              }`}
            >
              Repositories
            </Link>
            <Link
              href="/settings"
              className={`rounded-md px-2.5 py-1.5 text-sm transition-colors ${
                pathname === "/settings" || pathname.startsWith("/settings/")
                  ? "bg-inset text-fg"
                  : "text-dim hover:text-fg"
              }`}
            >
              Settings
            </Link>
          </nav>
        )}
        <div className="flex-1" />
        {authed && (
          <>
            {user && (
              <span className="flex items-center gap-2 text-[13px]">
                {user.avatarUrl && (
                  <img src={user.avatarUrl} width={24} height={24} alt="" className="h-6 w-6 rounded-full object-cover" />
                )}
                <span className="hidden sm:inline">{user.name || user.login}</span>
                {user.isAdmin && <span className={badge}>admin</span>}
              </span>
            )}
            <button type="button" className={`${btn} ${btnSmall}`} onClick={logout}>
              Sign out
            </button>
          </>
        )}
      </div>
    </header>
  );
}
