import { request } from 'node:http';
import { randomUUID } from 'node:crypto';
import { Buffer } from 'node:buffer';

const baseUrl = new URL(process.env.APP_API_SMOKE_URL ?? 'http://127.0.0.1:8787');
if (!['127.0.0.1', 'localhost', '::1'].includes(baseUrl.hostname)) {
  throw new Error('App E2E smoke only runs against a local host.');
}

let cookie = '';

const call = (method, path, body, headers = {}) => new Promise((resolve, reject) => {
  const payload = body === undefined ? undefined : JSON.stringify(body);
  const requestHeaders = {
    accept: 'application/json',
    ...(payload ? { 'content-type': 'application/json', 'content-length': Buffer.byteLength(payload) } : {}),
    ...(cookie ? { cookie } : {}),
    ...headers,
  };
  const req = request(new URL(path, baseUrl), { method, headers: requestHeaders }, response => {
    let text = '';
    response.setEncoding('utf8');
    response.on('data', chunk => { text += chunk; });
    response.on('end', () => {
      const setCookies = response.headers['set-cookie'] ?? [];
      if (setCookies.length > 0) {
        const nextCookies = setCookies.map(value => value.split(';', 1)[0]);
        const existing = cookie ? cookie.split('; ').filter(Boolean) : [];
        const merged = new Map(existing.map(value => value.split('=', 1)[0]).map((key, index) => [key, existing[index]]));
        nextCookies.forEach(value => merged.set(value.split('=', 1)[0], value));
        cookie = [...merged.values()].join('; ');
      }
      let json;
      if (text) {
        try { json = JSON.parse(text); } catch { json = undefined; }
      }
      resolve({ status: response.statusCode ?? 0, text, json });
    });
  });
  req.on('error', reject);
  if (payload) req.write(payload);
  req.end();
});

const expectStatus = (response, expected, label) => {
  if (response.status !== expected) {
    throw new Error(`${label} failed: ${response.status} ${response.text}`);
  }
  console.log(`PASS ${label} ${response.status}`);
};

const expectJson = (response, expected, label) => {
  expectStatus(response, expected, label);
  if (!response.json) throw new Error(`${label} did not return JSON.`);
  return response.json;
};

const email = `app-e2e-${randomUUID()}@example.test`;
const password = 'AppE2EFlow!2026';
const moodleAlias = process.env.APP_E2E_MOODLE_ALIAS ?? 'local-e2e';
const moodleBaseUrl = process.env.APP_E2E_MOODLE_URL ?? 'https://moodle.local';
const moodleUsername = process.env.APP_E2E_MOODLE_USERNAME ?? 'demo';
const moodlePassword = process.env.APP_E2E_MOODLE_PASSWORD ?? 'demo-password';

const registrationCsrf = expectJson(await call('GET', '/api/csrf'), 200, 'registration CSRF');
if (!registrationCsrf.token) throw new Error('registration CSRF token was not issued.');
expectJson(
  await call('POST', '/api/account/register', { name: 'App E2E Smoke', email, password }, { 'x-csrf-token': registrationCsrf.token }),
  200,
  'register',
);
const session = expectJson(await call('GET', '/api/session'), 200, 'session');
if (session.data?.authenticated !== true) throw new Error('session is not authenticated.');

const csrf = expectJson(await call('GET', '/api/csrf'), 200, 'csrf');
if (!csrf.token) throw new Error('CSRF token was not issued.');

const account = expectJson(await call('GET', '/api/account/me'), 200, 'account');
const permissionPayload = {
  name: `E2E ${randomUUID()}`,
  description: 'Permissões temporárias para o smoke test ponta a ponta.',
  permissions: ['connections.manage', 'courses.view', 'students.view', 'dashboard.view'],
};
const missingCsrf = await call('POST', '/api/permission-groups', permissionPayload);
expectStatus(missingCsrf, 400, 'reject missing CSRF');
if (missingCsrf.json?.error?.code !== 'csrf_invalid') {
  throw new Error(`missing CSRF contract is invalid: ${missingCsrf.text}`);
}

const permissionGroup = expectJson(await call('POST', '/api/permission-groups', permissionPayload, { 'x-csrf-token': csrf.token }), 200, 'create E2E permissions');
const groupId = permissionGroup.group?.id;
if (!groupId || !account.id) throw new Error('E2E permission group was not created.');
expectJson(await call('POST', `/api/permission-groups/${encodeURIComponent(groupId)}/members`, { userId: account.id }, { 'x-csrf-token': csrf.token }), 200, 'assign E2E permissions');
expectJson(await call('POST', '/api/account/login', { email, password }), 200, 'refresh session permissions');

const connectionPayload = {
  moodleAlias,
  moodleBaseUrl,
  moodleUsername,
  moodlePassword,
  isDefault: true,
  canWrite: false,
};
const connection = expectJson(await call('POST', '/api/connections', connectionPayload, { 'x-csrf-token': csrf.token }), 200, 'connect Moodle');
if (!connection.connectionRef || !connection.alias || connection.status !== 'active' || connection.apiKey || connection.password || connection.token) {
  throw new Error(`connection contract is invalid or contains a secret: ${JSON.stringify(connection)}`);
}

const connections = expectJson(await call('GET', '/api/connections'), 200, 'connections');
const connectionRef = connections.data?.[0]?.connectionRef;
if (!connectionRef) throw new Error('connectionRef was not returned.');

const courses = expectJson(await call('GET', `/api/courses?connectionRef=${encodeURIComponent(connectionRef)}`), 200, 'courses');
const course = courses.data?.[0];
if (!course?.courseId) throw new Error('stub course was not returned.');

const coursePath = `/api/courses/${encodeURIComponent(connectionRef)}/${encodeURIComponent(course.courseId)}`;
expectJson(await call('GET', coursePath), 200, 'course detail');
const activities = expectJson(await call('GET', `${coursePath}/activities`), 200, 'activities');
if (activities.data?.length !== 3 || activities.meta?.total !== 3) throw new Error('activity pagination contract is invalid.');

const students = expectJson(await call('GET', `${coursePath}/students`), 200, 'students');
const student = students.data?.[0];
if (!student?.studentId || students.data.length !== 3) throw new Error('stub students were not returned.');
expectJson(await call('GET', `${coursePath}/students/${encodeURIComponent(student.studentId)}`), 200, 'student profile');

const pending = expectJson(await call('GET', `/api/pending?connectionRef=${encodeURIComponent(connectionRef)}&courseId=${encodeURIComponent(course.courseId)}&periodDays=30`), 200, 'pending');
if (pending.meta?.total !== 3 || pending.data?.some(item => item.type !== 'pending_submission')) throw new Error('pending contract is invalid.');

const dashboard = expectJson(await call('GET', `/api/dashboard?connectionRef=${encodeURIComponent(connectionRef)}&courseId=${encodeURIComponent(course.courseId)}`), 200, 'dashboard');
if (dashboard.data?.summary?.activeCourses !== 1 || dashboard.data?.summary?.pendingDeliveries !== 3) throw new Error('dashboard stub indicators are invalid.');

console.log('App E2E smoke passed (login â†’ Moodle â†’ courses â†’ course â†’ students â†’ profile â†’ pending â†’ dashboard)');

