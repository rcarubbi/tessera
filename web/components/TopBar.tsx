"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useAuth } from "@/components/AuthContext";

export function TopBar() {
  const { token, user, logout } = useAuth();
  const pathname = usePathname();

  return (
    <div className="topbar">
      <Link href={token ? "/repos" : "/login"} className="brand">
        Tessera
      </Link>
      {token && (
        <>
          <Link href="/repos" className={pathname === "/repos" ? "active" : ""}>
            Repositories
          </Link>
          <div className="spacer" />
          {user && (
            <span style={{ display: "flex", alignItems: "center", gap: 6, fontSize: 13 }}>
              {user.avatarUrl && (
                <img src={user.avatarUrl} width={20} height={20} alt="" style={{ borderRadius: "50%" }} />
              )}
              <span>{user.name}</span>
              {user.isAdmin && <span className="badge">admin</span>}
            </span>
          )}
          <button className="btn small" onClick={logout}>
            Sign out
          </button>
        </>
      )}
    </div>
  );
}
