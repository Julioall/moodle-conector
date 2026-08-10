import { request } from 'node:http';
import { randomUUID } from 'node:crypto';
import { Buffer } from 'node:buffer';

const baseUrl = new URL(process.env.PORTAL_API_SMOKE_URL ?? 'http://127.0.0.1:8787');
if (!['127.0.0.1', 'localhost', '::1'].includes(baseUrl.hostname)) {
  throw new Error('Portal API smoke only runs against a local host.');
}

const call = (method, path, body, cookie) => new Promise((resolve, reject) => {
  const payload = body ? JSON.stringify(body) : undefined;
  const req = request(new URL(path, baseUrl), {
    method,
    headers: {
      ...(payload ? { 'content-type': 'application/json', 'content-length': Buffer.byteLength(payload) } : {}),
      ...(cookie ? { cookie } : {}),
    },
  }, response => {
    let text = '';
    response.setEncoding('utf8');
    response.on('data', chunk => { text += chunk; });
    response.on('end', () => resolve({ status: response.statusCode ?? 0, body: text, cookie: response.headers['set-cookie']?.[0]?.split(';')[0] }));
  });
  req.on('error', reject);
  if (payload) req.write(payload);
  req.end();
});

const email = `portal-smoke-${randomUUID()}@example.test`;
const password = 'PortalLocalSmoke!2026';
const registered = await call('POST', '/api/account/register', { name: 'Portal API Smoke', email, password });
if (registered.status !== 200 || !registered.cookie) throw new Error(`Register failed: ${registered.status} ${registered.body}`);

const session = await call('GET', '/api/portal/session', undefined, registered.cookie);
const sessionBody = JSON.parse(session.body);
if (session.status !== 200 || sessionBody.data?.authenticated !== true) throw new Error(`Session failed: ${session.status} ${session.body}`);

const dashboard = await call('GET', '/api/portal/dashboard', undefined, registered.cookie);
const dashboardBody = JSON.parse(dashboard.body);
if (dashboard.status !== 200 || !dashboardBody.data?.summary || !dashboardBody.meta?.generatedAt) {
  throw new Error(`Dashboard failed: ${dashboard.status} ${dashboard.body}`);
}

console.log(`PASS register ${registered.status}`);
console.log(`PASS session ${session.status}`);
console.log(`PASS dashboard ${dashboard.status}`);
