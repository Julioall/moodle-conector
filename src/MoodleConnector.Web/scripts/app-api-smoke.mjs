import { request } from 'node:http';
import { randomUUID } from 'node:crypto';
import { Buffer } from 'node:buffer';

const baseUrl = new URL(process.env.APP_API_SMOKE_URL ?? 'http://127.0.0.1:8787');
if (!['127.0.0.1', 'localhost', '::1'].includes(baseUrl.hostname)) {
  throw new Error('App API smoke only runs against a local host.');
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

const email = `app-smoke-${randomUUID()}@example.test`;
const password = 'AppLocalSmoke!2026';
const registered = await call('POST', '/api/account/register', { name: 'App API Smoke', email, password });
if (registered.status !== 200 || !registered.cookie) throw new Error(`Register failed: ${registered.status} ${registered.body}`);

const session = await call('GET', '/api/session', undefined, registered.cookie);
const sessionBody = JSON.parse(session.body);
if (session.status !== 200 || sessionBody.data?.authenticated !== true) throw new Error(`Session failed: ${session.status} ${session.body}`);

const dashboard = await call('GET', '/api/dashboard', undefined, registered.cookie);
const dashboardBody = JSON.parse(dashboard.body);
if (dashboard.status !== 200 || !dashboardBody.data?.summary || !dashboardBody.meta?.generatedAt) {
  throw new Error(`Dashboard failed: ${dashboard.status} ${dashboard.body}`);
}

for (const path of ['/api/connections', '/api/courses', '/api/pending']) {
  const response = await call('GET', path, undefined, registered.cookie);
  const body = JSON.parse(response.body);
  if (response.status !== 200 || !Array.isArray(body.data) || !body.meta?.generatedAt) {
    throw new Error(`${path} failed: ${response.status} ${response.body}`);
  }
  console.log(`PASS ${path} ${response.status}`);
}

const courses = await call('GET', '/api/courses', undefined, registered.cookie);
const coursesBody = JSON.parse(courses.body);
if (coursesBody.data?.[0]) {
  const course = coursesBody.data[0];
  const students = await call(
    'GET',
    `/api/courses/${encodeURIComponent(course.connectionRef)}/${encodeURIComponent(course.courseId)}/students`,
    undefined,
    registered.cookie,
  );
  const studentsBody = JSON.parse(students.body);
  if (students.status !== 200 || !Array.isArray(studentsBody.data) || !studentsBody.meta?.generatedAt) {
    throw new Error(`/api/courses/{connectionRef}/{courseId}/students failed: ${students.status} ${students.body}`);
  }
  console.log(`PASS scoped students ${students.status}`);
} else {
  console.log('PASS scoped students skipped (local account has no Moodle course)');
}

console.log(`PASS register ${registered.status}`);
console.log(`PASS session ${session.status}`);
console.log(`PASS dashboard ${dashboard.status}`);

