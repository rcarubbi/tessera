const { test } = require('node:test');
const assert = require('node:assert');
const Parser = require('web-tree-sitter');
const { analyzeBatch } = require('../src/index');

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
