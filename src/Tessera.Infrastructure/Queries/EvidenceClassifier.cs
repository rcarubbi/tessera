using Tessera.Domain.Entities;
using Tessera.Domain.Enums;

namespace Tessera.Infrastructure.Queries;

public sealed record EvidenceClassification(string Classification, string FactSource, string Tier);

public static class EvidenceClassifier
{
    public const double VerifiedThreshold = 0.9;
    public const double AcceptedThreshold = 0.7;

    public static EvidenceClassification ClassifyNode(KnowledgeNode node) =>
        ClassifyNode(node.Model, node.Confidence, node.ReviewStatus);

    public static EvidenceClassification ClassifyEdge(GraphEdge edge) =>
        ClassifyEdge(edge.IsStatic, edge.Confidence);

    public static EvidenceClassification ClassifyNode(string? model, double confidence, ReviewStatus reviewStatus)
    {
        var isInference = !string.IsNullOrEmpty(model) || confidence < 1.0;
        return new EvidenceClassification(
            isInference ? "inference" : "fact",
            isInference ? "Inference" : "AST",
            TierLabel(TierFor(confidence, reviewStatus)));
    }

    public static EvidenceClassification ClassifyEdge(bool isStatic, double confidence)
    {
        var isFact = isStatic && confidence >= 1.0;
        return new EvidenceClassification(
            isFact ? "fact" : "inference",
            isFact ? "AST" : "Inference",
            TierLabel(TierFor(confidence, null)));
    }

    public static string TierLabel(ConfidenceTier tier) => tier switch
    {
        ConfidenceTier.Verified => "verified",
        ConfidenceTier.Accepted => "accepted",
        _ => "low-confidence"
    };

    private static ConfidenceTier TierFor(double confidence, ReviewStatus? reviewStatus)
    {
        if (reviewStatus == ReviewStatus.Accepted || confidence >= VerifiedThreshold)
        {
            return ConfidenceTier.Verified;
        }
        if (confidence >= AcceptedThreshold)
        {
            return ConfidenceTier.Accepted;
        }
        return ConfidenceTier.LowConfidence;
    }
}
