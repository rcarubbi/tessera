const http = require('http');
const Parser = require('web-tree-sitter');
const { analyzeFile, getLanguage } = require('./analyzer');

const PORT = process.env.PORT || 4350;

async function analyzeBatch(files) {
  const results = [];
  for (const file of files) {
    try {
      const result = await analyzeFile(file);
      results.push({ path: file.path, ...result });
    } catch (err) {
      results.push({ path: file.path, entities: [], relationships: [], imports: [], error: err.message });
    }
  }

  const entities = results.flatMap((r) => r.entities);
  const inFileRelationships = results.flatMap((r) => r.relationships);

  const symbolIndex = new Map();
  for (const entity of entities) {
    const simple = entity.symbol.split('.').pop();
    if (!symbolIndex.has(simple)) symbolIndex.set(simple, []);
    symbolIndex.get(simple).push(entity.key);
  }

  const crossFileRelationships = [];
  const seen = new Set();
  const emit = (from, to, type, evidence, confidence) => {
    const sig = `${from}|${to}|${type}`;
    if (seen.has(sig)) return;
    seen.add(sig);
    crossFileRelationships.push({ from, to, type, evidence, confidence, isStatic: true });
  };

  const keyBySymbol = new Map(entities.map((e) => [e.symbol, e.key]));

  for (const entity of entities) {
    for (const call of entity.calls || []) {
      const parts = call.callee.split('.');
      const simple = parts[parts.length - 1];
      const alreadyLocal = inFileRelationships.some(
        (r) => r.from === entity.key && r.type === 'Calls' && r.evidence === `${entity.path}:${call.line}`
      );
      if (alreadyLocal) continue;
      if (keyBySymbol.has(call.callee) && keyBySymbol.get(call.callee) !== entity.key) {
        emit(entity.key, keyBySymbol.get(call.callee), 'Calls', `${entity.path}:${call.line}`, 0.8);
      } else {
        const candidates = (symbolIndex.get(simple) || []).filter((k) => k !== entity.key);
        if (candidates.length === 1) {
          emit(entity.key, candidates[0], 'Calls', `${entity.path}:${call.line}`, 0.6);
        }
      }
    }
  }

  for (const entity of entities) {
    for (const base of entity.bases || []) {
      const already = inFileRelationships.some(
        (r) => r.from === entity.key && (r.type === 'Inherits' || r.type === 'Implements')
      );
      if (already) continue;
      const candidates = (symbolIndex.get(base) || []).filter((k) => k !== entity.key);
      if (candidates.length === 1) {
        const target = entities.find((e) => e.key === candidates[0]);
        const type = target && target.kind === 'interface' ? 'Implements' : 'Inherits';
        emit(entity.key, candidates[0], type, `${entity.path}`, 0.8);
      }
    }
  }

  const keyToEntity = new Map(entities.map((e) => [e.key, e]));
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
      const candidates = (symbolIndex.get(base) || []).filter((k) => k !== entity.key);
      if (candidates.length !== 1) continue;
      const target = keyToEntity.get(candidates[0]);
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
      const already = inFileRelationships.some((r) => r.from === entity.key && r.type === type);
      if (already) continue;
      const candidates = (symbolIndex.get(dep) || []).filter((k) => k !== entity.key);
      if (candidates.length === 1) {
        emit(entity.key, candidates[0], type, `${entity.path}:${entity.startLine}`, confidence);
      }
    }
  };

  for (const entity of entities) {
    resolveTypes(entity, 'fieldTypes', 'FieldDependency', 0.7);
  }

  for (const entity of entities) {
    resolveTypes(entity, 'injectedTypes', 'Injected', 0.85);
  }

  const relationships = [...inFileRelationships, ...crossFileRelationships];

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
  };
}

async function handleParse(body) {
  const files = body.files || [];
  const parsed = await analyzeBatch(files);
  return {
    commitSha: body.commitSha || null,
    defaultBranch: body.defaultBranch || null,
    entities: parsed.entities,
    relationships: parsed.relationships,
  };
}

module.exports = { analyzeBatch, handleParse };

const server = http.createServer(async (req, res) => {
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type');

  if (req.method === 'OPTIONS') {
    res.writeHead(204);
    res.end();
    return;
  }

  if (req.method === 'GET' && req.url === '/health') {
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ status: 'ok', languageCount: 9 }));
    return;
  }

  if (req.method === 'POST' && req.url === '/parse') {
    let body = '';
    req.on('data', (chunk) => (body += chunk));
    req.on('end', async () => {
      try {
        const payload = JSON.parse(body || '{}');
        const result = await handleParse(payload);
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify(result));
      } catch (err) {
        res.writeHead(400, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ error: err.message }));
      }
    });
    return;
  }

  res.writeHead(404, { 'Content-Type': 'application/json' });
  res.end(JSON.stringify({ error: 'not found' }));
});

if (require.main === module) {
  server.listen(PORT, async () => {
    await Parser.init();
    console.log(`Tessera analyzer sidecar listening on :${PORT}`);
  });
}
