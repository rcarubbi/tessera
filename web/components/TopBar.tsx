"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useAuth } from "@/components/AuthContext";

export function TopBar() {
  const { token, logout } = useAuth();
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
          <button className="btn small" onClick={logout}>
            Sign out
          </button>
        </>
      )}
    </div>
  );
}
