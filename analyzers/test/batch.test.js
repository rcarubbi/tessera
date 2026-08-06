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
