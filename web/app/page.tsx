"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { useAuth } from "@/components/AuthContext";

export default function Home() {
  const { token } = useAuth();
  const router = useRouter();

  useEffect(() => {
    router.replace(token ? "/repos" : "/login");
  }, [token, router]);

  return (
    <div className="container">
      <div className="muted">Loading…</div>
    </div>
  );
}
