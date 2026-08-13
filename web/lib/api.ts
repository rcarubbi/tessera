export const API_BASE = process.env.NEXT_PUBLIC_API_BASE ?? "http://localhost:5080";

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message);
  }
}

function fetchOptions(init: RequestInit): RequestInit {
  return { ...init, credentials: "include", cache: "no-store" };
}

async function headers(extra?: Record<string, string>): Promise<Record<string, string>> {
  return { "Content-Type": "application/json", ...extra };
}

export async function apiGet<T>(path: string, signal?: AbortSignal): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, fetchOptions({ headers: await headers(), signal }));
  if (res.status === 401) throw new ApiError(401, "Unauthorized");
  if (!res.ok) {
    const body = await res.text();
    throw new ApiError(res.status, body || res.statusText);
  }
  return res.json() as Promise<T>;
}

export async function apiPost<T>(path: string, body?: unknown): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, fetchOptions({
    method: "POST",
    headers: await headers(),
    body: body === undefined ? undefined : JSON.stringify(body),
  }));
  if (res.status === 401) throw new ApiError(401, "Unauthorized");
  if (!res.ok) {
    const text = await res.text();
    throw new ApiError(res.status, text || res.statusText);
  }
  return res.json() as Promise<T>;
}

export async function apiPut<T>(path: string, body: unknown): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, fetchOptions({
    method: "PUT",
    headers: await headers(),
    body: JSON.stringify(body),
  }));
  if (res.status === 401) throw new ApiError(401, "Unauthorized");
  if (!res.ok) {
    const text = await res.text();
    throw new ApiError(res.status, text || res.statusText);
  }
  return res.json() as Promise<T>;
}

export async function apiDelete(path: string): Promise<void> {
  const res = await fetch(`${API_BASE}${path}`, fetchOptions({
    method: "DELETE",
    headers: await headers(),
  }));
  if (res.status === 401) throw new ApiError(401, "Unauthorized");
  if (!res.ok) {
    const text = await res.text();
    throw new ApiError(res.status, text || res.statusText);
  }
}

export type ChatStreamEvent = {
  kind: "mode" | "warnings" | "delta" | "citations" | "error";
  mode?: string;
  warnings?: string[];
  text?: string;
  citations?: Citation[];
  error?: string;
};

export type Citation = {
  key: string;
  symbol: string;
  file: string;
  line: number;
  label: string;
};

export type StoredMessage = {
  id: string;
  role: "user" | "assistant";
  content: string;
  mode?: string | null;
  citations: Citation[];
  warnings: string[];
  createdAt: string;
};

export async function getChatMessages(repositoryId: string): Promise<StoredMessage[]> {
  return apiGet<StoredMessage[]>(`/api/repositories/${repositoryId}/chat/messages`);
}

export async function postChatMessage(
  repositoryId: string,
  entry: { role: "user" | "assistant"; content: string; mode?: string; citations?: Citation[]; warnings?: string[] },
): Promise<StoredMessage> {
  return apiPost<StoredMessage>(`/api/repositories/${repositoryId}/chat/messages`, entry);
}

export async function streamChat(
  repositoryId: string,
  question: string,
  onEvent: (event: ChatStreamEvent) => void,
  signal?: AbortSignal,
): Promise<void> {
  const res = await fetch(`${API_BASE}/api/repositories/${repositoryId}/chat/stream`, {
    method: "POST",
    headers: await headers(),
    body: JSON.stringify({ question }),
    cache: "no-store",
    credentials: "include",
    signal,
  });
  if (!res.ok) {
    const text = await res.text();
    throw new ApiError(res.status, text || res.statusText);
  }
  if (!res.body) return;

  const reader = res.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  const flush = () => {
    let idx = buffer.indexOf("\n\n");
    while (idx !== -1) {
      const block = buffer.slice(0, idx);
      buffer = buffer.slice(idx + 2);
      const lines = block.split("\n");
      let event = "";
      const dataLines: string[] = [];
      for (const line of lines) {
        if (line.startsWith("event:")) event = line.slice(6).trim();
        else if (line.startsWith("data:")) dataLines.push(line.slice(5).trim());
      }
      if (dataLines.length > 0) {
        onEvent(parseEvent(event, dataLines.join("\n")));
      }
      idx = buffer.indexOf("\n\n");
    }
  };

  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });
    flush();
  }
  flush();
}

function parseEvent(kind: string, data: string): ChatStreamEvent {
  switch (kind) {
    case "mode":
      return { kind, mode: JSON.parse(data).mode };
    case "warnings":
      return { kind, warnings: JSON.parse(data) };
    case "delta":
      return { kind, text: JSON.parse(data).text };
    case "citations":
      return { kind, citations: JSON.parse(data) };
    case "error":
      return { kind, error: JSON.parse(data).error };
    default:
      return { kind: "delta", text: "" };
  }
}
