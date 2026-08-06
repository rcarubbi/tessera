"use client";

import { useEffect, useState } from "react";
import { TopBar } from "@/components/TopBar";
import RepoHub from "@/components/RepoHub";

export default async function RepoPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return (
    <>
      <TopBar />
      <RepoHub repoId={id} />
    </>
  );
}
