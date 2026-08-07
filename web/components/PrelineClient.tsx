"use client";

import { useEffect } from "react";
import { usePathname } from "next/navigation";

export default function PrelineClient() {
  const pathname = usePathname();

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const { HSStaticMethods } = await import("preline/non-auto");
      if (cancelled) return;
      HSStaticMethods.autoInit();
    })();
    return () => {
      cancelled = true;
    };
  }, [pathname]);

  return null;
}
