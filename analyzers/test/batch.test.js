const { test } = require('node:test');
const assert = require('node:assert');
const Parser = require('web-tree-sitter');
const { analyzeBatch, BatchParseError } = require('../src/index');

const CONTROLLER = `using System;

public class PaymentController
{
    public void Create(Payment p)
    {
        new PaymentService().Process(p);
    }
}
`;

const SERVICE = `public class PaymentService
{
    public void Process(Payment p)
    {
        Validate(p);
    }

    public void Validate(Payment p) { }
}
`;

const BASE = `public abstract class BaseController { }`;

const DERIVED = `public class HealthController : BaseController { }`;

test('cross-file call resolves to unique symbol', async () => {
  await Parser.init();
  const result = await analyzeBatch([
    { path: 'src/Payments/PaymentController.cs', content: CONTROLLER },
    { path: 'src/Payments/PaymentService.cs', content: SERVICE },
  ]);
  const entities = result.entities;
  const processKey = entities.find((e) => e.symbol === 'PaymentService.Process').key;
  const controllerKey = entities.find((e) => e.symbol === 'PaymentController.Create').key;
  const edge = result.relationships.find((r) => r.from === controllerKey && r.to === processKey);
  assert.ok(edge, 'cross-file call edge must exist');
  assert.equal(edge.type, 'Calls');
});

test('cross-file inheritance resolves', async () => {
  await Parser.init();
  const result = await analyzeBatch([
    { path: 'src/Api/BaseController.cs', content: BASE },
    { path: 'src/Api/HealthController.cs', content: DERIVED },
  ]);
  const derivedKey = result.entities.find((e) => e.symbol === 'HealthController').key;
  const baseKey = result.entities.find((e) => e.symbol === 'BaseController').key;
  const edge = result.relationships.find((r) => r.from === derivedKey && r.to === baseKey && r.type === 'Inherits');
  assert.ok(edge, 'cross-file inheritance edge must exist');
});

test('cross-file edge confidence is marked lower', async () => {
  await Parser.init();
  const result = await analyzeBatch([
    { path: 'a/Controller.cs', content: CONTROLLER },
    { path: 'b/Service.cs', content: SERVICE },
  ]);
  const crossEdges = result.relationships.filter((r) => r.confidence < 1);
  assert.ok(crossEdges.length > 0);
});

test('cross-file implements resolves to interface', async () => {
  await Parser.init();
  const result = await analyzeBatch([
    { path: 'src/Api/IAuditable.cs', content: `public interface IAuditable { DateTime? UpdatedAt { get; set; } }` },
    { path: 'src/Domain/Payment.cs', content: `public class Payment : IAuditable { public DateTime? UpdatedAt { get; set; } }` },
  ]);
  const edge = result.relationships.find((r) => r.type === 'Implements');
  assert.ok(edge, 'cross-file Implements edge must exist');
  assert.equal(edge.from.split('::').pop(), 'Payment');
  assert.equal(edge.to.split('::').pop(), 'IAuditable');
});

test('cross-file injected dependency resolves', async () => {
  await Parser.init();
  const result = await analyzeBatch([
    { path: 'src/Infra/PaymentRepo.cs', content: `public class PaymentRepo { }` },
    { path: 'src/Orders/OrderService.cs', content: `public class OrderService\n{\n    public OrderService(PaymentRepo repo) { }\n}` },
  ]);
  const edge = result.relationships.find((r) => r.type === 'Injected');
  assert.ok(edge, 'cross-file Injected edge must exist');
  assert.equal(edge.from.split('::').pop(), 'OrderService');
  assert.equal(edge.to.split('::').pop(), 'PaymentRepo');
});

