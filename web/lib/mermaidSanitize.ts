/**
 * Rewrites classDiagram member lines that mermaid cannot parse.
 * Braces inside member types (e.g. `+homeTeam: { id: number; name: string }`
 * or multi-line index signatures) produce OPEN_IN_STRUCT parse errors;
 * backtick quoting is not part of the class grammar. Member lines are
 * flattened: braces dropped, `;` becomes `-`, whitespace collapsed.
 * Class body delimiters are tracked by nesting depth so only real body
 * closers survive; notes and everything outside a class body stay untouched.
 */
export function sanitizeMermaidChart(chart: string): string {
  if (!chart.trimStart().startsWith("classDiagram")) return chart;

  let depth = 0;
  return chart
    .split("\n")
    .flatMap((line) => {
      const trimmed = line.trim();

      if (/^class\b/.test(trimmed)) {
        depth = trimmed.endsWith("{") ? 1 : 0;
        return [line];
      }

      if (depth === 0) return [line];

      if (trimmed === "}") {
        depth = Math.max(0, depth - 1);
        return depth === 0 ? [line] : [];
      }

      const opens = (line.match(/\{/g) ?? []).length;
      const closes = (line.match(/\}/g) ?? []).length;
      const sanitized = /[{;}]/.test(line)
        ? line
            .replace(/[{}]/g, " ")
            .replace(/;/g, " -")
            .replace(/\s+/g, " ")
            .trimEnd()
        : line;
      depth = Math.max(0, depth + opens - closes);
      return [sanitized];
    })
    .join("\n");
}
