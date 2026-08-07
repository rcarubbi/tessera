const Parser = require('web-tree-sitter');
const path = require('path');
const crypto = require('crypto');
const fs = require('fs');

const WASMS_DIR = path.join(__dirname, '..', 'node_modules', 'tree-sitter-wasms', 'out');

const EXTENSION_MAP = {
  cs: 'c_sharp',
  java: 'java',
  js: 'javascript',
  jsx: 'javascript',
  mjs: 'javascript',
  cjs: 'javascript',
  ts: 'typescript',
  tsx: 'tsx',
  py: 'python',
  go: 'go',
  php: 'php',
  rb: 'ruby',
};

const DECLARATIONS = {
  c_sharp: {
    class: ['class_declaration', 'interface_declaration', 'struct_declaration', 'enum_declaration', 'record_declaration'],
    method: ['method_declaration'],
  },
  java: {
    class: ['class_declaration', 'interface_declaration', 'enum_declaration', 'record_declaration'],
    method: ['method_declaration'],
  },
  javascript: {
    class: ['class_declaration'],
    method: ['method_definition'],
  },
  typescript: {
    class: ['class_declaration', 'interface_declaration', 'enum_declaration', 'type_alias_declaration'],
    method: ['method_definition'],
  },
  tsx: {
    class: ['class_declaration', 'interface_declaration', 'enum_declaration', 'type_alias_declaration'],
    method: ['method_definition'],
  },
  python: {
    class: ['class_definition'],
    method: ['function_definition'],
  },
  go: {
    class: ['type_spec'],
    method: ['function_declaration', 'method_declaration'],
  },
  php: {
    class: ['class_declaration', 'interface_declaration'],
    method: ['method_declaration'],
  },
  ruby: {
    class: ['class', 'module'],
    method: ['method'],
  },
};

const CALL_NODES = {
  c_sharp: ['invocation_expression'],
  java: ['method_invocation'],
  javascript: ['call_expression'],
  typescript: ['call_expression'],
  tsx: ['call_expression'],
  python: ['call'],
  go: ['call_expression'],
  php: ['function_call_expression'],
  ruby: ['call'],
};

const IMPORT_NODES = {
  c_sharp: ['using_directive'],
  java: ['import_declaration'],
  javascript: ['import_statement'],
  typescript: ['import_statement'],
  tsx: ['import_statement'],
  python: ['import_statement', 'import_from_statement'],
  go: ['import_declaration'],
  php: ['namespace_use_declaration'],
  ruby: ['require', 'require_relative'],
};

const parserCache = new Map();

async function loadParser(language) {
  if (parserCache.has(language)) return parserCache.get(language);
  const wasm = path.join(WASMS_DIR, `tree-sitter-${language}.wasm`);
  if (!fs.existsSync(wasm)) throw new Error(`No grammar for language: ${language}`);
  const lang = await Parser.Language.load(wasm);
  const parser = new Parser();
  parser.setLanguage(lang);
  parserCache.set(language, parser);
  return parser;
}

function sha256(input) {
  return crypto.createHash('sha256').update(input).digest('hex');
}

function getLanguage(filePath) {
  const ext = path.extname(filePath).slice(1).toLowerCase();
  return EXTENSION_MAP[ext] || null;
}

function nodeText(node, source) {
  return source.slice(node.startIndex, node.endIndex);
}

function normalizeName(name) {
  return name.replace(/\s+/g, ' ').trim();
}

function isKind(node, kinds) {
  return kinds.includes(node.type);
}

const KIND_BY_NODE = {
  interface_declaration: 'interface',
  struct_declaration: 'struct',
  enum_declaration: 'enum',
  record_declaration: 'record',
  type_alias_declaration: 'class',
};

function mapKind(nodeType) {
  return KIND_BY_NODE[nodeType] || 'class';
}

function memberTypeNode(decl) {
  const kids = decl.namedChildren.filter((c) => c.type !== 'modifier' && c.type !== 'comment');
  const vd = kids.find((c) => c.type === 'variable_declaration');
  if (vd) {
    const typeNode = vd.namedChildren.find((c) => c.type !== 'variable_declarator');
    if (typeNode) return typeNode;
  }
  const byField = decl.childForFieldName('type');
  if (byField) return byField;
  return kids[0] || null;
}

