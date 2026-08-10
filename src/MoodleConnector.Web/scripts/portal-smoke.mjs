import { request } from 'node:http';

const baseUrl = process.env.PORTAL_SMOKE_URL ?? 'http://127.0.0.1:4173';
const paths = [
  '/portal/',
  '/portal/meus-cursos',
  '/portal/cursos/demo/1',
  '/portal/alunos',
  '/portal/pendencias',
  '/portal/tarefas',
  '/portal/agenda',
  '/portal/mensagens',
  '/portal/followup',
  '/portal/relatorios',
  '/portal/configuracoes',
  '/portal/conexoes',
  '/portal/rota-inexistente',
];

const get = (url) => new Promise((resolve, reject) => {
  const req = request(url, response => {
    let body = '';
    response.setEncoding('utf8');
    response.on('data', chunk => { body += chunk; });
    response.on('end', () => resolve({ status: response.statusCode ?? 0, body, headers: response.headers }));
  });
  req.on('error', reject);
  req.end();
});

for (const path of paths) {
  const response = await get(`${baseUrl}${path}`);
  if (response.status < 200 || response.status >= 300 || !response.body.includes('id="root"')) {
    throw new Error(`Portal smoke failed for ${path}: status=${response.status}`);
  }
  console.log(`PASS ${path} ${response.status}`);
}

for (const path of ['/', '/app.html', '/auth.html']) {
  const response = await get(`${baseUrl}${path}`);
  if (response.status < 300 || response.status >= 400 || response.headers?.location !== '/portal/') {
    throw new Error(`Legacy redirect failed for ${path}: status=${response.status}`);
  }
  console.log(`PASS legacy redirect ${path} ${response.status}`);
}

console.log(`Portal smoke passed (${paths.length} SPA routes + 3 legacy redirects)`);
