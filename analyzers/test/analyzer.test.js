const { test } = require('node:test');
const assert = require('node:assert');
const Parser = require('web-tree-sitter');
const { analyzeFile } = require('../src/analyzer');

const CSHARP_SAMPLE = `using System;

namespace Payments;

public class PaymentService
{
    public bool Validate(Payment p) => p.Amount > 0;

    public void Process(Payment p)
    {
        Validate(p);
        _repo.Save(p);
    }
}

public class PaymentController
{
    public void Create(Payment p)
    {
        new PaymentService().Process(p);
    }
}
`;

const TS_SAMPLE = `import { PaymentRepo } from './repo';

export interface Payment {
  amount: number;
}

export class PaymentService {
  private repo: PaymentRepo;

  validate(p: Payment): boolean {
    return p.amount > 0;
  }

  process(p: Payment): void {
    if (this.validate(p)) {
      this.repo.save(p);
    }
  }
}
`;

test('analyzes C# entities', async () => {
  await Parser.init();
  const result = await analyzeFile({ path: 'Payments/PaymentService.cs', content: CSHARP_SAMPLE });
  const symbols = result.entities.map((e) => e.symbol);
  assert.ok(symbols.includes('PaymentService'));
  assert.ok(symbols.includes('PaymentService.Validate'));
  assert.ok(symbols.includes('PaymentService.Process'));
  assert.ok(symbols.includes('PaymentController'));
  assert.ok(symbols.includes('PaymentController.Create'));
});

test('C# intra-file calls produce edges', async () => {
  await Parser.init();
  const result = await analyzeFile({ path: 'Payments/PaymentService.cs', content: CSHARP_SAMPLE });
  const calls = result.relationships.filter((r) => r.type === 'Calls');
  assert.ok(calls.some((r) => r.from.endsWith('PaymentService.Process') && r.to.endsWith('PaymentService.Validate')));
  assert.ok(calls.some((r) => r.from.endsWith('PaymentController.Create') && r.to.endsWith('PaymentService.Process')));
});

test('C# base class captured on entity', async () => {
  await Parser.init();
  const source = `public class MyController : BaseController { public void Get() {} }`;
  const result = await analyzeFile({ path: 'Api/MyController.cs', content: source });
  const controller = result.entities.find((e) => e.symbol === 'MyController');
  assert.ok(controller);
  assert.deepEqual(controller.bases, ['BaseController']);
});

test('structuralHash ignores comment-only changes', async () => {
  await Parser.init();
  const a = await analyzeFile({ path: 'x.cs', content: `public class Foo { void Bar() { Baz(); } }` });
  const b = await analyzeFile({ path: 'x.cs', content: `// added comment\npublic class Foo { void Bar() { Baz(); } // trailing\n}` });
  const ha = a.entities.find((e) => e.symbol === 'Foo');
  const hb = b.entities.find((e) => e.symbol === 'Foo');
  assert.equal(ha.structuralHash, hb.structuralHash);
});

test('structuralHash changes when calls change', async () => {
  await Parser.init();
  const a = await analyzeFile({ path: 'x.cs', content: `public class Foo { void Bar() { Baz(); } }` });
  const b = await analyzeFile({ path: 'x.cs', content: `public class Foo { void Bar() { Qux(); } }` });
  const ha = a.entities.find((e) => e.symbol === 'Foo.Bar');
  const hb = b.entities.find((e) => e.symbol === 'Foo.Bar');
  assert.ok(ha && hb, 'method entities must exist');
  assert.notEqual(ha.structuralHash, hb.structuralHash);
});

test('analyzes TypeScript entities and imports', async () => {
  await Parser.init();
  const result = await analyzeFile({ path: 'src/payments/service.ts', content: TS_SAMPLE });
  assert.ok(result.entities.some((e) => e.symbol === 'PaymentService'));
  assert.ok(result.entities.some((e) => e.symbol === 'PaymentService.process'));
  assert.ok(result.imports.some((i) => i.includes('PaymentRepo')));
});

test('maps C# declaration kinds', async () => {
  await Parser.init();
  const source = `
public interface IAuditable { DateTime? UpdatedAt { get; set; } }
public enum PaymentStatus { Pending, Paid }
public record PaymentDto(int Id);
public struct Money { public decimal Amount; }
public class Payment { public int Id { get; set; } }
`;
  const result = await analyzeFile({ path: 'Types.cs', content: source });
  const kindOf = (symbol) => result.entities.find((e) => e.symbol === symbol).kind;
  assert.equal(kindOf('IAuditable'), 'interface');
  assert.equal(kindOf('PaymentStatus'), 'enum');
  assert.equal(kindOf('PaymentDto'), 'record');
  assert.equal(kindOf('Money'), 'struct');
  assert.equal(kindOf('Payment'), 'class');
});

test('implements edge emitted for interface in same file', async () => {
  await Parser.init();
  const source = `
public interface IAuditable { DateTime? UpdatedAt { get; set; } }
public class Payment : IAuditable { public DateTime? UpdatedAt { get; set; } }
`;
  const result = await analyzeFile({ path: 'Payment.cs', content: source });
  const edge = result.relationships.find((r) => r.type === 'Implements');
  assert.ok(edge, 'Implements edge must exist');
  assert.ok(edge.from.endsWith('Payment') && edge.to.endsWith('IAuditable'));
  assert.equal(edge.confidence, 1);
});

test('inherits edge for class base, implements for interface', async () => {
  await Parser.init();
  const source = `
public abstract class BaseController { }
public interface IHealth { string Check(); }
public class HealthController : BaseController, IHealth { public string Check() => "ok"; }
`;
  const result = await analyzeFile({ path: 'Api/HealthController.cs', content: source });
  const types = result.relationships.map((r) => r.type).sort();
  assert.ok(types.includes('Inherits'));
  assert.ok(types.includes('Implements'));
});

test('injected and field dependency edges from constructor and property types', async () => {
  await Parser.init();
  const source = `
public class PaymentRepo { }
public class Payment { }
public class OrderService
{
    private readonly PaymentRepo _repo;
    public Payment Current { get; set; }
    public OrderService(PaymentRepo repo, Payment current)
    {
        _repo = repo;
        Current = current;
    }
}
`;
  const result = await analyzeFile({ path: 'Order/OrderService.cs', content: source });
  const svc = result.entities.find((e) => e.symbol === 'OrderService');
  assert.deepEqual(svc.fieldTypes, ['PaymentRepo', 'Payment']);
  assert.deepEqual(svc.injectedTypes, ['PaymentRepo', 'Payment']);
  const injected = result.relationships.find((r) => r.type === 'Injected');
  const fieldDep = result.relationships.find((r) => r.type === 'FieldDependency');
  assert.ok(injected && fieldDep, 'Injected and FieldDependency edges must exist');
  assert.equal(injected.confidence, 0.95);
  assert.equal(fieldDep.confidence, 0.9);
});