function extractFieldTypes(node) {
  const types = [];
  for (const kind of ['field_declaration', 'property_declaration', 'property_signature', 'field_definition']) {
    for (const decl of node.descendantsOfType(kind)) {
      const typeNode = memberTypeNode(decl);
      if (typeNode) types.push(typeNode.text);
    }
  }
  return [...new Set(types)];
}

function extractCtorParamTypes(node) {
  const types = [];
  for (const ctorKind of ['constructor_declaration', 'method_definition']) {
    for (const ctor of node.descendantsOfType(ctorKind)) {
      if (ctorKind === 'method_definition') {
        const nameField = ctor.childForFieldName('name');
        if (!nameField || nameField.text !== 'constructor') continue;
      }
      const plist = ctor.childForFieldName('parameters') || ctor.descendantsOfType('parameter_list')[0];
      if (!plist) continue;
      for (const param of plist.descendantsOfType('parameter')) {
        const kids = param.namedChildren.filter((c) => c.type !== 'modifier');
        const typeNode = param.childForFieldName('type') || kids[0];
        if (typeNode) types.push(typeNode.text);
      }
    }
  }
  return [...new Set(types)];
}

function extractCallee(node, source, language) {
  if (language === 'ruby') {
    const methodField = node.childForFieldName('method');
    return methodField ? normalizeName(methodField.text) : null;
  }
  const fnField = node.childForFieldName('function');
  if (fnField) {
    return normalizeName(fnField.text);
  }
  if (node.children.length > 0) {
    return normalizeName(nodeText(node.children[0], source));
  }
  return null;
}

function extractBases(node, source) {
  const bases = [];
  const candidates = [
    node.childForFieldName('base_class'),
    node.childForFieldName('superclass'),
    node.childForFieldName('bases'),
  ].filter(Boolean);

  for (const child of node.children) {
    if (['extends_clause', 'implements_clause', 'base_list', 'super_interfaces', 'class_heritage'].includes(child.type)) {
      candidates.push(child);
    }
  }

  for (const base of candidates) {
    const types = base.descendantsOfType('type_identifier');
    for (const t of types) bases.push(t.text);
    const ids = base.descendantsOfType('identifier');
    for (const i of ids) bases.push(i.text);
  }
  return [...new Set(bases)];
}