test('mixed-technology batch parses both languages and emits source', async () => {
  await Parser.init();
  const result = await analyzeBatch([
    { path: 'web/src/api/orders.ts', content: `export class OrdersApi {\n  getOrders(): Promise<Response> {\n    return fetch('/api/orders');\n  }\n}` },
    { path: 'src/Api/OrdersController.cs', content: `public class OrdersController { }` },
  ]);
  const languages = new Set(result.entities.map((e) => e.language));
  assert.ok(languages.has('typescript'), 'TypeScript entities parsed');
  assert.ok(languages.has('c_sharp'), 'C# entities parsed');
  const tsEntity = result.entities.find((e) => e.language === 'typescript');
  const csEntity = result.entities.find((e) => e.language === 'c_sharp');
  assert.ok(tsEntity.source && tsEntity.source.includes('fetch'), 'source snippet emitted for TS entity');
  assert.ok(csEntity.source && csEntity.source.includes('OrdersController'), 'source snippet emitted for C# entity');
  const crossTechEdge = result.relationships.find(
    (r) => r.from.startsWith('web/') && r.to.includes('OrdersController'),
  );
  assert.equal(crossTechEdge, undefined, 'static analysis must not invent cross-technology edges');
});

test('any file error fails the whole batch with the failing path', async () => {
  await Parser.init();
  await assert.rejects(
    analyzeBatch([
      { path: 'src/Good.cs', content: 'public class Good { }' },
      { path: 'src/Bad.xyz', language: 'not_a_lang', content: 'x' },
    ]),
    (err) => {
      assert.ok(err instanceof BatchParseError);
      assert.equal(err.failures.length, 1);
      assert.match(err.failures[0].path, /Bad\.xyz/);
      assert.match(err.failures[0].message, /No grammar for language/);
      return true;
    },
  );
});

test('every failing file is reported, not just the first', async () => {
  await Parser.init();
  await assert.rejects(
    analyzeBatch([
      { path: 'a.cs', language: 'not_a_lang', content: 'x' },
      { path: 'b.cs', language: 'not_a_lang', content: 'y' },
    ]),
    (err) => {
      assert.ok(err instanceof BatchParseError);
      assert.equal(err.failures.length, 2);
      assert.deepEqual(err.failures.map((f) => f.path).sort(), ['a.cs', 'b.cs']);
      return true;
    },
  );
});

test('cross-file inheritance edge survives a same-type in-file edge', async () => {
  await Parser.init();
  const result = await analyzeBatch([
    { path: 'src/Api/BaseController.cs', content: 'public abstract class BaseController { }' },
    {
      path: 'src/Api/HealthController.cs',
      content: `public abstract class LocalBase { }
public class HealthController : BaseController, LocalBase { }`,
    },
  ]);
  const fromKey = result.entities.find((e) => e.symbol === 'HealthController').key;
  const baseKey = result.entities.find((e) => e.symbol === 'BaseController').key;
  const localKey = result.entities.find((e) => e.symbol === 'LocalBase').key;
  const toKeys = result.relationships
    .filter((r) => r.from === fromKey && (r.type === 'Inherits' || r.type === 'Implements'))
    .map((r) => r.to)
    .sort();
  assert.ok(toKeys.includes(baseKey), 'cross-file edge to BaseController must exist');
  assert.ok(toKeys.includes(localKey), 'in-file edge to LocalBase must exist');
  assert.equal(toKeys.length, 2, 'both inheritance edges must be preserved');
});

test('cross-file field dependency edge survives a same-type in-file edge', async () => {
  await Parser.init();
  const result = await analyzeBatch([
    { path: 'src/Infra/PaymentRepo.cs', content: 'public class PaymentRepo { }' },
    {
      path: 'src/Orders/OrderService.cs',
      content: `public class LocalRepo { }
public class OrderService
{
    private LocalRepo _a;
    private PaymentRepo _b;
}`,
    },
  ]);
  const fromKey = result.entities.find((e) => e.symbol === 'OrderService').key;
  const crossKey = result.entities.find((e) => e.symbol === 'PaymentRepo').key;
  const localKey = result.entities.find((e) => e.symbol === 'LocalRepo').key;
  const toKeys = result.relationships
    .filter((r) => r.from === fromKey && r.type === 'FieldDependency')
    .map((r) => r.to)
    .sort();
  assert.ok(toKeys.includes(crossKey), 'cross-file FieldDependency edge must exist');
  assert.ok(toKeys.includes(localKey), 'in-file FieldDependency edge must exist');
  assert.equal(toKeys.length, 2, 'both field dependency edges must be preserved');
});

