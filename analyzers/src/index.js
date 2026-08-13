const http = require('http');
const Parser = require('web-tree-sitter');
const { analyzeFile } = require('./analyzer');

const PORT = parseInt(process.env.PORT || '4350', 10);

// Request protection limits. These bound memory and protect the analyzer from
// oversized or malformed payloads; they are not a substitute for trusting the
// caller. The worker is the only intended client and reaches us on the Docker
// network (or loopback), so the host port is not exposed publicly.
const MAX_BODY_BYTES = 128 * 1024 * 1024; // 128 MB aggregate request body
const MAX_FILES = 1000; // max files per batch
const MAX_PATH_LENGTH = 4096; // characters per path
const MAX_FILE_BYTES = 8 * 1024 * 1024; // 8 MB per file content
const REQUEST_TIMEOUT_MS = 6 * 60 * 1000; // slightly above the sidecar client's 5-minute timeout

const DEFAULT_LIMITS = {
  maxBodyBytes: MAX_BODY_BYTES,
  maxFiles: MAX_FILES,
  maxPathLength: MAX_PATH_LENGTH,
  maxFileBytes: MAX_FILE_BYTES,
  requestTimeoutMs: REQUEST_TIMEOUT_MS,
};

class BatchParseError extends Error {
  constructor(failures) {
    super(`Parse failed for ${failures.length} file(s).`);
    this.name = 'BatchParseError';
    this.failures = failures;
  }
}

function httpError(status, message) {
  const err = new Error(message);
  err.status = status;
  return err;
}

function sha256Entities(files, results) {
  // Deduplicate entities by key (duplicate input paths or duplicate
  // declarations) so a single key never maps to more than one entity. First
  // occurrence wins to keep output stable for a given input.
  const entities = [];
  const seenKeys = new Set();
  for (const r of results) {
    for (const e of r.entities) {
      if (seenKeys.has(e.key)) continue;
      seenKeys.add(e.key);
      entities.push(e);
    }
  }
  return entities;
}

function buildSymbolIndex(entities) {
  // simple name -> sorted entity keys. Sorting keeps candidate ordering
  // deterministic regardless of input order.
  const index = new Map();
  for (const entity of entities) {
    const simple = entity.symbol.split('.').pop();
    if (!index.has(simple)) index.set(simple, []);
    index.get(simple).push(entity.key);
  }
  for (const keys of index.values()) keys.sort();
  return index;
}

function buildSymbolToKeys(entities) {
  // Fully qualified symbol -> sorted keys. Ambiguous symbols (duplicate keys)
  // are kept, never silently collapsed to a last-writer target.
  const map = new Map();
  for (const entity of entities) {
    if (!map.has(entity.symbol)) map.set(entity.symbol, []);
    map.get(entity.symbol).push(entity.key);
  }
  for (const keys of map.values()) keys.sort();
  return map;
}

