const baseUrl = process.env.PORTAL_SMOKE_URL ?? 'http://127.0.0.1:4173';
const paths = ['/portal/', '/portal/cursos/demo/1', '/portal/alunos', '/portal/pendencias', '/portal/conexoes', '/portal/relatorios'];

for (const path of paths) {
  const response = await fetch(`${baseUrl}${path}`);
  const html = await response.text();
  if (!response.ok || !html.includes('id="root"')) {
    throw new Error(`Portal smoke failed for ${path}: status=${response.status}`);
  }
  console.log(`PASS ${path} ${response.status}`);
}

console.log(`Portal smoke passed (${paths.length} SPA routes)`);
