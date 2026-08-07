import AnalysisTracker from "@/components/AnalysisTracker";

export default async function RepoProgressPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <AnalysisTracker repoId={id} />;
}