async function analyzeBatch(files) {
  const results = [];
  const failures = [];
  for (const file of files) {
    try {
      const result = await analyzeFile(file);
      results.push({ path: file.path, ...result });
    } catch (err) {
      failures.push({ path: file.path, message: err.message });
    }
  }

  if (failures.length > 0) {
    throw new BatchParseError(failures);
  }

  const entities = sha256Entities(files, results);
  const inFileRelationships = results.flatMap((r) => r.relationships);

  const symbolIndex = buildSymbolIndex(entities);
  const symbolToKeys = buildSymbolToKeys(entities);
  const keyToEntity = new Map(entities.map((e) => [e.key, e]));

  const crossFileRelationships = [];
  const diagnostics = [];
  const seenDiagnostics = new Set();
  const seen = new Set();
  const emit = (from, to, type, evidence, confidence) => {
    const sig = `${from}|${to}|${type}`;
    if (seen.has(sig)) return;
    seen.add(sig);
    crossFileRelationships.push({ from, to, type, evidence, confidence, isStatic: true });
  };

  // Only emit a cross-file relationship when the target is uniquely
  // identifiable. Ambiguity is surfaced as a diagnostic instead of being
  // silently dropped or resolved to an arbitrary candidate.
  const uniqueCandidate = (entity, name) => {
    const candidates = (symbolIndex.get(name) || []).filter((k) => k !== entity.key);
    if (candidates.length === 1) return candidates[0];
    if (candidates.length > 1) {
      const key = `${entity.key}|${name}`;
      if (!seenDiagnostics.has(key)) {
        seenDiagnostics.add(key);
        diagnostics.push(
          `Ambiguous reference to "${name}" from ${entity.path}: candidates are ${candidates.join(', ')}.`,
        );
      }
    }
    return null;
  };

  for (const entity of entities) {
    for (const call of entity.calls || []) {
      const parts = call.callee.split('.');
      const simple = parts[parts.length - 1];
      const alreadyLocal = inFileRelationships.some(
        (r) => r.from === entity.key && r.type === 'Calls' && r.evidence === `${entity.path}:${call.line}`,
      );
      if (alreadyLocal) continue;

      const exactKeys = symbolToKeys.get(call.callee) || [];
      if (exactKeys.length === 1 && exactKeys[0] !== entity.key) {
        emit(entity.key, exactKeys[0], 'Calls', `${entity.path}:${call.line}`, 0.8);
        continue;
      }
      const targetKey = uniqueCandidate(entity, simple);
      if (targetKey !== null) {
        emit(entity.key, targetKey, 'Calls', `${entity.path}:${call.line}`, 0.6);
      }
    }
  }

  for (const entity of entities) {
    for (const base of entity.bases || []) {
      const targetKey = uniqueCandidate(entity, base);
      if (targetKey === null) continue;
      const target = keyToEntity.get(targetKey);
      const type = target && target.kind === 'interface' ? 'Implements' : 'Inherits';
      // Suppress only when this entity already has an in-file relationship to
      // the same target of this relationship type. An entity can inherit from
      // one class and implement several interfaces at the same time, so a
      // broad "any inherits/implements edge" check would wrongly suppress
      // valid cross-file relationships.
      const already = inFileRelationships.some(
        (r) =>
          r.from === entity.key &&
          r.to === targetKey &&
          (r.type === 'Inherits' || r.type === 'Implements'),
      );
      if (already) continue;
      emit(entity.key, targetKey, type, `${entity.path}`, 0.8);
    }
  }

  const methodsByOwner = new Map();
  for (const entity of entities) {
    if (entity.kind !== 'method' || !entity.ownerKey) continue;
    if (!methodsByOwner.has(entity.ownerKey)) methodsByOwner.set(entity.ownerKey, []);
    methodsByOwner.get(entity.ownerKey).push(entity);
  }

  // Connect concrete member implementations to the interface member they implement.
  for (const entity of entities) {
    if (entity.kind === 'method') continue;
    for (const base of entity.bases || []) {
      const targetKey = uniqueCandidate(entity, base);
      if (targetKey === null) continue;
      const target = keyToEntity.get(targetKey);
      if (!target || target.kind !== 'interface') continue;
      const targetMethods = methodsByOwner.get(target.key) || [];
      for (const method of methodsByOwner.get(entity.key) || []) {
        const simple = method.symbol.split('.').pop();
        const implTarget = targetMethods.find((m) => m.symbol.split('.').pop() === simple);
        if (implTarget) {
          emit(method.key, implTarget.key, 'Implements', `${method.path}:${method.startLine}`, 0.9);
        }
      }
    }
  }

  const resolveTypes = (entity, field, type, confidence) => {
    for (const dep of entity[field] || []) {
      const targetKey = uniqueCandidate(entity, dep);
      if (targetKey === null) continue;
      // Suppress only when the specific (from, to, type) edge already exists
      // in this file. A class can depend on several types of the same kind
      // across file boundaries; a broad type-only check would wrongly suppress
      // valid cross-file field/injected dependencies.
      const already = inFileRelationships.some(
        (r) => r.from === entity.key && r.to === targetKey && r.type === type,
      );
      if (already) continue;
      emit(entity.key, targetKey, type, `${entity.path}:${entity.startLine}`, confidence);
    }
  };

  for (const entity of entities) {
    resolveTypes(entity, 'fieldTypes', 'FieldDependency', 0.7);
  }

  for (const entity of entities) {
    resolveTypes(entity, 'injectedTypes', 'Injected', 0.85);
  }

  // Deduplicate the final relationship set (in-file edges across duplicate
  // input paths can repeat) while preserving first-seen order.
  const relationships = [];
  const seenRelationships = new Set();
  for (const r of [...inFileRelationships, ...crossFileRelationships]) {
    const sig = `${r.from}|${r.to}|${r.type}`;
    if (seenRelationships.has(sig)) continue;
    seenRelationships.add(sig);
    relationships.push(r);
  }

  return {
    entities: entities.map((e) => ({
      key: e.key,
      path: e.path,
      symbol: e.symbol,
      kind: e.kind,
      language: e.language,
      startLine: e.startLine,
      endLine: e.endLine,
      source: e.source,
      structuralHash: e.structuralHash,
      ownerKey: e.ownerKey,
    })),
    relationships,
    diagnostics,
  };
}

