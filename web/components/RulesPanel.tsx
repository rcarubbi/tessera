"use client";

import { useCallback, useEffect, useState } from "react";
import { apiGet, apiPut } from "@/lib/api";
import { badge, badgeRed, badgeYellow, card, field, btn, btnPrimary, spinner } from "@/lib/ui";
import type { RuleDrift, RuleEvaluation, RuleSet } from "@/lib/types";

const DEFAULT_YAML = `rules:
  - name: "Domain must not depend on Infrastructure"
    severity: error
    deny:
      from: { path: "src/Tessera.Domain" }
      to: { path: "src/Tessera.Infrastructure" }
`;

function severityBadge(severity: string): string {
  if (severity === "error") return `${badge} ${badgeRed}`;
  if (severity === "warning") return `${badge} ${badgeYellow}`;
  return badge;
}

export default function RulesPanel({
  repoId,
  onOpenDiff,
}: {
  repoId: string;
  onOpenDiff: (from: string, to: string) => void;
}) {
  const [yaml, setYaml] = useState<string | null>(null);
  const [draft, setDraft] = useState("");
  const [saveState, setSaveState] = useState<"idle" | "saving" | "saved">("idle");
  const [saveError, setSaveError] = useState<string | null>(null);
  const [evaluation, setEvaluation] = useState<RuleEvaluation | null>(null);
  const [evaluationLoading, setEvaluationLoading] = useState(true);
  const [evaluationError, setEvaluationError] = useState<string | null>(null);
  const [drift, setDrift] = useState<RuleDrift | null>(null);
  const [driftLoading, setDriftLoading] = useState(true);
  const [driftError, setDriftError] = useState<string | null>(null);

  const loadViolations = useCallback(() => {
    setEvaluationLoading(true);
    setEvaluationError(null);
    apiGet<RuleEvaluation>(`/api/repositories/${repoId}/rules/violations`)
      .then(setEvaluation)
      .catch((e) => setEvaluationError((e as Error).message))
      .finally(() => setEvaluationLoading(false));
  }, [repoId]);

  const loadDrift = useCallback(() => {
    setDriftLoading(true);
    setDriftError(null);
    apiGet<RuleDrift>(`/api/repositories/${repoId}/rules/drift`)
      .then(setDrift)
      .catch((e) => setDriftError((e as Error).message))
      .finally(() => setDriftLoading(false));
  }, [repoId]);

  useEffect(() => {
    let cancelled = false;
    setYaml(null);
    apiGet<RuleSet>(`/api/repositories/${repoId}/rules`)
      .then((ruleSet) => {
        if (cancelled) return;
        setYaml(ruleSet.yaml);
        setDraft(ruleSet.yaml || DEFAULT_YAML);
      })
      .catch((e) => {
        if (!cancelled) setSaveError((e as Error).message);
      });
    return () => {
      cancelled = true;
    };
  }, [repoId]);

  useEffect(() => {
    loadViolations();
  }, [loadViolations]);

  useEffect(() => {
    loadDrift();
  }, [loadDrift]);

  const save = async () => {
    setSaveState("saving");
    setSaveError(null);
    try {
      const result = await apiPut<RuleSet>(`/api/repositories/${repoId}/rules`, { yaml: draft });
      setYaml(result.yaml);
      setDraft(result.yaml || DEFAULT_YAML);
      setSaveState("saved");
      loadViolations();
      loadDrift();
    } catch (e) {
      setSaveError((e as Error).message);
      setSaveState("idle");
    }
  };

  const violations = evaluation?.violations ?? [];
  const errorViolations = violations.filter((v) => v.severity === "error");
  const warningViolations = violations.filter((v) => v.severity === "warning");
  const infoViolations = violations.filter((v) => v.severity === "info");

  const hasRules = yaml !== null && yaml.trim().length > 0;

  return (
    <div className="space-y-4">
      <div className={card}>
        <div className="mb-4">
          <h2 className="text-lg font-bold">Architecture rules</h2>
          <p className="text-sm text-dim">
            Define <code>deny</code> and <code>require</code> constraints evaluated against every snapshot.
          </p>
        </div>

        <textarea
          value={draft}
          onChange={(e) => {
            setDraft(e.target.value);
            setSaveState("idle");
          }}
          spellCheck={false}
          className={`${field} h-72 font-mono text-[13px]`}
          placeholder="# rules:&#10;#   - name: ...&#10;#     severity: error&#10;#     deny:&#10;#       from: { path: src/domain }&#10;#       to: { path: src/infrastructure }"
        />

        <div className="mt-3 flex items-center gap-3">
          <button type="button" className={`${btn} ${btnPrimary}`} onClick={save} disabled={saveState === "saving"}>
            {saveState === "saving" ? "Saving…" : "Save rules"}
          </button>
          {saveState === "saved" && <span className="text-sm text-good">Saved</span>}
          {saveError && <span className="text-sm text-danger">{saveError}</span>}
        </div>
      </div>

      <div className={card}>
        <div className="mb-3 flex flex-wrap items-center gap-2">
          <h3 className="text-lg font-bold">Current violations</h3>
          {hasRules && evaluation && (
            <>
              <span className={badge}>snapshot {evaluation.commitSha.slice(0, 10)}</span>
              <span className={`${badge} ${badgeRed}`}>{errorViolations.length} errors</span>
              <span className={`${badge} ${badgeYellow}`}>{warningViolations.length} warnings</span>
              <span className={badge}>{infoViolations.length} info</span>
            </>
          )}
        </div>

        {!hasRules && <p className="py-4 text-sm text-dim">No rules defined yet. Write YAML above and save.</p>}

        {hasRules && evaluationLoading && (
          <div className="flex items-center justify-center gap-2 py-6 text-dim">
            <span className={spinner} /> Evaluating…
          </div>
        )}

        {hasRules && evaluationError && <div className="py-4 text-sm text-danger">{evaluationError}</div>}

        {hasRules && evaluation && violations.length === 0 && (
          <p className="py-4 text-sm text-good">No violations on the latest snapshot.</p>
        )}

        {hasRules && evaluation && violations.length > 0 && (
          <div className="space-y-4">
            {[errorViolations, warningViolations, infoViolations].map(
              (group) =>
                group.length > 0 && (
                  <div key={group[0].severity}>
                    <div className="mb-2 text-sm font-semibold capitalize text-dim">{group[0].severity}</div>
                    <ul className="space-y-2">
                      {group.map((v) => (
                        <li key={`${v.ruleName}|${v.fromKey}|${v.toKey}|${v.edgeType ?? "missing"}`} className="rounded-lg border border-border bg-inset px-3 py-2">
                          <div className="flex items-center gap-2 text-sm">
                            <span className={severityBadge(v.severity)}>{v.severity}</span>
                            <span className="font-semibold">{v.ruleName}</span>
                          </div>
                          {v.isMissingRequirement ? (
                            <p className="mt-1 font-mono text-xs text-dim">
                              Missing required edge from selector to selector.
                            </p>
                          ) : (
                            <p className="mt-1 font-mono text-xs text-dim">
                              {v.fromPath} <span className="text-fg">→</span> {v.toPath}
                              <span className="text-fg"> · {v.edgeType}</span>
                              {v.lowConfidence && <span className="ml-1 text-warn">low-confidence</span>}
                            </p>
                          )}
                        </li>
                      ))}
                    </ul>
                  </div>
                ),
            )}
          </div>
        )}
      </div>

      <div className={card}>
        <div className="mb-3 flex items-center gap-2">
          <h3 className="text-lg font-bold">Drift timeline</h3>
          {drift && (
            <span className={badge}>
              {drift.fromCommit.slice(0, 10)} … {drift.toCommit.slice(0, 10)}
            </span>
          )}
        </div>

        {!hasRules && <p className="py-4 text-sm text-dim">No rules defined yet.</p>}

        {hasRules && driftLoading && (
          <div className="flex items-center justify-center gap-2 py-6 text-dim">
            <span className={spinner} /> Walking snapshots…
          </div>
        )}

        {hasRules && driftError && <div className="py-4 text-sm text-danger">{driftError}</div>}

        {hasRules && drift && drift.entries.length === 0 && (
          <p className="py-4 text-sm text-good">No violations in the evaluated range.</p>
        )}

        {hasRules && drift && drift.entries.length > 0 && (
          <ul className="space-y-2">
            {drift.entries.map((entry) => (
              <li key={`${entry.ruleName}|${entry.fromKey}|${entry.toKey}|${entry.edgeType ?? "missing"}`} className="rounded-lg border border-border bg-inset px-3 py-2">
                <div className="flex flex-wrap items-center gap-2 text-sm">
                  <span className={severityBadge(entry.severity)}>{entry.severity}</span>
                  <span className="font-semibold">{entry.ruleName}</span>
                  {entry.isLive ? (
                    <span className={`${badge} ${badgeRed}`}>live</span>
                  ) : (
                    <span className={badge}>resolved</span>
                  )}
                  {entry.lowConfidence && <span className="text-xs text-warn">low-confidence</span>}
                </div>
                <p className="mt-1 flex flex-wrap items-center gap-2 font-mono text-xs text-dim">
                  introduced {entry.introducedCommit.slice(0, 10)}
                  {entry.fromPath && (
                    <>
                      <span className="text-fg">·</span> {entry.fromPath} <span className="text-fg">→</span> {entry.toPath}
                    </>
                  )}
                  <button
                    type="button"
                    className="text-accent hover:underline"
                    onClick={() => onOpenDiff(entry.introducedCommit, drift.toCommit)}
                  >
                    diff →
                  </button>
                </p>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}
