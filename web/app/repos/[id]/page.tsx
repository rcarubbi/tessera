import RepoHub from "@/components/RepoHub";

export default async function RepoPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <RepoHub repoId={id} />;
}