function handleParse(body, limits) {
  if (body === null || typeof body !== 'object') {
    throw httpError(400, 'Payload must be a JSON object.');
  }
  const files = body.files;
  if (!Array.isArray(files)) {
    throw httpError(400, '"files" must be an array.');
  }
  if (files.length > limits.maxFiles) {
    throw httpError(413, `Too many files: ${files.length} exceeds the limit of ${limits.maxFiles}.`);
  }
  for (let i = 0; i < files.length; i++) {
    const file = files[i];
    if (file === null || typeof file !== 'object') {
      throw httpError(400, `files[${i}] must be an object.`);
    }
    if (typeof file.path !== 'string' || file.path.length === 0) {
      throw httpError(400, `files[${i}].path is required.`);
    }
    if (file.path.length > limits.maxPathLength) {
      throw httpError(413, `files[${i}].path exceeds ${limits.maxPathLength} characters.`);
    }
    if (typeof file.content !== 'string') {
      throw httpError(400, `files[${i}].content must be a string.`);
    }
    if (Buffer.byteLength(file.content, 'utf8') > limits.maxFileBytes) {
      throw httpError(413, `files[${i}].content exceeds ${limits.maxFileBytes} bytes.`);
    }
  }
  return analyzeBatch(files).then((parsed) => ({
    commitSha: body.commitSha || null,
    defaultBranch: body.defaultBranch || null,
    entities: parsed.entities,
    relationships: parsed.relationships,
    diagnostics: parsed.diagnostics,
  }));
}

// Reads the request body up to a hard byte limit. When the limit is exceeded
// the request stream is paused so no more data is buffered, and the promise
// rejects with a 413 error for the caller to respond with.
function readBody(req, maxBytes) {
  return new Promise((resolve, reject) => {
    let received = 0;
    const chunks = [];
    let settled = false;
    req.on('data', (chunk) => {
      received += chunk.length;
      if (received > maxBytes) {
        settled = true;
        req.pause();
        reject(httpError(413, `Request body exceeds ${maxBytes} bytes.`));
        return;
      }
      chunks.push(chunk);
    });
    req.on('end', () => {
      if (!settled) resolve(Buffer.concat(chunks).toString('utf8'));
    });
    req.on('error', (err) => {
      if (!settled) {
        settled = true;
        reject(err);
      }
    });
  });
}

function send(res, status, payload) {
  res.writeHead(status, { 'Content-Type': 'application/json' });
  res.end(JSON.stringify(payload));
}

function createServer(overrides = {}) {
  const limits = { ...DEFAULT_LIMITS, ...overrides };
  const server = http.createServer((req, res) => {
    if (req.method === 'GET' && req.url === '/health') {
      send(res, 200, { status: 'ok', ready: true, languageCount: 9 });
      return;
    }

    if (req.method !== 'POST' || req.url !== '/parse') {
      send(res, 404, { error: 'not found' });
      return;
    }

    req.setTimeout(limits.requestTimeoutMs, () => {
      send(res, 408, { error: 'request timeout' });
      req.destroy();
    });

    readBody(req, limits.maxBodyBytes)
      .then((raw) => {
        let payload;
        try {
          payload = JSON.parse(raw || '{}');
        } catch {
          return send(res, 400, { error: 'Invalid JSON body.' });
        }
        return handleParse(payload, limits).then(
          (result) => send(res, 200, result),
          (err) => {
            if (err instanceof BatchParseError) {
              send(res, 400, { error: err.message, failures: err.failures });
            } else if (err && typeof err.status === 'number') {
              send(res, err.status, { error: err.message });
            } else {
              console.error('Parse request failed:', err);
              send(res, 500, { error: 'internal error' });
            }
          },
        );
      })
      .catch((err) => {
        if (err && typeof err.status === 'number') {
          send(res, err.status, { error: err.message });
        } else {
          console.error('Failed to read request body:', err);
          send(res, 500, { error: 'internal error' });
        }
      });
  });

  server.requestTimeout = limits.requestTimeoutMs;
  server.headersTimeout = limits.requestTimeoutMs;
  return server;
}

// Initializes the Tree-sitter runtime before the server starts listening so the
// process never advertises a listening port while the parser runtime is not
// ready. If initialization fails the process exits with a clear error instead
// of serving partial requests.
async function startServer({ port = PORT, host = '0.0.0.0', ...limitOverrides } = {}) {
  await Parser.init();
  const server = createServer(limitOverrides);
  return new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(port, host, () => resolve(server));
  });
}

module.exports = { analyzeBatch, handleParse, startServer, createServer, BatchParseError };

if (require.main === module) {
  startServer().catch((err) => {
    console.error('Failed to start Tessera analyzer sidecar:', err);
    process.exit(1);
  });
}
