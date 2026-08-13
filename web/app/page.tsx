"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { useAuth } from "@/components/AuthContext";

export default function Home() {
  const { user, hydrated } = useAuth();
  const router = useRouter();

  useEffect(() => {
    if (hydrated) router.replace(user ? "/repos" : "/login");
  }, [user, hydrated, router]);

  return (
    <div className="mx-auto max-w-[1400px] px-5 py-5">
      <div className="text-dim">Loading…</div>
    </div>
  );
}