test('cross-file injected edge survives a same-type in-file edge', async () => {
  await Parser.init();
  const result = await analyzeBatch([
    { path: 'src/Infra/PaymentRepo.cs', content: 'public class PaymentRepo { }' },
    {
      path: 'src/Orders/OrderService.cs',
      content: `public class LocalRepo { }
public class OrderService
{
    public OrderService(LocalRepo a, PaymentRepo b) { }
}`,
    },
  ]);
  const fromKey = result.entities.find((e) => e.symbol === 'OrderService').key;
  const crossKey = result.entities.find((e) => e.symbol === 'PaymentRepo').key;
  const localKey = result.entities.find((e) => e.symbol === 'LocalRepo').key;
  const toKeys = result.relationships
    .filter((r) => r.from === fromKey && r.type === 'Injected')
    .map((r) => r.to)
    .sort();
  assert.ok(toKeys.includes(crossKey), 'cross-file Injected edge must exist');
  assert.ok(toKeys.includes(localKey), 'in-file Injected edge must exist');
  assert.equal(toKeys.length, 2, 'both injected edges must be preserved');
});

test('in-file and cross-file calls both resolve without ambiguity', async () => {
  await Parser.init();
  const result = await analyzeBatch([
    { path: 'src/Payments/PaymentService.cs', content: 'public class PaymentService { public void Process() { } }' },
    {
      path: 'src/Api/PaymentController.cs',
      content: `public class LocalService { public void Help() { } }
public class PaymentController
{
    public void Create()
    {
        new PaymentService().Process();
        new LocalService().Help();
    }
}`,
    },
  ]);
  const createKey = result.entities.find((e) => e.symbol === 'PaymentController.Create').key;
  const processKey = result.entities.find((e) => e.symbol === 'PaymentService.Process').key;
  const helpKey = result.entities.find((e) => e.symbol === 'LocalService.Help').key;
  const calls = result.relationships.filter((r) => r.from === createKey && r.type === 'Calls');
  assert.ok(calls.some((r) => r.to === processKey), 'cross-file call edge must exist');
  assert.ok(calls.some((r) => r.to === helpKey), 'in-file call edge must exist');
});

test('ambiguous simple names are reported as diagnostics and not resolved', async () => {
  await Parser.init();
  const result = await analyzeBatch([
    { path: 'a/Repo.cs', content: 'public class Repo { }' },
    { path: 'b/Repo.cs', content: 'public class Repo { }' },
    { path: 'c/Use.cs', content: 'public class Use { private Repo _r; }' },
  ]);
  assert.ok(result.diagnostics.some((d) => d.includes('Use.cs') && d.includes('Repo')), 'ambiguity must be surfaced');
  assert.equal(
    result.relationships.filter((r) => r.type === 'FieldDependency' && r.from.includes('Use')).length,
    0,
    'ambiguous reference must not be resolved to a random candidate',
  );
});

test('duplicate input paths produce a single entity per key', async () => {
  await Parser.init();
  const file = { path: 'src/Dup.cs', content: 'public class Dup { }' };
  const result = await analyzeBatch([file, file]);
  const keys = result.entities.map((e) => e.key);
  assert.equal(new Set(keys).size, keys.length, 'no duplicate entity keys');
});

test('concurrent cold-start parses are deterministic and load the grammar once', async () => {
  const originalLoad = Parser.Language.load;
  let loads = 0;
  Parser.Language.load = async function (...args) {
    loads += 1;
    return originalLoad.apply(this, args);
  };
  try {
    const files = [{ path: 'app/main.py', content: 'class Handler:\n    def run(self):\n        return 1\n' }];
    const [a, b, c] = await Promise.all([
      analyzeBatch(files),
      analyzeBatch(files),
      analyzeBatch(files),
    ]);
    assert.deepEqual(a, b);
    assert.deepEqual(a, c);
    assert.equal(loads, 1, 'grammar must load exactly once under concurrency');
  } finally {
    Parser.Language.load = originalLoad;
  }
});
