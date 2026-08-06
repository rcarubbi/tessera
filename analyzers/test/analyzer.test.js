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
