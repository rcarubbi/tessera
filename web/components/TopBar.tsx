"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useAuth } from "@/components/AuthContext";

export function TopBar() {
  const { token, user, logout } = useAuth();
  const pathname = usePathname();

  return (
    <header className="flex items-center gap-3 border-b border-border bg-panel px-5 py-2.5">
      <Link href={token ? "/repos" : "/login"} className="text-base font-bold text-fg no-underline">
        Tessera
      </Link>
      {token && (
        <>
          <Link
            href="/repos"
            className={`text-sm ${pathname === "/repos" ? "text-fg" : "text-dim hover:text-fg"}`}
          >
            Repositories
          </Link>
          <div className="flex-1" />
          {user && (
            <span className="flex items-center gap-1.5 text-[13px]">
              {user.avatarUrl && (
                <img src={user.avatarUrl} width={20} height={20} alt="" className="rounded-full" />
              )}
              <span>{user.name}</span>
              {user.isAdmin && <span className="badge">admin</span>}
            </span>
          )}
          <button className="btn btn-small" onClick={logout}>
            Sign out
          </button>
        </>
      )}
    </header>
  );
}
