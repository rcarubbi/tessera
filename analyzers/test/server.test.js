const { test } = require('node:test');
const assert = require('node:assert');
const { startServer } = require('../src/index');

const SMALL_LIMITS = {
  maxBodyBytes: 4096,
  maxFiles: 5,
  maxPathLength: 80,
  maxFileBytes: 2048,
};

async function withServer(limits, fn) {
  const server = await startServer({ port: 0, ...limits });
  try {
    const port = server.address().port;
    await fn(`http://127.0.0.1:${port}`);
  } finally {
    await new Promise((resolve) => server.close(resolve));
  }
}

async function post(baseUrl, body) {
  const response = await fetch(`${baseUrl}/parse`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: typeof body === 'string' ? body : JSON.stringify(body),
  });
  const text = await response.text();
  let json = null;
  try {
    json = JSON.parse(text);
  } catch {
    // non-JSON response body
  }
  return { status: response.status, json };
}

test('startServer is ready before it starts listening (no manual Parser.init)', async () => {
  await withServer({}, async (baseUrl) => {
    const { status, json } = await post(baseUrl, {
      files: [{ path: 'src/Foo.cs', content: 'public class Foo { }' }],
    });
    assert.equal(status, 200);
    assert.ok(json.entities.some((e) => e.symbol === 'Foo'));
    assert.ok(Array.isArray(json.diagnostics), 'response must include diagnostics array');
  });
});

test('health endpoint reports ready', async () => {
  await withServer({}, async (baseUrl) => {
    const response = await fetch(`${baseUrl}/health`);
    const json = await response.json();
    assert.equal(response.status, 200);
    assert.equal(json.ready, true);
  });
});

test('unknown route returns 404', async () => {
  await withServer({}, async (baseUrl) => {
    const response = await fetch(`${baseUrl}/nope`);
    assert.equal(response.status, 404);
  });
});

test('invalid JSON body returns 400', async () => {
  await withServer({}, async (baseUrl) => {
    const { status, json } = await post(baseUrl, '{not json');
    assert.equal(status, 400);
    assert.ok(json.error);
  });
});

test('missing files field returns 400', async () => {
  await withServer({}, async (baseUrl) => {
    const { status, json } = await post(baseUrl, { commitSha: 'abc' });
    assert.equal(status, 400);
    assert.match(json.error, /"files"/);
  });
});

test('files field must be an array', async () => {
  await withServer({}, async (baseUrl) => {
    const { status, json } = await post(baseUrl, { files: 'nope' });
    assert.equal(status, 400);
    assert.match(json.error, /array/);
  });
});

test('too many files returns 413', async () => {
  await withServer(SMALL_LIMITS, async (baseUrl) => {
    const files = Array.from({ length: 6 }, (_, i) => ({ path: `f${i}.cs`, content: '' }));
    const { status, json } = await post(baseUrl, { files });
    assert.equal(status, 413);
    assert.match(json.error, /Too many files/);
  });
});

test('path too long returns 413', async () => {
  await withServer(SMALL_LIMITS, async (baseUrl) => {
    const { status, json } = await post(baseUrl, {
      files: [{ path: 'x'.repeat(81) + '.cs', content: '' }],
    });
    assert.equal(status, 413);
    assert.match(json.error, /path/);
  });
});

test('file too large returns 413', async () => {
  await withServer(SMALL_LIMITS, async (baseUrl) => {
    const { status, json } = await post(baseUrl, {
      files: [{ path: 'big.cs', content: 'x'.repeat(3000) }],
    });
    assert.equal(status, 413);
    assert.match(json.error, /content/);
  });
});

test('oversized request body returns 413', async () => {
  await withServer(SMALL_LIMITS, async (baseUrl) => {
    const big = 'x'.repeat(6000);
    const { status, json } = await post(baseUrl, { files: [{ path: 'big.cs', content: big }] });
    assert.equal(status, 413);
    assert.match(json.error, /body/);
  });
});

test('file entry must be an object with path and content', async () => {
  await withServer({}, async (baseUrl) => {
    const noPath = await post(baseUrl, { files: [{ content: 'x' }] });
    assert.equal(noPath.status, 400);
    assert.match(noPath.json.error, /path/);

    const noContent = await post(baseUrl, { files: [{ path: 'a.cs' }] });
    assert.equal(noContent.status, 400);
    assert.match(noContent.json.error, /content/);
  });
});

test('batch with a failing file returns 400 with failure details', async () => {
  await withServer({}, async (baseUrl) => {
    const { status, json } = await post(baseUrl, {
      files: [
        { path: 'src/Good.cs', content: 'public class Good { }' },
        { path: 'src/Bad.xyz', language: 'not_a_lang', content: 'x' },
      ],
    });
    assert.equal(status, 400);
    assert.ok(json.error);
    assert.ok(Array.isArray(json.failures));
    assert.equal(json.failures.length, 1);
    assert.match(json.failures[0].path, /Bad\.xyz/);
    assert.match(json.failures[0].message, /No grammar for language/);
  });
});

test('commitSha and defaultBranch echo back on success', async () => {
  await withServer({}, async (baseUrl) => {
    const { status, json } = await post(baseUrl, {
      commitSha: 'abc123',
      defaultBranch: 'main',
      files: [{ path: 'src/Foo.cs', content: 'public class Foo { }' }],
    });
    assert.equal(status, 200);
    assert.equal(json.commitSha, 'abc123');
    assert.equal(json.defaultBranch, 'main');
  });
});