function analyzeFile(file) {
  return (async () => {
    const language = file.language || getLanguage(file.path);
    if (!language) {
      return { entities: [], relationships: [], imports: [] };
    }
    const parser = await loadParser(language);
    const tree = parser.parse(file.content);
    const source = file.content;

    const cfg = DECLARATIONS[language];
    const callKinds = CALL_NODES[language] || [];
    const importKinds = IMPORT_NODES[language] || [];

    const declarations = [];
    const invocations = [];
    const imports = [];

    const visit = (node) => {
      const kind = isKind(node, cfg.class)
        ? mapKind(node.type)
        : isKind(node, cfg.method)
          ? 'method'
          : null;
      if (kind) {
        const nameField = node.childForFieldName('name');
        const name = nameField ? normalizeName(nameField.text) : null;
        if (name) {
          declarations.push({
            name,
            kind,
            node,
            startIndex: node.startIndex,
            endIndex: node.endIndex,
            startLine: node.startPosition.row + 1,
            endLine: node.endPosition.row + 1,
            bases: kind === 'class' ? extractBases(node, source) : [],
          });
        }
      }
      if (callKinds.includes(node.type)) {
        const callee = extractCallee(node, source, language);
        if (callee) {
          invocations.push({ callee, startIndex: node.startIndex, endIndex: node.endIndex, line: node.startPosition.row + 1 });
        }
      }
      if (importKinds.includes(node.type)) {
        imports.push(normalizeName(nodeText(node, source)));
      }
      const children = node.children;
      if (children) {
        for (const child of children) {
          if (child) visit(child);
        }
      }
    };
    visit(tree.rootNode);

    const classes = declarations.filter((d) => d.kind === 'class');
    const methods = declarations.filter((d) => d.kind === 'method');

    const methodForInvocation = (inv) =>
      methods.find(
        (m) => m.startIndex <= inv.startIndex && m.endIndex >= inv.endIndex
      ) || null;

    // A member's owner is any class-like declaration (class, interface, struct,
    // record, enum) that contains it. Pick the innermost one so nested types win.
    const owners = declarations.filter((d) => d.kind !== 'method');
    const ownerForEntity = (decl) => {
      let owner = null;
      for (const candidate of owners) {
        if (candidate.startIndex <= decl.startIndex && candidate.endIndex >= decl.endIndex) {
          if (!owner || candidate.endIndex - candidate.startIndex < owner.endIndex - owner.startIndex) {
            owner = candidate;
          }
        }
      }
      return owner;
    };

    const entitiesOut = [];
    for (const decl of declarations) {
      const owner = decl.kind === 'method' ? ownerForEntity(decl) : null;
      const scope = owner ? `${owner.name}.${decl.name}` : decl.name;
      const ownerKey = owner ? `${file.path}::${owner.name}` : null;
      const calls = decl.kind === 'method'
        ? invocations.filter((inv) => {
            const ownerMethod = methodForInvocation(inv);
            return ownerMethod === decl;
          }).map((inv) => ({ callee: inv.callee, line: inv.line }))
        : [];
      const localCallNames = calls.map((c) => c.callee.split('.').pop());
      const structuralHash = sha256(JSON.stringify({
        kind: decl.kind,
        name: decl.name,
        bases: (decl.bases || []).sort(),
        localCalls: [...new Set(localCallNames)].sort(),
      }));
      entitiesOut.push({
        key: `${file.path}::${scope}`,
        path: file.path,
        symbol: scope,
        kind: decl.kind,
        language,
        startLine: decl.startLine,
        endLine: decl.endLine,
        structuralHash,
        ownerKey,
        calls,
        bases: decl.bases || [],
        fieldTypes: decl.kind === 'method' ? [] : extractFieldTypes(decl.node),
        injectedTypes: decl.kind === 'method' ? [] : extractCtorParamTypes(decl.node),
      });
    }

    const relationships = [];
    const entityBySymbol = new Map(entitiesOut.map((e) => [e.symbol, e]));
    const entityByKey = new Map(entitiesOut.map((e) => [e.key, e]));

    for (const entity of entitiesOut) {
      if (!entity.ownerKey) continue;
      const owner = entityByKey.get(entity.ownerKey);
      if (owner && owner.key !== entity.key) {
        relationships.push({
          from: entity.ownerKey,
          to: entity.key,
          type: 'HasMethod',
          evidence: `${entity.path}:${entity.startLine}`,
          confidence: 1,
          isStatic: true,
        });
      }
    }

    for (const entity of entitiesOut) {
      for (const base of entity.bases) {
        const target = entityBySymbol.get(base);
        if (target && target.key !== entity.key) {
          const type = target.kind === 'interface' ? 'Implements' : 'Inherits';
          relationships.push({ from: entity.key, to: target.key, type, evidence: null, confidence: 1, isStatic: true });
        }
      }
    }

    for (const entity of entitiesOut) {
      for (const fieldType of entity.fieldTypes) {
        const target = entityBySymbol.get(fieldType);
        if (target && target.key !== entity.key) {
          relationships.push({
            from: entity.key,
            to: target.key,
            type: 'FieldDependency',
            evidence: `${entity.path}:${entity.startLine}`,
            confidence: 0.9,
            isStatic: true,
          });
        }
      }
    }

    for (const entity of entitiesOut) {
      for (const injectedType of entity.injectedTypes) {
        const target = entityBySymbol.get(injectedType);
        if (target && target.key !== entity.key) {
          relationships.push({
            from: entity.key,
            to: target.key,
            type: 'Injected',
            evidence: `${entity.path}:${entity.startLine}`,
            confidence: 0.95,
            isStatic: true,
          });
        }
      }
    }

    for (const entity of entitiesOut) {
      for (const call of entity.calls) {
        const parts = call.callee.split('.');
        const simple = parts[parts.length - 1];
        const direct = entityBySymbol.get(call.callee);
        if (direct && direct.key !== entity.key) {
          relationships.push({ from: entity.key, to: direct.key, type: 'Calls', evidence: `${file.path}:${call.line}`, confidence: 1, isStatic: true });
          continue;
        }
        const simpleMatches = entitiesOut.filter(
          (e) => e.symbol === simple || e.symbol.endsWith('.' + simple)
        );
        if (simpleMatches.length === 1 && simpleMatches[0].key !== entity.key) {
          relationships.push({ from: entity.key, to: simpleMatches[0].key, type: 'Calls', evidence: `${file.path}:${call.line}`, confidence: 0.8, isStatic: true });
        }
      }
    }

    return { entities: entitiesOut, relationships, imports };
  })();
}

module.exports = { analyzeFile, getLanguage, sha256, loadParser };
